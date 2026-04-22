using System.ComponentModel;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Exceptions;
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
        "Applies an RFC 6902 JSON Patch document to an existing value in the selected namespace and returns the updated value. The returned revision is monotonic and scoped to the namespace, not the key."
    )]
    internal async Task<PatchValueResult> PatchValueAsync(
        [Description("Key to patch.")] string key,
        [Description("JSON Patch document to apply, encoded as JSON text.")] string patch,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolInvalidArgumentException.ThrowIfNull(key);
        ToolInvalidArgumentException.ThrowIfEmptyOrWhitespace(@namespace, nameof(@namespace));
        var normalizedNamespace = @namespace ?? ToolArgumentHelper.DefaultNamespace;
        var parsedPatch = ToolArgumentHelper.ParseJsonPatch(patch);
        var updatedValue = await kvStore.PatchValueAsync(
                                             normalizedNamespace,
                                             key,
                                             parsedPatch,
                                             cancellationToken
                                         )
                                        .ConfigureAwait(false)
                        ?? throw new ToolNotFoundException(normalizedNamespace, key);
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
