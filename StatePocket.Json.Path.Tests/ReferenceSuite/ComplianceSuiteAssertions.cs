using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatePocket.Json.Path.Tests.ReferenceSuite;

internal static class ComplianceSuiteAssertions
{
    public static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static void AssertEvaluationMatches(ComplianceSuiteCase testCase, IReadOnlyList<JsonPathMatch> actualMatches)
    {
        var actualValues = actualMatches.Select(static match => match.Value.GetRawText())
                                        .ToArray();
        var actualPaths = actualMatches.Select(static match => match.NormalizedPath)
                                       .ToArray();
        if (testCase.ResultsJson is not null)
        {
            Assert.Contains(
                testCase.ResultsJson.Zip(
                    testCase.ResultsPaths!,
                    static (expectedValues, expectedPaths) => (expectedValues, expectedPaths)
                ),
                expected => JsonSequencesEqual(expected.expectedValues, actualValues)
                         && expected.expectedPaths.SequenceEqual(actualPaths)
            );
            return;
        }
        Assert.True(JsonSequencesEqual(testCase.ResultJson!, actualValues));
        Assert.Equal(testCase.ResultPaths, actualPaths);
    }

    private static bool JsonSequencesEqual(string[] expected, string[] actual)
    {
        if (expected.Length != actual.Length)
        {
            return false;
        }
        for (var index = 0; index < expected.Length; index++)
        {
            var expectedNode = JsonNode.Parse(expected[index]);
            var actualNode = JsonNode.Parse(actual[index]);
            if (!JsonNode.DeepEquals(expectedNode, actualNode))
            {
                return false;
            }
        }
        return true;
    }
}
