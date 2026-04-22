using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Patch;

[JsonConverter(typeof(JsonPatchJsonConverter))]
public sealed class JsonPatch
{
    private readonly JsonPatchOperation[] _operations;

    public JsonPatch(IReadOnlyList<JsonPatchOperation> operations) : this(
        CreateSnapshot(operations ?? throw new ArgumentNullException(nameof(operations)))
    ) {}

    public JsonPatch(ReadOnlySpan<JsonPatchOperation> operations) : this(CreateSnapshot(operations)) {}

    internal JsonPatch(JsonPatchOperation[] operations)
    {
        _operations = operations;
        Operations = Array.AsReadOnly(_operations);
    }

    public ReadOnlyCollection<JsonPatchOperation> Operations { get; }

    public JsonNode? Apply(JsonNode? document)
    {
        var workingDocument = document?.DeepClone();
        for (var i = 0; i < _operations.Length; i++)
        {
            var operation = _operations[i];
            try
            {
                workingDocument = operation.ApplyTo(workingDocument);
            }
            catch (JsonPatchException exception)
            {
                exception.OperationIndex ??= i;
                throw;
            }
        }
        return workingDocument;
    }

    private static JsonPatchOperation[] CreateSnapshot(IReadOnlyList<JsonPatchOperation> operations)
    {
        JsonPatchOperation[] array = [.. operations];
        ValidateOperations(array);
        return array;
    }

    private static JsonPatchOperation[] CreateSnapshot(ReadOnlySpan<JsonPatchOperation> operations)
    {
        JsonPatchOperation[] array = [.. operations];
        ValidateOperations(array);
        return array;
    }

    private static void ValidateOperations(JsonPatchOperation[] operations)
    {
        foreach (var operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
        }
    }
}
