using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using StatePocket.Configuration;
using StatePocket.Contracts;
using StatePocket.Json.Patch;
using StatePocket.Json.Pointer;
using StatePocket.Storage;
using StatePocket.Tools;

namespace StatePocket.Tests.Tools;

public sealed class ToolResponseTests : IDisposable
{
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
    public async Task SetValue_ReturnsStructuredContent()
    {
        SetValueTool tool = new(_store);
        var result = await tool.SetValueAsync(
            "smoke.test",
            ParseJson("\"ok\""),
            cancellationToken: CancellationToken.None
        );
        var data = DeserializeStructuredContent<SetValueResultData>(result);
        Assert.Equal("default", data.Namespace);
        Assert.Equal("smoke.test", data.Key);
        Assert.Null(data.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", data.UpdatedAt);
        Assert.Equal(1, data.Revision);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task SetValue_ReturnsExpiresAtWhenTtlIsSet()
    {
        SetValueTool tool = new(_store);
        var result = await tool.SetValueAsync(
            "smoke.test",
            ParseJson("\"ok\""),
            "codex",
            60,
            cancellationToken: CancellationToken.None
        );
        var data = DeserializeStructuredContent<SetValueResultData>(result);
        Assert.Equal("codex", data.Namespace);
        Assert.Equal("smoke.test", data.Key);
        Assert.Equal("2026-04-14T10:01:00.0000000Z", data.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", data.UpdatedAt);
        Assert.Equal(1, data.Revision);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task SetValue_SupportsExpectedRevisionCompareAndSet()
    {
        SetValueTool tool = new(_store);
        var firstWrite = await tool.SetValueAsync(
            "cas",
            ParseJson("\"first\""),
            "codex",
            cancellationToken: CancellationToken.None
        );
        var firstData = DeserializeStructuredContent<SetValueResultData>(firstWrite);
        var secondWrite = await tool.SetValueAsync(
            "cas",
            ParseJson("\"second\""),
            "codex",
            expectedRevision: firstData.Revision,
            cancellationToken: CancellationToken.None
        );
        var secondData = DeserializeStructuredContent<SetValueResultData>(secondWrite);
        Assert.Equal(2, secondData.Revision);
        AssertTextMatchesStructuredContent(secondWrite);
    }

    [Fact]
    public async Task SetValue_ThrowsMcpExceptionWhenExpectedRevisionConflicts()
    {
        SetValueTool tool = new(_store);
        await tool.SetValueAsync(
            "cas",
            ParseJson("\"first\""),
            "codex",
            cancellationToken: CancellationToken.None
        );
        var exception = await Assert.ThrowsAsync<McpException>(() => tool.SetValueAsync(
                "cas",
                ParseJson("\"second\""),
                "codex",
                expectedRevision: 99,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal(
            "Revision conflict for key 'cas' in namespace 'codex'. Expected revision 99, found 1.",
            exception.Message
        );
        Assert.IsType<KvStoreConflictException>(exception.InnerException);
    }

    [Fact]
    public async Task SetValue_SupportsIfAbsentWhenKeyDoesNotExist()
    {
        SetValueTool tool = new(_store);
        var result = await tool.SetValueAsync(
            "claimed",
            ParseJson("\"first\""),
            "codex",
            ifAbsent: true,
            cancellationToken: CancellationToken.None
        );
        var data = DeserializeStructuredContent<SetValueResultData>(result);
        Assert.Equal(1, data.Revision);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task SetValue_ThrowsMcpExceptionWhenIfAbsentConflicts()
    {
        SetValueTool tool = new(_store);
        await tool.SetValueAsync(
            "claimed",
            ParseJson("\"first\""),
            "codex",
            cancellationToken: CancellationToken.None
        );
        var exception = await Assert.ThrowsAsync<McpException>(() => tool.SetValueAsync(
                "claimed",
                ParseJson("\"second\""),
                "codex",
                ifAbsent: true,
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal("Key 'claimed' already exists in namespace 'codex'.", exception.Message);
        Assert.IsType<KvStoreConflictException>(exception.InnerException);
    }

    [Fact]
    public async Task SetValue_ThrowsMcpExceptionWhenIfAbsentIsCombinedWithExpectedRevision()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() => tool.SetValueAsync(
                "claimed",
                ParseJson("\"value\""),
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
            ParseJson("\"first\""),
            "codex",
            expectedRevision: null,
            ifAbsent: true,
            cancellationToken: CancellationToken.None
        );
        var data = DeserializeStructuredContent<SetValueResultData>(result);
        Assert.Equal(1, data.Revision);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task SetValue_ThrowsMcpExceptionWhenDatabaseIsLocked()
    {
        await using var lockHandle = await AcquireWriteLockAsync();
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() => tool.SetValueAsync(
                "smoke.test",
                ParseJson("\"ok\""),
                cancellationToken: CancellationToken.None
            )
        );
        Assert.Equal("The database is busy with another write operation. Try again.", exception.Message);
        Assert.IsType<KvStoreBusyException>(exception.InnerException);
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
        var data = DeserializeStructuredContent<GetValueResultData>(result);
        Assert.True(data.Found);
        Assert.True(data.PathFound);
        Assert.Equal("codex", data.Namespace);
        Assert.Equal("present", data.Key);
        Assert.True(data.Value.HasValue);
        Assert.Equal(JsonValueKind.Object, data.Value.Value.ValueKind);
        Assert.NotNull(data.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", data.UpdatedAt);
        Assert.Equal(1, data.Revision);
        AssertTextMatchesStructuredContent(result);
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
        var callToolResult = await getTool.GetValueAsync(
            "profile",
            "codex",
            ParsePointer("/nested/value"),
            CancellationToken.None
        );
        AssertTextMatchesStructuredContent(callToolResult);
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
    public async Task ListKeys_ReturnsStructuredContent()
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
        var data = DeserializeStructuredContent<ListKeysResultData>(result);
        Assert.Equal("codex", data.Namespace);
        Assert.Equal(["one"], data.Keys);
        Assert.Null(data.NextCursor);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task ListNamespaces_ReturnsStructuredContent()
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
        var data = DeserializeStructuredContent<ListNamespacesResultData>(result);
        Assert.Equal(["alpha", "beta"], data.Namespaces);
        Assert.Null(data.NextCursor);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task ListKeys_UsesDefaultLimitAndReturnsNextCursor()
    {
        ListKeysTool listTool = new(_store);
        SetValueTool setTool = new(_store);
        foreach (var index in Enumerable.Range(0, ToolResultFactory.DefaultPageSize + 1))
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
        var data = DeserializeStructuredContent<ListKeysResultData>(result);
        Assert.Equal(ToolResultFactory.DefaultPageSize, data.Keys.Count);
        Assert.Equal("key-49", data.Keys[^1]);
        Assert.Equal("key-49", data.NextCursor);
        AssertTextMatchesStructuredContent(result);
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
        var firstPageData = DeserializeStructuredContent<ListNamespacesResultData>(firstPage);
        Assert.Equal(["alpha", "beta"], firstPageData.Namespaces);
        Assert.Equal("beta", firstPageData.NextCursor);
        AssertTextMatchesStructuredContent(firstPage);
        var secondPage = await listTool.ListNamespacesAsync(
            null,
            2,
            firstPageData.NextCursor,
            CancellationToken.None
        );
        var secondPageData = DeserializeStructuredContent<ListNamespacesResultData>(secondPage);
        Assert.Equal(["gamma"], secondPageData.Namespaces);
        Assert.Null(secondPageData.NextCursor);
        AssertTextMatchesStructuredContent(secondPage);
    }

    [Fact]
    public async Task ListKeys_ThrowsMcpExceptionWhenLimitExceedsHardCap()
    {
        ListKeysTool listTool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() => listTool.ListKeysAsync(
                "codex",
                null,
                ToolResultFactory.MaxResultItems + 1,
                null,
                CancellationToken.None
            )
        );
        Assert.Equal("limit must be less than or equal to 100.", exception.Message);
    }

    [Fact]
    public async Task GetValues_ReturnsStructuredContent()
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
        var data = DeserializeStructuredContent<GetValuesResultData>(result);
        Assert.Equal("codex", data.Namespace);
        Assert.Equal(
            "\"a\"",
            data.Values["one"]
                .Value?.GetRawText()
        );
        Assert.True(data.Values["one"].Found);
        Assert.True(data.Values["one"].PathFound);
        Assert.NotNull(data.Values["one"].ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", data.Values["one"].UpdatedAt);
        Assert.Equal(1, data.Values["one"].Revision);
        Assert.True(data.Values["two"].Found);
        Assert.True(data.Values["two"].PathFound);
        Assert.Null(data.Values["two"].ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", data.Values["two"].UpdatedAt);
        Assert.Equal(2, data.Values["two"].Revision);
        var structuredContent = result.StructuredContent
                             ?? throw new InvalidOperationException("Expected structured content.");
        Assert.Equal(
            JsonValueKind.Null,
            structuredContent.GetProperty("values")
                             .GetProperty("two")
                             .GetProperty("value")
                             .ValueKind
        );
        Assert.False(data.Values["missing"].Found);
        Assert.False(data.Values["missing"].PathFound);
        Assert.Null(data.Values["missing"].Value);
        Assert.Null(data.Values["missing"].ExpiresAt);
        Assert.Null(data.Values["missing"].UpdatedAt);
        Assert.Null(data.Values["missing"].Revision);
        Assert.Null(data.NextCursor);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task GetValues_ReturnsSerializedStructuredContentWhenKeysAreEmpty()
    {
        GetValuesTool getValuesTool = new(_store);
        var result = await getValuesTool.GetValuesAsync(
            [],
            "codex",
            null,
            CancellationToken.None
        );
        var data = DeserializeStructuredContent<GetValuesResultData>(result);
        Assert.Equal("codex", data.Namespace);
        Assert.Empty(data.Values);
        Assert.Null(data.NextCursor);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task GetValues_TextMatchesStructuredContentWhenKeysContainDuplicates()
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
        var data = DeserializeStructuredContent<GetValuesResultData>(result);
        Assert.Equal(["one"], data.Values.Keys);
        Assert.Null(data.NextCursor);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task GetValues_ThrowsMcpExceptionWhenKeyCountExceedsHardCap()
    {
        GetValuesTool getValuesTool = new(_store);
        var keys = Enumerable.Range(0, ToolResultFactory.MaxResultItems + 1)
                             .Select(static index => $"key-{index:D3}")
                             .ToArray();
        var exception = await Assert.ThrowsAsync<McpException>(() => getValuesTool.GetValuesAsync(
                keys,
                "codex",
                null,
                CancellationToken.None
            )
        );
        Assert.Equal("keys must contain less than or equal to 100 items.", exception.Message);
    }

    [Fact]
    public async Task GetValues_ThrowsMcpExceptionWhenKeysContainNull()
    {
        GetValuesTool getValuesTool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() =>
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
            ParseJson("\"active\""),
            ParsePointer("/profile/name"),
            cancellationToken: CancellationToken.None
        );
        var data = DeserializeStructuredContent<QueryValuesResultData>(result);
        Assert.Equal("codex", data.Namespace);
        Assert.Equal(["alpha"], data.Values.Keys);
        Assert.True(data.Values["alpha"].Found);
        Assert.True(data.Values["alpha"].PathFound);
        Assert.NotNull(data.Values["alpha"].ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", data.Values["alpha"].UpdatedAt);
        Assert.Equal(1, data.Values["alpha"].Revision);
        Assert.Equal(
            "\"A\"",
            data.Values["alpha"]
                .Value?.GetRawText()
        );
        Assert.Null(data.NextCursor);
        AssertTextMatchesStructuredContent(result);
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
        var firstPageData = DeserializeStructuredContent<QueryValuesResultData>(firstPage);
        Assert.Equal(["alpha", "beta"], firstPageData.Values.Keys);
        Assert.Equal("beta", firstPageData.NextCursor);
        AssertTextMatchesStructuredContent(firstPage);
        var secondPage = await queryTool.QueryValuesAsync(
            "codex",
            "*",
            "$.status",
            path: ParsePointer("/status"),
            limit: 2,
            cursor: firstPageData.NextCursor,
            cancellationToken: CancellationToken.None
        );
        var secondPageData = DeserializeStructuredContent<QueryValuesResultData>(secondPage);
        Assert.Equal(["gamma"], secondPageData.Values.Keys);
        Assert.Null(secondPageData.NextCursor);
        AssertTextMatchesStructuredContent(secondPage);
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
    public async Task QueryValuesCore_ThrowsMcpExceptionWhenEqualsIsPassedWithoutQuery()
    {
        QueryValuesTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() => tool.QueryValuesCoreAsync(
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
    public async Task QueryValuesCore_ThrowsMcpExceptionWhenQueryIsMalformed()
    {
        QueryValuesTool tool = new(_store);
        await Assert.ThrowsAsync<McpException>(() => tool.QueryValuesCoreAsync(
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
    public async Task QueryValuesCore_ThrowsMcpExceptionWhenTopLevelCurrentNodeRootIsUsed()
    {
        QueryValuesTool tool = new(_store);
        await Assert.ThrowsAsync<McpException>(() => tool.QueryValuesCoreAsync(
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
    public async Task QueryValuesCore_ThrowsMcpExceptionWhenRegexPatternIsInvalid()
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
        await Assert.ThrowsAsync<McpException>(() => tool.QueryValuesCoreAsync(
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
    public async Task QueryValuesCore_ThrowsMcpExceptionWhenRegexPatternIsInvalidAndNoRowsAreScanned()
    {
        QueryValuesTool tool = new(_store);
        await Assert.ThrowsAsync<McpException>(() => tool.QueryValuesCoreAsync(
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
    public async Task QueryValuesCore_ThrowsMcpExceptionWhenRuntimeRegexPatternFromDataIsInvalid()
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
        await Assert.ThrowsAsync<McpException>(() => tool.QueryValuesCoreAsync(
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
    public async Task DeleteValue_ReturnsStructuredContent()
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
        var data = DeserializeStructuredContent<DeleteValueResultData>(result);
        Assert.Equal("codex", data.Namespace);
        Assert.Equal("gone", data.Key);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task DeleteValueCore_ThrowsMcpExceptionWhenKeyIsMissing()
    {
        DeleteValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() =>
            tool.DeleteValueCoreAsync("missing", "codex", CancellationToken.None)
        );
        Assert.Equal("Key 'missing' was not found in namespace 'codex'.", exception.Message);
    }

    [Fact]
    public async Task DeleteValue_ThrowsMcpExceptionWhenKeyIsMissing()
    {
        DeleteValueTool tool = new(_store);
        var exception =
            await Assert.ThrowsAsync<McpException>(() => tool.DeleteValueAsync(
                    "missing",
                    "codex",
                    CancellationToken.None
                )
            );
        Assert.Equal("Key 'missing' was not found in namespace 'codex'.", exception.Message);
    }

    [Fact]
    public async Task DeleteValue_ThrowsMcpExceptionWhenDatabaseIsLocked()
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
        var exception = await Assert.ThrowsAsync<McpException>(() =>
            deleteTool.DeleteValueAsync("gone", "codex", CancellationToken.None)
        );
        Assert.Equal("The database is busy with another write operation. Try again.", exception.Message);
        Assert.IsType<KvStoreBusyException>(exception.InnerException);
    }

    [Fact]
    public async Task PatchValue_ReturnsStructuredContent()
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
            Patch(Replace("/name", "\"new\"")),
            "codex",
            CancellationToken.None
        );
        var data = DeserializeStructuredContent<PatchValueResultData>(result);
        Assert.Equal("codex", data.Namespace);
        Assert.Equal("profile", data.Key);
        Assert.Equal("{\"name\":\"new\"}", data.Value.GetRawText());
        Assert.Null(data.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", data.UpdatedAt);
        Assert.Equal(2, data.Revision);
        AssertTextMatchesStructuredContent(result);
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
            Patch(Replace("/name", "\"new\"")),
            "codex",
            CancellationToken.None
        );
        var data = DeserializeStructuredContent<PatchValueResultData>(result);
        Assert.Equal("codex", data.Namespace);
        Assert.Equal("profile", data.Key);
        Assert.Equal("{\"name\":\"new\"}", data.Value.GetRawText());
        Assert.Equal("2026-04-14T10:01:00.0000000Z", data.ExpiresAt);
        Assert.Equal("2026-04-14T10:00:00.0000000Z", data.UpdatedAt);
        Assert.Equal(2, data.Revision);
        AssertTextMatchesStructuredContent(result);
    }

    [Fact]
    public async Task PatchValueCore_ThrowsMcpExceptionWhenKeyIsMissing()
    {
        PatchValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() => tool.PatchValueCoreAsync(
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
    public async Task PatchValueCore_ThrowsMcpExceptionForInvalidPatch()
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
        await Assert.ThrowsAsync<McpException>(() => updateTool.PatchValueCoreAsync(
                "profile",
                Patch(Test("/name", "\"other\"")),
                "codex",
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task PatchValue_ThrowsMcpExceptionWhenDatabaseIsLocked()
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
        var exception = await Assert.ThrowsAsync<McpException>(() => patchTool.PatchValueAsync(
                "profile",
                Patch(Replace("/name", "\"new\"")),
                "codex",
                CancellationToken.None
            )
        );
        Assert.Equal("The database is busy with another write operation. Try again.", exception.Message);
        Assert.IsType<KvStoreBusyException>(exception.InnerException);
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
    public async Task SetValueCore_ThrowsMcpExceptionForInvalidInput()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() =>
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
    public async Task SetValueCore_ThrowsMcpExceptionForInvalidExpectedRevision()
    {
        SetValueTool tool = new(_store);
        var exception = await Assert.ThrowsAsync<McpException>(() => tool.SetValueAsync(
                "bad",
                ParseJson("\"value\""),
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
    public Task SetValueCore_ThrowsMcpExceptionWhenNamespaceIsInvalid()
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
    public Task GetValueCore_ThrowsMcpExceptionWhenNamespaceIsInvalid()
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
    public Task GetValuesCore_ThrowsMcpExceptionWhenNamespaceIsInvalid()
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
    public Task QueryValuesCore_ThrowsMcpExceptionWhenNamespaceIsInvalid()
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
    public Task ListKeysCore_ThrowsMcpExceptionWhenNamespaceIsInvalid()
    {
        ListKeysTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace =>
            tool.ListKeysCoreAsync(invalidNamespace, null, CancellationToken.None)
        );
    }

    [Fact]
    public Task DeleteValueCore_ThrowsMcpExceptionWhenNamespaceIsInvalid()
    {
        DeleteValueTool tool = new(_store);
        return AssertInvalidNamespaceRejectedAsync(invalidNamespace =>
            tool.DeleteValueCoreAsync("key", invalidNamespace, CancellationToken.None)
        );
    }

    [Fact]
    public Task PatchValueCore_ThrowsMcpExceptionWhenNamespaceIsInvalid()
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
    public void GetValueResultData_UsesCamelCaseJsonFieldNames()
    {
        var json = JsonSerializer.SerializeToElement(
            new GetValueResultData
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
    public void SetValueResultData_UsesCamelCaseJsonFieldNames()
    {
        var json = JsonSerializer.SerializeToElement(
            new SetValueResultData
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
    public void PatchValueResultData_UsesCamelCaseJsonFieldNames()
    {
        var json = JsonSerializer.SerializeToElement(
            new PatchValueResultData
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
    public void GetValuesEntryData_UsesCamelCaseJsonFieldNames()
    {
        var json = JsonSerializer.SerializeToElement(
            new GetValuesEntryData
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
    public void GetValuesResultData_UsesCamelCaseJsonFieldNames()
    {
        var json = JsonSerializer.SerializeToElement(
            new GetValuesResultData
            {
                Namespace = "codex",
                Values = new Dictionary<string, GetValuesEntryData>(StringComparer.Ordinal),
                NextCursor = "cursor-1"
            }
        );
        Assert.True(json.TryGetProperty("nextCursor", out _));
        Assert.False(json.TryGetProperty("next_cursor", out _));
    }

    [Fact]
    public void QueryValuesResultData_UsesCamelCaseJsonFieldNames()
    {
        var json = JsonSerializer.SerializeToElement(
            new QueryValuesResultData
            {
                Namespace = "codex",
                Values = new Dictionary<string, GetValuesEntryData>(StringComparer.Ordinal),
                NextCursor = "cursor-1"
            }
        );
        Assert.True(json.TryGetProperty("nextCursor", out _));
        Assert.False(json.TryGetProperty("next_cursor", out _));
    }

    [Fact]
    public void ListKeysResultData_UsesCamelCaseJsonFieldNames()
    {
        var json = JsonSerializer.SerializeToElement(
            new ListKeysResultData
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
    public void ListNamespacesResultData_UsesCamelCaseJsonFieldNames()
    {
        var json = JsonSerializer.SerializeToElement(
            new ListNamespacesResultData
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

    private static async Task AssertInvalidNamespaceRejectedAsync(Func<string, Task> action)
    {
        foreach (var invalidNamespace in new[]
                 {
                     "", "   "
                 })
        {
            var exception = await Assert.ThrowsAsync<McpException>(() => action(invalidNamespace));
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

    private static T DeserializeStructuredContent<T>(CallToolResult result)
    {
        var structuredContent = result.StructuredContent
                             ?? throw new InvalidOperationException("Expected structured content.");
        return structuredContent.Deserialize<T>()
            ?? throw new InvalidOperationException($"Failed to deserialize structured content as '{typeof(T).Name}'.");
    }

    private static string GetTextContent(CallToolResult result)
    {
        return string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>()
                  .Select(static block => block.Text)
        );
    }

    private static void AssertTextMatchesStructuredContent(CallToolResult result)
    {
        var structuredContent = result.StructuredContent
                             ?? throw new InvalidOperationException("Expected structured content.");
        Assert.Equal(structuredContent.GetRawText(), GetTextContent(result));
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
    public static async Task<SetValueResultData> SetValueCoreAsync(
        this SetValueTool tool,
        string key,
        JsonElement value,
        string? @namespace = null,
        long? ttlSeconds = null,
        CancellationToken cancellationToken = default
    )
    {
        return DeserializeStructuredContent<SetValueResultData>(
            await tool.SetValueAsync(
                           key,
                           value,
                           @namespace,
                           ttlSeconds,
                           cancellationToken: cancellationToken
                       )
                      .ConfigureAwait(false)
        );
    }

    public static async Task<GetValueResultData> GetValueCoreAsync(
        this GetValueTool tool,
        string key,
        string? @namespace = null,
        string? path = null,
        CancellationToken cancellationToken = default
    )
    {
        return DeserializeStructuredContent<GetValueResultData>(
            await tool.GetValueAsync(
                           key,
                           @namespace,
                           ParsePointer(path),
                           cancellationToken
                       )
                      .ConfigureAwait(false)
        );
    }

    public static async Task<GetValuesResultData> GetValuesCoreAsync(
        this GetValuesTool tool,
        string[] keys,
        string? @namespace = null,
        string? path = null,
        CancellationToken cancellationToken = default
    )
    {
        return DeserializeProjectedValuesResult<GetValuesResultData>(
            await tool.GetValuesAsync(
                           keys,
                           @namespace,
                           ParsePointer(path),
                           cancellationToken
                       )
                      .ConfigureAwait(false)
        );
    }

    public static async Task<QueryValuesResultData> QueryValuesCoreAsync(
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
        return DeserializeProjectedValuesResult<QueryValuesResultData>(
            await tool.QueryValuesAsync(
                           @namespace,
                           pattern,
                           query,
                           hasEqualsArgument && !equals.HasValue ? ParseJsonElement("null") : equals,
                           ParsePointer(path),
                           null,
                           null,
                           null,
                           cancellationToken
                       )
                      .ConfigureAwait(false)
        );
    }

    public static async Task<ListKeysResultData> ListKeysCoreAsync(
        this ListKeysTool tool,
        string? @namespace = null,
        string? pattern = null,
        CancellationToken cancellationToken = default
    )
    {
        return DeserializeStructuredContent<ListKeysResultData>(
            await tool.ListKeysAsync(
                           @namespace,
                           pattern,
                           null,
                           null,
                           cancellationToken
                       )
                      .ConfigureAwait(false)
        );
    }

    public static async Task<DeleteValueResultData> DeleteValueCoreAsync(
        this DeleteValueTool tool,
        string key,
        string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        return DeserializeStructuredContent<DeleteValueResultData>(
            await tool.DeleteValueAsync(key, @namespace, cancellationToken)
                      .ConfigureAwait(false)
        );
    }

    public static async Task<PatchValueResultData> PatchValueCoreAsync(
        this PatchValueTool tool,
        string key,
        JsonPatch patch,
        string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        return DeserializeStructuredContent<PatchValueResultData>(
            await tool.PatchValueAsync(
                           key,
                           patch,
                           @namespace,
                           cancellationToken
                       )
                      .ConfigureAwait(false)
        );
    }

    private static T DeserializeStructuredContent<T>(CallToolResult result)
    {
        var structuredContent = result.StructuredContent
                             ?? throw new InvalidOperationException("Expected structured content.");
        return structuredContent.Deserialize<T>()
            ?? throw new InvalidOperationException($"Failed to deserialize structured content as '{typeof(T).Name}'.");
    }

    private static T DeserializeProjectedValuesResult<T>(CallToolResult result)
        where T : class
    {
        var structuredContent = result.StructuredContent
                             ?? throw new InvalidOperationException("Expected structured content.");
        var @namespace = structuredContent.GetProperty("namespace")
                                          .GetString()
                      ?? throw new InvalidOperationException("Expected namespace.");
        var nextCursor =
            structuredContent.TryGetProperty("nextCursor", out var nextCursorElement)
         && nextCursorElement.ValueKind != JsonValueKind.Null
              ? nextCursorElement.GetString()
              : null;
        Dictionary<string, GetValuesEntryData> values = new(StringComparer.Ordinal);
        foreach (var property in structuredContent.GetProperty("values")
                                                  .EnumerateObject())
        {
            var entry = property.Value;
            values[property.Name] = new GetValuesEntryData
            {
                Found = entry.GetProperty("found")
                             .GetBoolean(),
                PathFound = entry.GetProperty("pathFound")
                                 .GetBoolean(),
                Value = entry.TryGetProperty("value", out var valueElement) ? valueElement.Clone() : null,
                ExpiresAt = entry.TryGetProperty("expiresAt", out var expiresAtElement)
                         && expiresAtElement.ValueKind != JsonValueKind.Null
                  ? expiresAtElement.GetString()
                  : null,
                UpdatedAt = entry.TryGetProperty("updatedAt", out var updatedAtElement)
                         && updatedAtElement.ValueKind != JsonValueKind.Null
                  ? updatedAtElement.GetString()
                  : null,
                Revision = entry.TryGetProperty("revision", out var revisionElement)
                        && revisionElement.ValueKind != JsonValueKind.Null
                  ? revisionElement.GetInt64()
                  : null
            };
        }
        return typeof(T) == typeof(GetValuesResultData) ? (T)(object)new GetValuesResultData
            {
                Namespace = @namespace,
                Values = values,
                NextCursor = nextCursor
            } :
            typeof(T) == typeof(QueryValuesResultData) ? (T)(object)new QueryValuesResultData
            {
                Namespace = @namespace,
                Values = values,
                NextCursor = nextCursor
            } : throw new InvalidOperationException($"Unsupported result type '{typeof(T).Name}'.");
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonPointer? ParsePointer(string? path)
    {
        return path is null ? null : JsonPointer.Parse(path, null);
    }
}
