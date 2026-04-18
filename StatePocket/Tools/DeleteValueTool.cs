using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class DeleteValueTool(IKvStore kvStore)
{
    public const string ToolName = "delete_value";

    [McpServerTool(Name = ToolName)]
    [Description("Deletes a key from the selected namespace.")]
    internal async Task<CallToolResult> DeleteValueAsync(
        [Description("Key to delete.")] string key,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedNamespace = ToolResultFactory.NormalizeNamespace(@namespace);
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
        var result = new DeleteValueResultData
        {
            Namespace = normalizedNamespace,
            Key = key
        };
        return ToolResultFactory.Success(result);
    }
}
