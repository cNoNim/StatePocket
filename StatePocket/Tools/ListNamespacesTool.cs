using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class ListNamespacesTool(IKvStore kvStore)
{
    [McpServerTool(Name = "list_namespaces", ReadOnly = true)]
    [Description("Lists namespaces, optionally filtered by a wildcard pattern.")]
    internal async Task<CallToolResult> ListNamespacesAsync(
        [Description("Optional wildcard namespace pattern, for example 'team:*'.")] string? pattern = null,
        [Description("Maximum number of namespaces to return. Defaults to 50 and must be less than or equal to 100.")]
        int? limit = null,
        [Description(
            "Optional cursor for pagination. Pass the `next_cursor` value from a previous response to continue."
        )]
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedLimit = ToolResultFactory.NormalizeLimit(limit);
        var page = await kvStore.ListNamespacesPageAsync(
                                     pattern,
                                     cursor,
                                     normalizedLimit,
                                     cancellationToken
                                 )
                                .ConfigureAwait(false);
        var result = new ListNamespacesResultData
        {
            Namespaces = page.Items,
            NextCursor = page.NextCursor
        };
        return ToolResultFactory.Success(result);
    }
}
