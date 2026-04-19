using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.JsonPatch.Exceptions;

namespace StatePocket.JsonPatch;

public sealed class TestOperation : ValueOperation
{
    public TestOperation() {}

    [SetsRequiredMembers]
    internal TestOperation(string path, JsonNode? value) : base(path, value) {}

    [JsonIgnore]
    public override PatchOperationType Op => PatchOperationType.Test;

    internal override void Validate()
    {
        ValidatePath();
    }

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        var actual = GetTargetNode(document, ParsePath(Path));
        return JsonNode.DeepEquals(actual, GetValueNode())
          ? document
          : throw new JsonPatchException($"Test operation failed at path '{Path}'.");
    }
}
