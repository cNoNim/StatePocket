using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public sealed class MoveOperation : FromOperation
{
    public MoveOperation() {}

    [SetsRequiredMembers]
    internal MoveOperation(JsonPointer from, JsonPointer path) : base(from, path) {}

    [JsonIgnore]
    public override JsonPatchOperationType Op => JsonPatchOperationType.Move;

    internal override void Validate()
    {
        ValidatePath();
        ArgumentNullException.ThrowIfNull(From);
    }

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        var sourceValue = GetTargetNode(document, From);
        if (PathsEqual(From, Path))
        {
            return document;
        }
        if (From.IsPrefixOf(Path))
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
