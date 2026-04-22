using System.Text;
using System.Text.Json;

namespace StatePocket.Json.Path.Tests;

public sealed class JsonPathRegressionTests
{
    [Fact]
    public void Evaluate_PreservesFullPrecisionForNumericEquality()
    {
        var query = new JsonPath("$.items[?@.id==9007199254740993].id");
        var document = ParseJson(
            """
            {
              "items": [
                { "id": 9007199254740992 },
                { "id": 9007199254740993 }
              ]
            }
            """
        );
        var matches = query.Evaluate(document);
        Assert.Single(matches);
        Assert.Equal(
            "9007199254740993",
            matches[0]
               .Value.GetRawText()
        );
    }

    [Fact]
    public void Evaluate_PreservesFullPrecisionForNumericComparisons()
    {
        var query = new JsonPath("$.items[?@.id>9007199254740992].id");
        var document = ParseJson(
            """
            {
              "items": [
                { "id": 9007199254740992 },
                { "id": 9007199254740993 }
              ]
            }
            """
        );
        var matches = query.Evaluate(document);
        Assert.Single(matches);
        Assert.Equal(
            "9007199254740993",
            matches[0]
               .Value.GetRawText()
        );
    }

    [Fact]
    public void Evaluate_DoesNotCrashOnHugePositiveExponent()
    {
        var query = new JsonPath("$.items[?@.value==1e2147483648].value");
        var document = ParseJson(
            """
            {
              "items": [
                { "value": 1e2147483647 },
                { "value": 1e2147483648 }
              ]
            }
            """
        );
        var matches = query.Evaluate(document);
        Assert.Single(matches);
        Assert.Equal(
            "1e2147483648",
            matches[0]
               .Value.GetRawText()
        );
    }

    [Fact]
    public void Evaluate_DoesNotCrashOnIntMinValueExponentComparison()
    {
        var query = new JsonPath("$.items[?@.value>1e-2147483648].value");
        var document = ParseJson(
            """
            {
              "items": [
                { "value": 0 },
                { "value": 1 }
              ]
            }
            """
        );
        var matches = query.Evaluate(document);
        Assert.Single(matches);
        Assert.Equal(
            "1",
            matches[0]
               .Value.GetRawText()
        );
    }

    [Fact]
    public void Evaluate_DoesNotCrashWhenDeepEqualityComparesHugeExponentNumbersInObjects()
    {
        var query = new JsonPath("$.items[?@.a==@.b].a");
        var document = ParseJson(
            """
            {
              "items": [
                {
                  "a": { "n": 1e2147483648 },
                  "b": { "n": 10e2147483647 }
                }
              ]
            }
            """
        );
        var matches = query.Evaluate(document);
        Assert.Single(matches);
        Assert.Equal(
            "1e2147483648",
            matches[0]
               .Value.GetProperty("n")
               .GetRawText()
        );
    }

    [Fact]
    public void Constructor_ThrowsJsonPathExceptionWhenRegexPatternIsInvalid()
    {
        Assert.Throws<JsonPathException>(() => _ = new JsonPath("$[?match(@.name, '[')]"));
    }

    [Fact]
    public void Constructor_ThrowsJsonPathExceptionWhenRegexPatternIsInvalidEvenWithoutStringInput()
    {
        Assert.Throws<JsonPathException>(() => _ = new JsonPath("$[?match(@.name, '[')]"));
    }

    [Fact]
    public void Constructor_RejectsMalformedFilterPathInsteadOfStrippingInnerWhitespace()
    {
        Assert.Throws<JsonPathException>(() => _ = new JsonPath("$[?@.na me]"));
    }

    [Fact]
    public void Constructor_RejectsTopLevelCurrentNodeRoot()
    {
        Assert.Throws<JsonPathException>(() => _ = new JsonPath("@.status"));
        Assert.Throws<JsonPathException>(() => _ = new JsonPath("@"));
    }

    [Fact]
    public void Evaluate_NormalizedPathEscapesControlCharactersInPropertyNames()
    {
        var query = new JsonPath("$.*");
        var document = ParseJson("{\"\\u0000\":1}");
        var matches = query.Evaluate(document);
        Assert.Single(matches);
        Assert.Equal("$['\\u0000']", matches[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_HandlesDeepDescendantTraversalWithoutRecursiveEnumeration()
    {
        const int depth = 2048;
        var query = new JsonPath("$..value");
        var document = ParseJson(CreateDeepObjectJson(depth));
        var matches = query.Evaluate(document);
        Assert.Single(matches);
        Assert.Equal(
            "1",
            matches[0]
               .Value.GetRawText()
        );
        Assert.Equal(
            "$" + string.Concat(Enumerable.Repeat("['child']", depth)) + "['value']",
            matches[0].NormalizedPath
        );
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                MaxDepth = 4096
            }
        );
        return document.RootElement.Clone();
    }

    private static string CreateDeepObjectJson(int depth)
    {
        StringBuilder json = new();
        for (var index = 0; index < depth; index++)
        {
            json.Append("{\"child\":");
        }
        json.Append("{\"value\":1}");
        for (var index = 0; index < depth; index++)
        {
            json.Append('}');
        }
        return json.ToString();
    }
}
