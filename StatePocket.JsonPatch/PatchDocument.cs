using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StatePocket.JsonPatch;

[JsonConverter(typeof(PatchDocumentJsonConverter))]
public sealed class PatchDocument
{
    private readonly PatchOperation[] _operations;

    public PatchDocument(IEnumerable<PatchOperation> operations)
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

    public ReadOnlyCollection<PatchOperation> Operations { get; }

    public JsonNode? Apply(JsonNode? document)
    {
        var workingDocument = document?.DeepClone();
        return _operations.Aggregate(workingDocument, static (current, operation) => operation.ApplyTo(current));
    }
}
