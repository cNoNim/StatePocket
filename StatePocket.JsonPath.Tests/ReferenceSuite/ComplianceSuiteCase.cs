namespace StatePocket.JsonPath.Tests.ReferenceSuite;

public sealed record ComplianceSuiteCase(
    string Name,
    string Selector,
    string? DocumentJson,
    string[]? ResultJson,
    string[][]? ResultsJson,
    string[]? ResultPaths,
    string[][]? ResultsPaths,
    bool InvalidSelector,
    string[] Tags
);
