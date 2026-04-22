using System.CommandLine;

namespace StatePocket.Cli;

internal static class CliApplication
{
    public static Task<int> RunAsync(string[] args)
    {
        return BuildRootCommand()
              .Parse(args)
              .InvokeAsync();
    }

    private static RootCommand BuildRootCommand()
    {
        RootCommand rootCommand = new("StatePocket durable local state for agents and tools.");
        rootCommand.Subcommands.Add(McpCommand.Build());
        rootCommand.Subcommands.Add(ResourceCommand.Build());
        rootCommand.Subcommands.Add(SchemaCommand.Build());
        return rootCommand;
    }
}
