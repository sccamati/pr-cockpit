using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PRCockpit.Api.Persistence;

namespace PRCockpit.Api.Checklists;

public record FileReviewEntry(string Path, string? BlobId, string? HeadSha, DateTimeOffset UpdatedAt);

public record FileReviewState(
    IReadOnlyList<FileReviewEntry> Files, IReadOnlyList<string> ReadingPath, DateTimeOffset? UpdatedAt);

public record FileReviewUpdate(
    string? Path, bool? Reviewed, string? BlobId, string? HeadCommitSha, int? ChangedFilesCount);

public record FileReviewResult(FileReviewEntry? Entry, int ReviewedCount);
public record ReadingPathUpdate(IReadOnlyList<string>? Paths);
public record ReadingPathState(IReadOnlyList<string> Paths, DateTimeOffset? UpdatedAt);
public record FileReviewProgress(int PullRequestId, int ReviewedCount, int ChangedFilesCount);

/// <summary>
/// Remembers which files of a pull request have been read, and the order the user wants to
/// read them in. Deliberately shaped like <see cref="ChecklistStore"/> — same key, same
/// create-table-per-operation, same connection-per-call — and it reuses
/// <see cref="ChecklistException"/> so <c>Execute</c> in Program.cs needs no new arm.
/// </summary>
public sealed class ReviewProgressStore(IConfiguration configuration)
{
    // Mirrors the $top=2000 change page the Azure DevOps client already uses, so a pull
    // request that fits on screen always fits here too.
    private const int MaxFilesPerPullRequest = 2000;
    private const int MaxPathLength = 512;
    private const int MaxReadingPath = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // A reviewed file is a row that exists: unmarking is a DELETE, counting is COUNT(*),
    // and the table never fills up with zeros for files that were merely opened.
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS pr_file_reviews (
            organization TEXT NOT NULL,
            project TEXT NOT NULL,
            repository_id TEXT NOT NULL,
            pull_request_id INTEGER NOT NULL,
            file_path TEXT NOT NULL CHECK (file_path LIKE '/%' AND length(file_path) <= 512),
            reviewed_blob_id TEXT,
            reviewed_head_sha TEXT,
            changed_files_count INTEGER NOT NULL DEFAULT 0 CHECK (changed_files_count >= 0),
            updated_at TEXT NOT NULL,
            PRIMARY KEY (organization, project, repository_id, pull_request_id, file_path)
        );

