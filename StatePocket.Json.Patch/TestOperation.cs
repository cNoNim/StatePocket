using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public sealed class TestOperation : ValueOperation
{
    public TestOperation() {}

    [SetsRequiredMembers]
    internal TestOperation(JsonPointer path, JsonNode? value) : base(path, value) {}

    [JsonIgnore]
    public override JsonPatchOperationType Op => JsonPatchOperationType.Test;

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        var actual = GetTargetNode(document, Path, OpName);
        return JsonNode.DeepEquals(actual, GetValueNode())
          ? document
          : throw new JsonPatchException($"Test operation failed at path '{Path}'.", OpName, Path);
    }
}
