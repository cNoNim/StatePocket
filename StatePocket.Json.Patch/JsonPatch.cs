using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Patch;

[JsonConverter(typeof(JsonPatchJsonConverter))]
public sealed class JsonPatch
{
    private readonly JsonPatchOperation[] _operations;

    public JsonPatch(IEnumerable<JsonPatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = [.. operations];
        foreach (var operation in _operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            operation.Validate();
        }
        Operations = Array.AsReadOnly(_operations);
    }

    public ReadOnlyCollection<JsonPatchOperation> Operations { get; }

    public JsonNode? Apply(JsonNode? document)
    {
        var workingDocument = document?.DeepClone();
        return _operations.Aggregate(workingDocument, static (current, operation) => operation.ApplyTo(current));
    }
}
