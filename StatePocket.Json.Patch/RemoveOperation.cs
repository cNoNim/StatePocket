using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public sealed class RemoveOperation : JsonPatchOperation
{
    public RemoveOperation() {}

    [SetsRequiredMembers]
    internal RemoveOperation(JsonPointer path) : base(path) {}

    [JsonIgnore]
    public override JsonPatchOperationType Op => JsonPatchOperationType.Remove;

    internal override void Validate()
    {
        ValidatePath();
    }

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        if (Path.IsRoot)
        {
            throw new JsonPatchException("Removing the whole document is not supported.");
        }
        var parent = GetParentNode(document, Path);
        var segment = GetRequiredTargetSegment(Path);
        switch (parent)
        {
            case JsonObject jsonObject when jsonObject.Remove(segment):
                return document;
            case JsonArray jsonArray:
                jsonArray.RemoveAt(ParseExistingArrayIndex(jsonArray, segment));
                return document;
            default:
                throw new JsonPatchException($"Path '{Path}' does not exist.");
        }
    }
}
