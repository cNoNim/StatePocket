using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Json.Pointer;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class GetValuesTool(IKvStore kvStore)
{
    public const string ToolName = "get_values";

    [McpServerTool(Name = ToolName, ReadOnly = true)]
    [Description(
        "Retrieves multiple values by key from the selected namespace, with optional JSON Pointer projection."
    )]
    internal async Task<CallToolResult> GetValuesAsync(
        [Description("Keys to retrieve. Maximum 100 keys per request.")] string[] keys,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description(
            "Optional path to project part of each stored JSON value. Use JSON Pointer syntax starting with '/', for example '/profile/name' or '/items/0'. Omit to return whole values."
        )]
        JsonPointer? path = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            if (key is null)
            {
                throw new McpException("keys must not contain null values.");
            }
        }
        if (keys.Length > ToolResultFactory.MaxResultItems)
        {
            throw new McpException(
                $"keys must contain less than or equal to {ToolResultFactory.MaxResultItems} items."
            );
        }
        var normalizedNamespace = ToolResultFactory.NormalizeNamespace(@namespace);
        var storedValues = await kvStore.GetValuesAsync(normalizedNamespace, keys, cancellationToken)
                                        .ConfigureAwait(false);
        Dictionary<string, GetValuesEntryData> values = new(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            values[key] = ProjectValue(storedValues.GetValueOrDefault(key), path);
        }
        var result = new GetValuesResultData
        {
            Namespace = normalizedNamespace,
            Values = values,
            NextCursor = null
        };
        return ToolResultFactory.Success(result);
    }

    internal static GetValuesEntryData ProjectValue(KvValue? storedValue, JsonPointer? pointer)
    {
        if (storedValue is null)
        {
            return new GetValuesEntryData
            {
                Found = false,
                PathFound = false
            };
        }
        if (pointer is null)
        {
            return new GetValuesEntryData
            {
                Found = true,
                PathFound = true,
                Value = storedValue.Value,
                ExpiresAt = storedValue.ExpiresAt
            };
        }
        return pointer.TryEvaluate(storedValue.Value, out var projectedValue)
          ? new GetValuesEntryData
            {
                Found = true,
                PathFound = true,
                Value = projectedValue,
                ExpiresAt = storedValue.ExpiresAt
            }
          : new GetValuesEntryData
            {
                Found = true,
                PathFound = false,
                ExpiresAt = storedValue.ExpiresAt
            };
    }
}
