using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;
using StatePocket.Configuration;
using Tomlyn;

namespace StatePocket.Hosting;

internal static class McpPublishedDocumentation
{
    private const string AboutDocumentationUri = "statepocket://docs/about";
    private const string ResourcePrefix = "Docs/";
    private static readonly DocumentationGrouping[] Groupings =
    [
        new("concepts", "Core concepts"), new("workflows", "Suggested workflows")
    ];
    private static readonly Lazy<List<PublishedDocumentTemplate>> DocumentTemplates = new(LoadDocuments);

    internal static PublishedDocumentationCatalog CreateCatalog(IReadOnlyCollection<string> enabledTools)
    {
        ArgumentNullException.ThrowIfNull(enabledTools);
        var availableTemplates = DocumentTemplates.Value.Where(document =>
                                                       (document.ToolName is null
                                                     || enabledTools.Contains(
                                                            document.ToolName,
                                                            StringComparer.Ordinal
                                                        ))
                                                    && document.RequiresTools.All(tool =>
                                                           enabledTools.Contains(tool, StringComparer.Ordinal)
                                                       )
                                                   )
                                                  .ToArray();
        var availableDocumentsByUri = availableTemplates.ToDictionary(
            static document => document.Uri,
            static document => document,
            StringComparer.Ordinal
        );
        List<PublishedDocument> documents =
        [
            .. availableTemplates.Select(document => new PublishedDocument(
                    document.Uri,
                    document.Name,
                    document.Title,
                    document.Description,
                    document.MimeType,
                    document.ToolName,
                    RenderContent(document, availableDocumentsByUri)
                )
            )
        ];
        return new PublishedDocumentationCatalog(documents);
    }

    internal static IEnumerable<McpServerResource> CreateResources(PublishedDocumentationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Documents.Select(CreateResource);
    }

