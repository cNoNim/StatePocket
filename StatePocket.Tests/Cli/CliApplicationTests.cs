using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StatePocket.Cli;
using StatePocket.Configuration;
using StatePocket.Hosting;
using StatePocket.Json.Patch;
using StatePocket.Storage;
using StatePocket.Tools;

namespace StatePocket.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_RootHelpListsMcpSubcommand()
    {
        var (exitCode, output) = await CaptureConsoleAsync(static () => CliApplication.RunAsync(["--help"]));
        Assert.Equal(0, exitCode);
        Assert.Contains("mcp", output);
        Assert.Contains("resource", output);
        Assert.Contains("schema", output);
        Assert.Contains("Run the StatePocket MCP server over stdio.", output);
        Assert.Contains("Inspect one embedded documentation resource by URI or resource id.", output);
        Assert.Contains("Print the static MCP schema for a tool.", output);
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
    public async Task RunAsync_SchemaHelpListsToolArgument()
    {
        var (exitCode, output) = await CaptureConsoleAsync(static () => CliApplication.RunAsync(["schema", "--help"]));
        Assert.Equal(0, exitCode);
        Assert.Contains("tool", output);
        Assert.Contains("Print the static MCP schema for a tool.", output);
    }

    [Fact]
    public async Task RunAsync_ResourceHelpListsUriArgument()
    {
        var (exitCode, output) =
            await CaptureConsoleAsync(static () => CliApplication.RunAsync(["resource", "--help"]));
        Assert.Equal(0, exitCode);
        Assert.Contains("--list", output);
        Assert.Contains("resource", output);
        Assert.Contains("Inspect one embedded documentation resource by URI or resource id.", output);
    }

    [Fact]
    public async Task RunAsync_ResourceListWritesPublishedResourceUris()
    {
        var (exitCode, output) =
            await CaptureConsoleAsync(static () => CliApplication.RunAsync(["resource", "--list"]));
        Assert.Equal(0, exitCode);
        Assert.Contains("statepocket://docs/about", output);
        Assert.Contains("statepocket://docs/tools/set_value", output);
    }

    [Fact]
    public async Task RunAsync_ResourceWithoutArgumentReturns1()
    {
        StringWriter outputWriter = new();
        StringWriter errorWriter = new();
        var exitCode = await ResourceCommand.RunAsync(
            null,
            false,
            outputWriter,
            errorWriter,
            static () => McpPublishedDocumentation.CreateCatalog([GetValueTool.ToolName]),
            CancellationToken.None
        );
        Assert.Equal(1, exitCode);
        Assert.Equal("", outputWriter.ToString());
        Assert.Contains(
            "Missing resource id. Use --list to see embedded documentation resources.",
            errorWriter.ToString()
        );
    }

    [Fact]
    public async Task RunAsync_ResourceAboutWritesEmbeddedMarkdown()
    {
        var (exitCode, output) =
            await CaptureConsoleAsync(static () => CliApplication.RunAsync(["resource", "docs/about"]));
        Assert.Equal(0, exitCode);
        Assert.Contains("# About StatePocket", output);
        Assert.Contains("StatePocket is an MCP server", output);
    }

    [Fact]
    public async Task WriteResourceAsync_Returns1ForUnknownResource()
    {
        StringWriter outputWriter = new();
        StringWriter errorWriter = new();
        var exitCode = await ResourceCommand.WriteResourceAsync(
            "docs/nope",
            outputWriter,
            errorWriter,
            static () => McpPublishedDocumentation.CreateCatalog([GetValueTool.ToolName]),
            CancellationToken.None
        );
        Assert.Equal(1, exitCode);
        Assert.Equal("", outputWriter.ToString());
        Assert.Contains("Unknown resource 'docs/nope'.", errorWriter.ToString());
    }

    [Fact]
    public async Task RunAsync_SchemaSetValueWritesToolSchema()
    {
        var (exitCode, output) =
            await CaptureConsoleAsync(static () => CliApplication.RunAsync(["schema", "set_value"]));
        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(
            "set_value",
            document.RootElement.GetProperty("name")
                    .GetString()
        );
        Assert.Equal(
            "Set Value",
            document.RootElement.GetProperty("title")
                    .GetString()
        );
        Assert.Equal(
            "Set Value",
            document.RootElement.GetProperty("annotations")
                    .GetProperty("title")
                    .GetString()
        );
        Assert.Equal(
            "Stores a JSON value under a key, with optional TTL and conditional write controls.",
            document.RootElement.GetProperty("description")
                    .GetString()
        );
        Assert.DoesNotContain(
            "statepocket://docs/tools/set_value",
            document.RootElement.GetProperty("description")
                    .GetString(),
            StringComparison.Ordinal
        );
        Assert.False(
            document.RootElement.GetProperty("annotations")
                    .GetProperty("openWorldHint")
                    .GetBoolean()
        );
        Assert.Equal(
            "Value to store. Use format 'json' to parse this string as JSON text, or 'text' to store it as a JSON string. Example: value 'hello' with format 'text' stores the JSON string \"hello\".",
            document.RootElement.GetProperty("inputSchema")
                    .GetProperty("properties")
                    .GetProperty("value")
                    .GetProperty("description")
                    .GetString()
        );
        Assert.True(
            document.RootElement.GetProperty("inputSchema")
                    .GetProperty("properties")
                    .TryGetProperty("format", out _)
        );
        Assert.True(
            document.RootElement.GetProperty("inputSchema")
                    .GetProperty("properties")
                    .TryGetProperty("ttlSeconds", out _)
        );
        Assert.True(
            document.RootElement.GetProperty("inputSchema")
                    .GetProperty("properties")
                    .TryGetProperty("expectedRevision", out _)
        );
        Assert.True(
            document.RootElement.GetProperty("inputSchema")
                    .GetProperty("properties")
                    .TryGetProperty("ifAbsent", out _)
        );
        Assert.False(
            document.RootElement.GetProperty("inputSchema")
                    .TryGetProperty("allOf", out _)
        );
        Assert.False(
            document.RootElement.GetProperty("inputSchema")
                    .GetProperty("properties")
                    .TryGetProperty("ttl_seconds", out _)
        );
        Assert.Equal(
            ["namespace", "key", "updatedAt", "revision"],
            [
                .. document.RootElement.GetProperty("outputSchema")
                           .GetProperty("required")
                           .EnumerateArray()
                           .Select(static value => value.GetString()!)
            ]
        );
        Assert.False(
            document.RootElement.GetProperty("outputSchema")
                    .TryGetProperty("allOf", out _)
        );
    }

    [Fact]
    public async Task RunAsync_SchemaQueryValuesDoesNotWriteTopLevelAllOf()
    {
        var (exitCode, output) =
            await CaptureConsoleAsync(static () => CliApplication.RunAsync(["schema", "query_values"]));
        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.False(
            document.RootElement.GetProperty("inputSchema")
                    .TryGetProperty("allOf", out _)
        );
    }

    [Fact]
    public async Task RunAsync_SchemaGetValueWritesAutoGeneratedToolSchema()
    {
        var (exitCode, output) =
            await CaptureConsoleAsync(static () => CliApplication.RunAsync(["schema", "get_value"]));
        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(
            "get_value",
            document.RootElement.GetProperty("name")
                    .GetString()
        );
        Assert.Equal(
            "Get Value",
            document.RootElement.GetProperty("title")
                    .GetString()
        );
        Assert.Equal(
            "Get Value",
            document.RootElement.GetProperty("annotations")
                    .GetProperty("title")
                    .GetString()
        );
        Assert.False(
            document.RootElement.GetProperty("annotations")
                    .GetProperty("openWorldHint")
                    .GetBoolean()
        );
        Assert.True(
            document.RootElement.GetProperty("annotations")
                    .GetProperty("readOnlyHint")
                    .GetBoolean()
        );
        Assert.True(
            document.RootElement.GetProperty("outputSchema")
                    .GetProperty("properties")
                    .TryGetProperty("pathFound", out _)
        );
        Assert.True(
            document.RootElement.GetProperty("outputSchema")
                    .GetProperty("properties")
                    .TryGetProperty("value", out _)
        );
    }

    [Fact]
    public async Task WriteToolSchemaAsync_Returns1ForUnknownTool()
    {
        StringWriter outputWriter = new();
        StringWriter errorWriter = new();
        var exitCode = await SchemaCommand.WriteToolSchemaAsync(
            "nope",
            outputWriter,
            errorWriter,
            static _ => new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None
        );
        Assert.Equal(1, exitCode);
        Assert.Equal("", outputWriter.ToString());
        Assert.Contains("Unknown tool 'nope'.", errorWriter.ToString());
    }

    [Fact]
    public async Task RunServerAsync_Returns1AndWritesConfigurationMessage()
    {
        StringWriter errorWriter = new();
        var exitCode = await McpCommand.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static _ => throw new ConfigurationException("Unknown tool: nope"),
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
        var exitCode = await McpCommand.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static _ => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
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
        var exitCode = await McpCommand.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static _ => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
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
        var exitCode = await McpCommand.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static _ => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
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
        var exitCode = await McpCommand.RunServerAsync(
            new CommandLineOptions(null, null, null),
            errorWriter,
            static _ => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
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
        var exception = await Assert.ThrowsAsync<IOException>(() => McpCommand.RunServerAsync(
                new CommandLineOptions(null, null, null),
                errorWriter,
                static _ => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => McpCommand.RunServerAsync(
                new CommandLineOptions(null, null, null),
                TextWriter.Null,
                static _ => new ResolvedOptions("/tmp/statepocket.db", ["set_value"]),
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
        await McpCommand.TryPurgeExpiredAsync(kvStore, CancellationToken.None);
        Assert.True(kvStore.PurgeExpiredCalled);
    }

    [Fact]
    public async Task TryPurgeExpiredAsync_PropagatesUnexpectedExceptions()
    {
        ThrowingKvStore kvStore = new();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            McpCommand.TryPurgeExpiredAsync(kvStore, CancellationToken.None)
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

        public Task<SetValueMetadata> SetValueAsync(
            string? @namespace,
            string key,
            JsonElement value,
            long? ttlSeconds,
            long? expectedRevision = null,
            bool ifAbsent = false,
            CancellationToken cancellationToken = default
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

        public Task<KvValue?> PatchValueAsync(
            string? @namespace,
            string key,
            JsonPatch patch,
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
        public Task<SetValueMetadata> SetValueAsync(
            string? @namespace,
            string key,
            JsonElement value,
            long? ttlSeconds,
            long? expectedRevision = null,
            bool ifAbsent = false,
            CancellationToken cancellationToken = default
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

        public Task<KvValue?> PatchValueAsync(
            string? @namespace,
            string key,
            JsonPatch patch,
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
