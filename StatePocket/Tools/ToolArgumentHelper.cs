using ModelContextProtocol;

namespace StatePocket.Tools;

internal static class ToolArgumentHelper
{
    private const string DefaultNamespace = "default";
    public const int DefaultPageSize = 50;
    public const int MaxResultItems = 100;

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
}
