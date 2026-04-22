using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Exceptions;
using StatePocket.Json.Patch;

namespace StatePocket.Tools;

internal static class ToolArgumentHelper
{
    public const string DefaultNamespace = "default";
    public const int DefaultPageSize = 50;
    public const int MaxResultItems = 100;
    private static readonly JsonDocumentOptions StrictJsonDocumentOptions = new()
    {
        AllowDuplicateProperties = false
    };

    public static JsonElement ParseJsonValue(
        string value,
        JsonInputFormat format,
        [CallerArgumentExpression(nameof(value))] string? argumentName = null
    )
    {
        return format switch
        {
            JsonInputFormat.Text => ParseRawString(value),
            JsonInputFormat.Json => ParseJsonText(value, argumentName),
            _ => throw new UnreachableException()
        };
    }

    public static void ThrowIfInvalidJsonInputFormat(
        JsonInputFormat format,
        RequestContext<CallToolRequestParams>? requestContext
    )
    {
        if (requestContext?.Params.Arguments?.TryGetValue("format", out var rawFormat) == true
         && rawFormat.ValueKind != JsonValueKind.String)
        {
            throw new ToolInvalidArgumentException("format must be 'text' or 'json'.", "format");
        }
        if (format is not JsonInputFormat.Text and not JsonInputFormat.Json)
        {
            throw new ToolInvalidArgumentException("format must be 'text' or 'json'.", nameof(format));
        }
    }

    public static void ThrowIfEqualsRequiresQuery(string? query, bool hasEqualsArgument)
    {
        if (query is null && hasEqualsArgument)
        {
            throw new ToolValidationException("equals requires query.", "equals");
        }
    }

    public static JsonPatch ParseJsonPatch(string patch)
    {
        try
        {
            using var document = JsonDocument.Parse(patch, StrictJsonDocumentOptions);
            return document.RootElement.Deserialize(JsonPatchJsonContext.Default.JsonPatch)
                ?? throw new ToolInvalidPatchException("Patch document must be a JSON array.");
        }
        catch (JsonException exception)
        {
            throw new ToolInvalidPatchException(exception.Message, path: exception.Path, innerException: exception);
        }
    }

    private static JsonElement ParseJsonText(string json, string? argumentName)
    {
        try
        {
            using var document = JsonDocument.Parse(json, StrictJsonDocumentOptions);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ToolInvalidJsonException(
                $"{argumentName ?? "value"} must be valid JSON when format is 'json'.",
                exception.Path,
                exception
            );
        }
    }

    private static JsonElement ParseRawString(string value)
    {
        var encoded = JsonEncodedText.Encode(value);
        using var document = JsonDocument.Parse($"\"{encoded}\"", StrictJsonDocumentOptions);
        return document.RootElement.Clone();
    }
}
