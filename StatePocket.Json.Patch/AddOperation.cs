using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public sealed class AddOperation : ValueOperation
{
    public AddOperation() {}

    [SetsRequiredMembers]
    internal AddOperation(JsonPointer path, JsonNode? value) : base(path, value) {}

    [JsonIgnore]
    public override JsonPatchOperationType Op => JsonPatchOperationType.Add;

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        return AddValue(
            document,
            Path,
            GetValueNode(),
            OpName
        );
    }
}
