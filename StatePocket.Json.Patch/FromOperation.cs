using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public abstract class FromOperation : JsonPatchOperation
{
    protected FromOperation() {}

    [SetsRequiredMembers]
    protected FromOperation(JsonPointer from, JsonPointer path) : base(path) => From = from;

    [JsonPropertyName("from")]
    [JsonPropertyOrder(2)]
    [JsonRequired]
    public required JsonPointer From { get; init; }
}
