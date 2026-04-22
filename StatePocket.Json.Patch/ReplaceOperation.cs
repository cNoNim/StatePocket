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

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        return ReplaceValue(
            document,
            Path,
            GetValueNode(),
            OpName
        );
    }
}
