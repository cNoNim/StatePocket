using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatePocket.Json.Pointer;

public sealed class JsonPointer
{
    private readonly string[] _segments;

    public JsonPointer(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _segments = ParseSegments(path);
    }

    public bool IsRoot => _segments.Length == 0;
    public string? LastSegment => _segments.Length == 0 ? null : _segments[^1];
    public ReadOnlyCollection<string> Segments => Array.AsReadOnly(_segments);

    public bool IsPrefixOf(JsonPointer other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_segments.Length > other._segments.Length)
        {
            return false;
        }
        for (var index = 0; index < _segments.Length; index++)
        {
            if (!string.Equals(_segments[index], other._segments[index], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
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
        foreach (var segment in _segments)
        {
            if (!TryGetChild(current, segment, out current))
            {
                value = default;
                return false;
            }
        }
        value = current;
        return true;
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
        foreach (var segment in _segments)
        {
            if (!TryGetChild(current, segment, out current))
            {
                value = null;
                return false;
            }
        }
        value = current;
        return true;
    }

    public override string ToString()
    {
        return IsRoot ? "" : $"/{string.Join("/", _segments.Select(EscapeSegment))}";
    }

    private static string[] ParseSegments(string path)
    {
        if (path.Length == 0)
        {
            return [];
        }
        if (path[0] != '/')
        {
            throw new JsonPointerException($"Invalid JSON Pointer path '{path}'.");
        }
        List<string> segments = [];
        StringBuilder currentSegment = new(path.Length);
        for (var index = 1; index < path.Length; index++)
        {
            var current = path[index];
            switch (current)
            {
                case '/':
                    segments.Add(currentSegment.ToString());
                    currentSegment.Clear();
                    continue;
                case '~':
                    index++;
                    if (index >= path.Length)
                    {
                        throw new JsonPointerException($"Invalid JSON Pointer path '{path}'.");
                    }
                    currentSegment.Append(
                        path[index] switch
                        {
                            '0' => '~',
                            '1' => '/',
                            _ => throw new JsonPointerException($"Invalid JSON Pointer path '{path}'.")
                        }
                    );
                    continue;
                default:
                    currentSegment.Append(current);
                    break;
            }
        }
        segments.Add(currentSegment.ToString());
        return [.. segments];
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
                if (!TryParseArrayIndex(segment, out var index))
                {
                    child = default;
                    return false;
                }
                if (index < 0
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
                if (!TryParseArrayIndex(segment, out var index))
                {
                    child = null;
                    return false;
                }
                if (index < 0
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
        if (segment[0] == '0')
        {
            index = 0;
            return false;
        }
        foreach (var character in segment)
        {
            if (!char.IsAsciiDigit(character))
            {
                index = 0;
                return false;
            }
        }
        return int.TryParse(
            segment,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out index
        );
    }
}
