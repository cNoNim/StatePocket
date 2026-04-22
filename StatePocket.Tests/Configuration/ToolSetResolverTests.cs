using StatePocket.Configuration;
using StatePocket.Tools;

namespace StatePocket.Tests.Configuration;

public sealed class ToolSetResolverTests
{
    [Fact]
    public void Resolve_UsesCliValues()
    {
        CommandLineOptions commandLine = new("/tmp/cli.db", GetValueTool.ToolName, null);
        var resolved = ToolSetResolver.Resolve(commandLine);
        Assert.Equal("/tmp/cli.db", resolved.DatabasePath);
        Assert.Equal([GetValueTool.ToolName], resolved.EnabledTools);
    }

    [Fact]
    public void Resolve_DenylistWinsOverAllowlist()
    {
        CommandLineOptions commandLine = new(
            "/tmp/statepocket.db",
            $"{SetValueTool.ToolName},{GetValueTool.ToolName}",
            GetValueTool.ToolName
        );
        var resolved = ToolSetResolver.Resolve(commandLine);
        Assert.Equal([SetValueTool.ToolName], resolved.EnabledTools);
    }

    [Fact]
    public void Resolve_ThrowsForUnknownTool()
    {
        CommandLineOptions commandLine = new("/tmp/statepocket.db", "unknown_tool", null);
        var exception = Assert.Throws<ConfigurationException>(() => ToolSetResolver.Resolve(commandLine));
        Assert.Contains("unknown_tool", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ThrowsForInMemoryDataSource()
    {
        CommandLineOptions commandLine = new(":memory:", null, null);
        var exception = Assert.Throws<ConfigurationException>(() => ToolSetResolver.Resolve(commandLine));
        Assert.Contains("In-memory SQLite datasources are not supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AllowsFileUriDataSource()
    {
        CommandLineOptions commandLine = new("file:///tmp/statepocket.db?mode=rwc", null, null);
        var resolved = ToolSetResolver.Resolve(commandLine);
        Assert.Equal("file:///tmp/statepocket.db?mode=rwc", resolved.DatabasePath);
    }
}
