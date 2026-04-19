using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.JsonPatch.Exceptions;

namespace StatePocket.JsonPatch;

public sealed class MoveOperation : FromOperation
{
    public MoveOperation() {}

    [SetsRequiredMembers]
    internal MoveOperation(string from, string path) : base(from, path) {}

    [JsonIgnore]
    public override PatchOperationType Op => PatchOperationType.Move;

    internal override void Validate()
    {
        ValidatePath();
        _ = ParsePath(From);
    }

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        var fromPath = ParsePath(From);
        var targetPath = ParsePath(Path);
        var sourceValue = GetTargetNode(document, fromPath);
        if (PathsEqual(fromPath, targetPath))
        {
            return document;
        }
        if (fromPath.IsPrefixOf(targetPath))
        {
            throw new JsonPatchException("Move operation cannot move a value into its own child path.");
        }
        var value = CloneValue(sourceValue);
        var removedDocument = Remove(From)
           .ApplyTo(document);
        return Add(Path, value)
           .ApplyTo(removedDocument);
    }
}
