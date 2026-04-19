using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(AddOperation), "add")]
[JsonDerivedType(typeof(RemoveOperation), "remove")]
[JsonDerivedType(typeof(ReplaceOperation), "replace")]
[JsonDerivedType(typeof(MoveOperation), "move")]
[JsonDerivedType(typeof(CopyOperation), "copy")]
[JsonDerivedType(typeof(TestOperation), "test")]
public abstract class JsonPatchOperation
{
    protected JsonPatchOperation() {}

    [SetsRequiredMembers]
    protected JsonPatchOperation(JsonPointer path) => Path = path;

    [JsonPropertyName("path")]
    [JsonPropertyOrder(1)]
    [JsonRequired]
    public required JsonPointer Path { get; init; }
    [JsonIgnore]
    public abstract JsonPatchOperationType Op { get; }
    internal abstract JsonNode? ApplyTo(JsonNode? document);

    public static AddOperation Add(JsonPointer path, JsonNode? value)
    {
        return new AddOperation(path, value);
    }

    public static AddOperation Add(string path, JsonNode? value)
    {
        return Add(JsonPointer.Parse(path, null), value);
    }

    public static RemoveOperation Remove(JsonPointer path)
    {
        return new RemoveOperation(path);
    }

    public static RemoveOperation Remove(string path)
    {
        return Remove(JsonPointer.Parse(path, null));
    }

    public static ReplaceOperation Replace(JsonPointer path, JsonNode? value)
    {
        return new ReplaceOperation(path, value);
    }

    public static ReplaceOperation Replace(string path, JsonNode? value)
    {
        return Replace(JsonPointer.Parse(path, null), value);
    }

    public static MoveOperation Move(JsonPointer from, JsonPointer path)
    {
        return new MoveOperation(from, path);
    }

    public static MoveOperation Move(string from, string path)
    {
        return Move(JsonPointer.Parse(from, null), JsonPointer.Parse(path, null));
    }

    public static CopyOperation Copy(JsonPointer from, JsonPointer path)
    {
        return new CopyOperation(from, path);
    }

    public static CopyOperation Copy(string from, string path)
    {
        return Copy(JsonPointer.Parse(from, null), JsonPointer.Parse(path, null));
    }

    public static TestOperation Test(JsonPointer path, JsonNode? value)
    {
        return new TestOperation(path, value);
    }

    public static TestOperation Test(string path, JsonNode? value)
    {
        return Test(JsonPointer.Parse(path, null), value);
    }

    protected static JsonNode? GetTargetNode(JsonNode? document, JsonPointer parsedPath)
    {
        if (parsedPath.IsRoot)
        {
            return document;
        }
        var parent = GetParentNode(document, parsedPath);
        var segment = GetRequiredTargetSegment(parsedPath);
        return parent switch
        {
            JsonObject jsonObject when jsonObject.TryGetPropertyValue(segment, out var child) => child,
            JsonArray jsonArray => jsonArray[ParseExistingArrayIndex(jsonArray, segment)],
            _ => throw new JsonPatchException($"Path '{FormatPath(parsedPath)}' does not exist.")
        };
    }

    protected static JsonNode GetParentNode(JsonNode? document, JsonPointer parsedPath)
    {
        return parsedPath.TryEvaluateParent(document, out var parent) && parent is not null
          ? parent
          : throw new JsonPatchException($"Path '{FormatPath(parsedPath)}' does not exist.");
    }

    protected static int ParseExistingArrayIndex(JsonArray jsonArray, string segment)
    {
        var index = ParseArrayIndex(segment);
        if (index < 0
         || index >= jsonArray.Count)
        {
            throw new JsonPatchException($"Array index '{segment}' is out of range.");
        }
        return index;
    }

    protected static int ParseArrayInsertIndex(JsonArray jsonArray, string segment)
    {
        if (segment == "-")
        {
            return jsonArray.Count;
        }
        var index = ParseArrayIndex(segment);
        if (index < 0
         || index > jsonArray.Count)
        {
            throw new JsonPatchException($"Array index '{segment}' is out of range.");
        }
        return index;
    }

    protected static bool PathsEqual(JsonPointer left, JsonPointer right)
    {
        return left == right;
    }

    protected static JsonNode? CloneValue(JsonNode? value)
    {
        return value?.DeepClone();
    }

    protected static string GetRequiredTargetSegment(JsonPointer parsedPath)
    {
        return parsedPath.LastSegment
            ?? throw new JsonPatchException("JSON Pointer path must contain a target segment.");
    }

    private static int ParseArrayIndex(string segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            throw new JsonPatchException($"Array index '{segment}' is invalid.");
        }
        if (segment == "0")
        {
            return 0;
        }
        if (segment[0] == '0')
        {
            throw new JsonPatchException($"Array index '{segment}' is invalid.");
        }
        foreach (var character in segment)
        {
            if (!char.IsAsciiDigit(character))
            {
                throw new JsonPatchException($"Array index '{segment}' is invalid.");
            }
        }
        return int.TryParse(
            segment,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var index
        )
          ? index
          : throw new JsonPatchException($"Array index '{segment}' is invalid.");
    }

    private static string FormatPath(JsonPointer parsedPath)
    {
        return parsedPath.ToString();
    }
}
