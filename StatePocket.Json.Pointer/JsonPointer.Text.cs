namespace StatePocket.Json.Pointer;

public readonly partial struct JsonPointer
{
    public static JsonPointer Parse(string s, IFormatProvider? provider)
    {
        _ = provider;
        ArgumentNullException.ThrowIfNull(s);
        return new JsonPointer(ParseSegments(s));
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out JsonPointer result)
    {
        _ = provider;
        return TryParse(s, out result);
    }

    public static JsonPointer Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        _ = provider;
        return new JsonPointer(ParseSegments(s));
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out JsonPointer result)
    {
        _ = provider;
        if (!TryParseSegments(s, out var segments))
        {
            result = default;
            return false;
        }
        result = new JsonPointer(segments);
        return true;
    }

    public static JsonPointer Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        _ = provider;
        return new JsonPointer(ParseSegments(utf8Text));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out JsonPointer result)
    {
        _ = provider;
        if (!TryParseSegments(utf8Text, out var segments))
        {
            result = default;
            return false;
        }
        result = new JsonPointer(segments);
        return true;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = formatProvider;
        EnsureSupportedFormat(format);
        return ToString();
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider
    )
    {
        _ = provider;
        if (!IsSupportedFormat(format))
        {
            charsWritten = 0;
            return false;
        }
        var written = 0;
        foreach (var segment in _segments.Span)
        {
            if ((uint)written >= (uint)destination.Length)
            {
                charsWritten = 0;
                return false;
            }
            destination[written++] = '/';
            if (!TryWriteEscapedSegment(destination[written..], segment, out var segmentCharsWritten))
            {
                charsWritten = 0;
                return false;
            }
            written += segmentCharsWritten;
        }
        charsWritten = written;
        return true;
    }

    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider
    )
    {
        _ = provider;
        if (!IsSupportedFormat(format))
        {
            bytesWritten = 0;
            return false;
        }
        var written = 0;
        foreach (var segment in _segments.Span)
        {
            if ((uint)written >= (uint)utf8Destination.Length)
            {
                bytesWritten = 0;
                return false;
            }
            utf8Destination[written++] = (byte)'/';
            if (!TryWriteEscapedSegmentUtf8(utf8Destination[written..], segment, out var segmentBytesWritten))
            {
                bytesWritten = 0;
                return false;
            }
            written += segmentBytesWritten;
        }
        bytesWritten = written;
        return true;
    }

    private static void EnsureSupportedFormat(string? format)
    {
        if (!IsSupportedFormat(format))
        {
            throw new FormatException($"The '{format}' format string is not supported.");
        }
    }

    private static bool IsSupportedFormat(ReadOnlySpan<char> format)
    {
        return format.IsEmpty
            || format.Equals("G", StringComparison.Ordinal)
            || format.Equals("g", StringComparison.Ordinal);
    }

    private static bool IsSupportedFormat(string? format)
    {
        return string.IsNullOrEmpty(format)
            || string.Equals(format, "G", StringComparison.Ordinal)
            || string.Equals(format, "g", StringComparison.Ordinal);
    }

    private int GetEscapedLength()
    {
        var totalLength = 0;
        foreach (var segment in _segments.Span)
        {
            totalLength += 1 + GetEscapedLength(segment);
        }
        return totalLength;
    }

    private static int GetEscapedLength(string segment)
    {
        var length = 0;
        foreach (var character in segment)
        {
            length += character is '~' or '/' ? 2 : 1;
        }
        return length;
    }

    private static bool TryWriteEscapedSegment(Span<char> destination, string segment, out int charsWritten)
    {
        var remaining = segment.AsSpan();
        var written = 0;
        while (!remaining.IsEmpty)
        {
            var escapeIndex = remaining.IndexOfAny('~', '/');
            if (escapeIndex < 0)
            {
                if (!remaining.TryCopyTo(destination[written..]))
                {
                    charsWritten = 0;
                    return false;
                }
                charsWritten = written + remaining.Length;
                return true;
            }
            var chunk = remaining[..escapeIndex];
            if (!chunk.TryCopyTo(destination[written..]))
            {
                charsWritten = 0;
                return false;
            }
            written += chunk.Length;
            if (destination.Length - written < 2)
            {
                charsWritten = 0;
                return false;
            }
            destination[written++] = '~';
            destination[written++] = remaining[escapeIndex] == '~' ? '0' : '1';
            remaining = remaining[(escapeIndex + 1)..];
        }
        charsWritten = written;
        return true;
    }

    private static bool TryWriteEscapedSegmentUtf8(Span<byte> destination, string segment, out int bytesWritten)
    {
        var remaining = segment.AsSpan();
        var written = 0;
        while (!remaining.IsEmpty)
        {
            var escapeIndex = remaining.IndexOfAny('~', '/');
            var chunk = escapeIndex < 0 ? remaining : remaining[..escapeIndex];
            var byteCount = StrictUtf8Encoding.GetByteCount(chunk);
            if (destination.Length - written < byteCount)
            {
                bytesWritten = 0;
                return false;
            }
            written += StrictUtf8Encoding.GetBytes(chunk, destination[written..]);
            if (escapeIndex < 0)
            {
                bytesWritten = written;
                return true;
            }
            if (destination.Length - written < 2)
            {
                bytesWritten = 0;
                return false;
            }
            destination[written++] = (byte)'~';
            destination[written++] = remaining[escapeIndex] == '~' ? (byte)'0' : (byte)'1';
            remaining = remaining[(escapeIndex + 1)..];
        }
        bytesWritten = written;
        return true;
    }
}
