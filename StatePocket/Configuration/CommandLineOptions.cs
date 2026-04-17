namespace StatePocket.Configuration;

internal sealed record CommandLineOptions(string? DatabasePath, string? EnableTools, string? DisableTools);