    internal static void AppendToolDocumentationLink(
        PublishedDocumentationCatalog catalog,
        string toolName,
        McpServerTool tool
    )
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(tool);
        var documentationUri = catalog.GetToolDocumentationUri(toolName);
        if (documentationUri is null)
        {
            return;
        }
        var documentationSentence = $"See also: {documentationUri}";
        var description = tool.ProtocolTool.Description;
        if (description is null)
        {
            tool.ProtocolTool.Description = documentationSentence;
            return;
        }
        if (!description.Contains(documentationUri, StringComparison.Ordinal))
        {
            tool.ProtocolTool.Description = $"{description}\n{documentationSentence}";
        }
    }

    private static McpServerResource CreateResource(PublishedDocument document)
    {
        return McpServerResource.Create(
            (Func<string>)(() => document.Content),
            new McpServerResourceCreateOptions
            {
                Name = document.Name,
                Title = document.Title,
                Description = document.Description,
                UriTemplate = document.Uri,
                MimeType = document.MimeType
            }
        );
    }

    private static string RenderContent(
        PublishedDocumentTemplate document,
        IReadOnlyDictionary<string, PublishedDocumentTemplate> availableDocumentsByUri
    )
    {
        StringBuilder builder = new(document.Body.TrimEnd());
        if (string.Equals(document.Uri, AboutDocumentationUri, StringComparison.Ordinal))
        {
            AppendGroupedSections(builder, document.LinkSections, availableDocumentsByUri);
            return builder.ToString();
        }
        foreach (var section in document.LinkSections)
        {
            var availableLinks = GetAvailableLinks(section, availableDocumentsByUri);
            if (availableLinks.Length == 0)
            {
                continue;
            }
            AppendSection(builder, section.Title, availableLinks);
        }
        return builder.ToString();
    }

    private static AvailableLink[] GetAvailableLinks(
        PublishedDocumentLinkSection section,
        IReadOnlyDictionary<string, PublishedDocumentTemplate> availableDocumentsByUri
    )
    {
        return
        [
            .. section.Links.Select(link =>
                           {
                               if (!availableDocumentsByUri.TryGetValue(link.Uri, out var target))
                               {
                                   return null;
                               }
                               return new AvailableLink(
                                   string.IsNullOrWhiteSpace(target.Title) ? link.Label : target.Title,
                                   link.Uri,
                                   target.Tags
                               );
                           }
                       )
                      .Where(static link => link is not null)
                      .Select(static link => link!)
        ];
    }

    private static void AppendGroupedSections(
        StringBuilder builder,
        IReadOnlyList<PublishedDocumentLinkSection> linkSections,
        IReadOnlyDictionary<string, PublishedDocumentTemplate> availableDocumentsByUri
    )
    {
        List<AvailableLink> otherLinks = [];
        var groupedLinks = Groupings.ToDictionary(
            static grouping => grouping.Tag,
            static _ => new List<AvailableLink>(),
            StringComparer.Ordinal
        );
        foreach (var section in linkSections)
        {
            foreach (var link in GetAvailableLinks(section, availableDocumentsByUri))
            {
                var grouping = FindGrouping(link.Tags);
                if (grouping is null)
                {
                    otherLinks.Add(link);
                    continue;
                }
                groupedLinks[grouping.Tag]
                   .Add(link);
            }
        }
        foreach (var grouping in Groupings)
        {
            if (groupedLinks[grouping.Tag].Count == 0)
            {
                continue;
            }
            AppendSection(builder, grouping.Title, [.. groupedLinks[grouping.Tag]]);
        }
        if (otherLinks.Count > 0)
        {
            AppendSection(builder, "Related docs", [.. otherLinks]);
        }
    }

    private static DocumentationGrouping? FindGrouping(IReadOnlyList<string> tags)
    {
        foreach (var grouping in Groupings)
        {
            if (tags.Contains(grouping.Tag, StringComparer.Ordinal))
            {
                return grouping;
            }
        }
        return null;
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<AvailableLink> links)
    {
        if (links.Count == 0)
        {
            return;
        }
        if (builder.Length > 0)
        {
            builder.Append("\n\n");
        }
        builder.Append(title)
               .Append(':')
               .Append("\n\n");
        foreach (var link in links)
        {
            builder.Append("- [")
                   .Append(link.Label)
                   .Append("](")
                   .Append(link.Uri)
                   .Append(')')
                   .Append('\n');
        }
        builder.Length--;
    }

    private static List<PublishedDocumentTemplate> LoadDocuments()
    {
        var assembly = typeof(McpHostFactory).Assembly;
        List<PublishedDocumentTemplate> documents =
        [
            ..
            from resourceName in assembly.GetManifestResourceNames()
                                         .Order(StringComparer.Ordinal)
            let normalizedResourceName = NormalizeResourceName(resourceName)
            where normalizedResourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
               && normalizedResourceName.EndsWith(".md", StringComparison.Ordinal)
            let relativePath = normalizedResourceName[ResourcePrefix.Length..]
            select ParseDocument(relativePath, ReadEmbeddedText(assembly, resourceName))
        ];
        return documents;
    }

    private static PublishedDocumentTemplate ParseDocument(string relativePath, string content)
    {
        var (metadata, body) = ParseFrontMatter(relativePath, content);
        return new PublishedDocumentTemplate(
            NormalizeDocumentationUri(GetRequiredValue(metadata.Uri, "uri", relativePath)),
            GetRequiredValue(metadata.Name, "name", relativePath),
            GetRequiredValue(metadata.Title, "title", relativePath),
            metadata.Description,
            metadata.MimeType ?? "text/markdown",
            metadata.ToolName,
            ValidateToolNames(metadata.RequiresTools, "requires_tools", relativePath),
            [.. metadata.Tags],
            ParseLinkSections(metadata),
            body
        );
    }

    private static List<PublishedDocumentLinkSection> ParseLinkSections(DocumentationFrontMatter metadata)
    {
        return [.. CreateLinkSection("Related docs", metadata.Related)];
    }

    private static IEnumerable<PublishedDocumentLinkSection> CreateLinkSection(string title, List<string> links)
    {
        if (links.Count == 0)
        {
            yield break;
        }
        yield return new PublishedDocumentLinkSection(
            title,
            [
                .. links.Select(link => new PublishedDocumentLink(
                        NormalizeDocumentationUri(GetRequiredValue(link, "uri", title)),
                        NormalizeDocumentationUri(GetRequiredValue(link, "uri", title))
                    )
                )
            ]
        );
    }

    private static (DocumentationFrontMatter Metadata, string Body) ParseFrontMatter(
        string relativePath,
        string content
    )
    {
        var normalizedContent = content.Replace("\r\n", "\n");
        if (!normalizedContent.StartsWith("+++\n", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Published document '{relativePath}' is missing front matter.");
        }
        var metadataEnd = normalizedContent.IndexOf("\n+++\n", 4, StringComparison.Ordinal);
        if (metadataEnd < 0)
        {
            throw new InvalidOperationException(
                $"Published document '{relativePath}' has an unterminated front matter block."
            );
        }
        var metadataText = normalizedContent[4..metadataEnd];
        var metadata =
            TomlSerializer.Deserialize(metadataText, DocumentationTomlContext.Default.DocumentationFrontMatter)
         ?? throw new InvalidOperationException(
                $"Published document '{relativePath}' has an empty front matter block."
            );
        return (metadata, normalizedContent[(metadataEnd + 5)..]);
    }

    private static string GetRequiredValue(string? value, string key, string relativePath)
    {
        return !string.IsNullOrWhiteSpace(value)
          ? value
          : throw new InvalidOperationException($"Published document '{relativePath}' is missing '{key}' metadata.");
    }

    private static IReadOnlyList<string> ValidateToolNames(
        IReadOnlyList<string> toolNames,
        string key,
        string relativePath
    )
    {
        foreach (var toolName in toolNames)
        {
            if (!ToolNames.All.Contains(toolName, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Published document '{relativePath}' has unknown tool '{toolName}' in '{key}'."
                );
            }
        }
        return [.. toolNames];
    }

    private static string NormalizeDocumentationUri(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.StartsWith("statepocket://", StringComparison.Ordinal)
          ? value
          : $"statepocket://{value.TrimStart('/')}";
    }

    private static string ReadEmbeddedText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
                        ?? throw new InvalidOperationException(
                               $"Embedded documentation resource '{resourceName}' is unreadable."
                           );
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static string NormalizeResourceName(string resourceName)
    {
        return resourceName.Replace('\\', '/');
    }

    internal sealed class PublishedDocumentationCatalog
    {
        private readonly IReadOnlyDictionary<string, PublishedDocument> _documentsByUri;
        private readonly IReadOnlyDictionary<string, string> _toolDocumentationUris;

        internal PublishedDocumentationCatalog(IReadOnlyList<PublishedDocument> documents)
        {
            Documents = documents;
            _documentsByUri = documents.ToDictionary(
                static document => document.Uri,
                static document => document,
                StringComparer.Ordinal
            );
            _toolDocumentationUris = documents.Where(static document => document.ToolName is not null)
                                              .ToDictionary(
                                                   static document => document.ToolName!,
                                                   static document => document.Uri,
                                                   StringComparer.Ordinal
                                               );
        }

        internal IReadOnlyList<PublishedDocument> Documents { get; }

        internal string? GetToolDocumentationUri(string toolName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
            return _toolDocumentationUris.GetValueOrDefault(toolName);
        }

        internal PublishedDocument? FindDocument(string uri)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uri);
            return _documentsByUri.GetValueOrDefault(NormalizeDocumentationUri(uri));
        }
    }

    internal sealed record PublishedDocument(
        string Uri,
        string Name,
        string Title,
        string? Description,
        string MimeType,
        string? ToolName,
        string Content
    );

    private sealed record PublishedDocumentTemplate(
        string Uri,
        string Name,
        string Title,
        string? Description,
        string MimeType,
        string? ToolName,
        IReadOnlyList<string> RequiresTools,
        IReadOnlyList<string> Tags,
        IReadOnlyList<PublishedDocumentLinkSection> LinkSections,
        string Body
    );

    private sealed record PublishedDocumentLinkSection(string Title, IReadOnlyList<PublishedDocumentLink> Links);

    private sealed record PublishedDocumentLink(string Label, string Uri);

    private sealed record DocumentationGrouping(string Tag, string Title);

    private sealed record AvailableLink(string Label, string Uri, IReadOnlyList<string> Tags);
}
