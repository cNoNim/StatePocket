using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Configuration;
using StatePocket.Contracts;
using StatePocket.Exceptions;
using StatePocket.Hosting;
using StatePocket.Json.Patch;
using StatePocket.Json.Pointer;
using StatePocket.Serialization;
using StatePocket.Storage;
using StatePocket.Tools;

namespace StatePocket.Tests.Tools;

public sealed class ToolResponseTests : IDisposable
{
    private static readonly JsonSerializerOptions ToolResultSerializerOptions = CreateToolResultSerializerOptions();
    private readonly string _databasePath;
    private readonly SqliteKvStore _store;

    public ToolResponseTests()
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

    private static JsonPointer? ParsePointer(string? path)
    {
        return path is null ? null : JsonPointer.Parse(path, null);
    }

    [Fact]
    public async Task QueryValuesCore_PreservesExplicitJsonNullWhenEqualsArgumentIsPresent()
    {
        QueryValuesTool queryTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "alpha",
            ParseJson("{\"status\":null}"),
            "codex",
            null,
            CancellationToken.None
        );
        await setTool.SetValueCoreAsync(
            "beta",
            ParseJson("{\"status\":\"active\"}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await queryTool.QueryValuesCoreAsync(
            "codex",
            "*",
            "$.status",
            true,
            null,
            "/status",
            CancellationToken.None
        );
        Assert.Equal(["alpha"], result.Values.Keys);
        Assert.True(result.Values["alpha"].Found);
        Assert.True(result.Values["alpha"].PathFound);
        var explicitNullQueryValue = result.Values["alpha"].Value
                                  ?? throw new InvalidOperationException("Expected projected JSON null value.");
        Assert.Equal(JsonValueKind.Null, explicitNullQueryValue.ValueKind);
    }

