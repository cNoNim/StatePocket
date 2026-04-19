using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using StatePocket.Configuration;
using StatePocket.Json.Patch;
using StatePocket.Json.Patch.Exceptions;
using StatePocket.Storage;

namespace StatePocket.Tests.Storage;

public sealed class SqliteKvStoreTests : IDisposable
{
    private readonly string _databasePath;
    private readonly SqliteKvStore _store;

    public SqliteKvStoreTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        FakeTimeProvider timeProvider = new(
            new DateTimeOffset(
                2026,
                4,
                14,
                10,
                0,
                0,
                TimeSpan.Zero
            )
        );
        ResolvedOptions resolvedOptions = new(_databasePath, ToolNames.All);
        SqliteDatabaseInitializer initializer = new(resolvedOptions);
        initializer.InitializeAsync(CancellationToken.None)
                   .GetAwaiter()
                   .GetResult();
        _store = new SqliteKvStore(resolvedOptions, timeProvider);
    }

    public void Dispose()
    {
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task SetAndGetValue_RoundTripsJsonNull()
    {
        var storedValue = ParseJson("null");
        await _store.SetValueAsync(
            "default",
            "nullable",
            storedValue,
            null,
            CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "nullable", CancellationToken.None);
        Assert.NotNull(storedEntry);
        Assert.Equal(JsonValueKind.Null, storedEntry.Value.ValueKind);
        Assert.Null(storedEntry.ExpiresAt);
    }

    [Fact]
    public async Task GetValue_HidesExpiredEntries()
    {
        var storedValue = ParseJson("{\"ok\":true}");
        await _store.SetValueAsync(
            "default",
            "ephemeral",
            storedValue,
            0,
            CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "ephemeral", CancellationToken.None);
        var keys = await _store.ListKeysAsync("default", null, CancellationToken.None);
        Assert.Null(storedEntry);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task PurgeExpired_RemovesExpiredEntries()
    {
        var storedValue = ParseJson("\"value\"");
        await _store.SetValueAsync(
            "default",
            "startup-expired",
            storedValue,
            0,
            CancellationToken.None
        );
        await _store.PurgeExpiredAsync(CancellationToken.None);
        var storedEntry = await _store.GetValueAsync("default", "startup-expired", CancellationToken.None);
        var keys = await _store.ListKeysAsync("default", null, CancellationToken.None);
        Assert.Null(storedEntry);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ListKeys_AppliesPatternAndSortsAscending()
    {
        var firstValue = ParseJson("1");
        var secondValue = ParseJson("2");
        var thirdValue = ParseJson("3");
        await _store.SetValueAsync(
            "default",
            "b-key",
            firstValue,
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "a-key",
            secondValue,
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "other",
            thirdValue,
            null,
            CancellationToken.None
        );
        var keys = await _store.ListKeysAsync("default", "*-key", CancellationToken.None);
        Assert.Equal(["a-key", "b-key"], keys);
    }

    [Fact]
    public async Task ListKeysPage_AppliesCursorAndLimit()
    {
        foreach (var key in new[]
                 {
                     "alpha", "beta", "gamma"
                 })
        {
            await _store.SetValueAsync(
                "default",
                key,
                ParseJson("\"value\""),
                null,
                CancellationToken.None
            );
        }
        var firstPage = await _store.ListKeysPageAsync(
            "default",
            null,
            null,
            2,
            CancellationToken.None
        );
        Assert.Equal(["alpha", "beta"], firstPage.Items);
        Assert.Equal("beta", firstPage.NextCursor);
        var secondPage = await _store.ListKeysPageAsync(
            "default",
            null,
            firstPage.NextCursor,
            2,
            CancellationToken.None
        );
        Assert.Equal(["gamma"], secondPage.Items);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task ListNamespaces_AppliesPatternReturnsDistinctRowsAndIgnoresExpiredValues()
    {
        await _store.SetValueAsync(
            "beta",
            "one",
            ParseJson("1"),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "alpha",
            "two",
            ParseJson("2"),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "alpha",
            "three",
            ParseJson("3"),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "archive",
            "expired",
            ParseJson("4"),
            0,
            CancellationToken.None
        );
        var namespaces = await _store.ListNamespacesAsync("*a*", CancellationToken.None);
        Assert.Equal(["alpha", "beta"], namespaces);
    }

    [Fact]
    public async Task ListNamespacesPage_AppliesCursorAndLimit()
    {
        foreach (var currentNamespace in new[]
                 {
                     "alpha", "beta", "gamma"
                 })
        {
            await _store.SetValueAsync(
                currentNamespace,
                "shared",
                ParseJson("\"value\""),
                null,
                CancellationToken.None
            );
        }
        var firstPage = await _store.ListNamespacesPageAsync(
            null,
            null,
            2,
            CancellationToken.None
        );
        Assert.Equal(["alpha", "beta"], firstPage.Items);
        Assert.Equal("beta", firstPage.NextCursor);
        var secondPage = await _store.ListNamespacesPageAsync(
            null,
            firstPage.NextCursor,
            2,
            CancellationToken.None
        );
        Assert.Equal(["gamma"], secondPage.Items);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task GetValues_ReturnsAllRequestedExistingValues()
    {
        await _store.SetValueAsync(
            "default",
            "one",
            ParseJson("\"a\""),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "two",
            ParseJson("\"b\""),
            null,
            CancellationToken.None
        );
        var values = await _store.GetValuesAsync("default", ["one", "two", "missing"], CancellationToken.None);
        Assert.Equal(
            "\"a\"",
            values["one"]
               .Value.GetRawText()
        );
        Assert.Equal(
            "\"b\"",
            values["two"]
               .Value.GetRawText()
        );
        Assert.False(values.ContainsKey("missing"));
    }

    [Fact]
    public async Task GetValues_IsCaseSensitiveForNamespaceAndKey()
    {
        await _store.SetValueAsync(
            "SkillA",
            "MixedKey",
            ParseJson("\"value\""),
            null,
            CancellationToken.None
        );
        var values = await _store.GetValuesAsync("skilla", ["mixedkey"], CancellationToken.None);
        Assert.Empty(values);
    }

    [Fact]
    public async Task ListValues_AppliesPatternAndReturnsMatchingRows()
    {
        await _store.SetValueAsync(
            "default",
            "profile.one",
            ParseJson("\"a\""),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "profile.two",
            ParseJson("\"b\""),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "other",
            ParseJson("\"c\""),
            null,
            CancellationToken.None
        );
        var values = await _store.ListValuesAsync("default", "profile.*", CancellationToken.None);
        Assert.Equal(2, values.Count);
        Assert.Equal(
            "\"a\"",
            values["profile.one"]
               .Value.GetRawText()
        );
        Assert.Equal(
            "\"b\"",
            values["profile.two"]
               .Value.GetRawText()
        );
        Assert.False(values.ContainsKey("other"));
    }

    [Fact]
    public async Task ListValuesPage_AppliesCursorAndLimit()
    {
        foreach (var key in new[]
                 {
                     "alpha", "beta", "gamma"
                 })
        {
            await _store.SetValueAsync(
                "default",
                key,
                ParseJson($"\"{key}\""),
                null,
                CancellationToken.None
            );
        }
        var firstPage = await _store.ListValuesPageAsync(
            "default",
            null,
            null,
            2,
            CancellationToken.None
        );
        Assert.Equal(["alpha", "beta"], firstPage.Items.Select(static item => item.Key));
        Assert.Equal("beta", firstPage.NextCursor);
        Assert.Equal(
            "\"alpha\"",
            firstPage.Items[0]
                     .Value.Value.GetRawText()
        );
        var secondPage = await _store.ListValuesPageAsync(
            "default",
            null,
            firstPage.NextCursor,
            2,
            CancellationToken.None
        );
        Assert.Equal(["gamma"], secondPage.Items.Select(static item => item.Key));
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task GetValue_IsCaseSensitiveForNamespaceAndKey()
    {
        var storedValue = ParseJson("{\"ok\":true}");
        await _store.SetValueAsync(
            "SkillA",
            "MixedKey",
            storedValue,
            null,
            CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("skilla", "mixedkey", CancellationToken.None);
        Assert.Null(storedEntry);
    }

    [Fact]
    public async Task SetValue_TreatsDifferentCasingAsDistinctKeys()
    {
        var firstValue = ParseJson("\"first\"");
        var secondValue = ParseJson("\"second\"");
        await _store.SetValueAsync(
            "SkillA",
            "MixedKey",
            firstValue,
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "skilla",
            "mixedkey",
            secondValue,
            null,
            CancellationToken.None
        );
        var originalValue = await _store.GetValueAsync("SkillA", "MixedKey", CancellationToken.None);
        var differentlyCasedValue = await _store.GetValueAsync("skilla", "mixedkey", CancellationToken.None);
        var originalKeys = await _store.ListKeysAsync("SkillA", null, CancellationToken.None);
        var differentlyCasedKeys = await _store.ListKeysAsync("skilla", null, CancellationToken.None);
        Assert.NotNull(originalValue);
        Assert.NotNull(differentlyCasedValue);
        Assert.Equal("\"first\"", originalValue.Value.GetRawText());
        Assert.Equal("\"second\"", differentlyCasedValue.Value.GetRawText());
        Assert.Equal(["MixedKey"], originalKeys);
        Assert.Equal(["mixedkey"], differentlyCasedKeys);
    }

    [Fact]
    public async Task ListKeys_PatternMatchingIsCaseSensitive()
    {
        var storedValue = ParseJson("1");
        await _store.SetValueAsync(
            "default",
            "MixedKey",
            storedValue,
            null,
            CancellationToken.None
        );
        var keys = await _store.ListKeysAsync("default", "*key", CancellationToken.None);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ListKeys_PatternMatchingRespectsExactCase()
    {
        var storedValue = ParseJson("1");
        await _store.SetValueAsync(
            "default",
            "MixedKey",
            storedValue,
            null,
            CancellationToken.None
        );
        var keys = await _store.ListKeysAsync("default", "*Key", CancellationToken.None);
        Assert.Equal(["MixedKey"], keys);
    }

    [Fact]
    public async Task ListNamespaces_PatternMatchingIsCaseSensitive()
    {
        await _store.SetValueAsync(
            "MixedNamespace",
            "one",
            ParseJson("1"),
            null,
            CancellationToken.None
        );
        var namespaces = await _store.ListNamespacesAsync("*namespace", CancellationToken.None);
        Assert.Empty(namespaces);
    }

    [Fact]
    public async Task ListNamespaces_PreservesDistinctNonAsciiNamespaces()
    {
        await _store.SetValueAsync(
            "Äspace",
            "one",
            ParseJson("1"),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "äspace",
            "two",
            ParseJson("2"),
            null,
            CancellationToken.None
        );
        var namespaces = await _store.ListNamespacesAsync("*space", CancellationToken.None);
        Assert.Equal(["Äspace", "äspace"], namespaces);
    }

    [Fact]
    public async Task GetValue_PreservesDistinctNonAsciiNamespaces()
    {
        await _store.SetValueAsync(
            "Ä",
            "shared",
            ParseJson("\"upper\""),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "ä",
            "shared",
            ParseJson("\"lower\""),
            null,
            CancellationToken.None
        );
        var upper = await _store.GetValueAsync("Ä", "shared", CancellationToken.None);
        var lower = await _store.GetValueAsync("ä", "shared", CancellationToken.None);
        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.Equal("\"upper\"", upper.Value.GetRawText());
        Assert.Equal("\"lower\"", lower.Value.GetRawText());
    }

    [Fact]
    public async Task GetValues_PreservesDistinctNonAsciiKeys()
    {
        await _store.SetValueAsync(
            "default",
            "Ä",
            ParseJson("\"upper\""),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "ä",
            ParseJson("\"lower\""),
            null,
            CancellationToken.None
        );
        var values = await _store.GetValuesAsync("default", ["Ä", "ä"], CancellationToken.None);
        Assert.Equal(
            "\"upper\"",
            values["Ä"]
               .Value.GetRawText()
        );
        Assert.Equal(
            "\"lower\"",
            values["ä"]
               .Value.GetRawText()
        );
    }

    [Fact]
    public async Task GetValues_BatchesLargeKeySetsWithoutHittingSqliteParameterLimit()
    {
        await _store.SetValueAsync(
            "default",
            "key-0000",
            ParseJson("\"first\""),
            null,
            CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "key-1199",
            ParseJson("\"last\""),
            null,
            CancellationToken.None
        );
        var keys = Enumerable.Range(0, 1200)
                             .Select(static index => $"key-{index:D4}")
                             .ToArray();
        var values = await _store.GetValuesAsync("default", keys, CancellationToken.None);
        Assert.Equal(2, values.Count);
        Assert.Equal(
            "\"first\"",
            values["key-0000"]
               .Value.GetRawText()
        );
        Assert.Equal(
            "\"last\"",
            values["key-1199"]
               .Value.GetRawText()
        );
    }

    [Fact]
    public async Task DeleteValue_ReturnsTrueOnlyWhenRowExisted()
    {
        var storedValue = ParseJson("\"value\"");
        await _store.SetValueAsync(
            "default",
            "to-delete",
            storedValue,
            null,
            CancellationToken.None
        );
        var deleted = await _store.DeleteValueAsync("default", "to-delete", CancellationToken.None);
        var deletedAgain = await _store.DeleteValueAsync("default", "to-delete", CancellationToken.None);
        Assert.True(deleted);
        Assert.False(deletedAgain);
    }

    [Fact]
    public async Task DeleteValue_ReturnsFalseForExpiredRow()
    {
        var storedValue = ParseJson("\"value\"");
        await _store.SetValueAsync(
            "default",
            "expired-delete",
            storedValue,
            0,
            CancellationToken.None
        );
        var deleted = await _store.DeleteValueAsync("default", "expired-delete", CancellationToken.None);
        Assert.False(deleted);
    }

    [Fact]
    public async Task SetValue_RejectsNegativeTtl()
    {
        Task ActionAsync()
        {
            return _store.SetValueAsync(
                "default",
                "bad",
                ParseJson("{}"),
                -1,
                CancellationToken.None
            );
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(ActionAsync);
    }

    [Fact]
    public async Task SetValue_RejectsUndefinedJsonElement()
    {
        Task ActionAsync()
        {
            return _store.SetValueAsync(
                "default",
                "bad",
                default,
                null,
                CancellationToken.None
            );
        }

        await Assert.ThrowsAsync<ArgumentException>(ActionAsync);
    }

    [Fact]
    public async Task SetValue_ThrowsKvStoreBusyExceptionWhenAnotherWriterHoldsTheDatabaseLock()
    {
        await using var lockHandle = await AcquireWriteLockAsync();
        var exception = await Assert.ThrowsAsync<KvStoreBusyException>(() => _store.SetValueAsync(
                "default",
                "contended",
                ParseJson("\"value\""),
                null,
                CancellationToken.None
            )
        );
        Assert.Equal("The database is busy with another write operation. Try again.", exception.Message);
        Assert.IsType<SqliteException>(exception.InnerException);
    }

    [Fact]
    public async Task PatchValue_AppliesPatchAndPreservesExpiry()
    {
        await _store.SetValueAsync(
            "default",
            "profile",
            ParseJson("{\"name\":\"old\",\"tags\":[\"a\"]}"),
            60,
            CancellationToken.None
        );
        var before = await _store.GetValueAsync("default", "profile", CancellationToken.None);
        var updated = await _store.PatchValueAsync(
            "default",
            "profile",
            Patch(
                JsonPatchOperation.Replace("/name", JsonValue.Create("new")),
                JsonPatchOperation.Add("/tags/-", JsonValue.Create("b"))
            ),
            CancellationToken.None
        );
        var after = await _store.GetValueAsync("default", "profile", CancellationToken.None);
        Assert.True(updated);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal("{\"name\":\"new\",\"tags\":[\"a\",\"b\"]}", after.Value.GetRawText());
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
    }

    [Fact]
    public async Task PatchValue_ReturnsFalseWhenKeyIsMissing()
    {
        var updated = await _store.PatchValueAsync(
            "default",
            "missing",
            Patch(JsonPatchOperation.Add("/foo", JsonValue.Create(1))),
            CancellationToken.None
        );
        Assert.False(updated);
    }

    [Fact]
    public async Task PatchValue_ReturnsFalseForExpiredRow()
    {
        await _store.SetValueAsync(
            "default",
            "expired",
            ParseJson("{\"name\":\"old\"}"),
            0,
            CancellationToken.None
        );
        var updated = await _store.PatchValueAsync(
            "default",
            "expired",
            Patch(JsonPatchOperation.Replace("/name", JsonValue.Create("new"))),
            CancellationToken.None
        );
        Assert.False(updated);
    }

    [Fact]
    public async Task PatchValue_DoesNotPersistPartialChangesWhenPatchFails()
    {
        await _store.SetValueAsync(
            "default",
            "profile",
            ParseJson("{\"name\":\"old\",\"version\":1}"),
            null,
            CancellationToken.None
        );

        Task ActionAsync()
        {
            return _store.PatchValueAsync(
                "default",
                "profile",
                Patch(
                    JsonPatchOperation.Replace("/version", JsonValue.Create(2)),
                    JsonPatchOperation.Test("/name", JsonValue.Create("other"))
                ),
                CancellationToken.None
            );
        }

        await Assert.ThrowsAsync<JsonPatchException>(ActionAsync);
        var storedEntry = await _store.GetValueAsync("default", "profile", CancellationToken.None);
        Assert.NotNull(storedEntry);
        Assert.Equal("{\"name\":\"old\",\"version\":1}", storedEntry.Value.GetRawText());
    }

    [Fact]
    public async Task PatchValue_SupportsCopyAndMoveOperations()
    {
        await _store.SetValueAsync(
            "default",
            "profile",
            ParseJson("{\"name\":\"old\",\"nested\":{\"value\":1},\"items\":[\"a\"]}"),
            null,
            CancellationToken.None
        );
        var updated = await _store.PatchValueAsync(
            "default",
            "profile",
            Patch(JsonPatchOperation.Copy("/nested", "/nestedCopy"), JsonPatchOperation.Move("/name", "/displayName")),
            CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "profile", CancellationToken.None);
        Assert.True(updated);
        Assert.NotNull(storedEntry);
        Assert.Equal(
            "{\"nested\":{\"value\":1},\"items\":[\"a\"],\"nestedCopy\":{\"value\":1},\"displayName\":\"old\"}",
            storedEntry.Value.GetRawText()
        );
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonPatch Patch(params JsonPatchOperation[] operations)
    {
        return new JsonPatch(operations);
    }

    private async Task<WriteLockHandle> AcquireWriteLockAsync()
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString()
        );
        await connection.OpenAsync(CancellationToken.None);
        var transaction = connection.BeginTransaction(false);
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO kv(namespace, key, value, expires_at, updated_at)
                                  VALUES ($namespace, $key, $value, NULL, $updated_at)
                                  ON CONFLICT(namespace, key) DO UPDATE SET
                                      updated_at = excluded.updated_at;
                                  """;
            command.Parameters.AddWithValue("$namespace", "default");
            command.Parameters.AddWithValue("$key", "lock-holder");
            command.Parameters.AddWithValue("$value", "\"lock\"");
            command.Parameters.AddWithValue("$updated_at", "2026-04-14T10:00:00.0000000Z");
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        return new WriteLockHandle(connection, transaction);
    }

    private sealed class WriteLockHandle(SqliteConnection connection, SqliteTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
