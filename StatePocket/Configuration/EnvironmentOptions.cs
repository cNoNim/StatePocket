namespace StatePocket.Configuration;

internal sealed record EnvironmentOptions(string? DatabasePath, string? EnableTools, string? DisableTools)
{
    private const string DatabasePathVariable = "STATEPOCKET_MCP_DB_PATH";
    private const string EnableToolsVariable = "STATEPOCKET_MCP_ENABLE_TOOLS";
    private const string DisableToolsVariable = "STATEPOCKET_MCP_DISABLE_TOOLS";

    public static EnvironmentOptions Read()
    {
        return new EnvironmentOptions(
            Environment.GetEnvironmentVariable(DatabasePathVariable),
            Environment.GetEnvironmentVariable(EnableToolsVariable),
            Environment.GetEnvironmentVariable(DisableToolsVariable)
        );
    }
}
