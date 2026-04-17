using StatePocket.Configuration;

namespace StatePocket.Tests.Configuration;

public sealed class ToolSetResolverTests
{
    [Fact]
    public void Resolve_UsesCliValuesOverEnvironment()
    {
        CommandLineOptions commandLine = new("/tmp/cli.db", ToolNames.GetValue, null);
        EnvironmentOptions environment = new("/tmp/env.db", ToolNames.SetValue, ToolNames.ListKeys);
        var resolved = ToolSetResolver.Resolve(commandLine, environment);
        Assert.Equal("/tmp/cli.db", resolved.DatabasePath);
        Assert.Equal([ToolNames.GetValue], resolved.EnabledTools);
    }

    [Fact]
    public void Resolve_DenylistWinsOverAllowlist()
    {
        CommandLineOptions commandLine = new(
            "/tmp/statepocket.db",
            $"{ToolNames.SetValue},{ToolNames.GetValue}",
            ToolNames.GetValue
        );
        var resolved = ToolSetResolver.Resolve(commandLine, new EnvironmentOptions(null, null, null));
        Assert.Equal([ToolNames.SetValue], resolved.EnabledTools);
    }

    [Fact]
    public void Resolve_ThrowsForUnknownTool()
    {
        CommandLineOptions commandLine = new("/tmp/statepocket.db", "unknown_tool", null);
        var exception = Assert.Throws<ConfigurationException>(() =>
            ToolSetResolver.Resolve(commandLine, new EnvironmentOptions(null, null, null))
        );
        Assert.Contains("unknown_tool", exception.Message, StringComparison.Ordinal);
    }
}
