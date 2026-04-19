using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Pointer;

[TypeConverter(typeof(JsonPointerTypeConverter))]
[JsonConverter(typeof(JsonPointerJsonConverter))]
public readonly partial struct JsonPointer : IEquatable<JsonPointer>, ISpanParsable<JsonPointer>, ISpanFormattable,
                                             IUtf8SpanParsable<JsonPointer>, IUtf8SpanFormattable
{
    private readonly ReadOnlyMemory<string> _segments;
    private static readonly UTF8Encoding StrictUtf8Encoding = new(false, true);
    private JsonPointer(ReadOnlyMemory<string> segments) => _segments = segments;
    public bool IsRoot => _segments.IsEmpty;
    public string? LastSegment => _segments.IsEmpty ? null : _segments.Span[^1];

    public bool IsPrefixOf(JsonPointer other)
    {
        var left = _segments.Span;
        var right = other._segments.Span;
        if (left.Length > right.Length)
        {
            return false;
        }
        for (var index = 0; index < left.Length; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public override string ToString()
    {
        if (IsRoot)
        {
            return "";
        }
        return string.Create(
            GetEscapedLength(),
            this,
            static (destination, pointer) =>
            {
                var written = 0;
                foreach (var segment in pointer._segments.Span)
                {
                    destination[written++] = '/';
                    var formatted = TryWriteEscapedSegment(
                        destination[written..],
                        segment,
                        out var segmentCharsWritten
                    );
                    if (!formatted)
                    {
                        throw new InvalidOperationException(
                            "Destination length must match the escaped JSON Pointer length."
                        );
                    }
                    written += segmentCharsWritten;
                }
            }
        );
    }

    public bool Equals(JsonPointer other)
    {
        var left = _segments.Span;
        var right = other._segments.Span;
        if (left.Length != right.Length)
        {
            return false;
        }
        for (var index = 0; index < left.Length; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is JsonPointer other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hashCode = new();
        foreach (var segment in _segments.Span)
        {
            hashCode.Add(segment, StringComparer.Ordinal);
        }
        return hashCode.ToHashCode();
    }

    public static bool operator ==(JsonPointer left, JsonPointer right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(JsonPointer left, JsonPointer right)
    {
        return !left.Equals(right);
    }
}
