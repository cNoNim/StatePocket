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

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        return RemoveValue(document, Path, OpName);
    }
}
