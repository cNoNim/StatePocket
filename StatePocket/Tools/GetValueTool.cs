using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Json.Pointer;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class GetValueTool(IKvStore kvStore)
{
    public const string ToolName = "get_value";

    [McpServerTool(Name = ToolName, ReadOnly = true)]
    [Description("Retrieves a single value by key from the selected namespace, with optional JSON Pointer projection.")]
    internal async Task<CallToolResult> GetValueAsync(
        [Description("Key to retrieve.")] string key,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description(
            "Optional path to project part of the stored JSON value. Use JSON Pointer syntax starting with '/', for example '/profile/name' or '/items/0'. Omit to return the whole value."
        )]
        string? path = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedNamespace = ToolResultFactory.NormalizeNamespace(@namespace);
        JsonPointer? pointer;
        try
        {
            pointer = path is null ? null : new JsonPointer(path);
        }
        catch (JsonPointerException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        var value = await kvStore.GetValueAsync(normalizedNamespace, key, cancellationToken)
                                 .ConfigureAwait(false);
        var projectedValue = GetValuesTool.ProjectValue(value, pointer);
        var result = new GetValueResultData
        {
            Namespace = normalizedNamespace,
            Key = key,
            Found = projectedValue.Found,
            PathFound = projectedValue.PathFound,
            Value = projectedValue.Value,
            ExpiresAt = value?.ExpiresAt
        };
        return ToolResultFactory.Success(result);
    }
}
