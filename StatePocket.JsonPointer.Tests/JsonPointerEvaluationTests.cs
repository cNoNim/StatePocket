using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatePocket.JsonPointer.Tests;

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
