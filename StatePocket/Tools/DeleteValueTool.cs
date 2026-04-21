using System.ComponentModel;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Errors;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class DeleteValueTool(IKvStore kvStore)
{
    public const string ToolName = "delete_value";
    private const string ToolTitle = "Delete Value";

    [McpServerTool(
        Name = ToolName,
        Title = ToolTitle,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Deletes a key from the selected namespace.")]
    internal async Task<DeleteValueResult> DeleteValueAsync(
        [Description("Key to delete.")] string key,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolInvalidArgumentException.ThrowIfNull(key);
        ToolInvalidArgumentException.ThrowIfEmptyOrWhitespace(@namespace, nameof(@namespace));
        var normalizedNamespace = @namespace ?? ToolArgumentHelper.DefaultNamespace;
        var deleted = await kvStore.DeleteValueAsync(normalizedNamespace, key, cancellationToken)
                                   .ConfigureAwait(false);
        if (!deleted)
        {
            throw new ToolNotFoundException(normalizedNamespace, key);
        }
        return new DeleteValueResult
        {
            Namespace = normalizedNamespace,
            Key = key
        };
    }
}
