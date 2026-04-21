using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Errors;
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
        "Finds values in the selected namespace by key pattern and optional JSONPath filter, with optional equality and JSON Pointer projection. Pagination resumes after the last emitted key in ascending order. When present, returned revisions are monotonic and scoped to the namespace, not the key."
    )]
    internal async Task<QueryValuesResult> QueryValuesAsync(
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional wildcard key pattern, for example 'user:*'.")] string? pattern = null,
        [Description(
            "Optional query used to filter stored JSON values. Use JSONPath syntax, for example '$.status' or '$.profile.name'. Omit to match by key pattern only."
        )]
        string? query = null,
        [Description(
            "Optional value that at least one query match must equal. Requires query. When format is 'json', provide JSON text. When format is 'text', the raw string is matched as a JSON string. Example: query '$.age' with equals '26' and format 'json' matches a numeric field, while query '$.tags[*]' with equals 'admin' and format 'text' matches when any tag equals the string 'admin'. Pass explicit null to match JSON nulls."
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
            "Optional cursor for pagination. Pass the last key returned in `nextCursor` from a previous response to continue scanning after the last emitted match. Because filtering is applied while scanning, a follow-up request may return no matches even when `nextCursor` was present."
        )]
        string? cursor = null,
        RequestContext<CallToolRequestParams>? requestContext = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolInvalidArgumentException.ThrowIfEmptyOrWhitespace(@namespace, nameof(@namespace));
        ToolArgumentHelper.ThrowIfInvalidJsonInputFormat(format, requestContext);
        ToolInvalidArgumentException.ThrowIfOutOfRange(limit, 1, ToolArgumentHelper.MaxResultItems);
        var parsedEquals = ParseEqualsArgument(equals, format, requestContext);
        ToolArgumentHelper.ThrowIfEqualsRequiresQuery(query, parsedEquals.HasEqualsArgument);
        var jsonPath = ParseQuery(query);
        var normalizedNamespace = @namespace ?? ToolArgumentHelper.DefaultNamespace;
        var normalizedLimit = limit ?? ToolArgumentHelper.DefaultPageSize;
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

    private static ParsedEqualsArgument ParseEqualsArgument(
        string? equals,
        JsonInputFormat format,
        RequestContext<CallToolRequestParams>? requestContext,
        [CallerArgumentExpression(nameof(equals))] string? equalsArgumentName = null
    )
    {
        var hasEqualsArgument = requestContext?.Params.Arguments?.ContainsKey("equals") ?? equals is not null;
        if (!hasEqualsArgument)
        {
            return new ParsedEqualsArgument(false, null);
        }
        return equals is null
          ? new ParsedEqualsArgument(true, null)
          : new ParsedEqualsArgument(true, ToolArgumentHelper.ParseJsonValue(equals, format, equalsArgumentName));
    }

    private static JsonPath? ParseQuery(string? query)
    {
        try
        {
            return query is null ? null : new JsonPath(query);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonPathException)
        {
            throw new ToolInvalidQueryException(exception.Message, innerException: exception);
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
        IReadOnlyList<JsonPathMatch> matches;
        try
        {
            matches = query.Evaluate(document);
        }
        catch (JsonPathException exception)
        {
            throw new ToolInvalidQueryException(exception.Message, innerException: exception);
        }
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
