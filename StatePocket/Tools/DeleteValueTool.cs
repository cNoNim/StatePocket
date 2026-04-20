using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
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
        var normalizedNamespace = ToolArgumentHelper.NormalizeNamespace(@namespace);
        bool deleted;
        try
        {
            deleted = await kvStore.DeleteValueAsync(normalizedNamespace, key, cancellationToken)
                                   .ConfigureAwait(false);
        }
        catch (KvStoreBusyException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        if (!deleted)
        {
            throw new McpException($"Key '{key}' was not found in namespace '{normalizedNamespace}'.");
        }
        return new DeleteValueResult
        {
            Namespace = normalizedNamespace,
            Key = key
        };
    }
}
