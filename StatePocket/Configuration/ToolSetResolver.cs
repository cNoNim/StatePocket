namespace StatePocket.Configuration;

internal static class ToolSetResolver
{
    public static ResolvedOptions Resolve(CommandLineOptions commandLineOptions)
    {
        ArgumentNullException.ThrowIfNull(commandLineOptions);
        var databasePath = commandLineOptions.DatabasePath;
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ConfigurationException("Database path is required. Provide --db-path.");
        }
        var normalizedDatabasePath = databasePath.Trim();
        SqliteDataSource.ValidateSupported(normalizedDatabasePath);
        return new ResolvedOptions(normalizedDatabasePath, ResolveEnabledTools(commandLineOptions));
    }

    private static IReadOnlyCollection<string> ResolveEnabledTools(CommandLineOptions commandLineOptions)
    {
        ArgumentNullException.ThrowIfNull(commandLineOptions);
        HashSet<string> enabledTools = new(ToolNames.All, StringComparer.Ordinal);
        var allowlist = ParseToolList(commandLineOptions.EnableTools, "--enable-tools");
        var denylist = ParseToolList(commandLineOptions.DisableTools, "--disable-tools");
        if (allowlist is not null)
        {
            enabledTools.IntersectWith(allowlist);
        }
        if (denylist is not null)
        {
            enabledTools.ExceptWith(denylist);
        }
        return [.. enabledTools.Order(StringComparer.Ordinal)];
    }

    private static HashSet<string>? ParseToolList(string? rawValue, string cliName)
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
                    $"Unknown tool '{tool}' in {cliName}. Known tools: {string.Join(", ", ToolNames.All)}."
                );
            }
            tools.Add(tool);
        }
        return tools;
    }
}
