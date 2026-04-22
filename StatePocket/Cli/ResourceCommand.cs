using System.CommandLine;
using StatePocket.Hosting;

namespace StatePocket.Cli;

internal static class ResourceCommand
{
    public static Command Build()
    {
        Argument<string?> resourceArgument = new("resource")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Embedded documentation resource URI or resource id to print."
        };
        Option<bool> listOption = new("--list", "-l")
        {
            Description = "List embedded documentation resource URIs."
        };
        Command resourceCommand = new("resource", "Inspect one embedded documentation resource by URI or resource id.")
        {
            resourceArgument,
            listOption
        };
        resourceCommand.SetAction((parseResult, cancellationToken) => RunAsync(
                parseResult.GetValue(resourceArgument),
                parseResult.GetValue(listOption),
                Console.Out,
                Console.Error,
                static () =>
                    McpPublishedDocumentation.CreateCatalog([.. McpTools.All.Select(static tool => tool.Name)]),
                cancellationToken
            )
        );
        return resourceCommand;
    }

    internal static async Task<int> RunAsync(
        string? resource,
        bool list,
        TextWriter outputWriter,
        TextWriter errorWriter,
        Func<McpPublishedDocumentation.PublishedDocumentationCatalog>
            createCatalog,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentNullException.ThrowIfNull(errorWriter);
        ArgumentNullException.ThrowIfNull(createCatalog);
        if (list)
        {
            return await WriteResourceListAsync(
                    outputWriter,
                    errorWriter,
                    createCatalog,
                    cancellationToken
                )
               .ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(resource))
        {
            await errorWriter.WriteLineAsync("Missing resource id. Use --list to see embedded documentation resources.")
                             .ConfigureAwait(false);
            return 1;
        }
        return await WriteResourceAsync(
                resource,
                outputWriter,
                errorWriter,
                createCatalog,
                cancellationToken
            )
           .ConfigureAwait(false);
    }

    private static async Task<int> WriteResourceListAsync(
        TextWriter outputWriter,
        TextWriter errorWriter,
        Func<McpPublishedDocumentation.PublishedDocumentationCatalog> createCatalog,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentNullException.ThrowIfNull(errorWriter);
        ArgumentNullException.ThrowIfNull(createCatalog);
        try
        {
            var catalog = createCatalog();
            foreach (var document in catalog.Documents)
            {
                await outputWriter.WriteLineAsync(document.Uri)
                                  .ConfigureAwait(false);
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            await errorWriter.WriteLineAsync($"Failed to list resources. {exception.Message}")
                             .ConfigureAwait(false);
            return 1;
        }
    }

    internal static async Task<int> WriteResourceAsync(
        string uri,
        TextWriter outputWriter,
        TextWriter errorWriter,
        Func<McpPublishedDocumentation.PublishedDocumentationCatalog>
            createCatalog,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentNullException.ThrowIfNull(errorWriter);
        ArgumentNullException.ThrowIfNull(createCatalog);
        try
        {
            var catalog = createCatalog();
            if (catalog.FindDocument(uri) is not
                {} document)
            {
                await errorWriter.WriteLineAsync(
                                      $"Unknown resource '{uri}'. Known resources: {string.Join(", ", catalog.Documents.Select(static document => document.Uri))}."
                                  )
                                 .ConfigureAwait(false);
                return 1;
            }
            await outputWriter.WriteLineAsync(document.Content)
                              .ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            await errorWriter.WriteLineAsync($"Failed to load resource '{uri}'. {exception.Message}")
                             .ConfigureAwait(false);
            return 1;
        }
    }
}
