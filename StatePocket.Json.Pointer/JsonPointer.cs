using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Pointer;

[TypeConverter(typeof(JsonPointerTypeConverter))]
[JsonConverter(typeof(JsonPointerJsonConverter))]
public sealed class JsonPointer
{
    private readonly string[] _segments;
    public JsonPointer(string path) : this(ParseSegments(path ?? throw new ArgumentNullException(nameof(path)))) {}
    private JsonPointer(string[] segments) => _segments = segments;
    public bool IsRoot => _segments.Length == 0;
    public bool HasSegments => _segments.Length != 0;
    public string? LastSegment => _segments.Length == 0 ? null : _segments[^1];
    public ReadOnlyCollection<string> Segments => Array.AsReadOnly(_segments);

    public static JsonPointer Parse(string path)
    {
        return new JsonPointer(path);
    }

    public static bool TryParse(string? path, [NotNullWhen(true)] out JsonPointer? result)
    {
        if (path is null)
        {
            result = null;
            return false;
        }
        if (TryParseSegments(path, out var segments))
        {
            result = new JsonPointer(segments);
            return true;
        }
        result = null;
        return false;
    }

    public bool IsPrefixOf(JsonPointer other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _segments.Length <= other._segments.Length
            && !_segments.Where((t, index) => !string.Equals(t, other._segments[index], StringComparison.Ordinal))
                         .Any();
    }

    public JsonElement Evaluate(JsonElement document)
    {
        return TryEvaluate(document, out var value)
          ? value
          : throw new JsonPointerException($"Path '{this}' does not exist.");
    }

    public bool TryEvaluate(JsonElement document, out JsonElement value)
    {
        var current = document;
        if (_segments.Any(segment => !TryGetChild(current, segment, out current)))
        {
            value = default;
            return false;
        }
        value = current;
        return true;
    }

    public JsonElement EvaluateParent(JsonElement document)
    {
        return TryEvaluateParent(document, out var value) ? value :
            IsRoot ? throw new JsonPointerException("Root JSON Pointer does not have a parent.") :
            throw new JsonPointerException($"Parent path for '{this}' does not exist.");
    }

    public bool TryEvaluateParent(JsonElement document, out JsonElement value)
    {
        if (!IsRoot)
        {
            return TryEvaluate(document, _segments.Length - 1, out value);
        }
        value = default;
        return false;
    }

    public JsonNode? Evaluate(JsonNode? document)
    {
        return TryEvaluate(document, out var value)
          ? value
          : throw new JsonPointerException($"Path '{this}' does not exist.");
    }

    public bool TryEvaluate(JsonNode? document, out JsonNode? value)
    {
        var current = document;
        if (_segments.Any(segment => !TryGetChild(current, segment, out current)))
        {
            value = null;
            return false;
        }
        value = current;
        return true;
    }

    public JsonNode? EvaluateParent(JsonNode? document)
    {
        return TryEvaluateParent(document, out var value) ? value :
            IsRoot ? throw new JsonPointerException("Root JSON Pointer does not have a parent.") :
            throw new JsonPointerException($"Parent path for '{this}' does not exist.");
    }

    public bool TryEvaluateParent(JsonNode? document, out JsonNode? value)
    {
        if (!IsRoot)
        {
            return TryEvaluate(document, _segments.Length - 1, out value);
        }
        value = null;
        return false;
    }

    public override string ToString()
    {
        return IsRoot ? "" : $"/{string.Join("/", _segments.Select(EscapeSegment))}";
    }

    public bool TryGetLastSegment([NotNullWhen(true)] out string? segment)
    {
        if (_segments.Length == 0)
        {
            segment = null;
            return false;
        }
        segment = _segments[^1];
        return true;
    }

    private static string[] ParseSegments(string path)
    {
        return TryParseSegments(path, out var segments)
          ? segments
          : throw new JsonPointerException($"Invalid JSON Pointer path '{path}'.");
    }

    private static bool TryParseSegments(string path, [NotNullWhen(true)] out string[]? segments)
    {
        if (path.Length == 0)
        {
            segments = [];
            return true;
        }
        if (path[0] != '/')
        {
            segments = null;
            return false;
        }
        List<string> parsedSegments = [];
        StringBuilder currentSegment = new(path.Length);
        for (var index = 1; index < path.Length; index++)
        {
            var current = path[index];
            switch (current)
            {
                case '/':
                    parsedSegments.Add(currentSegment.ToString());
                    currentSegment.Clear();
                    continue;
                case '~':
                    index++;
                    if (index >= path.Length)
                    {
                        segments = null;
                        return false;
                    }
                    switch (path[index])
                    {
                        case '0':
                            currentSegment.Append('~');
                            break;
                        case '1':
                            currentSegment.Append('/');
                            break;
                        default:
                            segments = null;
                            return false;
                    }
                    continue;
                default:
                    currentSegment.Append(current);
                    break;
            }
        }
        parsedSegments.Add(currentSegment.ToString());
        segments = [.. parsedSegments];
        return true;
    }

    private static string EscapeSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Replace("~", "~0", StringComparison.Ordinal)
                      .Replace("/", "~1", StringComparison.Ordinal);
    }

    private static bool TryGetChild(JsonElement current, string segment, out JsonElement child)
    {
        switch (current.ValueKind)
        {
            case JsonValueKind.Object when current.TryGetProperty(segment, out child):
                return true;
            case JsonValueKind.Array:
                if (!TryParseArrayIndex(segment, out var index)
                 || index < 0
                 || index >= current.GetArrayLength())
                {
                    child = default;
                    return false;
                }
                child = current[index];
                return true;
            default:
                child = default;
                return false;
        }
    }

    private static bool TryGetChild(JsonNode? current, string segment, out JsonNode? child)
    {
        switch (current)
        {
            case JsonObject jsonObject:
                return jsonObject.TryGetPropertyValue(segment, out child);
            case JsonArray jsonArray:
                if (!TryParseArrayIndex(segment, out var index)
                 || index < 0
                 || index >= jsonArray.Count)
                {
                    child = null;
                    return false;
                }
                child = jsonArray[index];
                return true;
            default:
                child = null;
                return false;
        }
    }

    private static bool TryParseArrayIndex(string segment, out int index)
    {
        if (string.IsNullOrEmpty(segment))
        {
            index = 0;
            return false;
        }
        if (segment == "0")
        {
            index = 0;
            return true;
        }
        if (segment[0] == '0'
         || segment.Any(static character => !char.IsAsciiDigit(character)))
        {
            index = 0;
            return false;
        }
        return int.TryParse(
            segment,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out index
        );
    }

    private bool TryEvaluate(JsonElement document, int segmentCount, out JsonElement value)
    {
        var current = document;
        for (var index = 0; index < segmentCount; index++)
        {
            if (TryGetChild(current, _segments[index], out current))
            {
                continue;
            }
            value = default;
            return false;
        }
        value = current;
        return true;
    }

    private bool TryEvaluate(JsonNode? document, int segmentCount, out JsonNode? value)
    {
        var current = document;
        for (var index = 0; index < segmentCount; index++)
        {
            if (TryGetChild(current, _segments[index], out current))
            {
                continue;
            }
            value = null;
            return false;
        }
        value = current;
        return true;
    }
}
