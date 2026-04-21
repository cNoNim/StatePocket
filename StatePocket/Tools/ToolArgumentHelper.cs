using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Json.Patch;

namespace StatePocket.Tools;

internal static class ToolArgumentHelper
{
    private const string DefaultNamespace = "default";
    public const int DefaultPageSize = 50;
    public const int MaxResultItems = 100;
    private static readonly JsonDocumentOptions StrictJsonDocumentOptions = new()
    {
        AllowDuplicateProperties = false
    };

    public static string NormalizeNamespace(string? @namespace)
    {
        if (@namespace is not null
         && string.IsNullOrWhiteSpace(@namespace))
        {
            throw new McpException("namespace must not be empty or whitespace.");
        }
        return @namespace ?? DefaultNamespace;
    }

    public static int NormalizeLimit(int? limit)
    {
        var normalizedLimit = limit ?? DefaultPageSize;
        return normalizedLimit switch
        {
            < 1 => throw new McpException("limit must be greater than or equal to 1."),
            > MaxResultItems => throw new McpException($"limit must be less than or equal to {MaxResultItems}."),
            _ => normalizedLimit
        };
    }

    public static JsonElement ParseJsonValue(string value, JsonInputFormat format, string argumentName)
    {
        return format switch
        {
            JsonInputFormat.Text => ParseRawString(value),
            JsonInputFormat.Json => ParseJsonText(value, argumentName),
            _ => throw new UnreachableException()
        };
    }

    public static void ValidateFormatArgument(
        JsonInputFormat format,
        RequestContext<CallToolRequestParams>? requestContext
    )
    {
        if (requestContext?.Params.Arguments?.TryGetValue("format", out var rawFormat) == true
         && rawFormat.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("format must be 'text' or 'json'.");
        }
        if (format is not JsonInputFormat.Text and not JsonInputFormat.Json)
        {
            throw new JsonException("format must be 'text' or 'json'.");
        }
    }

    public static JsonPatch ParseJsonPatch(string patch)
    {
        using var document = JsonDocument.Parse(patch, StrictJsonDocumentOptions);
        return document.RootElement.Deserialize(JsonPatchJsonContext.Default.JsonPatch)
            ?? throw new JsonException("Patch document must be a JSON array.");
    }

    private static JsonElement ParseJsonText(string json, string argumentName)
    {
        try
        {
            using var document = JsonDocument.Parse(json, StrictJsonDocumentOptions);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new JsonException($"{argumentName} must be valid JSON when format is 'json'.", exception);
        }
    }

    private static JsonElement ParseRawString(string value)
    {
        var encoded = JsonEncodedText.Encode(value);
        using var document = JsonDocument.Parse($"\"{encoded}\"", StrictJsonDocumentOptions);
        return document.RootElement.Clone();
    }
}
