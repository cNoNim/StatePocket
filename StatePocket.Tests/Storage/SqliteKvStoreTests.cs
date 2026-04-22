using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using StatePocket.Configuration;
using StatePocket.Exceptions;
using StatePocket.Json.Patch;
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
        var writeResult = await _store.SetValueAsync(
            "default",
            "nullable",
            storedValue,
            null,
            cancellationToken: CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "nullable", CancellationToken.None);
        Assert.Null(writeResult.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", writeResult.UpdatedAt);
        Assert.Equal(1, writeResult.Revision);
        Assert.NotNull(storedEntry);
        Assert.Equal(JsonValueKind.Null, storedEntry.Value.ValueKind);
        Assert.Null(storedEntry.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", storedEntry.UpdatedAt);
        Assert.Equal(1, storedEntry.Revision);
    }

    [Fact]
    public async Task SetValue_ReturnsExpiryMetadataWhenTtlIsSet()
    {
        var writeResult = await _store.SetValueAsync(
            "default",
            "ephemeral",
            ParseJson("{\"ok\":true}"),
            60,
            cancellationToken: CancellationToken.None
        );
        Assert.Equal("2026-04-14T10:01:00.0000000Z", writeResult.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", writeResult.UpdatedAt);
        Assert.Equal(1, writeResult.Revision);
    }

    [Fact]
    public async Task SetValue_OverwriteIncrementsRevision()
    {
        var firstWrite = await _store.SetValueAsync(
            "default",
            "versioned",
            ParseJson("\"first\""),
            null,
            cancellationToken: CancellationToken.None
        );
        var secondWrite = await _store.SetValueAsync(
            "default",
            "versioned",
            ParseJson("\"second\""),
            null,
            cancellationToken: CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "versioned", CancellationToken.None);
        Assert.Equal(1, firstWrite.Revision);
        Assert.Equal(2, secondWrite.Revision);
        Assert.NotNull(storedEntry);
        Assert.Equal(2, storedEntry.Revision);
        Assert.Equal("\"second\"", storedEntry.Value.GetRawText());
    }

    [Fact]
    public async Task SetValue_IfAbsentThrowsConflictWhenLiveKeyExists()
    {
        await _store.SetValueAsync(
            "default",
            "claimed",
            ParseJson("\"first\""),
            null,
            cancellationToken: CancellationToken.None
        );
        var exception = await Assert.ThrowsAsync<KvStoreConflictException>(() => _store.SetValueAsync(
                "default",
                "claimed",
                ParseJson("\"second\""),
                null,
                ifAbsent: true,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal("Key 'claimed' already exists in namespace 'default'.", exception.Message);
        Assert.Equal(1, exception.CurrentRevision);
    }

    [Fact]
    public async Task SetValue_IfAbsentTreatsExpiredRowAsAbsent()
    {
        await _store.SetValueAsync(
            "default",
            "ephemeral-claim",
            ParseJson("\"expired\""),
            0,
            cancellationToken: CancellationToken.None
        );
        var writeResult = await _store.SetValueAsync(
            "default",
            "ephemeral-claim",
            ParseJson("\"fresh\""),
            null,
            ifAbsent: true,
            cancellationToken: CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "ephemeral-claim", CancellationToken.None);
        Assert.Equal(2, writeResult.Revision);
        Assert.NotNull(storedEntry);
        Assert.Equal(2, storedEntry.Revision);
        Assert.Equal("\"fresh\"", storedEntry.Value.GetRawText());
    }

    [Fact]
    public async Task SetValue_ExpectedRevisionUpdatesWhenRevisionMatches()
    {
        var initialWrite = await _store.SetValueAsync(
            "default",
            "cas-key",
            ParseJson("\"first\""),
            null,
            cancellationToken: CancellationToken.None
        );
        var writeResult = await _store.SetValueAsync(
            "default",
            "cas-key",
            ParseJson("\"second\""),
            null,
            initialWrite.Revision,
            cancellationToken: CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "cas-key", CancellationToken.None);
        Assert.Equal(2, writeResult.Revision);
        Assert.NotNull(storedEntry);
        Assert.Equal(2, storedEntry.Revision);
        Assert.Equal("\"second\"", storedEntry.Value.GetRawText());
    }

    [Fact]
    public async Task SetValue_ExpectedRevisionTreatsExpiredRowAsMissing()
    {
        await _store.SetValueAsync(
            "default",
            "expired-cas",
            ParseJson("\"first\""),
            0,
            cancellationToken: CancellationToken.None
        );
        var exception = await Assert.ThrowsAsync<KvStoreConflictException>(() => _store.SetValueAsync(
                "default",
                "expired-cas",
                ParseJson("\"fresh\""),
                null,
                1,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal(
            "Revision conflict for key 'expired-cas' in namespace 'default'. Expected revision 1, but the key does not exist.",
            exception.Message
        );
        Assert.Null(exception.CurrentRevision);
    }

    [Fact]
    public async Task SetValue_ExpectedRevisionThrowsConflictWhenRevisionDiffers()
    {
        await _store.SetValueAsync(
            "default",
            "cas-conflict",
            ParseJson("\"first\""),
            null,
            cancellationToken: CancellationToken.None
        );
        var exception = await Assert.ThrowsAsync<KvStoreConflictException>(() => _store.SetValueAsync(
                "default",
                "cas-conflict",
                ParseJson("\"second\""),
                null,
                99,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal(
            "Revision conflict for key 'cas-conflict' in namespace 'default'. Expected revision 99, found 1.",
            exception.Message
        );
        Assert.Equal(1, exception.CurrentRevision);
    }

    [Fact]
    public async Task SetValue_DeleteAndRecreateAllocatesNewRevision()
    {
        var firstWrite = await _store.SetValueAsync(
            "default",
            "recreated",
            ParseJson("\"first\""),
            null,
            cancellationToken: CancellationToken.None
        );
        var deleted = await _store.DeleteValueAsync("default", "recreated", CancellationToken.None);
        var recreatedWrite = await _store.SetValueAsync(
            "default",
            "recreated",
            ParseJson("\"second\""),
            null,
            cancellationToken: CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "recreated", CancellationToken.None);
        var deletedEntry = Assert.IsType<KvValue>(deleted);
        Assert.Equal("\"first\"", deletedEntry.Value.GetRawText());
        Assert.Equal(1, firstWrite.Revision);
        Assert.Equal(2, recreatedWrite.Revision);
        var entry = Assert.IsType<KvValue>(storedEntry);
        Assert.Equal(2, entry.Revision);
    }

    [Fact]
    public async Task SetValue_StaleExpectedRevisionFailsAfterDeleteAndRecreate()
    {
        var firstWrite = await _store.SetValueAsync(
            "default",
            "recreated-cas",
            ParseJson("\"first\""),
            null,
            cancellationToken: CancellationToken.None
        );
        await _store.DeleteValueAsync("default", "recreated-cas", CancellationToken.None);
        var recreatedWrite = await _store.SetValueAsync(
            "default",
            "recreated-cas",
            ParseJson("\"second\""),
            null,
            cancellationToken: CancellationToken.None
        );
        var exception = await Assert.ThrowsAsync<KvStoreConflictException>(() => _store.SetValueAsync(
                "default",
                "recreated-cas",
                ParseJson("\"stale\""),
                null,
                firstWrite.Revision,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal(2, recreatedWrite.Revision);
        Assert.Equal(
            "Revision conflict for key 'recreated-cas' in namespace 'default'. Expected revision 1, found 2.",
            exception.Message
        );
        Assert.Equal(2, exception.CurrentRevision);
    }

    [Fact]
    public async Task InitializeAsync_AddsRevisionMetadataToLegacySchemaRows()
    {
        var legacyDatabasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            await CreateLegacyDatabaseAsync(legacyDatabasePath, "legacy", CancellationToken.None);
            ResolvedOptions resolvedOptions = new(legacyDatabasePath, ToolNames.All);
            SqliteDatabaseInitializer initializer = new(resolvedOptions);
            await initializer.InitializeAsync(CancellationToken.None);
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
            SqliteKvStore legacyStore = new(resolvedOptions, timeProvider);
            var legacyEntry = await legacyStore.GetValueAsync("default", "legacy", CancellationToken.None);
            var writeResult = await legacyStore.SetValueAsync(
                "default",
                "legacy",
                ParseJson("\"new\""),
                null,
                cancellationToken: CancellationToken.None
            );
            var updatedEntry = await legacyStore.GetValueAsync("default", "legacy", CancellationToken.None);
            var entry = Assert.IsType<KvValue>(legacyEntry);
            Assert.Equal("\"old\"", entry.Value.GetRawText());
            Assert.Equal("2026-04-14T09:59:00.0000000Z", entry.UpdatedAt);
            Assert.Equal(0, entry.Revision);
            Assert.Equal(1, writeResult.Revision);
            Assert.Equal("2026-04-14T10:00:00.0000000Z", writeResult.UpdatedAt);
            var migratedEntry = Assert.IsType<KvValue>(updatedEntry);
            Assert.Equal("\"new\"", migratedEntry.Value.GetRawText());
            Assert.Equal(1, migratedEntry.Revision);
        }
        finally
        {
            File.Delete(legacyDatabasePath);
        }
    }

    [Fact]
    public async Task SetValue_ExpectedRevisionZeroUpdatesMigratedLegacyRow()
    {
        var legacyDatabasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            await CreateLegacyDatabaseAsync(legacyDatabasePath, "legacy-cas", CancellationToken.None);
            ResolvedOptions resolvedOptions = new(legacyDatabasePath, ToolNames.All);
            SqliteDatabaseInitializer initializer = new(resolvedOptions);
            await initializer.InitializeAsync(CancellationToken.None);
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
            SqliteKvStore legacyStore = new(resolvedOptions, timeProvider);
            var writeResult = await legacyStore.SetValueAsync(
                "default",
                "legacy-cas",
                ParseJson("\"new\""),
                null,
                0,
                cancellationToken: CancellationToken.None
            );
            var updatedEntry = await legacyStore.GetValueAsync("default", "legacy-cas", CancellationToken.None);
            Assert.Equal(1, writeResult.Revision);
            var migratedEntry = Assert.IsType<KvValue>(updatedEntry);
            Assert.Equal("\"new\"", migratedEntry.Value.GetRawText());
            Assert.Equal(1, migratedEntry.Revision);
        }
        finally
        {
            File.Delete(legacyDatabasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_SeedsNamespaceRevisionClockFromLegacySchemaRows()
    {
        var legacyDatabasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            await CreateLegacyDatabaseAsync(legacyDatabasePath, "legacy-a", CancellationToken.None);
            ResolvedOptions resolvedOptions = new(legacyDatabasePath, ToolNames.All);
            SqliteDatabaseInitializer initializer = new(resolvedOptions);
            await initializer.InitializeAsync(CancellationToken.None);
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
            SqliteKvStore legacyStore = new(resolvedOptions, timeProvider);
            var firstWrite = await legacyStore.SetValueAsync(
                "default",
                "legacy-a",
                ParseJson("\"first\""),
                null,
                0,
                cancellationToken: CancellationToken.None
            );
            var secondWrite = await legacyStore.SetValueAsync(
                "default",
                "legacy-b",
                ParseJson("\"second\""),
                null,
                cancellationToken: CancellationToken.None
            );
            Assert.Equal(1, firstWrite.Revision);
            Assert.Equal(2, secondWrite.Revision);
        }
        finally
        {
            File.Delete(legacyDatabasePath);
        }
    }

    [Fact]
    public async Task ShouldSeedNamespaceRevisionClockAsync_ReturnsFalseWhenClockAlreadyInitialized()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            ResolvedOptions resolvedOptions = new(databasePath, ToolNames.All);
            SqliteDatabaseInitializer initializer = new(resolvedOptions);
            await initializer.InitializeAsync(CancellationToken.None);
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString()
            );
            await connection.OpenAsync(CancellationToken.None);
            var insertCommand = connection.CreateCommand();
            await using (insertCommand.ConfigureAwait(false))
            {
                insertCommand.CommandText = """
                                            INSERT INTO kv_namespace_revision(namespace, last_revision)
                                            VALUES ('default', 3);
                                            """;
                await insertCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }
            var shouldSeed =
                await SqliteDatabaseInitializer.ShouldSeedNamespaceRevisionClockAsync(
                    connection,
                    CancellationToken.None
                );
            Assert.False(shouldSeed);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ShouldSeedNamespaceRevisionClockAsync_ReturnsTrueWhenKvContainsUnseededNamespace()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            ResolvedOptions resolvedOptions = new(databasePath, ToolNames.All);
            SqliteDatabaseInitializer initializer = new(resolvedOptions);
            await initializer.InitializeAsync(CancellationToken.None);
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString()
            );
            await connection.OpenAsync(CancellationToken.None);
            var insertCommand = connection.CreateCommand();
            await using (insertCommand.ConfigureAwait(false))
            {
                insertCommand.CommandText = """
                                            INSERT INTO kv(namespace, key, value, updated_at, revision)
                                            VALUES ('missing-clock', 'alpha', '"value"', '2026-04-14T10:00:00.0000000Z', 7);
                                            """;
                await insertCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }
            var shouldSeed =
                await SqliteDatabaseInitializer.ShouldSeedNamespaceRevisionClockAsync(
                    connection,
                    CancellationToken.None
                );
            Assert.True(shouldSeed);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_ToleratesConcurrentRevisionMigration()
    {
        var legacyDatabasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            await CreateLegacyDatabaseAsync(legacyDatabasePath, "legacy-race", CancellationToken.None);
            ResolvedOptions resolvedOptions = new(legacyDatabasePath, ToolNames.All);
            SqliteDatabaseInitializer firstInitializer = new(resolvedOptions);
            SqliteDatabaseInitializer secondInitializer = new(resolvedOptions);
            await Task.WhenAll(
                firstInitializer.InitializeAsync(CancellationToken.None),
                secondInitializer.InitializeAsync(CancellationToken.None)
            );
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
            SqliteKvStore legacyStore = new(resolvedOptions, timeProvider);
            var legacyEntry = await legacyStore.GetValueAsync("default", "legacy-race", CancellationToken.None);
            var entry = Assert.IsType<KvValue>(legacyEntry);
            Assert.Equal("\"old\"", entry.Value.GetRawText());
            Assert.Equal(0, entry.Revision);
        }
        finally
        {
            File.Delete(legacyDatabasePath);
        }
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
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "a-key",
            secondValue,
            null,
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "other",
            thirdValue,
            null,
            cancellationToken: CancellationToken.None
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
                cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "alpha",
            "two",
            ParseJson("2"),
            null,
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "alpha",
            "three",
            ParseJson("3"),
            null,
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "archive",
            "expired",
            ParseJson("4"),
            0,
            cancellationToken: CancellationToken.None
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
                cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "two",
            ParseJson("\"b\""),
            null,
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "profile.two",
            ParseJson("\"b\""),
            null,
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "other",
            ParseJson("\"c\""),
            null,
            cancellationToken: CancellationToken.None
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
                cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "skilla",
            "mixedkey",
            secondValue,
            null,
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "äspace",
            "two",
            ParseJson("2"),
            null,
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "ä",
            "shared",
            ParseJson("\"lower\""),
            null,
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "ä",
            ParseJson("\"lower\""),
            null,
            cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
        );
        await _store.SetValueAsync(
            "default",
            "key-1199",
            ParseJson("\"last\""),
            null,
            cancellationToken: CancellationToken.None
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
    public async Task DeleteValue_ReturnsDeletedEntryWhenRowExisted()
    {
        var storedValue = ParseJson("\"value\"");
        await _store.SetValueAsync(
            "default",
            "to-delete",
            storedValue,
            null,
            cancellationToken: CancellationToken.None
        );
        var deleted = await _store.DeleteValueAsync("default", "to-delete", CancellationToken.None);
        var deletedAgain = await _store.DeleteValueAsync("default", "to-delete", CancellationToken.None);
        var deletedEntry = Assert.IsType<KvValue>(deleted);
        Assert.Equal("\"value\"", deletedEntry.Value.GetRawText());
        Assert.Null(deletedAgain);
    }

    [Fact]
    public async Task DeleteValue_PreservesExplicitJsonNullInDeletedEntry()
    {
        await _store.SetValueAsync(
            "default",
            "nullable-delete",
            ParseJson("null"),
            null,
            cancellationToken: CancellationToken.None
        );
        var deleted = await _store.DeleteValueAsync("default", "nullable-delete", CancellationToken.None);
        var deletedEntry = Assert.IsType<KvValue>(deleted);
        Assert.Equal(JsonValueKind.Null, deletedEntry.Value.ValueKind);
    }

    [Fact]
    public async Task DeleteValue_ReturnsNullForExpiredRow()
    {
        var storedValue = ParseJson("\"value\"");
        await _store.SetValueAsync(
            "default",
            "expired-delete",
            storedValue,
            0,
            cancellationToken: CancellationToken.None
        );
        var deleted = await _store.DeleteValueAsync("default", "expired-delete", CancellationToken.None);
        Assert.Null(deleted);
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
                cancellationToken: CancellationToken.None
            );
        }

        await Assert.ThrowsAsync<ToolValidationException>(ActionAsync);
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
                cancellationToken: CancellationToken.None
            );
        }

        await Assert.ThrowsAsync<ToolValidationException>(ActionAsync);
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
                cancellationToken: CancellationToken.None
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
            cancellationToken: CancellationToken.None
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
        var updatedEntry = Assert.IsType<KvValue>(updated);
        var beforeEntry = Assert.IsType<KvValue>(before);
        var afterEntry = Assert.IsType<KvValue>(after);
        Assert.Equal("{\"name\":\"new\",\"tags\":[\"a\",\"b\"]}", updatedEntry.Value.GetRawText());
        Assert.Equal(beforeEntry.ExpiresAt, updatedEntry.ExpiresAt);
        Assert.Equal(1, beforeEntry.Revision);
        Assert.Equal(2, updatedEntry.Revision);
        Assert.Equal("{\"name\":\"new\",\"tags\":[\"a\",\"b\"]}", afterEntry.Value.GetRawText());
        Assert.Equal(beforeEntry.ExpiresAt, afterEntry.ExpiresAt);
        Assert.Equal(2, afterEntry.Revision);
    }

    [Fact]
    public async Task PatchValue_ReturnsNullWhenKeyIsMissing()
    {
        var updated = await _store.PatchValueAsync(
            "default",
            "missing",
            Patch(JsonPatchOperation.Add("/foo", JsonValue.Create(1))),
            CancellationToken.None
        );
        Assert.Null(updated);
    }

    [Fact]
    public async Task PatchValue_ReturnsNullForExpiredRow()
    {
        await _store.SetValueAsync(
            "default",
            "expired",
            ParseJson("{\"name\":\"old\"}"),
            0,
            cancellationToken: CancellationToken.None
        );
        var updated = await _store.PatchValueAsync(
            "default",
            "expired",
            Patch(JsonPatchOperation.Replace("/name", JsonValue.Create("new"))),
            CancellationToken.None
        );
        Assert.Null(updated);
    }

    [Fact]
    public async Task PatchValue_DoesNotPersistPartialChangesWhenPatchFails()
    {
        await _store.SetValueAsync(
            "default",
            "profile",
            ParseJson("{\"name\":\"old\",\"version\":1}"),
            null,
            cancellationToken: CancellationToken.None
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

        var exception = await Assert.ThrowsAsync<ToolInvalidPatchException>(ActionAsync);
        var structuredContent = exception.ToStructuredContent();
        Assert.Equal(
            1,
            structuredContent.GetProperty("operationIndex")
                             .GetInt32()
        );
        Assert.Equal(
            "test",
            structuredContent.GetProperty("operation")
                             .GetString()
        );
        Assert.Equal(
            "/name",
            structuredContent.GetProperty("targetPath")
                             .GetString()
        );
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
            cancellationToken: CancellationToken.None
        );
        var updated = await _store.PatchValueAsync(
            "default",
            "profile",
            Patch(JsonPatchOperation.Copy("/nested", "/nestedCopy"), JsonPatchOperation.Move("/name", "/displayName")),
            CancellationToken.None
        );
        var storedEntry = await _store.GetValueAsync("default", "profile", CancellationToken.None);
        var updatedEntry = Assert.IsType<KvValue>(updated);
        var storedValue = Assert.IsType<KvValue>(storedEntry);
        Assert.Equal(
            "{\"nested\":{\"value\":1},\"items\":[\"a\"],\"nestedCopy\":{\"value\":1},\"displayName\":\"old\"}",
            updatedEntry.Value.GetRawText()
        );
        Assert.Equal(
            "{\"nested\":{\"value\":1},\"items\":[\"a\"],\"nestedCopy\":{\"value\":1},\"displayName\":\"old\"}",
            storedValue.Value.GetRawText()
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

    private static async Task CreateLegacyDatabaseAsync(
        string databasePath,
        string key,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString()
        );
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = """
                                  CREATE TABLE kv (
                                      namespace  TEXT NOT NULL DEFAULT 'default',
                                      key        TEXT NOT NULL,
                                      value      TEXT NOT NULL CHECK(json_valid(value)),
                                      expires_at TEXT NULL,
                                      updated_at TEXT NOT NULL,
                                      PRIMARY KEY(namespace, key)
                                  ) WITHOUT ROWID;

                                  INSERT INTO kv(namespace, key, value, expires_at, updated_at)
                                  VALUES ('default', $key, '"old"', NULL, '2026-04-14T09:59:00.0000000Z');
                                  """;
            command.Parameters.AddWithValue("$key", key);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
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