        CREATE TABLE IF NOT EXISTS pr_reading_paths (
            organization TEXT NOT NULL,
            project TEXT NOT NULL,
            repository_id TEXT NOT NULL,
            pull_request_id INTEGER NOT NULL,
            paths_json TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            PRIMARY KEY (organization, project, repository_id, pull_request_id)
        );
        """;

    public async Task<FileReviewState> GetAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        await using var connection = await OpenAsync(ct);

        var files = new List<FileReviewEntry>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT file_path, reviewed_blob_id, reviewed_head_sha, updated_at FROM pr_file_reviews
                WHERE organization = $organization AND project = $project
                  AND repository_id = $repository AND pull_request_id = $pullRequestId;
                """;
            AddKey(command, organization, project, repositoryId, pullRequestId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                files.Add(new FileReviewEntry(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    ParseDate(reader.GetString(3))));
            }
        }

        var readingPath = await ReadPathAsync(connection, organization, project, repositoryId, pullRequestId, ct);
        var updatedAt = files.Count == 0 ? readingPath.UpdatedAt : files.Max(file => file.UpdatedAt);
        if (readingPath.UpdatedAt is { } pathUpdated && updatedAt is { } current && pathUpdated > current)
            updatedAt = pathUpdated;
        return new FileReviewState(files, readingPath.Paths, updatedAt);
    }

    public async Task<FileReviewResult> SetFileAsync(
        string project, string repositoryId, int pullRequestId, FileReviewUpdate update, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        ArgumentNullException.ThrowIfNull(update);
        if (update.Reviewed is null) throw new ChecklistException("Specify reviewed as true or false.", 400);
        var path = ValidatePath(update.Path);
        var blobId = ValidateSha(update.BlobId, "blob id");
        var headSha = ValidateSha(update.HeadCommitSha, "commit sha");
        if (update.ChangedFilesCount is < 0) throw new ChecklistException("Invalid changed file count.", 400);

        await using var connection = await OpenAsync(ct);
        FileReviewEntry? entry = null;

        if (update.Reviewed.Value)
        {
            await using var command = connection.CreateCommand();
            // The row cap lives inside the INSERT so enforcing it costs no extra round trip.
            // An existing row always passes, so re-marking a file can never be blocked.
            command.CommandText = $"""
                INSERT INTO pr_file_reviews
                    (organization, project, repository_id, pull_request_id, file_path,
                     reviewed_blob_id, reviewed_head_sha, changed_files_count, updated_at)
                SELECT $organization, $project, $repository, $pullRequestId, $path,
                       $blobId, $headSha, $changedFilesCount, $updatedAt
                WHERE EXISTS (SELECT 1 FROM pr_file_reviews
                              WHERE organization = $organization AND project = $project
                                AND repository_id = $repository AND pull_request_id = $pullRequestId
                                AND file_path = $path)
                   OR (SELECT COUNT(*) FROM pr_file_reviews
                       WHERE organization = $organization AND project = $project
                         AND repository_id = $repository AND pull_request_id = $pullRequestId)
                       < {MaxFilesPerPullRequest}
                ON CONFLICT (organization, project, repository_id, pull_request_id, file_path)
                DO UPDATE SET reviewed_blob_id = excluded.reviewed_blob_id,
                              reviewed_head_sha = excluded.reviewed_head_sha,
                              changed_files_count = excluded.changed_files_count,
                              updated_at = excluded.updated_at
                RETURNING file_path, reviewed_blob_id, reviewed_head_sha, updated_at;
                """;
            AddKey(command, organization, project, repositoryId, pullRequestId);
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$blobId", (object?)blobId ?? DBNull.Value);
            command.Parameters.AddWithValue("$headSha", (object?)headSha ?? DBNull.Value);
            command.Parameters.AddWithValue("$changedFilesCount", update.ChangedFilesCount ?? 0);
            command.Parameters.AddWithValue("$updatedAt", Now());

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new ChecklistException("This pull request has too many reviewed files.", 400);
            entry = new FileReviewEntry(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                ParseDate(reader.GetString(3)));
        }
        else
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM pr_file_reviews
                WHERE organization = $organization AND project = $project
                  AND repository_id = $repository AND pull_request_id = $pullRequestId
                  AND file_path = $path;
                """;
            AddKey(command, organization, project, repositoryId, pullRequestId);
            command.Parameters.AddWithValue("$path", path);
            await command.ExecuteNonQueryAsync(ct);
        }

        return new FileReviewResult(entry, await CountAsync(connection, organization, project, repositoryId, pullRequestId, ct));
    }

    public async Task<ReadingPathState> SetReadingPathAsync(
        string project, string repositoryId, int pullRequestId, IReadOnlyList<string>? paths, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        if (paths is null) throw new ChecklistException("Specify the reading path.", 400);
        if (paths.Count > MaxReadingPath)
            throw new ChecklistException($"The reading path holds at most {MaxReadingPath} files.", 400);
        var validated = paths.Select(ValidatePath).ToArray();
        if (validated.Distinct(StringComparer.Ordinal).Count() != validated.Length)
            throw new ChecklistException("The reading path contains a duplicate file.", 400);

        var updatedAt = Now();
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pr_reading_paths
                (organization, project, repository_id, pull_request_id, paths_json, updated_at)
            VALUES ($organization, $project, $repository, $pullRequestId, $paths, $updatedAt)
            ON CONFLICT (organization, project, repository_id, pull_request_id)
            DO UPDATE SET paths_json = excluded.paths_json, updated_at = excluded.updated_at;
            """;
        AddKey(command, organization, project, repositoryId, pullRequestId);
        command.Parameters.AddWithValue("$paths", JsonSerializer.Serialize(validated, JsonOptions));
        command.Parameters.AddWithValue("$updatedAt", updatedAt);
        await command.ExecuteNonQueryAsync(ct);
        return new ReadingPathState(validated, ParseDate(updatedAt));
    }

    public async Task<IReadOnlyList<FileReviewProgress>> GetProgressAsync(
        string project, string repositoryId, CancellationToken ct)
    {
        var organization = ValidateLocation(project, repositoryId);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pull_request_id, COUNT(*), MAX(changed_files_count)
            FROM pr_file_reviews
            WHERE organization = $organization AND project = $project AND repository_id = $repository
            GROUP BY pull_request_id;
            """;
        command.Parameters.AddWithValue("$organization", organization);
        command.Parameters.AddWithValue("$project", project);
        command.Parameters.AddWithValue("$repository", repositoryId);
        var progress = new List<FileReviewProgress>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            progress.Add(new FileReviewProgress(
                checked((int)reader.GetInt64(0)),
                checked((int)reader.GetInt64(1)),
                reader.IsDBNull(2) ? 0 : checked((int)reader.GetInt64(2))));
        }
        return progress;
    }

    private static async Task<int> CountAsync(
        SqliteConnection connection, string organization, string project, string repositoryId,
        int pullRequestId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM pr_file_reviews
            WHERE organization = $organization AND project = $project
              AND repository_id = $repository AND pull_request_id = $pullRequestId;
            """;
        AddKey(command, organization, project, repositoryId, pullRequestId);
        return checked((int)(long)(await command.ExecuteScalarAsync(ct) ?? 0L));
    }

    private static async Task<ReadingPathState> ReadPathAsync(
        SqliteConnection connection, string organization, string project, string repositoryId,
        int pullRequestId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT paths_json, updated_at FROM pr_reading_paths
            WHERE organization = $organization AND project = $project
              AND repository_id = $repository AND pull_request_id = $pullRequestId;
            """;
        AddKey(command, organization, project, repositoryId, pullRequestId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new ReadingPathState([], null);
        try
        {
            // Re-validated on read, the way SummaryStore treats its stored JSON: a corrupt
            // row must not reach the UI as if it were a real reading path.
            var paths = JsonSerializer.Deserialize<string[]>(reader.GetString(0), JsonOptions);
            if (paths is null || paths.Length > MaxReadingPath ||
                paths.Any(path => string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') ||
                                  path.Length > MaxPathLength) ||
                paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
                throw new JsonException();
            return new ReadingPathState(paths, ParseDate(reader.GetString(1)));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new ChecklistException("Saved reading path is invalid.", 503);
        }
    }

    private static string ValidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') ||
            path.Contains('\0') || path.Length > MaxPathLength)
            throw new ChecklistException("Invalid file path.", 400);
        return path;
    }

    private static string? ValidateSha(string? value, string name)
    {
        if (value is null) return null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-fA-F0-9]{40}$"))
            throw new ChecklistException($"Invalid {name}.", 400);
        return value;
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private string ValidateKey(string project, string repositoryId, int pullRequestId)
    {
        var organization = ValidateLocation(project, repositoryId);
        if (pullRequestId <= 0) throw new ChecklistException("Invalid pull request location.", 400);
        return organization;
    }

    private string ValidateLocation(string project, string repositoryId)
    {
        var organization = configuration["AzureDevOps:Organization"];
        if (string.IsNullOrWhiteSpace(organization))
            throw new ChecklistException("Configure AzureDevOps:Organization on the backend.", 503);
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(repositoryId))
            throw new ChecklistException("Invalid pull request location.", 400);
        return organization;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = await LocalDatabase.OpenAsync(configuration, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Schema;
            await command.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void AddKey(
        SqliteCommand command, string organization, string project, string repositoryId, int pullRequestId)
    {
        command.Parameters.AddWithValue("$organization", organization);
        command.Parameters.AddWithValue("$project", project);
        command.Parameters.AddWithValue("$repository", repositoryId);
        command.Parameters.AddWithValue("$pullRequestId", pullRequestId);
    }
}
