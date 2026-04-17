using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using StatePocket.Contracts;

namespace StatePocket.Tools;

internal static class ToolResultFactory
{
    private const string DefaultNamespace = "default";
    public const int DefaultPageSize = 50;
    public const int MaxResultItems = 100;

    public static CallToolResult Success<TData>(TData data)
    {
        var structuredContent = SerializeToElement(data);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = structuredContent.GetRawText(),
                },
            ],
            StructuredContent = structuredContent,
        };
    }

    private static JsonElement SerializeToElement<TData>(TData data)
    {
        return data switch
        {
            DeleteValueResultData result => JsonSerializer.SerializeToElement(
                result,
                ToolResultJsonContext.Default.DeleteValueResultData
            ),
            GetValueResultData result => JsonSerializer.SerializeToElement(
                result,
                ToolResultJsonContext.Default.GetValueResultData
            ),
            GetValuesResultData result => JsonSerializer.SerializeToElement(
                result,
                ToolResultJsonContext.Default.GetValuesResultData
            ),
            ListKeysResultData result => JsonSerializer.SerializeToElement(
                result,
                ToolResultJsonContext.Default.ListKeysResultData
            ),
            ListNamespacesResultData result => JsonSerializer.SerializeToElement(
                result,
                ToolResultJsonContext.Default.ListNamespacesResultData
            ),
            PatchValueResultData result => JsonSerializer.SerializeToElement(
                result,
                ToolResultJsonContext.Default.PatchValueResultData
            ),
            QueryValuesResultData result => JsonSerializer.SerializeToElement(
                result,
                ToolResultJsonContext.Default.QueryValuesResultData
            ),
            SetValueResultData result => JsonSerializer.SerializeToElement(
                result,
                ToolResultJsonContext.Default.SetValueResultData
            ),
            null => throw new ArgumentNullException(nameof(data)),
            _ => throw new NotSupportedException($"Unsupported tool result type: {typeof(TData).FullName}"),
        };
    }

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
            _ => normalizedLimit,
        };
    }
}
