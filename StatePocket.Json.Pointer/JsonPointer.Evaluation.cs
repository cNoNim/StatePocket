using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatePocket.Json.Pointer;

public readonly partial struct JsonPointer
{
    public JsonElement Evaluate(JsonElement document)
    {
        return TryEvaluate(document, out var value)
          ? value
          : throw new JsonPointerException($"Path '{this}' does not exist.");
    }

    public bool TryEvaluate(JsonElement document, out JsonElement value)
    {
        var current = document;
        foreach (var segment in _segments.Span)
        {
            if (TryGetChild(current, segment, out current))
            {
                continue;
            }
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
        foreach (var segment in _segments.Span)
        {
            if (TryGetChild(current, segment, out current))
            {
                continue;
            }
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
        var segments = _segments.Span;
        for (var index = 0; index < segmentCount; index++)
        {
            if (TryGetChild(current, segments[index], out current))
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
        var segments = _segments.Span;
        for (var index = 0; index < segmentCount; index++)
        {
            if (TryGetChild(current, segments[index], out current))
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
