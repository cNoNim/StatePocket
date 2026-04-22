using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Exceptions;
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
    [Description("Stores a JSON value under a key, with optional TTL and conditional write controls.")]
    internal async Task<SetValueResult> SetValueAsync(
        [Description("Key to create or replace.")] string key,
        [Description(
            "Value to store. Use format 'json' to parse this string as JSON text, or 'text' to store it as a JSON string. Example: value 'hello' with format 'text' stores the JSON string \"hello\"."
        )]
        string value,
        [Description(
            "How to interpret value. Use 'json' for JSON text or 'text' for a raw string. Defaults to 'json'."
        )]
        JsonInputFormat format = JsonInputFormat.Json,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional TTL in whole seconds. Omit to store the value without expiration.")] long? ttlSeconds =
            null,
        [Description(
            "Optional expected revision for compare-and-set writes. When provided, the write succeeds only if the current live value has this exact revision. Cannot be combined with ifAbsent."
        )]
        long? expectedRevision = null,
        [Description(
            "When true, create the key only if no live value currently exists. Cannot be combined with expectedRevision."
        )]
        bool ifAbsent = false,
        RequestContext<CallToolRequestParams>? requestContext = null,
        CancellationToken cancellationToken = default
    )
    {
        ToolInvalidArgumentException.ThrowIfNull(key);
        ToolInvalidArgumentException.ThrowIfNull(value);
        ToolInvalidArgumentException.ThrowIfEmptyOrWhitespace(@namespace, nameof(@namespace));
        ToolArgumentHelper.ThrowIfInvalidJsonInputFormat(format, requestContext);
        var parsedValue = ToolArgumentHelper.ParseJsonValue(value, format);
        var normalizedNamespace = @namespace ?? ToolArgumentHelper.DefaultNamespace;
        SetValueMetadata storedValue;
        try
        {
            storedValue = await kvStore.SetValueAsync(
                                            normalizedNamespace,
                                            key,
                                            parsedValue,
                                            ttlSeconds,
                                            expectedRevision,
                                            ifAbsent,
                                            cancellationToken
                                        )
                                       .ConfigureAwait(false);
        }
        catch (KvStoreConflictException exception)
        {
            throw CreateConflictException(
                normalizedNamespace,
                key,
                expectedRevision,
                ifAbsent,
                exception
            );
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

    private static ToolConflictException CreateConflictException(
        string @namespace,
        string key,
        long? expectedRevision,
        bool ifAbsent,
        KvStoreConflictException exception
    )
    {
        return ifAbsent
          ? new ToolAlreadyExistsException(@namespace, key, exception.CurrentRevision ?? 0)
          : new ToolRevisionConflictException(
                @namespace,
                key,
                expectedRevision ?? 0,
                exception.CurrentRevision
            );
    }
}
