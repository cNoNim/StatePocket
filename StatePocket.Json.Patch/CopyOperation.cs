using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Patch;

public sealed class CopyOperation : FromOperation
{
    public CopyOperation() {}

    [SetsRequiredMembers]
    internal CopyOperation(string from, string path) : base(from, path) {}

    [JsonIgnore]
    public override JsonPatchOperationType Op => JsonPatchOperationType.Copy;

    internal override void Validate()
    {
        ValidatePath();
        _ = ParsePath(From);
    }

    internal override JsonNode? ApplyTo(JsonNode? document)
    {
        var fromPath = ParsePath(From);
        var value = CloneValue(GetTargetNode(document, fromPath));
        return Add(Path, value)
           .ApplyTo(document);
    }
}
