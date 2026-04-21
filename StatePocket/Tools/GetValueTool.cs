using System.ComponentModel;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Errors;
using StatePocket.Json.Pointer;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class GetValueTool(IKvStore kvStore)
{
    public const string ToolName = "get_value";
    private const string ToolTitle = "Get Value";

    [McpServerTool(
        Name = ToolName,
        Title = ToolTitle,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Retrieves a single value by key from the selected namespace, with optional JSON Pointer projection.")]
    internal async Task<GetValueResult> GetValueAsync(
        [Description("Key to retrieve.")] string key,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description(
            "Optional path to project part of the stored JSON value. Use JSON Pointer syntax starting with '/', for example '/profile/name' or '/items/0'. Omit to return the whole value."
        )]
        JsonPointer? path = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolInvalidArgumentException.ThrowIfNull(key);
        ToolInvalidArgumentException.ThrowIfEmptyOrWhitespace(@namespace, nameof(@namespace));
        var normalizedNamespace = @namespace ?? ToolArgumentHelper.DefaultNamespace;
        var value = await kvStore.GetValueAsync(normalizedNamespace, key, cancellationToken)
                                 .ConfigureAwait(false);
        var projectedValue = GetValuesTool.ProjectValue(value, path);
        return new GetValueResult
        {
            Namespace = normalizedNamespace,
            Key = key,
            Found = projectedValue.Found,
            PathFound = projectedValue.PathFound,
            Value = projectedValue.Value,
            ExpiresAt = projectedValue.ExpiresAt,
            UpdatedAt = projectedValue.UpdatedAt,
            Revision = projectedValue.Revision
        };
    }
}
