using System.ComponentModel;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Errors;
using StatePocket.Json.Pointer;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class GetValuesTool(IKvStore kvStore)
{
    public const string ToolName = "get_values";
    private const string ToolTitle = "Get Values";

    [McpServerTool(
        Name = ToolName,
        Title = ToolTitle,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Retrieves multiple values by key from the selected namespace, with optional JSON Pointer projection."
    )]
    internal async Task<GetValuesResult> GetValuesAsync(
        [Description("Keys to retrieve. Maximum 100 keys per request.")] string[] keys,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description(
            "Optional path to project part of each stored JSON value. Use JSON Pointer syntax starting with '/', for example '/profile/name' or '/items/0'. Omit to return whole values."
        )]
        JsonPointer? path = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolInvalidArgumentException.ThrowIfNull(keys);
        ToolInvalidArgumentException.ThrowIfContainsNull(keys);
        ToolInvalidArgumentException.ThrowIfCountExceeds(keys, ToolArgumentHelper.MaxResultItems);
        ToolInvalidArgumentException.ThrowIfEmptyOrWhitespace(@namespace, nameof(@namespace));
        var normalizedNamespace = @namespace ?? ToolArgumentHelper.DefaultNamespace;
        var storedValues = await kvStore.GetValuesAsync(normalizedNamespace, keys, cancellationToken)
                                        .ConfigureAwait(false);
        Dictionary<string, GetValuesEntry> values = new(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            values[key] = ProjectValue(storedValues.GetValueOrDefault(key), path);
        }
        return new GetValuesResult
        {
            Namespace = normalizedNamespace,
            Values = values,
            NextCursor = null
        };
    }

    internal static GetValuesEntry ProjectValue(KvValue? storedValue, JsonPointer? pointer)
    {
        if (storedValue is null)
        {
            return new GetValuesEntry
            {
                Found = false,
                PathFound = false
            };
        }
        if (pointer is null)
        {
            return new GetValuesEntry
            {
                Found = true,
                PathFound = true,
                Value = storedValue.Value,
                ExpiresAt = storedValue.ExpiresAt,
                UpdatedAt = storedValue.UpdatedAt,
                Revision = storedValue.Revision
            };
        }
        return pointer.Value.TryEvaluate(storedValue.Value, out var projectedValue)
          ? new GetValuesEntry
            {
                Found = true,
                PathFound = true,
                Value = projectedValue,
                ExpiresAt = storedValue.ExpiresAt,
                UpdatedAt = storedValue.UpdatedAt,
                Revision = storedValue.Revision
            }
          : new GetValuesEntry
            {
                Found = true,
                PathFound = false,
                ExpiresAt = storedValue.ExpiresAt,
                UpdatedAt = storedValue.UpdatedAt,
                Revision = storedValue.Revision
            };
    }
}
