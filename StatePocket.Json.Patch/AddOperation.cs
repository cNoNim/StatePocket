using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Patch.Exceptions;

namespace StatePocket.Json.Patch;

public sealed class AddOperation : ValueOperation
{
    public AddOperation() {}

    [SetsRequiredMembers]
    internal AddOperation(string path, JsonNode? value) : base(path, value) {}

    [JsonIgnore]
    public override JsonPatchOperationType Op => JsonPatchOperationType.Add;

    internal override void Validate()
    {
        ValidatePath();
    }

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        var parsedPath = ParsePath(Path);
        if (parsedPath.IsRoot)
        {
            return GetValueNode();
        }
        var parent = GetParentNode(document, parsedPath);
        var segment = parsedPath.LastSegment
                   ?? throw new JsonPatchException("JSON Pointer path must contain a target segment.");
        switch (parent)
        {
            case JsonObject jsonObject:
                jsonObject[segment] = GetValueNode();
                return document;
            case JsonArray jsonArray:
                jsonArray.Insert(ParseArrayInsertIndex(jsonArray, segment), GetValueNode());
                return document;
            default:
                throw new JsonPatchException($"Path '{Path}' does not target an object or array.");
        }
    }
}
