using System.Text.Json;
using System.Text.Json.Nodes;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch.Tests;

public sealed class StrictParsingTests
{
    [Theory]
    [InlineData(JsonPatchOperationType.Add)]
    [InlineData(JsonPatchOperationType.Replace)]
    [InlineData(JsonPatchOperationType.Test)]
    public void Deserialize_MissingValueForValueOperations_ThrowsJsonException(JsonPatchOperationType operationType)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JsonPatch>($"[{{\"op\":\"{ToJsonName(operationType)}\",\"path\":\"/x\"}}]")
        );
        Assert.Contains("value", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(JsonPatchOperationType.Move)]
    [InlineData(JsonPatchOperationType.Copy)]
    public void Deserialize_MissingFromForMoveOrCopy_ThrowsJsonException(JsonPatchOperationType operationType)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JsonPatch>($"[{{\"op\":\"{ToJsonName(operationType)}\",\"path\":\"/x\"}}]")
        );
        Assert.Contains("from", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_NullJsonPatchOperation_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(static () => JsonSerializer.Deserialize<JsonPatch>("[null]"));
        Assert.Contains("object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/01")]
    [InlineData("/+1")]
    public void Apply_InvalidArrayIndexFormat_Throws(string path)
    {
        var patch = new JsonPatch([JsonPatchOperation.Add(path, JsonValue.Create("x"))]);
        Assert.Throws<JsonPatchException>(() => _ = patch.Apply(ParseNode("""["a"]""")));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("foo")]
    public void Constructor_InvalidJsonPointer_ThrowsJsonPointerException(string path)
    {
        Assert.Throws<JsonPointerException>(() =>
            _ = new JsonPatch([JsonPatchOperation.Replace(path, JsonValue.Create(1))])
        );
    }

    private static JsonNode ParseNode(string json)
    {
        return JsonNode.Parse(json) ?? throw new InvalidOperationException("JSON must parse.");
    }

    private static string ToJsonName(JsonPatchOperationType operationType)
    {
        return operationType switch
        {
            JsonPatchOperationType.Add => "add",
            JsonPatchOperationType.Remove => "remove",
            JsonPatchOperationType.Replace => "replace",
            JsonPatchOperationType.Move => "move",
            JsonPatchOperationType.Copy => "copy",
            JsonPatchOperationType.Test => "test",
            _ => throw new InvalidOperationException($"Unsupported operation type '{operationType}'.")
        };
    }
}
