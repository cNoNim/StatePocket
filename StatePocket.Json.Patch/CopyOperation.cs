using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public sealed class CopyOperation : FromOperation
{
    public CopyOperation() {}

    [SetsRequiredMembers]
    internal CopyOperation(JsonPointer from, JsonPointer path) : base(from, path) {}

    [JsonIgnore]
    public override JsonPatchOperationType Op => JsonPatchOperationType.Copy;

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        var value = CloneValue(GetTargetNode(document, From, OpName));
        return AddValue(
            document,
            Path,
            value,
            OpName
        );
    }
}
