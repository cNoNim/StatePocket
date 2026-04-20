using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;

namespace StatePocket.Storage;

internal sealed partial class SqliteKvStore
{
    private const string GetValueCommandText = """
                                               SELECT value, expires_at, updated_at, revision
                                               FROM kv
                                               WHERE namespace = $namespace
                                                 AND key = $key
                                                 AND (expires_at IS NULL OR expires_at > $now);
                                               """;
    private const string ListValuesPageCommandText = """
                                                     SELECT key, value, expires_at, updated_at, revision
                                                     FROM kv
                                                     WHERE namespace = $namespace
                                                       AND (expires_at IS NULL OR expires_at > $now)
                                                       AND ($pattern IS NULL OR key GLOB $pattern)
                                                       AND ($cursor IS NULL OR key > $cursor)
                                                     ORDER BY key ASC
                                                     LIMIT $limit_plus_one;
                                                     """;
    private const string ListKeysPageCommandText = """
                                                   SELECT key
                                                   FROM kv
                                                   WHERE namespace = $namespace
                                                     AND (expires_at IS NULL OR expires_at > $now)
                                                     AND ($pattern IS NULL OR key GLOB $pattern)
                                                     AND ($cursor IS NULL OR key > $cursor)
                                                   ORDER BY key ASC
                                                   LIMIT $limit_plus_one;
                                                   """;
    private const string ListNamespacesPageCommandText = """
                                                         SELECT DISTINCT namespace
                                                         FROM kv
                                                         WHERE (expires_at IS NULL OR expires_at > $now)
                                                           AND ($pattern IS NULL OR namespace GLOB $pattern)
                                                           AND ($cursor IS NULL OR namespace > $cursor)
                                                         ORDER BY namespace ASC
                                                         LIMIT $limit_plus_one;
                                                         """;
    private const string ListValuesCommandText = """
                                                 SELECT key, value, expires_at, updated_at, revision
                                                 FROM kv
                                                 WHERE namespace = $namespace
                                                   AND (expires_at IS NULL OR expires_at > $now)
                                                   AND ($pattern IS NULL OR key GLOB $pattern)
                                                 ORDER BY key ASC;
                                                 """;
    private const string ListKeysCommandText = """
                                               SELECT key
                                               FROM kv
                                               WHERE namespace = $namespace
                                                 AND (expires_at IS NULL OR expires_at > $now)
                                                 AND ($pattern IS NULL OR key GLOB $pattern)
                                               ORDER BY key ASC;
                                               """;
    private const string ListNamespacesCommandText = """
                                                     SELECT DISTINCT namespace
                                                     FROM kv
                                                     WHERE (expires_at IS NULL OR expires_at > $now)
                                                       AND ($pattern IS NULL OR namespace GLOB $pattern)
                                                     ORDER BY namespace ASC;
                                                     """;
    private const string BatchGetCommandTextPrefix = """
                                                     SELECT key, value, expires_at, updated_at, revision
                                                     FROM kv
                                                     WHERE namespace = $namespace
                                                       AND key IN (
                                                     """;
    private const string BatchGetCommandTextSuffix = """
                                                     )
                                                       AND (expires_at IS NULL OR expires_at > $now);
                                                     """;

