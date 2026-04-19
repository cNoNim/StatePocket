using System.Text;

namespace StatePocket.Json.Pointer.Tests;

public sealed class JsonPointerTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/foo/bar")]
    [InlineData("/m~0n")]
    [InlineData("/a~1b")]
    public void Parse_WithProvider_RoundTrips(string path)
    {
        Assert.Equal(JsonPointer.Parse(path, null), JsonPointer.Parse(path, null));
        Assert.True(JsonPointer.TryParse(path, null, out var parsed));
        Assert.Equal(JsonPointer.Parse(path, null), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/foo/bar")]
    public void Parse_FromCharSpan_RoundTrips(string path)
    {
        Assert.Equal(JsonPointer.Parse(path, null), JsonPointer.Parse(path.AsSpan(), null));
        Assert.True(JsonPointer.TryParse(path.AsSpan(), null, out var parsed));
        Assert.Equal(JsonPointer.Parse(path, null), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/foo/bar")]
    [InlineData("/привет/мир")]
    public void Parse_FromUtf8Span_RoundTrips(string path)
    {
        var utf8 = Encoding.UTF8.GetBytes(path);
        Assert.Equal(JsonPointer.Parse(path, null), JsonPointer.Parse(utf8, null));
        Assert.True(JsonPointer.TryParse(utf8, null, out var parsed));
        Assert.Equal(JsonPointer.Parse(path, null), parsed);
    }

    [Fact]
    public void TryParse_FromUtf8Span_InvalidPath_ReturnsFalse()
    {
        var utf8 = "foo/bar"u8;
        Assert.False(JsonPointer.TryParse(utf8, null, out var parsed));
        Assert.Equal(default, parsed);
    }

    [Fact]
    public void TryParse_FromUtf8Span_InvalidUtf8_ReturnsFalse()
    {
        ReadOnlySpan<byte> utf8 = [(byte)'/', 0xC3, 0x28];
        Assert.False(JsonPointer.TryParse(utf8, null, out var parsed));
        Assert.Equal(default, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("G")]
    [InlineData("g")]
    public void ToString_WithFormat_ReturnsCanonicalString(string? format)
    {
        var pointer = JsonPointer.Parse("/foo/bar", null);
        Assert.Equal("/foo/bar", pointer.ToString(format, null));
    }

    [Fact]
    public void ToString_WithUnsupportedFormat_Throws()
    {
        var pointer = JsonPointer.Parse("/foo/bar", null);
        var exception = Assert.Throws<FormatException>(() => pointer.ToString("X", null));
        Assert.Equal("The 'X' format string is not supported.", exception.Message);
    }

    [Fact]
    public void TryFormat_WritesUtf16Text()
    {
        var pointer = JsonPointer.Parse("/foo~0~1bar/baz", null);
        Span<char> buffer = stackalloc char[32];
        Assert.True(
            pointer.TryFormat(
                buffer,
                out var written,
                default,
                null
            )
        );
        Assert.Equal("/foo~0~1bar/baz", new string(buffer[..written]));
    }

    [Fact]
    public void TryFormat_WritesUtf8Text()
    {
        var pointer = JsonPointer.Parse("/привет~0~1мир", null);
        Span<byte> buffer = stackalloc byte[64];
        Assert.True(
            pointer.TryFormat(
                buffer,
                out var written,
                default,
                null
            )
        );
        Assert.Equal("/привет~0~1мир", Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public void TryFormat_WithUnsupportedFormat_ReturnsFalse()
    {
        var pointer = JsonPointer.Parse("/foo/bar", null);
        Span<char> charBuffer = stackalloc char[32];
        Span<byte> byteBuffer = stackalloc byte[32];
        Assert.False(
            pointer.TryFormat(
                charBuffer,
                out var charsWritten,
                "X",
                null
            )
        );
        Assert.Equal(0, charsWritten);
        Assert.False(
            pointer.TryFormat(
                byteBuffer,
                out var bytesWritten,
                "X",
                null
            )
        );
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryFormat_WithTooSmallBuffer_ReturnsFalse()
    {
        var pointer = JsonPointer.Parse("/foo/bar", null);
        Span<char> charBuffer = stackalloc char[4];
        Span<byte> byteBuffer = stackalloc byte[4];
        Assert.False(
            pointer.TryFormat(
                charBuffer,
                out var charsWritten,
                default,
                null
            )
        );
        Assert.Equal(0, charsWritten);
        Assert.False(
            pointer.TryFormat(
                byteBuffer,
                out var bytesWritten,
                default,
                null
            )
        );
        Assert.Equal(0, bytesWritten);
    }
}
