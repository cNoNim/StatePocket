using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public sealed class ReplaceOperation : ValueOperation
{
    public ReplaceOperation() {}

    [SetsRequiredMembers]
    internal ReplaceOperation(JsonPointer path, JsonNode? value) : base(path, value) {}

    [JsonIgnore]
    public override JsonPatchOperationType Op => JsonPatchOperationType.Replace;

    internal override void Validate()
    {
        ValidatePath();
    }

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        if (Path.IsRoot)
        {
            return GetValueNode();
        }
        var parent = GetParentNode(document, Path);
        var segment = GetRequiredTargetSegment(Path);
        switch (parent)
        {
            case JsonObject jsonObject when jsonObject.ContainsKey(segment):
                jsonObject[segment] = GetValueNode();
                return document;
            case JsonArray jsonArray:
                jsonArray[ParseExistingArrayIndex(jsonArray, segment)] = GetValueNode();
                return document;
            default:
                throw new JsonPatchException($"Path '{Path}' does not exist.");
        }
    }
}
