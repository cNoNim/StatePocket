using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public abstract class ValueOperation : JsonPatchOperation
{
    private readonly JsonNode? _value;
    protected ValueOperation() {}

    [SetsRequiredMembers]
    protected ValueOperation(JsonPointer path, JsonNode? value) : base(path) => Value = value;

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
        return _value;
    }
}
