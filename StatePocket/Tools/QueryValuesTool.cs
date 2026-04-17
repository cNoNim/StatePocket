using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Storage;
using PathQuery = StatePocket.JsonPath.JsonPath;
using Pointer = StatePocket.JsonPointer.JsonPointer;
using JsonPointerException = StatePocket.JsonPointer.JsonPointerException;
using JsonPathException = StatePocket.JsonPath.JsonPathException;

namespace StatePocket.Tools;

internal sealed class QueryValuesTool(IKvStore kvStore)
{
    [McpServerTool(Name = "query_values", ReadOnly = true)]
    [Description(
        "Finds values in the selected namespace by key pattern and optional JSONPath filter, with optional equality and JSON Pointer projection. Pagination uses an opaque scan cursor over keys in ascending order."
    )]
    internal async Task<CallToolResult> QueryValuesAsync(
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional wildcard key pattern, for example 'user:*'.")] string? pattern = null,
        [Description(
            "Optional query used to filter stored JSON values. Use JSONPath syntax, for example '$.status' or '$.profile.name'. Omit to match by key pattern only."
        )]
        string? query = null,
        [Description(
            "Optional JSON value that at least one query match must equal. Requires query to be set. Pass explicit null to match JSON nulls."
        )]
        JsonElement? equals = null,
        [Description(
            "Optional path to project part of each matched JSON value. Use JSON Pointer syntax starting with '/', for example '/profile/name' or '/items/0'. Omit to return whole values."
        )]
        string? path = null,
        [Description(
            "Maximum number of matching values to return. Defaults to 50 and must be less than or equal to 100."
        )]
        int? limit = null,
        [Description(
            "Optional opaque cursor for pagination. Pass the `next_cursor` value from a previous response to continue scanning after the last emitted match. Because filtering is applied while scanning, a follow-up request may return no matches even when `next_cursor` was present."
        )]
        string? cursor = null,
        RequestContext<CallToolRequestParams>? requestContext = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedNamespace = ToolResultFactory.NormalizeNamespace(@namespace);
        var normalizedLimit = ToolResultFactory.NormalizeLimit(limit);
        var hasEqualsArgument = requestContext?.Params.Arguments?.ContainsKey("equals") ?? equals.HasValue;
        if (query is null && hasEqualsArgument)
        {
            throw new McpException("equals requires query.");
        }
        var (jsonPath, pointer) = ParseQueryAndPath(query, path);
        try
        {
            var pageResult = await LoadMatchingValuesAsync(
                    normalizedNamespace,
                    pattern,
                    cursor,
                    normalizedLimit,
                    jsonPath,
                    hasEqualsArgument,
                    equals,
                    pointer,
                    cancellationToken
                )
               .ConfigureAwait(false);
            var result = new QueryValuesResultData
            {
                Namespace = normalizedNamespace,
                Values = pageResult.Values,
                NextCursor = pageResult.NextCursor,
            };
            return ToolResultFactory.Success(result);
        }
        catch (JsonPathException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    private static (PathQuery? Query, Pointer? Pointer) ParseQueryAndPath(string? query, string? path)
    {
        try
        {
            return (query is null ? null : new PathQuery(query), path is null ? null : new Pointer(path));
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (JsonPathException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (JsonPointerException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    private static bool MatchesQuery(
        JsonElement document,
        PathQuery? query,
        bool hasEqualsArgument,
        JsonElement? equals
    )
    {
        if (query is null)
        {
            return true;
        }
        var matches = query.Evaluate(document);
        if (!hasEqualsArgument)
        {
            return matches.Count != 0;
        }
        return !equals.HasValue
          ? matches.Any(static match => match.Value.ValueKind == JsonValueKind.Null)
          : matches.Any(match => JsonEquals(match.Value, equals.Value));
    }

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        return PathQuery.DeepEquals(left, right);
    }

    private async Task<QueryPageResult> LoadMatchingValuesAsync(
        string @namespace,
        string? pattern,
        string? cursor,
        int limit,
        PathQuery? jsonPath,
        bool hasEqualsArgument,
        JsonElement? equals,
        Pointer? pointer,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, GetValuesEntryData> values = new(StringComparer.Ordinal);
        var nextScanCursor = cursor;
        string? nextResultCursor = null;
        while (values.Count < limit)
        {
            var page = await kvStore.ListValuesPageAsync(
                                         @namespace,
                                         pattern,
                                         nextScanCursor,
                                         ToolResultFactory.MaxResultItems,
                                         cancellationToken
                                     )
                                    .ConfigureAwait(false);
            if (page.Items.Count == 0)
            {
                break;
            }
            for (var index = 0; index < page.Items.Count; index++)
            {
                var (storedKey, storedValue) = page.Items[index];
                nextScanCursor = storedKey;
                if (!MatchesQuery(
                        storedValue.Value,
                        jsonPath,
                        hasEqualsArgument,
                        equals
                    ))
                {
                    continue;
                }
                values[storedKey] = GetValuesTool.ProjectValue(storedValue, pointer);
                if (values.Count == limit)
                {
                    nextResultCursor = index < page.Items.Count - 1 || page.NextCursor is not null ? storedKey : null;
                    return new QueryPageResult(values, nextResultCursor);
                }
            }
            if (page.NextCursor is null)
            {
                break;
            }
        }
        return new QueryPageResult(values, nextResultCursor);
    }

    private sealed record QueryPageResult(IReadOnlyDictionary<string, GetValuesEntryData> Values, string? NextCursor);
}
