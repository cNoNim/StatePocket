namespace StatePocket.Json.Path.Tests.ReferenceSuite;

public sealed class ComplianceSuiteInventoryTests
{
    [Fact]
    public void LoadAll_TracksFullComplianceInventory()
    {
        var cases = ComplianceSuiteLoader.LoadAll();
        Assert.Equal(703, cases.Count);
        Assert.Equal(247, cases.Count(static testCase => testCase.InvalidSelector));
        Assert.Equal(447, cases.Count(static testCase => testCase.ResultJson is not null));
        Assert.Equal(9, cases.Count(static testCase => testCase.ResultsJson is not null));
    }

    [Fact]
    public void LoadAll_TracksTagDistribution()
    {
        var tagCounts = ComplianceSuiteLoader.LoadAll()
                                             .SelectMany(static testCase => testCase.Tags)
                                             .GroupBy(static tag => tag, StringComparer.Ordinal)
                                             .OrderBy(static group => group.Key, StringComparer.Ordinal)
                                             .ToDictionary(
                                                  static group => group.Key,
                                                  static group => group.Count(),
                                                  StringComparer.Ordinal
                                              );
        Assert.Equal(12, tagCounts.Count);
        Assert.Equal(34, tagCounts["boundary"]);
        Assert.Equal(9, tagCounts["case"]);
        Assert.Equal(22, tagCounts["count"]);
        Assert.Equal(110, tagCounts["function"]);
        Assert.Equal(35, tagCounts["index"]);
        Assert.Equal(24, tagCounts["length"]);
        Assert.Equal(24, tagCounts["match"]);
        Assert.Equal(33, tagCounts["search"]);
        Assert.Equal(72, tagCounts["slice"]);
        Assert.Equal(96, tagCounts["unicode"]);
        Assert.Equal(5, tagCounts["value"]);
        Assert.Equal(205, tagCounts["whitespace"]);
    }
}
