namespace StatePocket.Json.Pointer.Tests;

public sealed class JsonPointerParseTests
{
    [Fact]
    public void Parse_EmptyPath_ReturnsRoot()
    {
        var pointer = JsonPointer.Parse("", null);
        Assert.True(pointer.IsRoot);
        Assert.Null(pointer.LastSegment);
    }

    [Theory]
    [InlineData(
        "/foo/bar",
        new[]
        {
            "foo", "bar"
        }
    )]
    [InlineData(
        "/foo/bar~0baz",
        new[]
        {
            "foo", "bar~baz"
        }
    )]
    [InlineData(
        "/foo/bar~1baz",
        new[]
        {
            "foo", "bar/baz"
        }
    )]
    [InlineData(
        "/foo/~0/~1~1/~0~0/baz",
        new[]
        {
            "foo",
            "~",
            "//",
            "~~",
            "baz"
        }
    )]
    public void Parse_ValidPath_ReturnsSegments(string path, string[] expectedSegments)
    {
        var pointer = JsonPointer.Parse(path, null);
        Assert.False(pointer.IsRoot);
        Assert.Equal(expectedSegments[^1], pointer.LastSegment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/foo/bar")]
    [InlineData("/a~1b")]
    [InlineData("/m~0n")]
    [InlineData("/foo/~0/~1~1/~0~0/baz")]
    public void ToString_RoundTripsPointer(string path)
    {
        var pointer = JsonPointer.Parse(path, null);
        Assert.Equal(path, pointer.ToString());
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("foo/bar")]
    public void Parse_PathWithoutLeadingSlash_Throws(string path)
    {
        Assert.Throws<JsonPointerException>(() => _ = JsonPointer.Parse(path, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/foo/bar")]
    public void TryParse_ValidPath_ReturnsPointer(string path)
    {
        var parsed = JsonPointer.TryParse(path, out var pointer);
        Assert.True(parsed);
        Assert.Equal(path, pointer.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("foo")]
    [InlineData("/foo/~")]
    public void TryParse_InvalidPath_ReturnsFalse(string? path)
    {
        var parsed = JsonPointer.TryParse(path, out var pointer);
        Assert.False(parsed);
        Assert.Equal(default, pointer);
    }

    [Theory]
    [InlineData("/foo/~")]
    [InlineData("/foo/~2")]
    [InlineData("/foo/~a")]
    public void Parse_PathWithInvalidEscape_Throws(string path)
    {
        Assert.Throws<JsonPointerException>(() => _ = JsonPointer.Parse(path, null));
    }

    [Fact]
    public void DefaultPointer_BehavesAsRoot()
    {
        JsonPointer pointer = default;
        Assert.True(pointer.IsRoot);
        Assert.Null(pointer.LastSegment);
        Assert.Equal("", pointer.ToString());
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        Assert.Equal(JsonPointer.Parse("/foo/bar", null), JsonPointer.Parse("/foo/bar", null));
        Assert.NotEqual(JsonPointer.Parse("/foo/bar", null), JsonPointer.Parse("/foo/baz", null));
    }
}
