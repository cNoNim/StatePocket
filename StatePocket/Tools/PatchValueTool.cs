using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Json.Patch;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class PatchValueTool(IKvStore kvStore)
{
    public const string ToolName = "patch_value";
    private const string ToolTitle = "Patch Value";

    [McpServerTool(
        Name = ToolName,
        Title = ToolTitle,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Applies an RFC 6902 JSON Patch document to an existing value in the selected namespace and returns the updated value."
    )]
    internal async Task<PatchValueResult> PatchValueAsync(
        [Description("Key to patch.")] string key,
        [Description("JSON Patch document to apply, encoded as JSON text.")] string patch,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedNamespace = ToolArgumentHelper.NormalizeNamespace(@namespace);
        var parsedPatch = ToolArgumentHelper.ParseJsonPatch(patch);
        KvValue? updatedValue;
        try
        {
            updatedValue = await kvStore.PatchValueAsync(
                                             normalizedNamespace,
                                             key,
                                             parsedPatch,
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
        if (updatedValue is null)
        {
            throw new McpException($"Key '{key}' was not found in namespace '{normalizedNamespace}'.");
        }
        return new PatchValueResult
        {
            Namespace = normalizedNamespace,
            Key = key,
            Value = updatedValue.Value,
            ExpiresAt = updatedValue.ExpiresAt,
            UpdatedAt = updatedValue.UpdatedAt,
            Revision = updatedValue.Revision
        };
    }
}