    public async Task<KvValue?> GetValueAsync(string? @namespace, string key, CancellationToken cancellationToken)
    {
        var normalizedNamespace = NormalizeNamespace(@namespace);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var connection = await OpenConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = GetValueCommandText;
                command.Parameters.AddWithValue("$namespace", normalizedNamespace);
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$now", now);
                var reader = await command.ExecuteReaderAsync(cancellationToken)
                                          .ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken)
                                     .ConfigureAwait(false))
                    {
                        return null;
                    }
                    return await ReadKvValueAsync(reader, 0, cancellationToken)
                       .ConfigureAwait(false);
                }
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, KvValue>> GetValuesAsync(
        string? @namespace,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            return ReadOnlyDictionary<string, KvValue>.Empty;
        }
        var normalizedNamespace = NormalizeNamespace(@namespace);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var connection = await OpenConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = connection.BeginTransaction(true);
            await using (transaction.ConfigureAwait(false))
            {
                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.Transaction = transaction;
                    Dictionary<string, KvValue> values = new(StringComparer.Ordinal);
                    foreach (var keyBatch in keys.Chunk(MaxBatchGetKeys))
                    {
                        await LoadBatchValuesAsync(
                                command,
                                normalizedNamespace,
                                now,
                                keyBatch,
                                values,
                                cancellationToken
                            )
                           .ConfigureAwait(false);
                    }
                    await transaction.CommitAsync(cancellationToken)
                                     .ConfigureAwait(false);
                    return values;
                }
            }
        }
    }

    public async Task<PageResult<KeyValuePair<string, KvValue>>> ListValuesPageAsync(
        string? @namespace,
        string? pattern,
        string? cursor,
        int limit,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var normalizedNamespace = NormalizeNamespace(@namespace);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var connection = await OpenConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ListValuesPageCommandText;
                command.Parameters.AddWithValue("$namespace", normalizedNamespace);
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$pattern", (object?)pattern ?? DBNull.Value);
                command.Parameters.AddWithValue("$cursor", (object?)cursor ?? DBNull.Value);
                command.Parameters.AddWithValue("$limit_plus_one", limit + 1);
                List<KeyValuePair<string, KvValue>> items = [];
                var reader = await command.ExecuteReaderAsync(cancellationToken)
                                          .ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        items.Add(
                            new KeyValuePair<string, KvValue>(
                                reader.GetString(0),
                                await ReadKvValueAsync(reader, 1, cancellationToken)
                                   .ConfigureAwait(false)
                            )
                        );
                    }
                }
                return CreatePageResult(items, limit, static item => item.Key);
            }
        }
    }

    public async Task<PageResult<string>> ListKeysPageAsync(
        string? @namespace,
        string? pattern,
        string? cursor,
        int limit,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var normalizedNamespace = NormalizeNamespace(@namespace);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var connection = await OpenConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ListKeysPageCommandText;
                command.Parameters.AddWithValue("$namespace", normalizedNamespace);
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$pattern", (object?)pattern ?? DBNull.Value);
                command.Parameters.AddWithValue("$cursor", (object?)cursor ?? DBNull.Value);
                command.Parameters.AddWithValue("$limit_plus_one", limit + 1);
                List<string> keys = [];
                var reader = await command.ExecuteReaderAsync(cancellationToken)
                                          .ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        keys.Add(reader.GetString(0));
                    }
                }
                return CreatePageResult(keys, limit, static key => key);
            }
        }
    }

    public async Task<PageResult<string>> ListNamespacesPageAsync(
        string? pattern,
        string? cursor,
        int limit,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var connection = await OpenConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ListNamespacesPageCommandText;
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$pattern", (object?)pattern ?? DBNull.Value);
                command.Parameters.AddWithValue("$cursor", (object?)cursor ?? DBNull.Value);
                command.Parameters.AddWithValue("$limit_plus_one", limit + 1);
                List<string> namespaces = [];
                var reader = await command.ExecuteReaderAsync(cancellationToken)
                                          .ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        namespaces.Add(reader.GetString(0));
                    }
                }
                return CreatePageResult(namespaces, limit, static currentNamespace => currentNamespace);
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, KvValue>> ListValuesAsync(
        string? @namespace,
        string? pattern,
        CancellationToken cancellationToken
    )
    {
        var normalizedNamespace = NormalizeNamespace(@namespace);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var connection = await OpenConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ListValuesCommandText;
                command.Parameters.AddWithValue("$namespace", normalizedNamespace);
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$pattern", (object?)pattern ?? DBNull.Value);
                Dictionary<string, KvValue> values = new(StringComparer.Ordinal);
                var reader = await command.ExecuteReaderAsync(cancellationToken)
                                          .ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        values[reader.GetString(0)] = await ReadKvValueAsync(reader, 1, cancellationToken)
                           .ConfigureAwait(false);
                    }
                }
                return values;
            }
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string? @namespace,
        string? pattern,
        CancellationToken cancellationToken
    )
    {
        var normalizedNamespace = NormalizeNamespace(@namespace);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var connection = await OpenConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ListKeysCommandText;
                command.Parameters.AddWithValue("$namespace", normalizedNamespace);
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$pattern", (object?)pattern ?? DBNull.Value);
                List<string> keys = [];
                var reader = await command.ExecuteReaderAsync(cancellationToken)
                                          .ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        keys.Add(reader.GetString(0));
                    }
                }
                return keys;
            }
        }
    }

    public async Task<IReadOnlyList<string>> ListNamespacesAsync(string? pattern, CancellationToken cancellationToken)
    {
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var connection = await OpenConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ListNamespacesCommandText;
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$pattern", (object?)pattern ?? DBNull.Value);
                List<string> namespaces = [];
                var reader = await command.ExecuteReaderAsync(cancellationToken)
                                          .ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        namespaces.Add(reader.GetString(0));
                    }
                }
                return namespaces;
            }
        }
    }

    private static async Task LoadBatchValuesAsync(
        SqliteCommand command,
        string @namespace,
        string now,
        string[] keys,
        Dictionary<string, KvValue> values,
        CancellationToken cancellationToken
    )
    {
        command.Parameters.Clear();
#pragma warning disable CA2100
        command.CommandText = CreateBatchGetCommandText(keys.Length);
#pragma warning restore CA2100
        command.Parameters.AddWithValue("$namespace", @namespace);
        command.Parameters.AddWithValue("$now", now);
        for (var index = 0; index < keys.Length; index++)
        {
            command.Parameters.AddWithValue($"$key{index}", keys[index]);
        }
        var reader = await command.ExecuteReaderAsync(cancellationToken)
                                  .ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                values[reader.GetString(0)] = await ReadKvValueAsync(reader, 1, cancellationToken)
                   .ConfigureAwait(false);
            }
        }
    }

    private static async Task<KvValue> ReadKvValueAsync(
        SqliteDataReader reader,
        int startOrdinal,
        CancellationToken cancellationToken
    )
    {
        return new KvValue
        {
            Value = ParseJson(reader.GetString(startOrdinal)),
            ExpiresAt = await reader.IsDBNullAsync(startOrdinal + 1, cancellationToken)
                                    .ConfigureAwait(false)
              ? null
              : reader.GetString(startOrdinal + 1),
            UpdatedAt = reader.GetString(startOrdinal + 2),
            Revision = reader.GetInt64(startOrdinal + 3)
        };
    }

    private static string CreateBatchGetCommandText(int keyCount)
    {
        return BatchGetCommandTextPrefix + CreateBatchGetParameterList(keyCount) + BatchGetCommandTextSuffix;
    }

    private static string CreateBatchGetParameterList(int keyCount)
    {
        return string.Join(
            ", ",
            Enumerable.Range(0, keyCount)
                      .Select(static index => $"$key{index}")
        );
    }
}
