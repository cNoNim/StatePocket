using Microsoft.Data.Sqlite;
using StatePocket.Configuration;

namespace StatePocket.Storage;

internal sealed class SqliteDatabaseInitializer(ResolvedOptions resolvedOptions)
{
    private const string RevisionColumnName = "revision";
    private const int SqliteGeneralErrorCode = 1;
    private const string DuplicateRevisionColumnErrorMessage = "duplicate column name: revision";
    private const string SeedNamespaceRevisionClockCommandText = """
                                                                 INSERT INTO kv_namespace_revision(namespace, last_revision)
                                                                 SELECT namespace, MAX(revision)
                                                                 FROM kv
                                                                 GROUP BY namespace
                                                                 ON CONFLICT(namespace) DO UPDATE SET
                                                                     last_revision = MAX(
                                                                         kv_namespace_revision.last_revision,
                                                                         excluded.last_revision
                                                                     );
                                                                 """;
    private const string Schema = """
                                  CREATE TABLE IF NOT EXISTS kv (
                                      namespace  TEXT NOT NULL DEFAULT 'default',
                                      key        TEXT NOT NULL,
                                      value      TEXT NOT NULL CHECK(json_valid(value)),
                                      expires_at TEXT NULL,
                                      updated_at TEXT NOT NULL,
                                      revision   INTEGER NOT NULL DEFAULT 0,
                                      PRIMARY KEY(namespace, key)
                                  ) WITHOUT ROWID;

                                  CREATE INDEX IF NOT EXISTS idx_kv_expires ON kv(expires_at) WHERE expires_at IS NOT NULL;

                                  CREATE TABLE IF NOT EXISTS kv_namespace_revision (
                                      namespace     TEXT NOT NULL PRIMARY KEY,
                                      last_revision INTEGER NOT NULL
                                  ) WITHOUT ROWID;
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
            await EnsureRevisionColumnAsync(connection, cancellationToken)
               .ConfigureAwait(false);
            if (await ShouldSeedNamespaceRevisionClockAsync(connection, cancellationToken)
                   .ConfigureAwait(false))
            {
                await SeedNamespaceRevisionClockAsync(connection, cancellationToken)
                   .ConfigureAwait(false);
            }
        }
    }

    private static async Task EnsureRevisionColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        if (await HasColumnAsync(connection, RevisionColumnName, cancellationToken)
               .ConfigureAwait(false))
        {
            return;
        }
        var alterTableCommand = connection.CreateCommand();
        await using (alterTableCommand.ConfigureAwait(false))
        {
            alterTableCommand.CommandText = "ALTER TABLE kv ADD COLUMN revision INTEGER NOT NULL DEFAULT 0;";
            try
            {
                await alterTableCommand.ExecuteNonQueryAsync(cancellationToken)
                                       .ConfigureAwait(false);
            }
            catch (SqliteException exception) when (IsDuplicateRevisionColumnError(exception))
            {
                // A concurrent initializer already added the column.
            }
        }
    }

    private static bool IsDuplicateRevisionColumnError(SqliteException exception)
    {
        return exception.SqliteErrorCode == SqliteGeneralErrorCode
            && exception.Message.Contains(DuplicateRevisionColumnErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedNamespaceRevisionClockAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        var seedClockCommand = connection.CreateCommand();
        await using (seedClockCommand.ConfigureAwait(false))
        {
            seedClockCommand.CommandText = SeedNamespaceRevisionClockCommandText;
            await seedClockCommand.ExecuteNonQueryAsync(cancellationToken)
                                  .ConfigureAwait(false);
        }
    }

    internal static async Task<bool> ShouldSeedNamespaceRevisionClockAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        var missingNamespaceClockCommand = connection.CreateCommand();
        await using (missingNamespaceClockCommand.ConfigureAwait(false))
        {
            missingNamespaceClockCommand.CommandText = """
                                                       SELECT 1
                                                       FROM (
                                                           SELECT DISTINCT namespace
                                                           FROM kv
                                                           EXCEPT
                                                           SELECT namespace
                                                           FROM kv_namespace_revision
                                                       )
                                                       LIMIT 1;
                                                       """;
            var result = await missingNamespaceClockCommand.ExecuteScalarAsync(cancellationToken)
                                                           .ConfigureAwait(false);
            return result is not null;
        }
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string columnName,
        CancellationToken cancellationToken
    )
    {
        var pragmaCommand = connection.CreateCommand();
        await using (pragmaCommand.ConfigureAwait(false))
        {
            pragmaCommand.CommandText = "PRAGMA table_info(kv);";
            var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken)
                                            .ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken)
                                   .ConfigureAwait(false))
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = resolvedOptions.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString()
        );
    }
}