    [Fact]
    public async Task SetValue_ReturnsTypedResult()
    {
        SetValueTool tool = new(_store);
        var result = await tool.SetValueAsync(
            "smoke.test",
            "ok",
            JsonInputFormat.Text,
            cancellationToken: CancellationToken.None
        );
        Assert.Equal("default", result.Namespace);
        Assert.Equal("smoke.test", result.Key);
        Assert.Null(result.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.UpdatedAt);
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task SetValue_ReturnsExpiresAtWhenTtlIsSet()
    {
        SetValueTool tool = new(_store);
        var result = await tool.SetValueAsync(
            "smoke.test",
            "ok",
            JsonInputFormat.Text,
            "codex",
            60,
            cancellationToken: CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Equal("smoke.test", result.Key);
        Assert.Equal("2026-04-14T10:01:00.0000000Z", result.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.UpdatedAt);
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task SetValue_SupportsExpectedRevisionCompareAndSet()
    {
        SetValueTool tool = new(_store);
        var firstWrite = await tool.SetValueAsync(
            "cas",
            "first",
            JsonInputFormat.Text,
            "codex",
            cancellationToken: CancellationToken.None
        );
        var secondWrite = await tool.SetValueAsync(
            "cas",
            "second",
            JsonInputFormat.Text,
            "codex",
            expectedRevision: firstWrite.Revision,
            cancellationToken: CancellationToken.None
        );
        Assert.Equal(2, secondWrite.Revision);
    }

    [Fact]
    public async Task SetValue_ThrowsToolRevisionConflictExceptionWhenExpectedRevisionConflicts()
    {
        SetValueTool tool = new(_store);
        await tool.SetValueAsync(
            "cas",
            "first",
            JsonInputFormat.Text,
            "codex",
            cancellationToken: CancellationToken.None
        );
        var exception = await Assert.ThrowsAsync<ToolRevisionConflictException>(() => tool.SetValueAsync(
                "cas",
                "second",
                JsonInputFormat.Text,
                "codex",
                expectedRevision: 99,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal(
            "Revision conflict for key 'cas' in namespace 'codex'. Expected revision 99, found 1.",
            exception.Message
        );
    }

    [Fact]
    public async Task SetValue_SupportsIfAbsentWhenKeyDoesNotExist()
    {
        SetValueTool tool = new(_store);
        var result = await tool.SetValueAsync(
            "claimed",
            "first",
            JsonInputFormat.Text,
            "codex",
            ifAbsent: true,
            cancellationToken: CancellationToken.None
        );
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task SetValue_ThrowsToolAlreadyExistsExceptionWhenIfAbsentConflicts()
    {
        SetValueTool tool = new(_store);
        await tool.SetValueAsync(
            "claimed",
            "first",
            JsonInputFormat.Text,
            "codex",
            cancellationToken: CancellationToken.None
        );
        var exception = await Assert.ThrowsAsync<ToolAlreadyExistsException>(() => tool.SetValueAsync(
                "claimed",
                "second",
                JsonInputFormat.Text,
                "codex",
                ifAbsent: true,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal("Key 'claimed' already exists in namespace 'codex'.", exception.Message);
    }

    [Fact]
    public async Task SetValue_ThrowsToolValidationExceptionWhenIfAbsentIsCombinedWithExpectedRevision()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolValidationException>(() => tool.SetValueAsync(
                "claimed",
                "value",
                JsonInputFormat.Text,
                "codex",
                expectedRevision: 1,
                ifAbsent: true,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal("ifAbsent cannot be combined with expectedRevision. (Parameter 'ifAbsent')", exception.Message);
    }

    [Fact]
    public async Task SetValue_SupportsIfAbsentWhenExpectedRevisionIsExplicitNull()
    {
        SetValueTool tool = new(_store);
        var result = await tool.SetValueAsync(
            "claimed-null",
            "first",
            JsonInputFormat.Text,
            "codex",
            expectedRevision: null,
            ifAbsent: true,
            cancellationToken: CancellationToken.None
        );
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task SetValue_ThrowsToolBusyExceptionWhenDatabaseIsLocked()
    {
        await using var lockHandle = await AcquireWriteLockAsync();
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAnyAsync<ToolBusyException>(() => tool.SetValueAsync(
                "smoke.test",
                "ok",
                JsonInputFormat.Text,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal("The database is busy with another write operation. Try again.", exception.Message);
        Assert.IsType<KvStoreBusyException>(exception);
    }

    [Fact]
    public async Task SetValue_ThrowsToolInvalidJsonExceptionWhenJsonFormatInputIsInvalid()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidJsonException>(() =>
            tool.SetValueAsync("bad", "{", cancellationToken: CancellationToken.None)
        );
        Assert.Equal("value must be valid JSON when format is 'json'.", exception.Message);
    }

    [Fact]
    public async Task SetValue_ThrowsToolInvalidJsonExceptionWhenJsonFormatInputContainsDuplicateProperties()
    {
        SetValueTool tool = new(_store);
        await Assert.ThrowsAsync<ToolInvalidJsonException>(() => tool.SetValueAsync(
                "bad",
                """{"a":1,"a":2}""",
                cancellationToken: CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task SetValue_ThrowsToolInvalidArgumentExceptionWhenKeyIsNull()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() =>
            tool.SetValueAsync(
                null!,
                "ok",
                JsonInputFormat.Text,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal("key must not be null.", exception.Message);
    }

    [Fact]
    public async Task SetValue_ThrowsToolInvalidArgumentExceptionWhenValueIsNull()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() =>
            tool.SetValueAsync("alpha", null!, cancellationToken: CancellationToken.None)
        );
        Assert.Equal("value must not be null.", exception.Message);
    }

    [Fact]
    public async Task GetValue_ReturnsFoundFalseWhenKeyIsMissing()
    {
        GetValueTool tool = new(_store);
        var result = await tool.GetValueCoreAsync(
            "missing",
            "codex",
            null,
            CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Equal("missing", result.Key);
        Assert.False(result.Found);
        Assert.False(result.PathFound);
        Assert.Null(result.Value);
        Assert.Null(result.ExpiresAt);
        Assert.Null(result.UpdatedAt);
        Assert.Null(result.Revision);
    }

    [Fact]
    public async Task GetValue_ReturnsFoundValueAndExpiryWhenKeyExists()
    {
        SetValueTool setTool = new(_store);
        GetValueTool getTool = new(_store);
        await setTool.SetValueCoreAsync(
            "present",
            ParseJson("{\"ok\":true}"),
            "codex",
            60,
            CancellationToken.None
        );
        var result = await getTool.GetValueAsync(
            "present",
            "codex",
            null,
            CancellationToken.None
        );
        Assert.True(result.Found);
        Assert.True(result.PathFound);
        Assert.Equal("codex", result.Namespace);
        Assert.Equal("present", result.Key);
        Assert.True(result.Value.HasValue);
        Assert.Equal(JsonValueKind.Object, result.Value.Value.ValueKind);
        Assert.NotNull(result.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.UpdatedAt);
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task GetValue_ReturnsProjectedValueWhenPathIsProvided()
    {
        SetValueTool setTool = new(_store);
        GetValueTool getTool = new(_store);
        await setTool.SetValueCoreAsync(
            "profile",
            ParseJson("{\"name\":\"old\",\"nested\":{\"value\":1}}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await getTool.GetValueCoreAsync(
            "profile",
            "codex",
            "/nested/value",
            CancellationToken.None
        );
        Assert.True(result.Found);
        Assert.True(result.PathFound);
        Assert.Equal("1", result.Value?.GetRawText());
    }

    [Fact]
    public async Task GetValue_ReturnsPathFoundFalseWhenPathIsMissing()
    {
        SetValueTool setTool = new(_store);
        GetValueTool getTool = new(_store);
        await setTool.SetValueCoreAsync(
            "profile",
            ParseJson("{\"name\":\"old\"}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await getTool.GetValueCoreAsync(
            "profile",
            "codex",
            "/nested/value",
            CancellationToken.None
        );
        Assert.True(result.Found);
        Assert.False(result.PathFound);
        Assert.Null(result.Value);
        Assert.Null(result.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.UpdatedAt);
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task GetValue_ThrowsToolInvalidArgumentExceptionWhenKeyIsNull()
    {
        GetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() =>
            tool.GetValueAsync(
                null!,
                "codex",
                null,
                CancellationToken.None
            )
        );
        Assert.Equal("key must not be null.", exception.Message);
    }

    [Fact]
    public void GetValueCore_Helper_ThrowsJsonPointerExceptionWhenPointerIsMalformed()
    {
        GetValueTool tool = new(_store);
        _ = tool;
        var exception = Assert.Throws<JsonPointerException>(static () => ParsePointer("nested/value"));
        Assert.Equal("Invalid JSON Pointer path 'nested/value'.", exception.Message);
    }

    [Fact]
    public async Task ListKeys_ReturnsTypedResult()
    {
        ListKeysTool listTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "one",
            ParseJson("\"a\""),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await listTool.ListKeysAsync(
            "codex",
            null,
            null,
            null,
            CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Equal(["one"], result.Keys);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task ListNamespaces_ReturnsTypedResult()
    {
        ListNamespacesTool listTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "one",
            ParseJson("\"a\""),
            "beta",
            null,
            CancellationToken.None
        );
        await setTool.SetValueCoreAsync(
            "two",
            ParseJson("\"b\""),
            "alpha",
            null,
            CancellationToken.None
        );
        var result = await listTool.ListNamespacesAsync(
            null,
            null,
            null,
            CancellationToken.None
        );
        Assert.Equal(["alpha", "beta"], result.Namespaces);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task ListKeys_UsesDefaultLimitAndReturnsNextCursor()
    {
        ListKeysTool listTool = new(_store);
        SetValueTool setTool = new(_store);
        foreach (var index in Enumerable.Range(0, ToolArgumentHelper.DefaultPageSize + 1))
        {
            await setTool.SetValueCoreAsync(
                $"key-{index:D2}",
                ParseJson($"{index}"),
                "codex",
                null,
                CancellationToken.None
            );
        }
        var result = await listTool.ListKeysAsync(
            "codex",
            null,
            null,
            null,
            CancellationToken.None
        );
        Assert.Equal(ToolArgumentHelper.DefaultPageSize, result.Keys.Count);
        Assert.Equal("key-49", result.Keys[^1]);
        Assert.Equal("key-49", result.NextCursor);
    }

    [Fact]
    public async Task ListNamespaces_SupportsCursorPagination()
    {
        ListNamespacesTool listTool = new(_store);
        SetValueTool setTool = new(_store);
        foreach (var currentNamespace in new[]
                 {
                     "alpha", "beta", "gamma"
                 })
        {
            await setTool.SetValueCoreAsync(
                "shared",
                ParseJson("\"ok\""),
                currentNamespace,
                null,
                CancellationToken.None
            );
        }
        var firstPage = await listTool.ListNamespacesAsync(
            null,
            2,
            null,
            CancellationToken.None
        );
        Assert.Equal(["alpha", "beta"], firstPage.Namespaces);
        Assert.Equal("beta", firstPage.NextCursor);
        var secondPage = await listTool.ListNamespacesAsync(
            null,
            2,
            firstPage.NextCursor,
            CancellationToken.None
        );
        Assert.Equal(["gamma"], secondPage.Namespaces);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task ListKeys_ThrowsToolInvalidArgumentExceptionWhenLimitExceedsHardCap()
    {
        ListKeysTool listTool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() => listTool.ListKeysAsync(
                "codex",
                null,
                ToolArgumentHelper.MaxResultItems + 1,
                null,
                CancellationToken.None
            )
        );
        Assert.Equal("limit must be less than or equal to 100.", exception.Message);
    }

    [Fact]
    public async Task GetValues_ReturnsTypedResult()
    {
        GetValuesTool getValuesTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "one",
            ParseJson("{\"profile\":{\"name\":\"a\"}}"),
            "codex",
            60,
            CancellationToken.None
        );
        await setTool.SetValueCoreAsync(
            "two",
            ParseJson("{\"profile\":{\"name\":null}}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await getValuesTool.GetValuesAsync(
            ["one", "two", "missing"],
            "codex",
            ParsePointer("/profile/name"),
            CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Equal(
            "\"a\"",
            result.Values["one"]
                  .Value?.GetRawText()
        );
        Assert.True(result.Values["one"].Found);
        Assert.True(result.Values["one"].PathFound);
        Assert.NotNull(result.Values["one"].ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.Values["one"].UpdatedAt);
        Assert.Equal(1, result.Values["one"].Revision);
        Assert.True(result.Values["two"].Found);
        Assert.True(result.Values["two"].PathFound);
        Assert.Null(result.Values["two"].ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.Values["two"].UpdatedAt);
        Assert.Equal(2, result.Values["two"].Revision);
        var explicitNullValue = result.Values["two"].Value;
        Assert.True(explicitNullValue.HasValue);
        Assert.Equal(
            JsonValueKind.Null,
            explicitNullValue.GetValueOrDefault()
                             .ValueKind
        );
        Assert.False(result.Values["missing"].Found);
        Assert.False(result.Values["missing"].PathFound);
        Assert.Null(result.Values["missing"].Value);
        Assert.Null(result.Values["missing"].ExpiresAt);
        Assert.Null(result.Values["missing"].UpdatedAt);
        Assert.Null(result.Values["missing"].Revision);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetValues_ReturnsEmptyResultWhenKeysAreEmpty()
    {
        GetValuesTool getValuesTool = new(_store);
        var result = await getValuesTool.GetValuesAsync(
            [],
            "codex",
            null,
            CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Empty(result.Values);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetValues_DeduplicatesKeysWhenInputContainsDuplicates()
    {
        GetValuesTool getValuesTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "one",
            ParseJson("\"a\""),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await getValuesTool.GetValuesAsync(
            ["one", "one"],
            "codex",
            null,
            CancellationToken.None
        );
        Assert.Equal(["one"], result.Values.Keys);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetValues_ThrowsToolInvalidArgumentExceptionWhenKeyCountExceedsHardCap()
    {
        GetValuesTool getValuesTool = new(_store);
        var keys = Enumerable.Range(0, ToolArgumentHelper.MaxResultItems + 1)
                             .Select(static index => $"key-{index:D3}")
                             .ToArray();
        var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() =>
            getValuesTool.GetValuesAsync(
                keys,
                "codex",
                null,
                CancellationToken.None
            )
        );
        Assert.Equal("keys must contain less than or equal to 100 items.", exception.Message);
    }

    [Fact]
    public async Task GetValues_ThrowsToolInvalidArgumentExceptionWhenKeysContainNull()
    {
        GetValuesTool getValuesTool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() =>
            getValuesTool.GetValuesAsync(
                ["one", null!],
                "codex",
                null,
                CancellationToken.None
            )
        );
        Assert.Equal("keys must not contain null values.", exception.Message);
    }

    [Fact]
    public void GetValuesCore_Helper_ThrowsJsonPointerExceptionWhenPointerIsMalformed()
    {
        GetValuesTool tool = new(_store);
        _ = tool;
        var exception = Assert.Throws<JsonPointerException>(static () => ParsePointer("profile/name"));
        Assert.Equal("Invalid JSON Pointer path 'profile/name'.", exception.Message);
    }

    [Fact]
    public async Task QueryValues_ReturnsOnlyKeysMatchingQueryAndEquals()
    {
        QueryValuesTool queryTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "alpha",
            ParseJson("{\"status\":\"active\",\"profile\":{\"name\":\"A\"}}"),
            "codex",
            60,
            CancellationToken.None
        );
        await setTool.SetValueCoreAsync(
            "beta",
            ParseJson("{\"status\":\"inactive\",\"profile\":{\"name\":\"B\"}}"),
            "codex",
            null,
            CancellationToken.None
        );
        await setTool.SetValueCoreAsync(
            "gamma",
            ParseJson("{\"profile\":{\"name\":\"C\"}}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await queryTool.QueryValuesAsync(
            "codex",
            "*",
            "$.status",
            "active",
            JsonInputFormat.Text,
            ParsePointer("/profile/name"),
            cancellationToken: CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Equal(["alpha"], result.Values.Keys);
        Assert.True(result.Values["alpha"].Found);
        Assert.True(result.Values["alpha"].PathFound);
        Assert.NotNull(result.Values["alpha"].ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.Values["alpha"].UpdatedAt);
        Assert.Equal(1, result.Values["alpha"].Revision);
        Assert.Equal(
            "\"A\"",
            result.Values["alpha"]
                  .Value?.GetRawText()
        );
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task QueryValues_UsesExistenceWhenEqualsIsOmitted()
    {
        QueryValuesTool queryTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "alpha",
            ParseJson("{\"status\":\"active\"}"),
            "codex",
            null,
            CancellationToken.None
        );
        await setTool.SetValueCoreAsync(
            "beta",
            ParseJson("{\"other\":1}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await queryTool.QueryValuesCoreAsync(
            "codex",
            "*",
            "$.status",
            false,
            null,
            "/status",
            CancellationToken.None
        );
        Assert.Equal(["alpha"], result.Values.Keys);
        Assert.True(result.Values["alpha"].Found);
        Assert.True(result.Values["alpha"].PathFound);
        Assert.Equal(
            "\"active\"",
            result.Values["alpha"]
                  .Value?.GetRawText()
        );
    }

    [Fact]
    public async Task QueryValues_ReturnsNextCursorWhenMoreMatchesRemain()
    {
        QueryValuesTool queryTool = new(_store);
        SetValueTool setTool = new(_store);
        foreach (var key in new[]
                 {
                     "alpha", "beta", "gamma"
                 })
        {
            await setTool.SetValueCoreAsync(
                key,
                ParseJson("{\"status\":\"active\"}"),
                "codex",
                null,
                CancellationToken.None
            );
        }
        var firstPage = await queryTool.QueryValuesAsync(
            "codex",
            "*",
            "$.status",
            path: ParsePointer("/status"),
            limit: 2,
            cancellationToken: CancellationToken.None
        );
        Assert.Equal(["alpha", "beta"], firstPage.Values.Keys);
        Assert.Equal("beta", firstPage.NextCursor);
        var secondPage = await queryTool.QueryValuesAsync(
            "codex",
            "*",
            "$.status",
            path: ParsePointer("/status"),
            limit: 2,
            cursor: firstPage.NextCursor,
            cancellationToken: CancellationToken.None
        );
        Assert.Equal(["gamma"], secondPage.Values.Keys);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task QueryValues_TreatsEquivalentJsonNumbersAsEqual()
    {
        QueryValuesTool queryTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "alpha",
            ParseJson("{\"count\":1}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await queryTool.QueryValuesCoreAsync(
            "codex",
            "*",
            "$.count",
            true,
            ParseJson("1.0"),
            "/count",
            CancellationToken.None
        );
        Assert.Equal(["alpha"], result.Values.Keys);
        Assert.Equal(
            "1",
            result.Values["alpha"]
                  .Value?.GetRawText()
        );
    }

    [Fact]
    public async Task QueryValues_TreatsHugeExponentNumbersAsEqual()
    {
        QueryValuesTool queryTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "alpha",
            ParseJson("{\"count\":1e2147483648}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await queryTool.QueryValuesCoreAsync(
            "codex",
            "*",
            "$.count",
            true,
            ParseJson("10e2147483647"),
            "/count",
            CancellationToken.None
        );
        Assert.Equal(["alpha"], result.Values.Keys);
        Assert.Equal(
            "1e2147483648",
            result.Values["alpha"]
                  .Value?.GetRawText()
        );
    }

    [Fact]
    public async Task QueryValuesCore_ThrowsToolValidationExceptionWhenEqualsIsPassedWithoutQuery()
    {
        QueryValuesTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolValidationException>(() => tool.QueryValuesCoreAsync(
                "codex",
                "*",
                null,
                true,
                ParseJson("\"active\""),
                null,
                CancellationToken.None
            )
        );
        Assert.Equal("equals requires query.", exception.Message);
    }

    [Fact]
    public async Task QueryValues_ThrowsToolInvalidJsonExceptionWhenEqualsJsonIsInvalid()
    {
        QueryValuesTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidJsonException>(() =>
            tool.QueryValuesAsync(
                "codex",
                "*",
                "$.status",
                "{",
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal("equals must be valid JSON when format is 'json'.", exception.Message);
    }

    [Fact]
    public async Task QueryValues_ThrowsToolInvalidJsonExceptionWhenEqualsJsonContainsDuplicateProperties()
    {
        QueryValuesTool tool = new(_store);
        await Assert.ThrowsAsync<ToolInvalidJsonException>(() => tool.QueryValuesAsync(
                "codex",
                "*",
                "$.status",
                """{"a":1,"a":2}""",
                cancellationToken: CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task QueryValuesCore_ThrowsToolInvalidQueryExceptionWhenQueryIsMalformed()
    {
        QueryValuesTool tool = new(_store);
        await Assert.ThrowsAsync<ToolInvalidQueryException>(() => tool.QueryValuesCoreAsync(
                "codex",
                "*",
                "status",
                false,
                null,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task QueryValuesCore_ThrowsToolInvalidQueryExceptionWhenTopLevelCurrentNodeRootIsUsed()
    {
        QueryValuesTool tool = new(_store);
        await Assert.ThrowsAsync<ToolInvalidQueryException>(() => tool.QueryValuesCoreAsync(
                "codex",
                "*",
                "@.status",
                false,
                null,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task QueryValuesCore_ThrowsToolInvalidQueryExceptionWhenRegexPatternIsInvalid()
    {
        QueryValuesTool tool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "alpha",
            ParseJson("{\"values\":[{\"name\":\"alpha\"}]}"),
            "codex",
            null,
            CancellationToken.None
        );
        await Assert.ThrowsAsync<ToolInvalidQueryException>(() => tool.QueryValuesCoreAsync(
                "codex",
                "*",
                "$.values[?match(@.name, '[')]",
                false,
                null,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task QueryValuesCore_ThrowsToolInvalidQueryExceptionWhenRegexPatternIsInvalidAndNoRowsAreScanned()
    {
        QueryValuesTool tool = new(_store);
        await Assert.ThrowsAsync<ToolInvalidQueryException>(() => tool.QueryValuesCoreAsync(
                "missing",
                "*",
                "$.values[?match(@.name, '[')]",
                false,
                null,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task QueryValuesCore_ThrowsToolInvalidQueryExceptionWhenRuntimeRegexPatternFromDataIsInvalid()
    {
        QueryValuesTool tool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "alpha",
            ParseJson("{\"values\":[{\"name\":\"alpha\",\"pattern\":\"[\"}]}"),
            "codex",
            null,
            CancellationToken.None
        );
        await Assert.ThrowsAsync<ToolInvalidQueryException>(() => tool.QueryValuesCoreAsync(
                "codex",
                "*",
                "$.values[?match(@.name, @.pattern)]",
                false,
                null,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public void QueryValuesCore_Helper_ThrowsJsonPointerExceptionWhenPointerIsMalformed()
    {
        QueryValuesTool tool = new(_store);
        _ = tool;
        var exception = Assert.Throws<JsonPointerException>(static () => ParsePointer("profile/name"));
        Assert.Equal("Invalid JSON Pointer path 'profile/name'.", exception.Message);
    }

    [Fact]
    public async Task DeleteValue_ReturnsTypedResult()
    {
        DeleteValueTool deleteTool = new(_store);
        SetValueTool setTool = new(_store);
        await setTool.SetValueCoreAsync(
            "gone",
            ParseJson("\"a\""),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await deleteTool.DeleteValueAsync("gone", "codex", CancellationToken.None);
        Assert.Equal("codex", result.Namespace);
        Assert.Equal("gone", result.Key);
    }

    [Fact]
    public async Task DeleteValueCore_ThrowsToolNotFoundExceptionWhenKeyIsMissing()
    {
        DeleteValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolNotFoundException>(() =>
            tool.DeleteValueCoreAsync("missing", "codex", CancellationToken.None)
        );
        Assert.Equal("Key 'missing' was not found in namespace 'codex'.", exception.Message);
    }

    [Fact]
    public async Task DeleteValue_ThrowsToolNotFoundExceptionWhenKeyIsMissing()
    {
        DeleteValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolNotFoundException>(() =>
            tool.DeleteValueAsync("missing", "codex", CancellationToken.None)
        );
        Assert.Equal("Key 'missing' was not found in namespace 'codex'.", exception.Message);
    }

    [Fact]
    public async Task DeleteValue_ThrowsToolInvalidArgumentExceptionWhenKeyIsNull()
    {
        DeleteValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() =>
            tool.DeleteValueAsync(null!, "codex", CancellationToken.None)
        );
        Assert.Equal("key must not be null.", exception.Message);
    }

    [Fact]
    public async Task DeleteValue_ThrowsToolBusyExceptionWhenDatabaseIsLocked()
    {
        SetValueTool setTool = new(_store);
        DeleteValueTool deleteTool = new(_store);
        await setTool.SetValueCoreAsync(
            "gone",
            ParseJson("\"a\""),
            "codex",
            null,
            CancellationToken.None
        );
        await using var lockHandle = await AcquireWriteLockAsync();
        var exception = await Assert.ThrowsAnyAsync<ToolBusyException>(() =>
            deleteTool.DeleteValueAsync("gone", "codex", CancellationToken.None)
        );
        Assert.Equal("The database is busy with another write operation. Try again.", exception.Message);
        Assert.IsType<KvStoreBusyException>(exception);
    }

    [Fact]
    public async Task PatchValue_ReturnsTypedResult()
    {
        SetValueTool setTool = new(_store);
        PatchValueTool updateTool = new(_store);
        await setTool.SetValueCoreAsync(
            "profile",
            ParseJson("{\"name\":\"old\"}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await updateTool.PatchValueAsync(
            "profile",
            SerializePatch(Patch(Replace("/name", "\"new\""))),
            "codex",
            CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Equal("profile", result.Key);
        Assert.Equal("{\"name\":\"new\"}", result.Value.GetRawText());
        Assert.Null(result.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.UpdatedAt);
        Assert.Equal(2, result.Revision);
    }

    [Fact]
    public async Task PatchValue_ReturnsExpiresAtWhenKeyHasTtl()
    {
        SetValueTool setTool = new(_store);
        PatchValueTool updateTool = new(_store);
        await setTool.SetValueCoreAsync(
            "profile",
            ParseJson("{\"name\":\"old\"}"),
            "codex",
            60,
            CancellationToken.None
        );
        var result = await updateTool.PatchValueAsync(
            "profile",
            SerializePatch(Patch(Replace("/name", "\"new\""))),
            "codex",
            CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Equal("profile", result.Key);
        Assert.Equal("{\"name\":\"new\"}", result.Value.GetRawText());
        Assert.Equal("2026-04-14T10:01:00.0000000Z", result.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", result.UpdatedAt);
        Assert.Equal(2, result.Revision);
    }

    [Fact]
    public async Task PatchValueCore_ThrowsToolNotFoundExceptionWhenKeyIsMissing()
    {
        PatchValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolNotFoundException>(() => tool.PatchValueCoreAsync(
                "missing",
                Patch(Replace("/name", "\"new\"")),
                "codex",
                CancellationToken.None
            )
        );
        Assert.Equal("Key 'missing' was not found in namespace 'codex'.", exception.Message);
    }

    [Fact]
    public void PatchValueCore_ThrowsForMalformedPointerBeforeToolExecution()
    {
        Assert.Throws<JsonPointerException>(static () => Patch(JsonPatchOperation.Replace("name", ParseNode("\"new\"")))
        );
    }

    [Fact]
    public async Task PatchValueCore_ThrowsToolInvalidPatchExceptionForInvalidPatch()
    {
        SetValueTool setTool = new(_store);
        PatchValueTool updateTool = new(_store);
        await setTool.SetValueCoreAsync(
            "profile",
            ParseJson("{\"name\":\"old\"}"),
            "codex",
            null,
            CancellationToken.None
        );
        await Assert.ThrowsAsync<ToolInvalidPatchException>(() => updateTool.PatchValueCoreAsync(
                "profile",
                Patch(Test("/name", "\"other\"")),
                "codex",
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task PatchValue_ThrowsToolBusyExceptionWhenDatabaseIsLocked()
    {
        SetValueTool setTool = new(_store);
        PatchValueTool patchTool = new(_store);
        await setTool.SetValueCoreAsync(
            "profile",
            ParseJson("{\"name\":\"old\"}"),
            "codex",
            null,
            CancellationToken.None
        );
        await using var lockHandle = await AcquireWriteLockAsync();
        var exception = await Assert.ThrowsAnyAsync<ToolBusyException>(() => patchTool.PatchValueAsync(
                "profile",
                SerializePatch(Patch(Replace("/name", "\"new\""))),
                "codex",
                CancellationToken.None
            )
        );
        Assert.Equal("The database is busy with another write operation. Try again.", exception.Message);
        Assert.IsType<KvStoreBusyException>(exception);
    }

    [Fact]
    public async Task PatchValue_ThrowsToolInvalidPatchExceptionWhenPatchTextIsInvalid()
    {
        PatchValueTool patchTool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidPatchException>(() => patchTool.PatchValueAsync(
                "profile",
                """{"op":"replace","path":"/name","value":"new"}""",
                "codex",
                CancellationToken.None
            )
        );
        Assert.Contains("could not be converted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PatchValue_ThrowsToolInvalidPatchExceptionWhenPatchContainsDuplicateProperties()
    {
        PatchValueTool patchTool = new(_store);
        await Assert.ThrowsAsync<ToolInvalidPatchException>(() => patchTool.PatchValueAsync(
                "profile",
                """[{ "op": "replace", "path": "/name", "value": "new", "value": "other" }]""",
                "codex",
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task PatchValue_ThrowsToolInvalidArgumentExceptionWhenKeyIsNull()
    {
        PatchValueTool patchTool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() => patchTool.PatchValueAsync(
                null!,
                SerializePatch(Patch(Replace("/name", "\"new\""))),
                "codex",
                CancellationToken.None
            )
        );
        Assert.Equal("key must not be null.", exception.Message);
    }

    [Fact]
    public async Task PatchValue_SupportsCopyAndMoveOperations()
    {
        SetValueTool setTool = new(_store);
        PatchValueTool updateTool = new(_store);
        GetValueTool getTool = new(_store);
        await setTool.SetValueCoreAsync(
            "profile",
            ParseJson("{\"name\":\"old\",\"nested\":{\"value\":1}}"),
            "codex",
            null,
            CancellationToken.None
        );
        var result = await updateTool.PatchValueCoreAsync(
            "profile",
            Patch(Copy("/nested", "/nestedCopy"), Move("/name", "/displayName")),
            "codex",
            CancellationToken.None
        );
        var stored = await getTool.GetValueCoreAsync(
            "profile",
            "codex",
            null,
            CancellationToken.None
        );
        Assert.Equal("codex", result.Namespace);
        Assert.Equal("profile", result.Key);
        Assert.Equal(
            "{\"nested\":{\"value\":1},\"nestedCopy\":{\"value\":1},\"displayName\":\"old\"}",
            result.Value.GetRawText()
        );
        Assert.True(stored.Found);
        Assert.True(stored.Value.HasValue);
        Assert.Equal(
            "{\"nested\":{\"value\":1},\"nestedCopy\":{\"value\":1},\"displayName\":\"old\"}",
            stored.Value.Value.GetRawText()
        );
    }

    [Fact]
    public void PatchValue_JsonContractRequiresFromForMoveAndCopy()
    {
        var exception = Assert.Throws<JsonException>(static () =>
            JsonSerializer.Deserialize<JsonPatch>("""[{ "op": "copy", "path": "/nameCopy" }]""")
        );
        Assert.Contains("from", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetValueCore_ThrowsToolValidationExceptionForInvalidInput()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolValidationException>(() =>
            tool.SetValueCoreAsync(
                "bad",
                ParseJson("\"value\""),
                null,
                -1,
                CancellationToken.None
            )
        );
        Assert.Equal("ttlSeconds must be greater than or equal to 0. (Parameter 'ttlSeconds')", exception.Message);
    }

    [Fact]
    public async Task SetValueCore_ThrowsToolValidationExceptionForInvalidExpectedRevision()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<ToolValidationException>(() => tool.SetValueAsync(
                "bad",
                "value",
                JsonInputFormat.Text,
                "codex",
                expectedRevision: -1,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal(
            "expectedRevision must be greater than or equal to 0. (Parameter 'expectedRevision')",
            exception.Message
        );
    }

    [Fact]
    public Task SetValueCore_ThrowsToolInvalidArgumentExceptionWhenNamespaceIsInvalid()
    {
        SetValueTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace => tool.SetValueCoreAsync(
                "key",
                ParseJson("\"value\""),
                invalidNamespace,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public Task GetValueCore_ThrowsToolInvalidArgumentExceptionWhenNamespaceIsInvalid()
    {
        GetValueTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace =>
            tool.GetValueCoreAsync(
                "key",
                invalidNamespace,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public Task GetValuesCore_ThrowsToolInvalidArgumentExceptionWhenNamespaceIsInvalid()
    {
        GetValuesTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace =>
            tool.GetValuesCoreAsync(
                ["key"],
                invalidNamespace,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public Task QueryValuesCore_ThrowsToolInvalidArgumentExceptionWhenNamespaceIsInvalid()
    {
        QueryValuesTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace => tool.QueryValuesCoreAsync(
                invalidNamespace,
                "*",
                null,
                false,
                null,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public Task ListKeysCore_ThrowsToolInvalidArgumentExceptionWhenNamespaceIsInvalid()
    {
        ListKeysTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace =>
            tool.ListKeysCoreAsync(invalidNamespace, null, CancellationToken.None)
        );
    }

    [Fact]
    public Task DeleteValueCore_ThrowsToolInvalidArgumentExceptionWhenNamespaceIsInvalid()
    {
        DeleteValueTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace =>
            tool.DeleteValueCoreAsync("key", invalidNamespace, CancellationToken.None)
        );
    }

    [Fact]
    public Task PatchValueCore_ThrowsToolInvalidArgumentExceptionWhenNamespaceIsInvalid()
    {
        PatchValueTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace => tool.PatchValueCoreAsync(
                "key",
                Patch(Replace("/name", "\"new\"")),
                invalidNamespace,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public void GetValueResult_UsesCamelCaseJsonFieldNames()
    {
        var json = SerializeToolResult(
            new GetValueResult
            {
                Namespace = "codex",
                Key = "key",
                Found = true,
                PathFound = true,
                Value = ParseJson("\"value\""),
                ExpiresAt = "2026-04-14T10:01:00.0000000Z",
                UpdatedAt = "2026-04-14T10:00:00.0000000Z",
                Revision = 1
            }
        );
        Assert.True(json.TryGetProperty("pathFound", out _));
        Assert.True(json.TryGetProperty("expiresAt", out _));
        Assert.True(json.TryGetProperty("updatedAt", out _));
        Assert.True(json.TryGetProperty("revision", out _));
        Assert.False(json.TryGetProperty("path_found", out _));
    }

    [Fact]
    public void SetValueResult_UsesCamelCaseJsonFieldNames()
    {
        var json = SerializeToolResult(
            new SetValueResult
            {
                Namespace = "codex",
                Key = "key",
                ExpiresAt = "2026-04-14T10:01:00.0000000Z",
                UpdatedAt = "2026-04-14T10:00:00.0000000Z",
                Revision = 1
            }
        );
        Assert.True(json.TryGetProperty("expiresAt", out _));
        Assert.True(json.TryGetProperty("updatedAt", out _));
        Assert.True(json.TryGetProperty("revision", out _));
        Assert.False(json.TryGetProperty("expires_at", out _));
    }

    [Fact]
    public void PatchValueResult_UsesCamelCaseJsonFieldNames()
    {
        var json = SerializeToolResult(
            new PatchValueResult
            {
                Namespace = "codex",
                Key = "key",
                Value = ParseJson("\"value\""),
                ExpiresAt = "2026-04-14T10:01:00.0000000Z",
                UpdatedAt = "2026-04-14T10:00:00.0000000Z",
                Revision = 2
            }
        );
        Assert.True(json.TryGetProperty("expiresAt", out _));
        Assert.True(json.TryGetProperty("updatedAt", out _));
        Assert.True(json.TryGetProperty("revision", out _));
        Assert.False(json.TryGetProperty("expires_at", out _));
    }

    [Fact]
    public void GetValuesEntry_UsesCamelCaseJsonFieldNames()
    {
        var json = SerializeToolResult(
            new GetValuesEntry
            {
                Found = true,
                PathFound = true,
                Value = ParseJson("\"value\""),
                ExpiresAt = "2026-04-14T10:01:00.0000000Z",
                UpdatedAt = "2026-04-14T10:00:00.0000000Z",
                Revision = 1
            }
        );
        Assert.True(json.TryGetProperty("pathFound", out _));
        Assert.True(json.TryGetProperty("expiresAt", out _));
        Assert.True(json.TryGetProperty("updatedAt", out _));
        Assert.True(json.TryGetProperty("revision", out _));
        Assert.False(json.TryGetProperty("path_found", out _));
    }

    [Fact]
    public void GetValuesResult_UsesCamelCaseJsonFieldNames()
    {
        var json = SerializeToolResult(
            new GetValuesResult
            {
                Namespace = "codex",
                Values = new Dictionary<string, GetValuesEntry>(StringComparer.Ordinal),
                NextCursor = "cursor-1"
            }
        );
        Assert.True(json.TryGetProperty("nextCursor", out _));
        Assert.False(json.TryGetProperty("next_cursor", out _));
    }

    [Fact]
    public void QueryValuesResult_UsesCamelCaseJsonFieldNames()
    {
        var json = SerializeToolResult(
            new QueryValuesResult
            {
                Namespace = "codex",
                Values = new Dictionary<string, GetValuesEntry>(StringComparer.Ordinal),
                NextCursor = "cursor-1"
            }
        );
        Assert.True(json.TryGetProperty("nextCursor", out _));
        Assert.False(json.TryGetProperty("next_cursor", out _));
    }

    [Fact]
    public void ListKeysResult_UsesCamelCaseJsonFieldNames()
    {
        var json = SerializeToolResult(
            new ListKeysResult
            {
                Namespace = "codex",
                Keys = ["key"],
                NextCursor = "cursor-1"
            }
        );
        Assert.True(json.TryGetProperty("nextCursor", out _));
        Assert.False(json.TryGetProperty("next_cursor", out _));
    }

    [Fact]
    public void ListNamespacesResult_UsesCamelCaseJsonFieldNames()
    {
        var json = SerializeToolResult(
            new ListNamespacesResult
            {
                Namespaces = ["codex"],
                NextCursor = "cursor-1"
            }
        );
        Assert.True(json.TryGetProperty("nextCursor", out _));
        Assert.False(json.TryGetProperty("next_cursor", out _));
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement SerializeToolResult<TValue>(TValue value)
    {
        return JsonSerializer.SerializeToElement(value, ToolResultSerializerOptions);
    }

    private static JsonSerializerOptions CreateToolResultSerializerOptions()
    {
        JsonSerializerOptions options = new(McpJsonUtilities.DefaultOptions)
        {
            AllowDuplicateProperties = false
        };
        options.TypeInfoResolverChain.Insert(0, ToolResultJsonContext.Default);
        return options;
    }

    private static async Task AssertInvalidNamespaceRejectedAsync(Func<string, Task> action)
    {
        foreach (var invalidNamespace in new[]
                 {
                     "", "   "
                 })
        {
            var exception = await Assert.ThrowsAsync<ToolInvalidArgumentException>(() => action(invalidNamespace));
            Assert.Equal("namespace must not be empty or whitespace.", exception.Message);
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

    private static ReplaceOperation Replace(string path, string valueJson)
    {
        return JsonPatchOperation.Replace(path, ParseNode(valueJson));
    }

    private static TestOperation Test(string path, string valueJson)
    {
        return JsonPatchOperation.Test(path, ParseNode(valueJson));
    }

    private static CopyOperation Copy(string from, string path)
    {
        return JsonPatchOperation.Copy(from, path);
    }

    private static MoveOperation Move(string from, string path)
    {
        return JsonPatchOperation.Move(from, path);
    }

    private static JsonPatch Patch(params JsonPatchOperation[] operations)
    {
        return new JsonPatch(operations);
    }

    private static string SerializePatch(JsonPatch patch)
    {
        return JsonSerializer.Serialize(patch, JsonPatchJsonContext.Default.JsonPatch);
    }

    private static JsonNode? ParseNode(string json)
    {
        return JsonNode.Parse(json);
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

internal static class ToolResponseTestExtensions
{
    private static readonly ServiceProvider McpServerServices = CreateMcpServerServices();
    private static readonly McpServer McpServer = McpServerServices.GetRequiredService<McpServer>();

    public static Task<SetValueResult> SetValueCoreAsync(
        this SetValueTool tool,
        string key,
        JsonElement value,
        string? @namespace = null,
        long? ttlSeconds = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.SetValueAsync(
            key,
            value.GetRawText(),
            JsonInputFormat.Json,
            @namespace,
            ttlSeconds,
            null,
            false,
            null,
            cancellationToken
        );
    }

    public static Task<GetValueResult> GetValueCoreAsync(
        this GetValueTool tool,
        string key,
        string? @namespace = null,
        string? path = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.GetValueAsync(
            key,
            @namespace,
            ParsePointer(path),
            cancellationToken
        );
    }

    public static Task<GetValuesResult> GetValuesCoreAsync(
        this GetValuesTool tool,
        string[] keys,
        string? @namespace = null,
        string? path = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.GetValuesAsync(
            keys,
            @namespace,
            ParsePointer(path),
            cancellationToken
        );
    }

    public static Task<QueryValuesResult> QueryValuesCoreAsync(
        this QueryValuesTool tool,
        string? @namespace = null,
        string? pattern = null,
        string? query = null,
        bool hasEqualsArgument = false,
        JsonElement? equals = null,
        string? path = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.QueryValuesAsync(
            @namespace,
            pattern,
            query,
            equals?.GetRawText(),
            JsonInputFormat.Json,
            ParsePointer(path),
            null,
            null,
            CreateCallToolRequestContext(hasEqualsArgument, equals),
            cancellationToken
        );
    }

    public static Task<PatchValueResult> PatchValueCoreAsync(
        this PatchValueTool tool,
        string key,
        JsonPatch patch,
        string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.PatchValueAsync(
            key,
            JsonSerializer.Serialize(patch, JsonPatchJsonContext.Default.JsonPatch),
            @namespace,
            cancellationToken
        );
    }

    public static Task<ListKeysResult> ListKeysCoreAsync(
        this ListKeysTool tool,
        string? @namespace = null,
        string? pattern = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.ListKeysAsync(
            @namespace,
            pattern,
            null,
            null,
            cancellationToken
        );
    }

    public static Task<DeleteValueResult> DeleteValueCoreAsync(
        this DeleteValueTool tool,
        string key,
        string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.DeleteValueAsync(key, @namespace, cancellationToken);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static RequestContext<CallToolRequestParams>? CreateCallToolRequestContext(
        bool hasEqualsArgument,
        JsonElement? equals
    )
    {
        if (!hasEqualsArgument)
        {
            return null;
        }
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = new RequestId(1),
            Method = RequestMethods.ToolsCall
        };
        IDictionary<string, JsonElement> arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["equals"] = equals ?? ParseJsonElement("null")
        };
        return new RequestContext<CallToolRequestParams>(
            McpServer,
            request,
            new CallToolRequestParams
            {
                Name = QueryValuesTool.ToolName,
                Arguments = arguments
            }
        );
    }

    private static ServiceProvider CreateMcpServerServices()
    {
        var services = new ServiceCollection();
        McpRegistration.AddServer(services)
                       .WithStreamServerTransport(Stream.Null, Stream.Null);
        return services.BuildServiceProvider();
    }

    private static JsonPointer? ParsePointer(string? path)
    {
        return path is null ? null : JsonPointer.Parse(path, null);
    }
}
