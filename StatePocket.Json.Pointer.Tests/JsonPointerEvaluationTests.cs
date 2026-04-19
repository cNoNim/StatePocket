using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatePocket.Json.Pointer.Tests;

public sealed class JsonPointerEvaluationTests
{
    private const string RfcExampleJson = """
                                          {
                                            "foo": ["bar", "baz"],
                                            "": 0,
                                            "a/b": 1,
                                            "c%d": 2,
                                            "e^f": 3,
                                            "g|h": 4,
                                            "i\\j": 5,
                                            "k\"l": 6,
                                            " ": 7,
                                            "m~n": 8
                                          }
                                          """;

    [Theory]
    [InlineData("", RfcExampleJson)]
    [InlineData("/foo", """["bar","baz"]""")]
    [InlineData("/foo/0", "\"bar\"")]
    [InlineData("/", "0")]
    [InlineData("/a~1b", "1")]
    [InlineData("/c%d", "2")]
    [InlineData("/e^f", "3")]
    [InlineData("/g|h", "4")]
    [InlineData("/i\\j", "5")]
    [InlineData("/k\"l", "6")]
    [InlineData("/ ", "7")]
    [InlineData("/m~0n", "8")]
    public void Evaluate_RfcExamples_Succeeds(string path, string expectedJson)
    {
        JsonPointer pointer = new(path);
        var document = ParseJson(RfcExampleJson);
        var result = pointer.Evaluate(document);
        AssertJsonEqual(expectedJson, result.GetRawText());
    }

    [Theory]
    [InlineData("/foo/2")]
    [InlineData("/foo/01")]
    [InlineData("/missing")]
    [InlineData("/foo/0/missing")]
    public void TryEvaluate_MissingPath_ReturnsFalse(string path)
    {
        JsonPointer pointer = new(path);
        var document = ParseJson(RfcExampleJson);
        var found = pointer.TryEvaluate(document, out _);
        Assert.False(found);
    }

    [Fact]
    public void Evaluate_MissingPath_Throws()
    {
        JsonPointer pointer = new("/missing");
        var document = ParseJson(RfcExampleJson);
        Assert.Throws<JsonPointerException>(() => pointer.Evaluate(document));
    }

    [Fact]
    public void TryEvaluateParent_JsonElement_Succeeds()
    {
        JsonPointer pointer = new("/foo/1");
        var document = ParseJson(RfcExampleJson);
        var found = pointer.TryEvaluateParent(document, out var parent);
        Assert.True(found);
        Assert.Equal(JsonValueKind.Array, parent.ValueKind);
        AssertJsonEqual("""["bar","baz"]""", parent.GetRawText());
    }

    [Fact]
    public void EvaluateParent_JsonElement_AtRootChild_ReturnsDocument()
    {
        JsonPointer pointer = new("/foo");
        var document = ParseJson(RfcExampleJson);
        var parent = pointer.EvaluateParent(document);
        Assert.Equal(JsonValueKind.Object, parent.ValueKind);
        AssertJsonEqual(RfcExampleJson, parent.GetRawText());
    }

    [Fact]
    public void EvaluateParent_RootJsonElement_Throws()
    {
        JsonPointer pointer = new("");
        var document = ParseJson(RfcExampleJson);
        var exception = Assert.Throws<JsonPointerException>(() => pointer.EvaluateParent(document));
        Assert.Equal("Root JSON Pointer does not have a parent.", exception.Message);
    }

    [Fact]
    public void TryEvaluate_JsonNode_Succeeds()
    {
        JsonPointer pointer = new("/foo/1");
        var document = JsonNode.Parse(RfcExampleJson);
        var found = pointer.TryEvaluate(document, out var value);
        Assert.True(found);
        Assert.NotNull(value);
        Assert.Equal("\"baz\"", value.ToJsonString());
    }

    [Fact]
    public void Evaluate_JsonNode_Succeeds()
    {
        JsonPointer pointer = new("/foo/1");
        var document = JsonNode.Parse(RfcExampleJson);
        var value = pointer.Evaluate(document);
        Assert.NotNull(value);
        Assert.Equal("\"baz\"", value.ToJsonString());
    }

    [Fact]
    public void TryEvaluateParent_JsonNode_Succeeds()
    {
        JsonPointer pointer = new("/foo/1");
        var document = JsonNode.Parse(RfcExampleJson);
        var found = pointer.TryEvaluateParent(document, out var parent);
        Assert.True(found);
        Assert.NotNull(parent);
        Assert.Equal("""["bar","baz"]""", parent.ToJsonString());
    }

    [Fact]
    public void EvaluateParent_JsonNode_AtRootChild_ReturnsDocument()
    {
        JsonPointer pointer = new("/foo");
        var document = JsonNode.Parse(RfcExampleJson);
        var parent = pointer.EvaluateParent(document);
        Assert.NotNull(parent);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RfcExampleJson), parent));
    }

    [Fact]
    public void EvaluateParent_RootJsonNode_Throws()
    {
        JsonPointer pointer = new("");
        var document = JsonNode.Parse(RfcExampleJson);
        var exception = Assert.Throws<JsonPointerException>(() => pointer.EvaluateParent(document));
        Assert.Equal("Root JSON Pointer does not have a parent.", exception.Message);
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void AssertJsonEqual(string expectedJson, string actualJson)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actual = JsonNode.Parse(actualJson);
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }
}
