using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Storage;

namespace StatePocket.Tools;

internal sealed class SetValueTool(IKvStore kvStore)
{
    public const string ToolName = "set_value";

    [McpServerTool(Name = ToolName)]
    [Description(
        "Stores a JSON value under a key in the selected namespace, creating the key or replacing its current value."
    )]
    internal async Task<CallToolResult> SetValueAsync(
        [Description("Key to create or replace.")] string key,
        [Description("JSON value to store.")] JsonElement value,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional TTL in seconds. Omit to store the value without expiration.")] long? ttlSeconds = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedNamespace = ToolResultFactory.NormalizeNamespace(@namespace);
        SetValueMetadata storedValue;
        try
        {
            storedValue = await kvStore.SetValueAsync(
                                            normalizedNamespace,
                                            key,
                                            value,
                                            ttlSeconds,
                                            cancellationToken
                                        )
                                       .ConfigureAwait(false);
        }
        catch (KvStoreBusyException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        var result = new SetValueResultData
        {
            Namespace = normalizedNamespace,
            Key = key,
            ExpiresAt = storedValue.ExpiresAt
        };
        return ToolResultFactory.Success(result);
    }
}
