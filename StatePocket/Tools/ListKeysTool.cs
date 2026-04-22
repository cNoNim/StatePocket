using System.ComponentModel;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Exceptions;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class ListKeysTool(IKvStore kvStore)
{
    public const string ToolName = "list_keys";
    private const string ToolTitle = "List Keys";

    [McpServerTool(
        Name = ToolName,
        Title = ToolTitle,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Lists keys in the selected namespace, optionally filtered by a wildcard pattern.")]
    internal async Task<ListKeysResult> ListKeysAsync(
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional wildcard key pattern, for example 'user:*'.")] string? pattern = null,
        [Description("Maximum number of keys to return. Defaults to 50 and must be less than or equal to 100.")]
        int? limit = null,
        [Description(
            "Optional cursor for pagination. Pass the last key returned in `nextCursor` from a previous response to continue."
        )]
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolInvalidArgumentException.ThrowIfEmptyOrWhitespace(@namespace, nameof(@namespace));
        ToolInvalidArgumentException.ThrowIfOutOfRange(limit, 1, ToolArgumentHelper.MaxResultItems);
        var normalizedNamespace = @namespace ?? ToolArgumentHelper.DefaultNamespace;
        var normalizedLimit = limit ?? ToolArgumentHelper.DefaultPageSize;
        var page = await kvStore.ListKeysPageAsync(
                                     normalizedNamespace,
                                     pattern,
                                     cursor,
                                     normalizedLimit,
                                     cancellationToken
                                 )
                                .ConfigureAwait(false);
        return new ListKeysResult
        {
            Namespace = normalizedNamespace,
            Keys = page.Items,
            NextCursor = page.NextCursor
        };
    }
}
