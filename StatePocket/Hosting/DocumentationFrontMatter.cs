using System.Text.Json.Serialization;

namespace StatePocket.Hosting;

internal sealed class DocumentationFrontMatter
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = "";
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }
    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }
    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
    [JsonPropertyName("requires_tools")]
    public List<string> RequiresTools { get; init; } = [];
    [JsonPropertyName("related")]
    public List<string> Related { get; init; } = [];
}
