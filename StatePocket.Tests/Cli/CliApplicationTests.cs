using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StatePocket.Cli;
using StatePocket.Configuration;
using StatePocket.Storage;

namespace StatePocket.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_RootHelpListsMcpSubcommand()
    {
        var (exitCode, output) = await CaptureConsoleAsync(static () => CliApplication.RunAsync(["--help"]));
        Assert.Equal(0, exitCode);
        Assert.Contains("mcp", output);
        Assert.Contains("Run the StatePocket MCP server over stdio.", output);
    }

    [Fact]
    public async Task RunAsync_McpHelpListsServerOptions()
    {
        var (exitCode, output) = await CaptureConsoleAsync(static () => CliApplication.RunAsync(["mcp", "--help"]));
        Assert.Equal(0, exitCode);
        Assert.Contains("--db-path", output);
        Assert.Contains("--enable-tools", output);
        Assert.Contains("--disable-tools", output);
    }

    [Fact]
    public async Task RunServerAsync_Returns1AndWritesConfigurationMessage()
    {
        StringWriter errorWriter = new();
        var exitCode = await CliApplication.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static (_, _) => throw new ConfigurationException("Unknown tool: nope"),
            static () => new EnvironmentOptions(null, null, null),
            static _ => new StubHost(),
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.CompletedTask,
            CancellationToken.None
        );
        Assert.Equal(1, exitCode);
        Assert.Equal($"Unknown tool: nope{Environment.NewLine}", errorWriter.ToString());
    }

    [Fact]
    public async Task RunServerAsync_Returns1AndWritesReadableDatabasePathError()
    {
        StringWriter errorWriter = new();
        var exitCode = await CliApplication.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static (_, _) => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
            static () => new EnvironmentOptions(null, null, null),
            static _ => new StubHost(),
            static (_, _) => throw new IOException("Permission denied."),
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.CompletedTask,
            CancellationToken.None
        );
        Assert.Equal(1, exitCode);
        Assert.Equal(
            $"Failed to access the database path. Permission denied.{Environment.NewLine}",
            errorWriter.ToString()
        );
    }

    [Fact]
    public async Task RunServerAsync_Returns1AndWritesReadableHostStartupError()
    {
        StringWriter errorWriter = new();
        DisposableStubHost host = new();
        var exitCode = await CliApplication.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static (_, _) => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
            static () => new EnvironmentOptions(null, null, null),
            _ => host,
            static (_, _) => throw new InvalidOperationException("Listener failed."),
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.CompletedTask,
            CancellationToken.None
        );
        Assert.Equal(1, exitCode);
        Assert.True(host.IsDisposed);
        Assert.Equal($"Failed to start statepocket. Listener failed.{Environment.NewLine}", errorWriter.ToString());
    }

    [Fact]
    public async Task RunServerAsync_Returns1AndWritesReadableHostStartError()
    {
        StringWriter errorWriter = new();
        var exitCode = await CliApplication.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static (_, _) => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
            static () => new EnvironmentOptions(null, null, null),
            static _ => new StubHost(),
            static (_, _) => Task.CompletedTask,
            static (_, _) => throw new InvalidOperationException("Hosted service failed."),
            static (_, _) => Task.CompletedTask,
            CancellationToken.None
        );
        Assert.Equal(1, exitCode);
        Assert.Equal(
            $"Failed to start statepocket. Hosted service failed.{Environment.NewLine}",
            errorWriter.ToString()
        );
    }

    [Fact]
    public async Task RunServerAsync_Returns1AndDoesNotReportHostStartIoFailureAsDatabasePathError()
    {
        StringWriter errorWriter = new();
        var exitCode = await CliApplication.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static (_, _) => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
            static () => new EnvironmentOptions(null, null, null),
            static _ => new StubHost(),
            static (_, _) => Task.CompletedTask,
            static (_, _) => throw new IOException("Broken stdin."),
            static (_, _) => Task.CompletedTask,
            CancellationToken.None
        );
        Assert.Equal(1, exitCode);
        Assert.Equal($"Failed to start statepocket. Broken stdin.{Environment.NewLine}", errorWriter.ToString());
    }

    [Fact]
    public async Task RunServerAsync_DoesNotRewriteRuntimeIoFailuresAsStartupDatabaseErrors()
    {
        StringWriter errorWriter = new();
        var exception = await Assert.ThrowsAsync<IOException>(() => CliApplication.RunServerAsync(
                new CommandLineOptions(null, null, null),
                errorWriter,
                static (_, _) => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
                static () => new EnvironmentOptions(null, null, null),
                static _ => new StubHost(),
                static (_, _) => Task.CompletedTask,
                static (_, _) => Task.CompletedTask,
                static (_, _) => throw new IOException("Broken pipe."),
                CancellationToken.None
            )
        );
        Assert.Equal("Broken pipe.", exception.Message);
        Assert.Equal("", errorWriter.ToString());
    }

    [Fact]
    public async Task RunServerAsync_DisposesHostWhenInitializationIsCanceled()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();
        DisposableStubHost host = new();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CliApplication.RunServerAsync(
                new CommandLineOptions(null, null, null),
                TextWriter.Null,
                static (_, _) => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
                static () => new EnvironmentOptions(null, null, null),
                _ => host,
                static (_, token) => Task.FromCanceled(token),
                static (_, _) => Task.CompletedTask,
                static (_, _) => Task.CompletedTask,
                cancellationTokenSource.Token
            )
        );
        Assert.True(host.IsDisposed);
    }

    [Fact]
    public async Task TryPurgeExpiredAsync_IgnoresKvStoreBusyException()
    {
        BusyKvStore kvStore = new();
        await CliApplication.TryPurgeExpiredAsync(kvStore, CancellationToken.None);
        Assert.True(kvStore.PurgeExpiredCalled);
    }

    [Fact]
    public async Task TryPurgeExpiredAsync_PropagatesUnexpectedExceptions()
    {
        ThrowingKvStore kvStore = new();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CliApplication.TryPurgeExpiredAsync(kvStore, CancellationToken.None)
        );
    }

    private static async Task<(int ExitCode, string Output)> CaptureConsoleAsync(Func<Task<int>> action)
    {
        StringBuilder buffer = new();
        await using StringWriter writer = new(buffer);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);
            var exitCode = await action();
            await writer.FlushAsync();
            return (exitCode, buffer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed class BusyKvStore : IKvStore
    {
        public bool PurgeExpiredCalled { get; private set; }

        public Task SetValueAsync(
            string? @namespace,
            string key,
            JsonElement value,
            long? ttlSeconds,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<KvValue?> GetValueAsync(string? @namespace, string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyDictionary<string, KvValue>> GetValuesAsync(
            string? @namespace,
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<KeyValuePair<string, KvValue>>> ListValuesPageAsync(
            string? @namespace,
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<string>> ListNamespacesPageAsync(
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<string>> ListKeysPageAsync(
            string? @namespace,
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<bool> PatchValueAsync(
            string? @namespace,
            string key,
            JsonElement patch,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteValueAsync(string? @namespace, string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task PurgeExpiredAsync(CancellationToken cancellationToken)
        {
            PurgeExpiredCalled = true;
            throw new KvStoreBusyException("busy", new InvalidOperationException("lock"));
        }
    }

    private sealed class ThrowingKvStore : IKvStore
    {
        public Task SetValueAsync(
            string? @namespace,
            string key,
            JsonElement value,
            long? ttlSeconds,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<KvValue?> GetValueAsync(string? @namespace, string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyDictionary<string, KvValue>> GetValuesAsync(
            string? @namespace,
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<KeyValuePair<string, KvValue>>> ListValuesPageAsync(
            string? @namespace,
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<string>> ListNamespacesPageAsync(
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<string>> ListKeysPageAsync(
            string? @namespace,
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<bool> PatchValueAsync(
            string? @namespace,
            string key,
            JsonElement patch,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteValueAsync(string? @namespace, string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task PurgeExpiredAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("unexpected");
        }
    }

    private sealed class StubHost : IHost
    {
        public IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();
        public void Dispose() {}

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class DisposableStubHost : IHost
    {
        public bool IsDisposed { get; private set; }
        public IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();

        public void Dispose()
        {
            IsDisposed = true;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
