using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.JsonPatch.Exceptions;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class PatchValueTool(IKvStore kvStore)
{
    [McpServerTool(Name = "patch_value")]
    [Description("Applies an RFC 6902 JSON Patch document to an existing value in the selected namespace.")]
    internal async Task<CallToolResult> PatchValueAsync(
        [Description("Key to patch.")] string key,
        [Description("JSON Patch document to apply.")] JsonElement patch,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedNamespace = ToolResultFactory.NormalizeNamespace(@namespace);
        bool updated;
        try
        {
            updated = await kvStore.PatchValueAsync(
                                        normalizedNamespace,
                                        key,
                                        patch,
                                        cancellationToken
                                    )
                                   .ConfigureAwait(false);
        }
        catch (JsonPatchException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (KvStoreBusyException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        if (!updated)
        {
            throw new McpException($"Key '{key}' was not found in namespace '{normalizedNamespace}'.");
        }
        var result = new PatchValueResultData
        {
            Namespace = normalizedNamespace,
            Key = key
        };
        return ToolResultFactory.Success(result);
    }
}
