namespace StatePocket.JsonPath.Tests.ReferenceSuite;

public sealed class ComplianceSuiteTests
{
    public static TheoryData<string, ComplianceSuiteCase> Cases => BuildCases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void ComplianceCase_MatchesSuite(string name, ComplianceSuiteCase testCase)
    {
        _ = name;
        if (testCase.InvalidSelector)
        {
            Assert.Throws<JsonPathException>(() => _ = new JsonPath(testCase.Selector));
            return;
        }
        Assert.NotNull(testCase.DocumentJson);
        var query = new JsonPath(testCase.Selector);
        var document = ComplianceSuiteAssertions.ParseJson(testCase.DocumentJson!);
        ComplianceSuiteAssertions.AssertEvaluationMatches(testCase, query.Evaluate(document));
    }

    private static TheoryData<string, ComplianceSuiteCase> BuildCases()
    {
        TheoryData<string, ComplianceSuiteCase> rows = [];
        foreach (var testCase in ComplianceSuiteLoader.LoadAll())
        {
            rows.Add(testCase.Name, testCase);
        }
        return rows;
    }
}
