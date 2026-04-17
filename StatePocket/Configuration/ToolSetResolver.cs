namespace StatePocket.Configuration;

internal static class ToolSetResolver
{
    public static ResolvedOptions Resolve(CommandLineOptions commandLineOptions, EnvironmentOptions environmentOptions)
    {
        ArgumentNullException.ThrowIfNull(commandLineOptions);
        ArgumentNullException.ThrowIfNull(environmentOptions);
        var databasePath = FirstNonEmpty(commandLineOptions.DatabasePath, environmentOptions.DatabasePath);
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ConfigurationException(
                "Database path is required. Provide --db-path or STATEPOCKET_MCP_DB_PATH."
            );
        }
        HashSet<string> enabledTools = new(ToolNames.All, StringComparer.Ordinal);
        var allowlist = ParseToolList(
            FirstNonEmpty(commandLineOptions.EnableTools, environmentOptions.EnableTools),
            "--enable-tools",
            "STATEPOCKET_MCP_ENABLE_TOOLS"
        );
        var denylist = ParseToolList(
            FirstNonEmpty(commandLineOptions.DisableTools, environmentOptions.DisableTools),
            "--disable-tools",
            "STATEPOCKET_MCP_DISABLE_TOOLS"
        );
        if (allowlist is not null)
        {
            enabledTools.IntersectWith(allowlist);
        }
        if (denylist is not null)
        {
            enabledTools.ExceptWith(denylist);
        }
        return new ResolvedOptions(databasePath.Trim(), enabledTools);
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        return !string.IsNullOrWhiteSpace(primary) ? primary : fallback;
    }

    private static HashSet<string>? ParseToolList(string? rawValue, string cliName, string environmentName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }
        HashSet<string> tools = new(StringComparer.Ordinal);
        foreach (var tool in rawValue.Split(
                     ',',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                 ))
        {
            if (!ToolNames.All.Contains(tool, StringComparer.Ordinal))
            {
                throw new ConfigurationException(
                    $"Unknown tool '{tool}' in {cliName}/{environmentName}. Known tools: {string.Join(", ", ToolNames.All)}."
                );
            }
            tools.Add(tool);
        }
        return tools;
    }
}
