using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class ListKeysTool(IKvStore kvStore)
{
    public const string ToolName = "list_keys";

    [McpServerTool(Name = ToolName, ReadOnly = true)]
    [Description("Lists keys in the selected namespace, optionally filtered by a wildcard pattern.")]
    internal async Task<CallToolResult> ListKeysAsync(
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional wildcard key pattern, for example 'user:*'.")] string? pattern = null,
        [Description("Maximum number of keys to return. Defaults to 50 and must be less than or equal to 100.")]
        int? limit = null,
        [Description(
            "Optional cursor for pagination. Pass the `nextCursor` value from a previous response to continue."
        )]
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedNamespace = ToolResultFactory.NormalizeNamespace(@namespace);
        var normalizedLimit = ToolResultFactory.NormalizeLimit(limit);
        var page = await kvStore.ListKeysPageAsync(
                                     normalizedNamespace,
                                     pattern,
                                     cursor,
                                     normalizedLimit,
                                     cancellationToken
                                 )
                                .ConfigureAwait(false);
        var result = new ListKeysResultData
        {
            Namespace = normalizedNamespace,
            Keys = page.Items,
            NextCursor = page.NextCursor
        };
        return ToolResultFactory.Success(result);
    }
}
