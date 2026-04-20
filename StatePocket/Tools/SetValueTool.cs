using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class SetValueTool(IKvStore kvStore)
{
    public const string ToolName = "set_value";
    private const string ToolTitle = "Set Value";

    [McpServerTool(
        Name = ToolName,
        Title = ToolTitle,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Stores a JSON value under a key in the selected namespace, creating the key or replacing its current value."
    )]
    internal async Task<SetValueResult> SetValueAsync(
        [Description("Key to create or replace.")] string key,
        [Description("JSON value to store.")] JsonElement value,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional TTL in seconds. Omit to store the value without expiration.")] long? ttlSeconds = null,
        [Description(
            "Optional expected revision for compare-and-set writes. When provided, the write succeeds only if the current live value has this exact revision. Cannot be combined with ifAbsent."
        )]
        long? expectedRevision = null,
        [Description(
            "When true, create the key only if no live value currently exists. Cannot be combined with expectedRevision."
        )]
        bool ifAbsent = false,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedNamespace = ToolArgumentHelper.NormalizeNamespace(@namespace);
        SetValueMetadata storedValue;
        try
        {
            storedValue = await kvStore.SetValueAsync(
                                            normalizedNamespace,
                                            key,
                                            value,
                                            ttlSeconds,
                                            expectedRevision,
                                            ifAbsent,
                                            cancellationToken
                                        )
                                       .ConfigureAwait(false);
        }
        catch (KvStoreConflictException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (KvStoreBusyException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        return new SetValueResult
        {
            Namespace = normalizedNamespace,
            Key = key,
            ExpiresAt = storedValue.ExpiresAt,
            UpdatedAt = storedValue.UpdatedAt,
            Revision = storedValue.Revision
        };
    }
}
