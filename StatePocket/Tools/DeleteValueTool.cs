using System.ComponentModel;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Exceptions;
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
    [Description("Deletes one key from a namespace and reports whether anything was removed.")]
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
        return new DeleteValueResult
        {
            Namespace = normalizedNamespace,
            Key = key,
            Deleted = deleted is not null,
            DeletedValue = deleted?.Value
        };
    }
}
