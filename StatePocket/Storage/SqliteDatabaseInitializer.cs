using Microsoft.Data.Sqlite;
using StatePocket.Configuration;

namespace StatePocket.Storage;

internal sealed class SqliteDatabaseInitializer(ResolvedOptions resolvedOptions)
{
    private const string Schema = """
                                  CREATE TABLE IF NOT EXISTS kv (
                                      namespace  TEXT NOT NULL DEFAULT 'default',
                                      key        TEXT NOT NULL,
                                      value      TEXT NOT NULL CHECK(json_valid(value)),
                                      expires_at TEXT NULL,
                                      updated_at TEXT NOT NULL,
                                      PRIMARY KEY(namespace, key)
                                  ) WITHOUT ROWID;

                                  CREATE INDEX IF NOT EXISTS idx_kv_expires ON kv(expires_at) WHERE expires_at IS NOT NULL;
                                  """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(resolvedOptions.DatabasePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        var connection = CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken)
                            .ConfigureAwait(false);
            var pragmaCommand = connection.CreateCommand();
            await using (pragmaCommand.ConfigureAwait(false))
            {
                pragmaCommand.CommandText = "PRAGMA journal_mode = WAL;";
                await pragmaCommand.ExecuteNonQueryAsync(cancellationToken)
                                   .ConfigureAwait(false);
            }
            var schemaCommand = connection.CreateCommand();
            await using (schemaCommand.ConfigureAwait(false))
            {
                schemaCommand.CommandText = Schema;
                await schemaCommand.ExecuteNonQueryAsync(cancellationToken)
                                   .ConfigureAwait(false);
            }
        }
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = resolvedOptions.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString()
        );
    }
}
