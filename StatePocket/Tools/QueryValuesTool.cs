using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Json.Path;
using StatePocket.Json.Pointer;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class QueryValuesTool(IKvStore kvStore)
{
    public const string ToolName = "query_values";
    private const string ToolTitle = "Query Values";

    [McpServerTool(
        Name = ToolName,
        Title = ToolTitle,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Finds values in the selected namespace by key pattern and optional JSONPath filter, with optional equality and JSON Pointer projection. Pagination uses an opaque scan cursor over keys in ascending order."
    )]
    internal async Task<QueryValuesResult> QueryValuesAsync(
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional wildcard key pattern, for example 'user:*'.")] string? pattern = null,
        [Description(
            "Optional query used to filter stored JSON values. Use JSONPath syntax, for example '$.status' or '$.profile.name'. Omit to match by key pattern only."
        )]
        string? query = null,
        [Description(
            "Optional value that at least one query match must equal. Requires query. When format is 'json', provide JSON text. When format is 'text', the raw string is matched as a JSON string. Pass explicit null to match JSON nulls."
        )]
        string? equals = null,
        [Description(
            "How to interpret equals when it is provided. Use 'json' for JSON text or 'text' for a raw string. Defaults to 'json'."
        )]
        JsonInputFormat format = JsonInputFormat.Json,
        [Description(
            "Optional path to project part of each matched JSON value. Use JSON Pointer syntax starting with '/', for example '/profile/name' or '/items/0'. Omit to return whole values."
        )]
        JsonPointer? path = null,
        [Description(
            "Maximum number of matching values to return. Defaults to 50 and must be less than or equal to 100."
        )]
        int? limit = null,
        [Description(
            "Optional opaque cursor for pagination. Pass the `nextCursor` value from a previous response to continue scanning after the last emitted match. Because filtering is applied while scanning, a follow-up request may return no matches even when `nextCursor` was present."
        )]
        string? cursor = null,
        RequestContext<CallToolRequestParams>? requestContext = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolArgumentHelper.ValidateFormatArgument(format, requestContext);
        var parsedEquals = ParseEqualsArgument(equals, format, requestContext);
        var normalizedNamespace = ToolArgumentHelper.NormalizeNamespace(@namespace);
        var normalizedLimit = ToolArgumentHelper.NormalizeLimit(limit);
        if (query is null
         && parsedEquals.HasEqualsArgument)
        {
            throw new McpException("equals requires query.");
        }
        var jsonPath = ParseQuery(query);
        try
        {
            var pageResult = await LoadMatchingValuesAsync(
                    normalizedNamespace,
                    pattern,
                    cursor,
                    normalizedLimit,
                    jsonPath,
                    parsedEquals.HasEqualsArgument,
                    parsedEquals.Value,
                    path,
                    cancellationToken
                )
               .ConfigureAwait(false);
            return new QueryValuesResult
            {
                Namespace = normalizedNamespace,
                Values = pageResult.Values,
                NextCursor = pageResult.NextCursor
            };
        }
        catch (JsonPathException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    private static ParsedEqualsArgument ParseEqualsArgument(
        string? equals,
        JsonInputFormat format,
        RequestContext<CallToolRequestParams>? requestContext
    )
    {
        var hasEqualsArgument = requestContext?.Params.Arguments?.ContainsKey("equals") ?? equals is not null;
        if (!hasEqualsArgument)
        {
            return new ParsedEqualsArgument(false, null);
        }
        return equals is null
          ? new ParsedEqualsArgument(true, null)
          : new ParsedEqualsArgument(true, ToolArgumentHelper.ParseJsonValue(equals, format, nameof(equals)));
    }

    private static JsonPath? ParseQuery(string? query)
    {
        try
        {
            return query is null ? null : new JsonPath(query);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (JsonPathException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    private static bool MatchesQuery(
        JsonElement document,
        JsonPath? query,
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
        return JsonPath.DeepEquals(left, right);
    }

    private async Task<QueryPageResult> LoadMatchingValuesAsync(
        string @namespace,
        string? pattern,
        string? cursor,
        int limit,
        JsonPath? jsonPath,
        bool hasEqualsArgument,
        JsonElement? equals,
        JsonPointer? pointer,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, GetValuesEntry> values = new(StringComparer.Ordinal);
        var nextScanCursor = cursor;
        string? nextResultCursor = null;
        while (values.Count < limit)
        {
            var page = await kvStore.ListValuesPageAsync(
                                         @namespace,
                                         pattern,
                                         nextScanCursor,
                                         ToolArgumentHelper.MaxResultItems,
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

    private sealed record QueryPageResult(IReadOnlyDictionary<string, GetValuesEntry> Values, string? NextCursor);

    private sealed record ParsedEqualsArgument(bool HasEqualsArgument, JsonElement? Value);
}
