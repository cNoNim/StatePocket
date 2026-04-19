using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.JsonPatch.Exceptions;

namespace StatePocket.JsonPatch;

public sealed class RemoveOperation : PatchOperation
{
    public RemoveOperation() {}

    [SetsRequiredMembers]
    internal RemoveOperation(string path) : base(path) {}

    [JsonIgnore]
    public override PatchOperationType Op => PatchOperationType.Remove;

    internal override void Validate()
    {
        ValidatePath();
    }

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        var parsedPath = ParsePath(Path);
        if (parsedPath.IsRoot)
        {
            throw new JsonPatchException("Removing the whole document is not supported.");
        }
        var parent = GetParentNode(document, parsedPath);
        var segment = parsedPath.LastSegment
                   ?? throw new JsonPatchException("JSON Pointer path must contain a target segment.");
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
