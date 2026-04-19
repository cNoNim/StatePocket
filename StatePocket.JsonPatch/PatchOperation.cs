using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.JsonPatch.Exceptions;
using Pointer = StatePocket.JsonPointer.JsonPointer;

namespace StatePocket.JsonPatch;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(AddOperation), "add")]
[JsonDerivedType(typeof(RemoveOperation), "remove")]
[JsonDerivedType(typeof(ReplaceOperation), "replace")]
[JsonDerivedType(typeof(MoveOperation), "move")]
[JsonDerivedType(typeof(CopyOperation), "copy")]
[JsonDerivedType(typeof(TestOperation), "test")]
public abstract class PatchOperation
{
    protected PatchOperation() {}

    [SetsRequiredMembers]
    protected PatchOperation(string path) => Path = path;

    [JsonPropertyName("path")]
    [JsonPropertyOrder(1)]
    [JsonRequired]
    public required string Path { get; init; }
    [JsonIgnore]
    public abstract PatchOperationType Op { get; }
    internal abstract JsonNode? ApplyTo(JsonNode? document);
    internal abstract void Validate();

    public static AddOperation Add(string path, JsonNode? value)
    {
        return new AddOperation(path, value);
    }

    public static RemoveOperation Remove(string path)
    {
        return new RemoveOperation(path);
    }

    public static ReplaceOperation Replace(string path, JsonNode? value)
    {
        return new ReplaceOperation(path, value);
    }

    public static MoveOperation Move(string from, string path)
    {
        return new MoveOperation(from, path);
    }

    public static CopyOperation Copy(string from, string path)
    {
        return new CopyOperation(from, path);
    }

    public static TestOperation Test(string path, JsonNode? value)
    {
        return new TestOperation(path, value);
    }

    protected static Pointer ParsePath(string path)
    {
        return new Pointer(path);
    }

    protected void ValidatePath()
    {
        _ = ParsePath(Path);
    }

    protected static JsonNode? GetTargetNode(JsonNode? document, Pointer parsedPath)
    {
        if (parsedPath.IsRoot)
        {
            return document;
        }
        var parent = GetParentNode(document, parsedPath);
        var segment = parsedPath.LastSegment
                   ?? throw new JsonPatchException("JSON Pointer path must contain a target segment.");
        return parent switch
        {
            JsonObject jsonObject when jsonObject.TryGetPropertyValue(segment, out var child) => child,
            JsonArray jsonArray => jsonArray[ParseExistingArrayIndex(jsonArray, segment)],
            _ => throw new JsonPatchException($"Path '{FormatPath(parsedPath)}' does not exist.")
        };
    }

    protected static JsonNode GetParentNode(JsonNode? document, Pointer parsedPath)
    {
        var current = document;
        for (var i = 0; i < parsedPath.Segments.Count - 1; i++)
        {
            var segment = parsedPath.Segments[i];
            current = current switch
            {
                JsonObject jsonObject => GetObjectChild(jsonObject, segment, parsedPath),
                JsonArray jsonArray => GetArrayChild(jsonArray, segment, parsedPath),
                _ => throw new JsonPatchException($"Path '{FormatPath(parsedPath)}' does not exist.")
            };
        }
        return current ?? throw new JsonPatchException($"Path '{FormatPath(parsedPath)}' does not exist.");
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

    protected static bool PathsEqual(Pointer left, Pointer right)
    {
        if (left.Segments.Count != right.Segments.Count)
        {
            return false;
        }
        for (var i = 0; i < left.Segments.Count; i++)
        {
            if (!string.Equals(left.Segments[i], right.Segments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    protected static JsonNode? CloneValue(JsonNode? value)
    {
        return value?.DeepClone();
    }

    private static JsonNode GetObjectChild(JsonObject jsonObject, string segment, Pointer parsedPath)
    {
        if (!jsonObject.TryGetPropertyValue(segment, out var child)
         || child is null)
        {
            throw new JsonPatchException($"Path '{FormatPath(parsedPath)}' does not exist.");
        }
        return child;
    }

    private static JsonNode GetArrayChild(JsonArray jsonArray, string segment, Pointer parsedPath)
    {
        return jsonArray[ParseExistingArrayIndex(jsonArray, segment)]
            ?? throw new JsonPatchException($"Path '{FormatPath(parsedPath)}' does not exist.");
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

    private static string FormatPath(Pointer parsedPath)
    {
        return parsedPath.IsRoot ? "" : $"/{string.Join("/", parsedPath.Segments)}";
    }
}
