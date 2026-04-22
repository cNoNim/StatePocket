using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using StatePocket.Configuration;
using Tomlyn;

namespace StatePocket.Hosting;

internal static class McpPublishedDocumentation
{
    private const string ResourcePrefix = "Docs/";
    private static readonly string AboutDocumentationUri = ResourceUri.Format("docs/about");
    private static readonly DocumentationGrouping[] Groupings =
    [
        new("concepts", "Core concepts"), new("workflows", "Suggested workflows")
    ];
    private static readonly Lazy<List<PublishedDocumentTemplate>> DocumentTemplates = new(LoadDocuments);

    internal static PublishedDocumentationCatalog CreateCatalog(
        IReadOnlyCollection<string> enabledTools,
        IReadOnlyCollection<PublishedLinkTarget> additionalLinkTargets
    )
    {
        ArgumentNullException.ThrowIfNull(enabledTools);
        ArgumentNullException.ThrowIfNull(additionalLinkTargets);
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
        var availableLinkTargetsByUri = CreateAvailableLinkTargets(availableTemplates, additionalLinkTargets);
        List<PublishedDocument> documents =
        [
            .. availableTemplates.Select(document => new PublishedDocument(
                    document.Uri,
                    document.Name,
                    document.Title,
                    document.Description,
                    document.MimeType,
                    document.ToolName,
                    [.. document.Tags],
                    RenderContent(document, availableLinkTargetsByUri)
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
        var description = tool.ProtocolTool.Description;
        tool.ProtocolTool.Description = AppendDocumentationLink(description, documentationUri);
    }

    internal static string AppendAboutDocumentationLink(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return AppendDocumentationLink(description, AboutDocumentationUri);
    }

    internal static string AppendDocumentationLink(string? description, string documentationUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationUri);
        var documentationSentence = $"See also: {documentationUri}";
        return string.IsNullOrWhiteSpace(description) ? documentationSentence :
            description.Contains(documentationUri, StringComparison.Ordinal) ? description :
            $"{description}\n{documentationSentence}";
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
                MimeType = document.MimeType,
                Meta = CreateDocumentMeta(document.Tags)
            }
        );
    }

    private static JsonObject? CreateDocumentMeta(IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            return null;
        }
        JsonArray tagArray = [];
        foreach (var tag in tags)
        {
            tagArray.Add((JsonNode?)tag);
        }
        return new JsonObject
        {
            ["tags"] = tagArray
        };
    }

    private static string RenderContent(
        PublishedDocumentTemplate document,
        IReadOnlyDictionary<string, PublishedLinkTarget> availableLinkTargetsByUri
    )
    {
        StringBuilder builder = new(document.Body.TrimEnd());
        AppendGroupedSections(builder, document.Links, availableLinkTargetsByUri);
        return builder.ToString();
    }

    private static AvailableLink[] GetAvailableLinks(
        IReadOnlyList<PublishedDocumentLink> links,
        IReadOnlyDictionary<string, PublishedLinkTarget> availableLinkTargetsByUri
    )
    {
        return
        [
            .. links.Select(link =>
                         {
                             if (!availableLinkTargetsByUri.TryGetValue(link.Uri, out var target))
                             {
                                 return null;
                             }
                             return new AvailableLink(
                                 string.IsNullOrWhiteSpace(target.Title) ? link.Label : target.Title,
                                 link.Uri,
                                 target.GroupingTag
                             );
                         }
                     )
                    .Where(static link => link is not null)
                    .Select(static link => link!)
        ];
    }

    private static void AppendGroupedSections(
        StringBuilder builder,
        IReadOnlyList<PublishedDocumentLink> links,
        IReadOnlyDictionary<string, PublishedLinkTarget> availableLinkTargetsByUri
    )
    {
        List<AvailableLink> otherLinks = [];
        var groupedLinks = Groupings.ToDictionary(
            static grouping => grouping.Tag,
            static _ => new List<AvailableLink>(),
            StringComparer.Ordinal
        );
        foreach (var link in GetAvailableLinks(links, availableLinkTargetsByUri))
        {
            if (link.GroupingTag is null)
            {
                otherLinks.Add(link);
                continue;
            }
            groupedLinks[link.GroupingTag]
               .Add(link);
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
            AppendSection(builder, "Related resources", [.. otherLinks]);
        }
    }

    private static Dictionary<string, PublishedLinkTarget> CreateAvailableLinkTargets(
        IReadOnlyCollection<PublishedDocumentTemplate> documents,
        IReadOnlyCollection<PublishedLinkTarget> additionalLinkTargets
    )
    {
        var linkTargets = documents.ToDictionary(
            static document => document.Uri,
            static document => new PublishedLinkTarget(document.Uri, document.Title, document.GroupingTag),
            StringComparer.Ordinal
        );
        foreach (var target in additionalLinkTargets)
        {
            linkTargets[target.Uri] = target;
        }
        return linkTargets;
    }

    private static string? FindGroupingTag(List<string> tags)
    {
        if (tags.Count == 0)
        {
            return null;
        }
        foreach (var grouping in Groupings)
        {
            if (tags.Contains(grouping.Tag, StringComparer.Ordinal))
            {
                return grouping.Tag;
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
            FindGroupingTag(metadata.Tags),
            ParseLinks(metadata),
            body
        );
    }

    private static List<PublishedDocumentLink> ParseLinks(DocumentationFrontMatter metadata)
    {
        return
        [
            .. metadata.Related.Select(static link => new PublishedDocumentLink(
                    NormalizeDocumentationUri(GetRequiredValue(link, "uri", "related")),
                    NormalizeDocumentationUri(GetRequiredValue(link, "uri", "related"))
                )
            )
        ];
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
        return ResourceUri.HasScheme(value) ? value : ResourceUri.Format(value);
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
        IReadOnlyList<string> Tags,
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
        string? GroupingTag,
        IReadOnlyList<PublishedDocumentLink> Links,
        string Body
    );

    private sealed record PublishedDocumentLink(string Label, string Uri);

    private sealed record DocumentationGrouping(string Tag, string Title);

    internal sealed record PublishedLinkTarget(string Uri, string Title, string? GroupingTag);

    private sealed record AvailableLink(string Label, string Uri, string? GroupingTag);
}
