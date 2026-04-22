using System.ComponentModel;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Exceptions;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class ListNamespacesTool(IKvStore kvStore)
{
    public const string ToolName = "list_namespaces";
    private const string ToolTitle = "List Namespaces";

    [McpServerTool(
        Name = ToolName,
        Title = ToolTitle,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Lists namespaces that currently contain at least one live, unexpired key, optionally filtered by a wildcard pattern."
    )]
    internal async Task<ListNamespacesResult> ListNamespacesAsync(
        [Description("Optional wildcard namespace pattern, for example 'team:*'.")] string? pattern = null,
        [Description("Maximum number of namespaces to return. Defaults to 50 and must be less than or equal to 100.")]
        int? limit = null,
        [Description(
            "Optional cursor for pagination. Pass the last namespace returned in `nextCursor` from a previous response to continue."
        )]
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolInvalidArgumentException.ThrowIfOutOfRange(limit, 1, ToolArgumentHelper.MaxResultItems);
        var normalizedLimit = limit ?? ToolArgumentHelper.DefaultPageSize;
        var page = await kvStore.ListNamespacesPageAsync(
                                     pattern,
                                     cursor,
                                     normalizedLimit,
                                     cancellationToken
                                 )
                                .ConfigureAwait(false);
        return new ListNamespacesResult
        {
            Namespaces = page.Items,
            NextCursor = page.NextCursor
        };
    }
}
