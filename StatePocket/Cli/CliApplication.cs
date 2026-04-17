using System.CommandLine;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StatePocket.Configuration;
using StatePocket.Hosting;
using StatePocket.Storage;

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
        rootCommand.Subcommands.Add(BuildMcpCommand());
        return rootCommand;
    }

    private static Command BuildMcpCommand()
    {
        Option<string?> dbPathOption = new("--db-path")
        {
            Description = "Path to the SQLite database file.",
        };
        Option<string?> enableToolsOption = new("--enable-tools")
        {
            Description = "Comma-separated allowlist of tool names.",
        };
        Option<string?> disableToolsOption = new("--disable-tools")
        {
            Description = "Comma-separated denylist of tool names.",
        };
        Command mcpCommand = new("mcp", "Run the StatePocket MCP server over stdio.")
        {
            dbPathOption,
            enableToolsOption,
            disableToolsOption,
        };
        mcpCommand.SetAction((parseResult, cancellationToken) =>
            {
                CommandLineOptions commandLineOptions = new(
                    parseResult.GetValue(dbPathOption),
                    parseResult.GetValue(enableToolsOption),
                    parseResult.GetValue(disableToolsOption)
                );
                return RunServerAsync(commandLineOptions, cancellationToken);
            }
        );
        return mcpCommand;
    }

    private static async Task<int> RunServerAsync(
        CommandLineOptions commandLineOptions,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await RunServerAsync(
                    commandLineOptions,
                    Console.Error,
                    ToolSetResolver.Resolve,
                    EnvironmentOptions.Read,
                    StatePocketMcpHostFactory.Create,
                    InitializeHostAsync,
                    static (host, token) => host.StartAsync(token),
                    static (host, token) => host.WaitForShutdownAsync(token),
                    cancellationToken
                )
               .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
    }

    internal static async Task<int> RunServerAsync(
        CommandLineOptions commandLineOptions,
        TextWriter errorWriter,
        Func<CommandLineOptions, EnvironmentOptions, ResolvedOptions> resolveOptions,
        Func<EnvironmentOptions> readEnvironmentOptions,
        Func<ResolvedOptions, IHost> createHost,
        Func<IHost, CancellationToken, Task> initializeHostAsync,
        Func<IHost, CancellationToken, Task> startHostAsync,
        Func<IHost, CancellationToken, Task> waitForShutdownAsync,
        CancellationToken cancellationToken
    )
    {
        using var host = await CreateAndInitializeHostAsync(
                commandLineOptions,
                errorWriter,
                resolveOptions,
                readEnvironmentOptions,
                createHost,
                initializeHostAsync,
                cancellationToken
            )
           .ConfigureAwait(false);
        if (host is null)
        {
            return 1;
        }
        var started = await StartHostAsync(
                host,
                errorWriter,
                startHostAsync,
                cancellationToken
            )
           .ConfigureAwait(false);
        if (!started)
        {
            return 1;
        }
        await waitForShutdownAsync(host, cancellationToken)
           .ConfigureAwait(false);
        return 0;
    }

    private static async Task<bool> StartHostAsync(
        IHost host,
        TextWriter errorWriter,
        Func<IHost, CancellationToken, Task> startHostAsync,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await startHostAsync(host, cancellationToken)
               .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await errorWriter.WriteLineAsync(GetHostStartErrorMessage(exception))
                             .ConfigureAwait(false);
            return false;
        }
    }

    private static async Task<IHost?> CreateAndInitializeHostAsync(
        CommandLineOptions commandLineOptions,
        TextWriter errorWriter,
        Func<CommandLineOptions, EnvironmentOptions, ResolvedOptions> resolveOptions,
        Func<EnvironmentOptions> readEnvironmentOptions,
        Func<ResolvedOptions, IHost> createHost,
        Func<IHost, CancellationToken, Task> initializeHostAsync,
        CancellationToken cancellationToken
    )
    {
        IHost? host = null;
        try
        {
            var resolvedOptions = resolveOptions(commandLineOptions, readEnvironmentOptions());
            host = createHost(resolvedOptions);
            await initializeHostAsync(host, cancellationToken)
               .ConfigureAwait(false);
            return host;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            host?.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            host?.Dispose();
            await errorWriter.WriteLineAsync(GetInitializationErrorMessage(exception))
                             .ConfigureAwait(false);
            return null;
        }
    }

    private static string GetInitializationErrorMessage(Exception exception)
    {
        return exception switch
        {
            ConfigurationException configurationException => configurationException.Message,
            UnauthorizedAccessException or IOException => $"Failed to access the database path. {exception.Message}",
            _ => exception is SqliteException
              ? $"Failed to open or initialize the SQLite database. {exception.Message}"
              : $"Failed to start statepocket. {exception.Message}",
        };
    }

    private static string GetHostStartErrorMessage(Exception exception)
    {
        return $"Failed to start statepocket. {exception.Message}";
    }

    private static async Task InitializeHostAsync(IHost host, CancellationToken cancellationToken)
    {
        await host.Services.GetRequiredService<SqliteDatabaseInitializer>()
                  .InitializeAsync(cancellationToken)
                  .ConfigureAwait(false);
        await TryPurgeExpiredAsync(host.Services.GetRequiredService<IKvStore>(), cancellationToken)
           .ConfigureAwait(false);
    }

    internal static async Task TryPurgeExpiredAsync(IKvStore kvStore, CancellationToken cancellationToken)
    {
        try
        {
            await kvStore.PurgeExpiredAsync(cancellationToken)
                         .ConfigureAwait(false);
        }
        catch (KvStoreBusyException)
        {
            // Startup purge is best-effort housekeeping; transient write contention
            // must not prevent the server from starting.
        }
    }
}
