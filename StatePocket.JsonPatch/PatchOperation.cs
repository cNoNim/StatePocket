using System.Text.Json.Nodes;

namespace StatePocket.JsonPatch;

internal sealed class PatchOperation(
    PatchOperationType type,
    string path,
    JsonNode? value = null,
    string? from = null
)
{
    public PatchOperationType Type { get; } = type;
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));
    public JsonNode? Value { get; } = value?.DeepClone();
    public string? From { get; } = from;
}
