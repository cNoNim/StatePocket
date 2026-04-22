using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace StatePocket.Json.Pointer;

public readonly partial struct JsonPointer
{
    public static JsonPointer Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new JsonPointer(ParseSegments(path));
    }

    public static bool TryParse(string? path, out JsonPointer result)
    {
        if (path is null
         || !TryParseSegments(path.AsSpan(), out var segments))
        {
            result = default;
            return false;
        }
        result = new JsonPointer(segments);
        return true;
    }

    private static ReadOnlyMemory<string> ParseSegments(string path)
    {
        return TryParseSegments(path.AsSpan(), out var segments)
          ? segments
          : throw new JsonPointerException($"Invalid JSON Pointer path '{path}'.");
    }

    private static ReadOnlyMemory<string> ParseSegments(ReadOnlySpan<char> path)
    {
        return TryParseSegments(path, out var segments)
          ? segments
          : throw new JsonPointerException($"Invalid JSON Pointer path '{path.ToString()}'.");
    }

    private static ReadOnlyMemory<string> ParseSegments(ReadOnlySpan<byte> utf8Text)
    {
        if (TryParseSegments(utf8Text, out var segments))
        {
            return segments;
        }
        try
        {
            _ = StrictUtf8Encoding.GetString(utf8Text);
        }
        catch (DecoderFallbackException exception)
        {
            throw new JsonPointerException("Invalid JSON Pointer UTF-8 path.", exception);
        }
        throw new JsonPointerException("Invalid JSON Pointer UTF-8 path.");
    }

    private static bool TryParseSegments(ReadOnlySpan<char> path, out ReadOnlyMemory<string> segments)
    {
        if (path.Length == 0)
        {
            segments = ReadOnlyMemory<string>.Empty;
            return true;
        }
        if (path[0] != '/')
        {
            segments = default;
            return false;
        }
        var content = path[1..];
        if (content.IndexOfAny('/', '~') < 0)
        {
            segments = new[]
            {
                path[1..]
                   .ToString()
            };
            return true;
        }
        var segmentCount = 1;
        for (var index = 0; index < content.Length; index++)
        {
            switch (content[index])
            {
                case '/':
                    segmentCount++;
                    break;
                case '~':
                    index++;
                    if (index >= content.Length
                     || (content[index] != '0' && content[index] != '1'))
                    {
                        segments = default;
                        return false;
                    }
                    break;
            }
        }
        var parsedSegments = new string[segmentCount];
        var segmentStart = 0;
        for (var segmentIndex = 0; segmentIndex < parsedSegments.Length; segmentIndex++)
        {
            var separatorIndex = content[segmentStart..]
               .IndexOf('/');
            var segmentLength = separatorIndex >= 0 ? separatorIndex : content.Length - segmentStart;
            var segment = content.Slice(segmentStart, segmentLength);
            if (!TryUnescapeSegment(segment, out var parsedSegment))
            {
                segments = default;
                return false;
            }
            parsedSegments[segmentIndex] = parsedSegment;
            if (separatorIndex >= 0)
            {
                segmentStart += separatorIndex + 1;
            }
        }
        segments = parsedSegments;
        return true;
    }

    private static bool TryParseSegments(ReadOnlySpan<byte> utf8Text, out ReadOnlyMemory<string> segments)
    {
        if (utf8Text.Length == 0)
        {
            segments = ReadOnlyMemory<string>.Empty;
            return true;
        }
        if (utf8Text[0] != (byte)'/')
        {
            segments = default;
            return false;
        }
        var content = utf8Text[1..];
        if (content.IndexOfAny((byte)'/', (byte)'~') < 0)
        {
            if (!TryDecodeUtf8(content, out var parsedSegment))
            {
                segments = default;
                return false;
            }
            segments = new[]
            {
                parsedSegment
            };
            return true;
        }
        var segmentCount = 1;
        for (var index = 0; index < content.Length; index++)
        {
            switch (content[index])
            {
                case (byte)'/':
                    segmentCount++;
                    break;
                case (byte)'~':
                    index++;
                    if (index >= content.Length
                     || (content[index] != (byte)'0' && content[index] != (byte)'1'))
                    {
                        segments = default;
                        return false;
                    }
                    break;
            }
        }
        var parsedSegments = new string[segmentCount];
        var segmentStart = 0;
        for (var segmentIndex = 0; segmentIndex < parsedSegments.Length; segmentIndex++)
        {
            var separatorIndex = content[segmentStart..]
               .IndexOf((byte)'/');
            var segmentLength = separatorIndex >= 0 ? separatorIndex : content.Length - segmentStart;
            var segment = content.Slice(segmentStart, segmentLength);
            if (!TryUnescapeSegment(segment, out var parsedSegment))
            {
                segments = default;
                return false;
            }
            parsedSegments[segmentIndex] = parsedSegment;
            if (separatorIndex >= 0)
            {
                segmentStart += separatorIndex + 1;
            }
        }
        segments = parsedSegments;
        return true;
    }

    private static bool TryUnescapeSegment(ReadOnlySpan<char> segment, [NotNullWhen(true)] out string? value)
    {
        var escapeIndex = segment.IndexOf('~');
        if (escapeIndex < 0)
        {
            value = segment.ToString();
            return true;
        }
        StringBuilder builder = new(segment.Length);
        builder.Append(segment[..escapeIndex]);
        for (var index = escapeIndex; index < segment.Length; index++)
        {
            if (segment[index] != '~')
            {
                builder.Append(segment[index]);
                continue;
            }
            index++;
            if (index >= segment.Length)
            {
                value = null;
                return false;
            }
            switch (segment[index])
            {
                case '0':
                    builder.Append('~');
                    break;
                case '1':
                    builder.Append('/');
                    break;
                default:
                    value = null;
                    return false;
            }
        }
        value = builder.ToString();
        return true;
    }

    private static bool TryUnescapeSegment(ReadOnlySpan<byte> segment, [NotNullWhen(true)] out string? value)
    {
        var escapeIndex = segment.IndexOf((byte)'~');
        if (escapeIndex < 0)
        {
            return TryDecodeUtf8(segment, out value);
        }
        byte[]? rented = null;
        var buffer = segment.Length <= 256
          ? stackalloc byte[segment.Length]
          : rented = ArrayPool<byte>.Shared.Rent(segment.Length);
        try
        {
            segment[..escapeIndex]
               .CopyTo(buffer);
            var bytesWritten = escapeIndex;
            for (var index = escapeIndex; index < segment.Length; index++)
            {
                if (segment[index] != (byte)'~')
                {
                    buffer[bytesWritten++] = segment[index];
                    continue;
                }
                index++;
                if (index >= segment.Length)
                {
                    value = null;
                    return false;
                }
                switch (segment[index])
                {
                    case (byte)'0':
                        buffer[bytesWritten++] = (byte)'~';
                        break;
                    case (byte)'1':
                        buffer[bytesWritten++] = (byte)'/';
                        break;
                    default:
                        value = null;
                        return false;
                }
            }
            return TryDecodeUtf8(buffer[..bytesWritten], out value);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static bool TryDecodeUtf8(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out string? value)
    {
        try
        {
            value = StrictUtf8Encoding.GetString(utf8Text);
            return true;
        }
        catch (DecoderFallbackException)
        {
            value = null;
            return false;
        }
    }
}
