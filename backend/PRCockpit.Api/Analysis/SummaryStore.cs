using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PRCockpit.Api.Persistence;

namespace PRCockpit.Api.Analysis;

public record StoredSummary(SummaryResponse Result, DateTimeOffset SavedAt);

public sealed class SummaryStore(IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS pr_summaries (
            organization TEXT NOT NULL,
            project TEXT NOT NULL,
            repository_id TEXT NOT NULL,
            pull_request_id INTEGER NOT NULL,
            head_commit_sha TEXT,
            response_json TEXT NOT NULL,
            saved_at TEXT NOT NULL,
            PRIMARY KEY (organization, project, repository_id, pull_request_id)
        );
        """;

    public async Task<StoredSummary?> GetAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT response_json, saved_at FROM pr_summaries
            WHERE organization = $organization AND project = $project
              AND repository_id = $repository AND pull_request_id = $pullRequestId;
            """;
        AddKey(command, organization, project, repositoryId, pullRequestId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        try
        {
            var result = JsonSerializer.Deserialize<SummaryResponse>(reader.GetString(0), JsonOptions);
            if (result is null || result.SchemaVersion != 1 || result.Sentences is not { Count: >= 2 and <= 5 })
                throw new JsonException();
            var savedAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            return new StoredSummary(result, savedAt);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new SummaryAnalysisException("Saved Summary is invalid.", 503);
        }
    }

    public async Task<StoredSummary> SaveAsync(
        string project, string repositoryId, int pullRequestId, SummaryResponse result, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        var savedAt = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pr_summaries
                (organization, project, repository_id, pull_request_id, head_commit_sha, response_json, saved_at)
            VALUES ($organization, $project, $repository, $pullRequestId, $headSha, $response, $savedAt)
            ON CONFLICT (organization, project, repository_id, pull_request_id)
            DO UPDATE SET head_commit_sha = excluded.head_commit_sha,
                          response_json = excluded.response_json, saved_at = excluded.saved_at;
            """;
        AddKey(command, organization, project, repositoryId, pullRequestId);
        command.Parameters.AddWithValue("$headSha", (object?)result.HeadCommitSha ?? DBNull.Value);
        command.Parameters.AddWithValue("$response", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("$savedAt", savedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct);
        return new StoredSummary(result, savedAt);
    }

    private string ValidateKey(string project, string repositoryId, int pullRequestId)
    {
        var organization = configuration["AzureDevOps:Organization"];
        if (string.IsNullOrWhiteSpace(organization))
            throw new SummaryAnalysisException("Configure AzureDevOps:Organization on the backend.", 503);
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(repositoryId) || pullRequestId <= 0)
            throw new SummaryAnalysisException("Invalid pull request location.", 400);
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
