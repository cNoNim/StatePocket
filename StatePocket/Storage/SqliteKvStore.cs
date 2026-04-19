using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using StatePocket.Configuration;
using StatePocket.JsonPatch;

namespace StatePocket.Storage;

internal sealed class SqliteKvStore(ResolvedOptions resolvedOptions, TimeProvider timeProvider) : IKvStore
{
    private const string DefaultNamespace = "default";
    private const string BusyMessage = "The database is busy with another write operation. Try again.";
    private const int SqliteLegacyParameterLimit = 999;
    private const int BatchGetReservedParameterCount = 2;
    private const int MaxBatchGetKeys = SqliteLegacyParameterLimit - BatchGetReservedParameterCount;
    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;
    private const int SqliteBusyTimeoutSeconds = 1;

    public Task SetValueAsync(
        string? @namespace,
        string key,
        JsonElement value,
        long? ttlSeconds,
        CancellationToken cancellationToken
    )
    {
        if (ttlSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttlSeconds),
                "ttl_seconds must be greater than or equal to 0."
            );
        }
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("value is required.", nameof(value));
        }
        var now = timeProvider.GetUtcNow();
        var formattedNow = FormatTimestamp(now);
        var expiresAt = ttlSeconds is not null ? FormatTimestamp(now.AddSeconds(ttlSeconds.Value)) : null;
        return ExecuteWriteAsync(async () =>
            {
                var connection = await OpenWriteConnectionAsync(cancellationToken)
                   .ConfigureAwait(false);
                await using (connection.ConfigureAwait(false))
                {
                    var command = connection.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = """
                                              INSERT INTO kv(namespace, key, value, expires_at, updated_at)
                                              VALUES ($namespace, $key, $value, $expires_at, $updated_at)
                                              ON CONFLICT(namespace, key) DO UPDATE SET
                                                  value = excluded.value,
                                                  expires_at = excluded.expires_at,
                                                  updated_at = excluded.updated_at;
                                              """;
                        command.Parameters.AddWithValue("$namespace", NormalizeNamespace(@namespace));
                        command.Parameters.AddWithValue("$key", key);
                        command.Parameters.AddWithValue("$value", value.GetRawText());
                        command.Parameters.AddWithValue("$expires_at", (object?)expiresAt ?? DBNull.Value);
                        command.Parameters.AddWithValue("$updated_at", formattedNow);
                        await command.ExecuteNonQueryAsync(cancellationToken)
                                     .ConfigureAwait(false);
                    }
                }
            }
        );
    }

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
                command.CommandText = """
                                      SELECT value, expires_at
                                      FROM kv
                                      WHERE namespace = $namespace
                                        AND key = $key
                                        AND (expires_at IS NULL OR expires_at > $now);
                                      """;
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
                    return new KvValue
                    {
                        Value = ParseJson(reader.GetString(0)),
                        ExpiresAt = await reader.IsDBNullAsync(1, cancellationToken)
                                                .ConfigureAwait(false)
                          ? null
                          : reader.GetString(1)
                    };
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
                command.CommandText = """
                                      SELECT key, value, expires_at
                                      FROM kv
                                      WHERE namespace = $namespace
                                        AND (expires_at IS NULL OR expires_at > $now)
                                        AND ($pattern IS NULL OR key GLOB $pattern)
                                        AND ($cursor IS NULL OR key > $cursor)
                                      ORDER BY key ASC
                                      LIMIT $limit_plus_one;
                                      """;
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
                                new KvValue
                                {
                                    Value = ParseJson(reader.GetString(1)),
                                    ExpiresAt = await reader.IsDBNullAsync(2, cancellationToken)
                                                            .ConfigureAwait(false)
                                      ? null
                                      : reader.GetString(2)
                                }
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
                command.CommandText = """
                                      SELECT key
                                      FROM kv
                                      WHERE namespace = $namespace
                                        AND (expires_at IS NULL OR expires_at > $now)
                                        AND ($pattern IS NULL OR key GLOB $pattern)
                                        AND ($cursor IS NULL OR key > $cursor)
                                      ORDER BY key ASC
                                      LIMIT $limit_plus_one;
                                      """;
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
                command.CommandText = """
                                      SELECT DISTINCT namespace
                                      FROM kv
                                      WHERE (expires_at IS NULL OR expires_at > $now)
                                        AND ($pattern IS NULL OR namespace GLOB $pattern)
                                        AND ($cursor IS NULL OR namespace > $cursor)
                                      ORDER BY namespace ASC
                                      LIMIT $limit_plus_one;
                                      """;
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

    public Task<bool> PatchValueAsync(
        string? @namespace,
        string key,
        PatchDocument patch,
        CancellationToken cancellationToken
    )
    {
        var normalizedNamespace = NormalizeNamespace(@namespace);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        var updatedAt = FormatTimestamp(timeProvider.GetUtcNow());
        return ExecuteWriteAsync(() => ExecutePatchValueCoreAsync(
                normalizedNamespace,
                key,
                patch,
                updatedAt,
                now,
                cancellationToken
            )
        );
    }

    public Task<bool> DeleteValueAsync(string? @namespace, string key, CancellationToken cancellationToken)
    {
        var normalizedNamespace = NormalizeNamespace(@namespace);
        var now = FormatTimestamp(timeProvider.GetUtcNow());
        return ExecuteWriteAsync(async () =>
            {
                var connection = await OpenWriteConnectionAsync(cancellationToken)
                   .ConfigureAwait(false);
                await using (connection.ConfigureAwait(false))
                {
                    var command = connection.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = """
                                              DELETE FROM kv
                                              WHERE namespace = $namespace
                                                AND key = $key
                                                AND (expires_at IS NULL OR expires_at > $now);
                                              """;
                        command.Parameters.AddWithValue("$namespace", normalizedNamespace);
                        command.Parameters.AddWithValue("$key", key);
                        command.Parameters.AddWithValue("$now", now);
                        return await command.ExecuteNonQueryAsync(cancellationToken)
                                            .ConfigureAwait(false)
                             > 0;
                    }
                }
            }
        );
    }

    public Task PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(async () =>
            {
                var connection = await OpenWriteConnectionAsync(cancellationToken)
                   .ConfigureAwait(false);
                await using (connection.ConfigureAwait(false))
                {
                    var command = connection.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = """
                                              DELETE FROM kv
                                              WHERE expires_at IS NOT NULL
                                                AND expires_at <= $now;
                                              """;
                        command.Parameters.AddWithValue("$now", FormatTimestamp(timeProvider.GetUtcNow()));
                        await command.ExecuteNonQueryAsync(cancellationToken)
                                     .ConfigureAwait(false);
                    }
                }
            }
        );
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
                command.CommandText = """
                                      SELECT key, value, expires_at
                                      FROM kv
                                      WHERE namespace = $namespace
                                        AND (expires_at IS NULL OR expires_at > $now)
                                        AND ($pattern IS NULL OR key GLOB $pattern)
                                      ORDER BY key ASC;
                                      """;
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
                        values[reader.GetString(0)] = new KvValue
                        {
                            Value = ParseJson(reader.GetString(1)),
                            ExpiresAt = await reader.IsDBNullAsync(2, cancellationToken)
                                                    .ConfigureAwait(false)
                              ? null
                              : reader.GetString(2)
                        };
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
                command.CommandText = """
                                      SELECT key
                                      FROM kv
                                      WHERE namespace = $namespace
                                        AND (expires_at IS NULL OR expires_at > $now)
                                        AND ($pattern IS NULL OR key GLOB $pattern)
                                      ORDER BY key ASC;
                                      """;
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
                command.CommandText = """
                                      SELECT DISTINCT namespace
                                      FROM kv
                                      WHERE (expires_at IS NULL OR expires_at > $now)
                                        AND ($pattern IS NULL OR namespace GLOB $pattern)
                                      ORDER BY namespace ASC;
                                      """;
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

    private Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        return OpenConnectionCoreAsync(CreateConnection(null), cancellationToken);
    }

    private Task<SqliteConnection> OpenWriteConnectionAsync(CancellationToken cancellationToken)
    {
        return OpenConnectionCoreAsync(CreateConnection(SqliteBusyTimeoutSeconds), cancellationToken);
    }

    private SqliteConnection CreateConnection(int? defaultTimeoutSeconds)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = resolvedOptions.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        if (defaultTimeoutSeconds is not null)
        {
            builder.DefaultTimeout = defaultTimeoutSeconds.Value;
        }
        return new SqliteConnection(builder.ToString());
    }

    private static async Task<SqliteConnection> OpenConnectionCoreAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await connection.OpenAsync(cancellationToken)
                        .ConfigureAwait(false);
        return connection;
    }

    private static PageResult<T> CreatePageResult<T>(List<T> items, int limit, Func<T, string> cursorSelector)
    {
        if (items.Count <= limit)
        {
            return new PageResult<T>
            {
                Items = items,
                NextCursor = null
            };
        }
        var nextCursor = cursorSelector(items[limit - 1]);
        items.RemoveAt(items.Count - 1);
        return new PageResult<T>
        {
            Items = items,
            NextCursor = nextCursor
        };
    }

    private static async Task ExecuteWriteAsync(Func<Task> action)
    {
        try
        {
            await action()
               .ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsWriteContention(exception))
        {
            throw new KvStoreBusyException(BusyMessage, exception);
        }
    }

    private static async Task<T> ExecuteWriteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action()
               .ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsWriteContention(exception))
        {
            throw new KvStoreBusyException(BusyMessage, exception);
        }
    }

    private static string NormalizeNamespace(string? @namespace)
    {
        return @namespace ?? DefaultNamespace;
    }

    private static bool IsWriteContention(SqliteException exception)
    {
        return exception.SqliteErrorCode is SqliteBusyErrorCode or SqliteLockedErrorCode;
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
                values[reader.GetString(0)] = new KvValue
                {
                    Value = ParseJson(reader.GetString(1)),
                    ExpiresAt = await reader.IsDBNullAsync(2, cancellationToken)
                                            .ConfigureAwait(false)
                      ? null
                      : reader.GetString(2)
                };
            }
        }
    }

    private async Task<bool> ExecutePatchValueCoreAsync(
        string @namespace,
        string key,
        PatchDocument patchDocument,
        string updatedAt,
        string now,
        CancellationToken cancellationToken
    )
    {
        var connection = await OpenWriteConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // patch_value is read-modify-write; take the write lock up front to avoid
            // deferred-transaction snapshot upgrades failing under concurrent writers.
            var transaction = connection.BeginTransaction(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    var rawJson = await LoadCurrentValueAsync(
                            connection,
                            transaction,
                            @namespace,
                            key,
                            now,
                            cancellationToken
                        )
                       .ConfigureAwait(false);
                    if (rawJson is null)
                    {
                        await transaction.CommitAsync(cancellationToken)
                                         .ConfigureAwait(false);
                        return false;
                    }
                    var updatedValue = patchDocument.Apply(ParseNode(rawJson));
                    var updated = await PersistUpdatedValueAsync(
                            connection,
                            transaction,
                            @namespace,
                            key,
                            updatedValue,
                            updatedAt,
                            now,
                            cancellationToken
                        )
                       .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken)
                                     .ConfigureAwait(false);
                    return updated;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken)
                                     .ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    private static string CreateBatchGetCommandText(int keyCount)
    {
        return $"""
                SELECT key, value, expires_at
                FROM kv
                WHERE namespace = $namespace
                  AND key IN ({string.Join(", ", Enumerable.Range(0, keyCount).Select(static index => $"$key{index}"))})
                  AND (expires_at IS NULL OR expires_at > $now);
                """;
    }

    private static JsonElement ParseJson(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return document.RootElement.Clone();
    }

    private static JsonNode? ParseNode(string rawJson)
    {
        return JsonNode.Parse(rawJson);
    }

    private static async Task<string?> LoadCurrentValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        string key,
        string now,
        CancellationToken cancellationToken
    )
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  SELECT value
                                  FROM kv
                                  WHERE namespace = $namespace
                                    AND key = $key
                                    AND (expires_at IS NULL OR expires_at > $now);
                                  """;
            command.Parameters.AddWithValue("$namespace", @namespace);
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
                return reader.GetString(0);
            }
        }
    }

    private static async Task<bool> PersistUpdatedValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        string key,
        JsonNode? updatedValue,
        string updatedAt,
        string now,
        CancellationToken cancellationToken
    )
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  UPDATE kv
                                  SET value = $value,
                                      updated_at = $updated_at
                                  WHERE namespace = $namespace
                                    AND key = $key
                                    AND (expires_at IS NULL OR expires_at > $now);
                                  """;
            command.Parameters.AddWithValue("$value", updatedValue?.ToJsonString() ?? "null");
            command.Parameters.AddWithValue("$updated_at", updatedAt);
            command.Parameters.AddWithValue("$namespace", @namespace);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$now", now);
            return await command.ExecuteNonQueryAsync(cancellationToken)
                                .ConfigureAwait(false)
                 > 0;
        }
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }
}
