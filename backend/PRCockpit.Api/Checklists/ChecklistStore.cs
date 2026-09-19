using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PRCockpit.Api.Checklists;

public record ChecklistState(
    bool AiReview, bool Quality, bool Understand, bool Architecture, bool Debug, bool Ready,
    DateTimeOffset? UpdatedAt);

public record ChecklistUpdate(bool? Completed);
public record ChecklistProgress(int PullRequestId, int CompletedCount);

public sealed class ChecklistException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ChecklistStore(IConfiguration configuration)
{
    private const string Columns = "ai_review, quality, understand, architecture, debug, ready, updated_at";
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS pr_checklists (
            organization TEXT NOT NULL,
            project TEXT NOT NULL,
            repository_id TEXT NOT NULL,
            pull_request_id INTEGER NOT NULL,
            ai_review INTEGER NOT NULL DEFAULT 0 CHECK (ai_review IN (0, 1)),
            quality INTEGER NOT NULL DEFAULT 0 CHECK (quality IN (0, 1)),
            understand INTEGER NOT NULL DEFAULT 0 CHECK (understand IN (0, 1)),
            architecture INTEGER NOT NULL DEFAULT 0 CHECK (architecture IN (0, 1)),
            debug INTEGER NOT NULL DEFAULT 0 CHECK (debug IN (0, 1)),
            ready INTEGER NOT NULL DEFAULT 0 CHECK (ready IN (0, 1)),
            updated_at TEXT NOT NULL,
            PRIMARY KEY (organization, project, repository_id, pull_request_id)
        );
        """;

    public async Task<ChecklistState> GetAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM pr_checklists
            WHERE organization = $organization AND project = $project
              AND repository_id = $repository AND pull_request_id = $pullRequestId;
            """;
        AddKey(command, organization, project, repositoryId, pullRequestId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadState(reader) : new(false, false, false, false, false, false, null);
    }

    public async Task<IReadOnlyList<ChecklistProgress>> GetProgressAsync(
        string project, string repositoryId, CancellationToken ct)
    {
        var organization = ValidateLocation(project, repositoryId);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pull_request_id,
                   ai_review + quality + understand + architecture + debug + ready
            FROM pr_checklists
            WHERE organization = $organization AND project = $project AND repository_id = $repository;
            """;
        command.Parameters.AddWithValue("$organization", organization);
        command.Parameters.AddWithValue("$project", project);
        command.Parameters.AddWithValue("$repository", repositoryId);
        var progress = new List<ChecklistProgress>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            progress.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1))));
        return progress;
    }

    public async Task<ChecklistState> SetAsync(
        string project, string repositoryId, int pullRequestId, string item, bool? completed,
        CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        if (completed is null) throw new ChecklistException("Specify completed as true or false.", 400);
        var column = item.ToLowerInvariant() switch
        {
            "ai-review" => "ai_review",
            "quality" => "quality",
            "understand" => "understand",
            "architecture" => "architecture",
            "debug" => "debug",
            "ready" => "ready",
            _ => throw new ChecklistException("Unknown checklist item.", 400)
        };

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO pr_checklists
                (organization, project, repository_id, pull_request_id, {column}, updated_at)
            VALUES ($organization, $project, $repository, $pullRequestId, $completed, $updatedAt)
            ON CONFLICT (organization, project, repository_id, pull_request_id)
            DO UPDATE SET {column} = excluded.{column}, updated_at = excluded.updated_at
            RETURNING {Columns};
            """;
        AddKey(command, organization, project, repositoryId, pullRequestId);
        command.Parameters.AddWithValue("$completed", completed.Value ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ChecklistException("Checklist could not be saved.", 503);
        return ReadState(reader);
    }

    private string ValidateKey(string project, string repositoryId, int pullRequestId)
    {
        var organization = ValidateLocation(project, repositoryId);
        if (pullRequestId <= 0)
            throw new ChecklistException("Invalid pull request location.", 400);
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
        var path = configuration["Checklist:DatabasePath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localData)) localData = AppContext.BaseDirectory;
            path = Path.Combine(localData, "PRCockpit", "checklist.db");
        }
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        try
        {
            await connection.OpenAsync(ct);
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

    private static ChecklistState ReadState(SqliteDataReader reader) => new(
        reader.GetInt64(0) != 0,
        reader.GetInt64(1) != 0,
        reader.GetInt64(2) != 0,
        reader.GetInt64(3) != 0,
        reader.GetInt64(4) != 0,
        reader.GetInt64(5) != 0,
        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
