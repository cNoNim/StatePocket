using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StatePocket.JsonPatch;

public abstract class ValueOperation : PatchOperation
{
    private readonly JsonNode? _value;
    protected ValueOperation() {}

    [SetsRequiredMembers]
    protected ValueOperation(string path, JsonNode? value) : base(path) => Value = value;

    [JsonPropertyName("value")]
    [JsonPropertyOrder(2)]
    [JsonRequired]
    public required JsonNode? Value
    {
        get => CloneValue(_value);
        init => _value = CloneValue(value);
    }

    internal JsonNode? GetValueNode()
    {
        return CloneValue(_value);
    }
}
