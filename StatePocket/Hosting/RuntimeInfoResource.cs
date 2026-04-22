using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using StatePocket.Configuration;

namespace StatePocket.Hosting;

internal static class RuntimeInfoResource
{
    private const string Name = "info";
    private const string Title = "StatePocket Runtime Info";
    internal static readonly string InfoUri = ResourceUri.Format("info");
    private static readonly IReadOnlyList<string> Tags = ["runtime"];

    internal static McpPublishedDocumentation.PublishedLinkTarget CreateLinkTarget()
    {
        return new McpPublishedDocumentation.PublishedLinkTarget(InfoUri, Title, null);
    }

    internal static McpServerResource CreateResource(ResolvedOptions resolvedOptions)
    {
        ArgumentNullException.ThrowIfNull(resolvedOptions);
        return McpServerResource.Create(
            (Func<string>)(() => RenderContent(resolvedOptions)),
            new McpServerResourceCreateOptions
            {
                Name = Name,
                Title = Title,
                Description = "Current StatePocket runtime state, including database path and working directory.",
                UriTemplate = InfoUri,
                MimeType = "text/markdown",
                Meta = CreateMeta()
            }
        );
    }

    private static string RenderContent(ResolvedOptions resolvedOptions)
    {
        ArgumentNullException.ThrowIfNull(resolvedOptions);
        StringBuilder builder = new();
        builder.AppendLine("# StatePocket Runtime Info")
               .AppendLine()
               .Append("- Storage backend: SQLite")
               .AppendLine()
               .Append("- Database path: ")
               .AppendLine(FormatDatabasePath(resolvedOptions.DatabasePath))
               .Append("- Working directory: ")
               .AppendLine(Environment.CurrentDirectory);
        return builder.ToString()
                      .TrimEnd();
    }

    private static string FormatDatabasePath(string databasePath)
    {
        return SqliteDataSource.FormatForDisplay(databasePath);
    }

    private static JsonObject CreateMeta()
    {
        JsonArray tagArray = [];
        foreach (var tag in Tags)
        {
            tagArray.Add((JsonNode?)tag);
        }
        return new JsonObject
        {
            ["tags"] = tagArray
        };
    }
}
