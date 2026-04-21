using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using StatePocket.Errors;
using StatePocket.Json.Patch;

namespace StatePocket.Storage;

internal sealed partial class SqliteKvStore
{
    private const string DeleteValueCommandText = """
                                                  DELETE FROM kv
                                                  WHERE namespace = $namespace
                                                    AND key = $key
                                                    AND (expires_at IS NULL OR expires_at > $now);
                                                  """;
    private const string PurgeExpiredCommandText = """
                                                   DELETE FROM kv
                                                   WHERE expires_at IS NOT NULL
                                                     AND expires_at <= $now;
                                                   """;
    private const string LoadCurrentValueCommandText = """
                                                       SELECT value, expires_at, updated_at, revision
                                                       FROM kv
                                                       WHERE namespace = $namespace
                                                         AND key = $key
                                                         AND (expires_at IS NULL OR expires_at > $now);
                                                       """;
    private const string LoadStoredMetadataCommandText = """
                                                         SELECT expires_at, updated_at, revision
                                                         FROM kv
                                                         WHERE namespace = $namespace
                                                           AND key = $key;
                                                         """;
    private const string AllocateNextRevisionCommandText = """
                                                           INSERT INTO kv_namespace_revision(namespace, last_revision)
                                                           VALUES ($namespace, 1)
                                                           ON CONFLICT(namespace) DO UPDATE SET
                                                               last_revision = last_revision + 1
                                                           RETURNING last_revision;
                                                           """;
    private const string InsertStoredValueCommandText = """
                                                        INSERT INTO kv(namespace, key, value, expires_at, updated_at, revision)
                                                        VALUES ($namespace, $key, $value, $expires_at, $updated_at, $revision);
                                                        """;
    private const string UpdateStoredValueCommandText = """
                                                        UPDATE kv
                                                        SET value = $value,
                                                            expires_at = $expires_at,
                                                            updated_at = $updated_at,
                                                            revision = $revision
                                                        WHERE namespace = $namespace
                                                          AND key = $key;
                                                        """;
    private const string PersistUpdatedValueCommandText = """
                                                          UPDATE kv
                                                          SET value = $value,
                                                              updated_at = $updated_at,
                                                              revision = $revision
                                                          WHERE namespace = $namespace
                                                            AND key = $key
                                                            AND (expires_at IS NULL OR expires_at > $now);
                                                          """;

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
            throw new ToolValidationException(
                "ttlSeconds must be greater than or equal to 0. (Parameter 'ttlSeconds')",
                nameof(ttlSeconds)
            );
        }
        if (expectedRevision < 0)
        {
            throw new ToolValidationException(
                "expectedRevision must be greater than or equal to 0. (Parameter 'expectedRevision')",
                nameof(expectedRevision)
            );
        }
        if (ifAbsent && expectedRevision is not null)
        {
            throw new ToolValidationException(
                "ifAbsent cannot be combined with expectedRevision. (Parameter 'ifAbsent')",
                nameof(ifAbsent)
            );
        }
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ToolValidationException("value is required. (Parameter 'value')", nameof(value));
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
                        command.CommandText = DeleteValueCommandText;
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
                        command.CommandText = PurgeExpiredCommandText;
                        command.Parameters.AddWithValue("$now", FormatTimestamp(timeProvider.GetUtcNow()));
                        await command.ExecuteNonQueryAsync(cancellationToken)
                                     .ConfigureAwait(false);
                    }
                }
            }
        );
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
        JsonNode? updatedValue;
        try
        {
            updatedValue = patchDocument.Apply(ParseNode(currentEntry.Value.RawJson));
        }
        catch (JsonPatchException exception)
        {
            throw new ToolInvalidPatchException(exception.Message, innerException: exception);
        }
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
            command.CommandText = LoadCurrentValueCommandText;
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
            command.CommandText = LoadStoredMetadataCommandText;
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
            command.CommandText = AllocateNextRevisionCommandText;
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
            command.CommandText = InsertStoredValueCommandText;
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
            command.CommandText = UpdateStoredValueCommandText;
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
            command.CommandText = PersistUpdatedValueCommandText;
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
}
