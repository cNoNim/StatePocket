namespace StatePocket.Json.Pointer.Tests;

public sealed class JsonPointerParseTests
{
    [Fact]
    public void Parse_EmptyPath_ReturnsRoot()
    {
        JsonPointer pointer = new("");
        Assert.True(pointer.IsRoot);
        Assert.False(pointer.HasSegments);
        Assert.Empty(pointer.Segments);
        Assert.Null(pointer.LastSegment);
        Assert.False(pointer.TryGetLastSegment(out var segment));
        Assert.Null(segment);
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
        JsonPointer pointer = new(path);
        Assert.False(pointer.IsRoot);
        Assert.True(pointer.HasSegments);
        Assert.Equal(expectedSegments, pointer.Segments);
        Assert.Equal(expectedSegments[^1], pointer.LastSegment);
        Assert.True(pointer.TryGetLastSegment(out var segment));
        Assert.Equal(expectedSegments[^1], segment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/foo/bar")]
    [InlineData("/a~1b")]
    [InlineData("/m~0n")]
    [InlineData("/foo/~0/~1~1/~0~0/baz")]
    public void ToString_RoundTripsPointer(string path)
    {
        JsonPointer pointer = new(path);
        Assert.Equal(path, pointer.ToString());
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("foo/bar")]
    public void Parse_PathWithoutLeadingSlash_Throws(string path)
    {
        Assert.Throws<JsonPointerException>(() => _ = new JsonPointer(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/foo/bar")]
    public void TryParse_ValidPath_ReturnsPointer(string path)
    {
        var parsed = JsonPointer.TryParse(path, out var pointer);
        Assert.True(parsed);
        Assert.NotNull(pointer);
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
        Assert.Null(pointer);
    }

    [Theory]
    [InlineData("/foo/~")]
    [InlineData("/foo/~2")]
    [InlineData("/foo/~a")]
    public void Parse_PathWithInvalidEscape_Throws(string path)
    {
        Assert.Throws<JsonPointerException>(() => _ = new JsonPointer(path));
    }
}
