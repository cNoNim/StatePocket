using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using StatePocket.Configuration;
using StatePocket.Json.Patch;

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
    private const string NamespaceRevisionTableName = "kv_namespace_revision";

    public Task<SetValueMetadata> SetValueAsync(
        string? @namespace,
        string key,
        JsonElement value,
        long? ttlSeconds,
        long? expectedRevision = null,
        bool ifAbsent = false,
        CancellationToken cancellationToken = default
    )
    {
        if (ttlSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ttlSeconds), "ttlSeconds must be greater than or equal to 0.");
        }
        if (expectedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedRevision),
                "expectedRevision must be greater than or equal to 0."
            );
        }
        if (ifAbsent && expectedRevision is not null)
        {
            throw new ArgumentException("ifAbsent cannot be combined with expectedRevision.", nameof(ifAbsent));
        }
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("value is required.", nameof(value));
        }
        var normalizedNamespace = NormalizeNamespace(@namespace);
        return ExecuteWriteAsync(() => ExecuteSetValueCoreAsync(
                normalizedNamespace,
                key,
                value.GetRawText(),
                ttlSeconds,
                expectedRevision,
                ifAbsent,
                cancellationToken
            )
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
                                      SELECT value, expires_at, updated_at, revision
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
                          : reader.GetString(1),
                        UpdatedAt = reader.GetString(2),
                        Revision = reader.GetInt64(3)
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
                                      SELECT key, value, expires_at, updated_at, revision
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
                                      : reader.GetString(2),
                                    UpdatedAt = reader.GetString(3),
                                    Revision = reader.GetInt64(4)
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

    public Task<KvValue?> PatchValueAsync(
        string? @namespace,
        string key,
        JsonPatch patch,
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
                                      SELECT key, value, expires_at, updated_at, revision
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
                              : reader.GetString(2),
                            UpdatedAt = reader.GetString(3),
                            Revision = reader.GetInt64(4)
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
                      : reader.GetString(2),
                    UpdatedAt = reader.GetString(3),
                    Revision = reader.GetInt64(4)
                };
            }
        }
    }

    private async Task<SetValueMetadata> ExecuteSetValueCoreAsync(
        string @namespace,
        string key,
        string rawJson,
        long? ttlSeconds,
        long? expectedRevision,
        bool ifAbsent,
        CancellationToken cancellationToken
    )
    {
        var connection = await OpenWriteConnectionAsync(cancellationToken)
           .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = connection.BeginTransaction(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    var now = timeProvider.GetUtcNow();
                    var updatedAt = FormatTimestamp(now);
                    var expiresAt = ttlSeconds is not null ? FormatTimestamp(now.AddSeconds(ttlSeconds.Value)) : null;
                    var storedValue = await WriteSetValueAsync(
                            connection,
                            transaction,
                            @namespace,
                            key,
                            rawJson,
                            expiresAt,
                            updatedAt,
                            expectedRevision,
                            ifAbsent,
                            cancellationToken
                        )
                       .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken)
                                     .ConfigureAwait(false);
                    return storedValue;
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

    private static async Task<SetValueMetadata> WriteSetValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        string key,
        string rawJson,
        string? expiresAt,
        string updatedAt,
        long? expectedRevision,
        bool ifAbsent,
        CancellationToken cancellationToken
    )
    {
        var currentEntry = await LoadStoredMetadataAsync(
                connection,
                transaction,
                @namespace,
                key,
                cancellationToken
            )
           .ConfigureAwait(false);
        ValidateSetRevision(
            currentEntry,
            key,
            @namespace,
            updatedAt,
            expectedRevision,
            ifAbsent
        );
        var nextRevision = await AllocateNextRevisionAsync(
                connection,
                transaction,
                @namespace,
                cancellationToken
            )
           .ConfigureAwait(false);
        await PersistSetValueAsync(
                connection,
                transaction,
                currentEntry is null,
                @namespace,
                key,
                rawJson,
                expiresAt,
                updatedAt,
                nextRevision,
                cancellationToken
            )
           .ConfigureAwait(false);
        return new SetValueMetadata
        {
            ExpiresAt = expiresAt,
            UpdatedAt = updatedAt,
            Revision = nextRevision
        };
    }

    private static Task PersistSetValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        bool insert,
        string @namespace,
        string key,
        string rawJson,
        string? expiresAt,
        string updatedAt,
        long revision,
        CancellationToken cancellationToken
    )
    {
        return insert
          ? InsertStoredValueAsync(
                connection,
                transaction,
                @namespace,
                key,
                rawJson,
                expiresAt,
                updatedAt,
                revision,
                cancellationToken
            )
          : UpdateStoredValueAsync(
                connection,
                transaction,
                @namespace,
                key,
                rawJson,
                expiresAt,
                updatedAt,
                revision,
                cancellationToken
            );
    }

    private static void ValidateSetRevision(
        (string? ExpiresAt, string UpdatedAt, long Revision)? currentEntry,
        string key,
        string @namespace,
        string now,
        long? expectedRevision,
        bool ifAbsent
    )
    {
        if (currentEntry is
                {} liveEntry
         && IsLiveAt(liveEntry.ExpiresAt, now))
        {
            if (ifAbsent)
            {
                throw new KvStoreConflictException(
                    $"Key '{key}' already exists in namespace '{@namespace}'.",
                    liveEntry.Revision
                );
            }
            if (expectedRevision is not null
             && liveEntry.Revision != expectedRevision.Value)
            {
                throw new KvStoreConflictException(
                    $"Revision conflict for key '{key}' in namespace '{@namespace}'. Expected revision {expectedRevision.Value}, found {liveEntry.Revision}.",
                    liveEntry.Revision
                );
            }
            return;
        }
        if (expectedRevision is not null)
        {
            throw new KvStoreConflictException(
                $"Revision conflict for key '{key}' in namespace '{@namespace}'. Expected revision {expectedRevision.Value}, but the key does not exist."
            );
        }
    }

    private async Task<KvValue?> ExecutePatchValueCoreAsync(
        string @namespace,
        string key,
        JsonPatch patchDocument,
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
                    var updated = await WritePatchedValueAsync(
                            connection,
                            transaction,
                            @namespace,
                            key,
                            patchDocument,
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

    private static async Task<KvValue?> WritePatchedValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        string key,
        JsonPatch patchDocument,
        string updatedAt,
        string now,
        CancellationToken cancellationToken
    )
    {
        var currentEntry = await LoadCurrentValueAsync(
                connection,
                transaction,
                @namespace,
                key,
                now,
                cancellationToken
            )
           .ConfigureAwait(false);
        if (currentEntry is null)
        {
            return null;
        }
        var updatedValue = patchDocument.Apply(ParseNode(currentEntry.Value.RawJson));
        var updatedRawJson = updatedValue?.ToJsonString() ?? "null";
        var nextRevision = await AllocateNextRevisionAsync(
                connection,
                transaction,
                @namespace,
                cancellationToken
            )
           .ConfigureAwait(false);
        var updated = await PersistUpdatedValueAsync(
                connection,
                transaction,
                @namespace,
                key,
                updatedRawJson,
                updatedAt,
                nextRevision,
                now,
                cancellationToken
            )
           .ConfigureAwait(false);
        return !updated
          ? null
          : new KvValue
            {
                Value = ParseJson(updatedRawJson),
                ExpiresAt = currentEntry.Value.ExpiresAt,
                UpdatedAt = updatedAt,
                Revision = nextRevision
            };
    }

    private static string CreateBatchGetCommandText(int keyCount)
    {
        return $"""
                SELECT key, value, expires_at, updated_at, revision
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

    private static bool IsLiveAt(string? expiresAt, string now)
    {
        return expiresAt is null || string.CompareOrdinal(expiresAt, now) > 0;
    }

    private static async Task<(string RawJson, string? ExpiresAt, string UpdatedAt, long Revision)?>
        LoadCurrentValueAsync(
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
                                  SELECT value, expires_at, updated_at, revision
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
                return (reader.GetString(0), await reader.IsDBNullAsync(1, cancellationToken)
                                                         .ConfigureAwait(false)
                          ? null
                          : reader.GetString(1), reader.GetString(2), reader.GetInt64(3));
            }
        }
    }

    private static async Task<(string? ExpiresAt, string UpdatedAt, long Revision)?> LoadStoredMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        string key,
        CancellationToken cancellationToken
    )
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  SELECT expires_at, updated_at, revision
                                  FROM kv
                                  WHERE namespace = $namespace
                                    AND key = $key;
                                  """;
            command.Parameters.AddWithValue("$namespace", @namespace);
            command.Parameters.AddWithValue("$key", key);
            var reader = await command.ExecuteReaderAsync(cancellationToken)
                                      .ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken)
                                 .ConfigureAwait(false))
                {
                    return null;
                }
                return (await reader.IsDBNullAsync(0, cancellationToken)
                                    .ConfigureAwait(false)
                          ? null
                          : reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
            }
        }
    }

    private static async Task<long> AllocateNextRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        CancellationToken cancellationToken
    )
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                                   INSERT INTO {NamespaceRevisionTableName}(namespace, last_revision)
                                   VALUES ($namespace, 1)
                                   ON CONFLICT(namespace) DO UPDATE SET
                                       last_revision = last_revision + 1
                                   RETURNING last_revision;
                                   """;
            command.Parameters.AddWithValue("$namespace", @namespace);
            var result = await command.ExecuteScalarAsync(cancellationToken)
                                      .ConfigureAwait(false);
            return result is null
              ? throw new InvalidOperationException("Expected namespace revision clock to return a value.")
              : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
    }

    private static async Task InsertStoredValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        string key,
        string rawJson,
        string? expiresAt,
        string updatedAt,
        long revision,
        CancellationToken cancellationToken
    )
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO kv(namespace, key, value, expires_at, updated_at, revision)
                                  VALUES ($namespace, $key, $value, $expires_at, $updated_at, $revision);
                                  """;
            command.Parameters.AddWithValue("$namespace", @namespace);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", rawJson);
            command.Parameters.AddWithValue("$expires_at", (object?)expiresAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_at", updatedAt);
            command.Parameters.AddWithValue("$revision", revision);
            await command.ExecuteNonQueryAsync(cancellationToken)
                         .ConfigureAwait(false);
        }
    }

    private static async Task UpdateStoredValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        string key,
        string rawJson,
        string? expiresAt,
        string updatedAt,
        long revision,
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
                                      expires_at = $expires_at,
                                      updated_at = $updated_at,
                                      revision = $revision
                                  WHERE namespace = $namespace
                                    AND key = $key;
                                  """;
            command.Parameters.AddWithValue("$value", rawJson);
            command.Parameters.AddWithValue("$expires_at", (object?)expiresAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_at", updatedAt);
            command.Parameters.AddWithValue("$revision", revision);
            command.Parameters.AddWithValue("$namespace", @namespace);
            command.Parameters.AddWithValue("$key", key);
            await command.ExecuteNonQueryAsync(cancellationToken)
                         .ConfigureAwait(false);
        }
    }

    private static async Task<bool> PersistUpdatedValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string @namespace,
        string key,
        string updatedRawJson,
        string updatedAt,
        long revision,
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
                                      updated_at = $updated_at,
                                      revision = $revision
                                  WHERE namespace = $namespace
                                    AND key = $key
                                    AND (expires_at IS NULL OR expires_at > $now);
                                  """;
            command.Parameters.AddWithValue("$value", updatedRawJson);
            command.Parameters.AddWithValue("$updated_at", updatedAt);
            command.Parameters.AddWithValue("$revision", revision);
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
