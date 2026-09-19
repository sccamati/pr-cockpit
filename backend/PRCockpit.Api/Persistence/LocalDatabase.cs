using Microsoft.Data.Sqlite;

namespace PRCockpit.Api.Persistence;

internal static class LocalDatabase
{
    public static async Task<SqliteConnection> OpenAsync(IConfiguration configuration, CancellationToken ct)
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
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
