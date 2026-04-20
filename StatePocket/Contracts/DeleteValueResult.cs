using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record DeleteValueResult
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }
    [JsonPropertyName("key")]
    public required string Key { get; init; }
}
