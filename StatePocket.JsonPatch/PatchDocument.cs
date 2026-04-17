using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatePocket.JsonPatch.Exceptions;
using Pointer = StatePocket.JsonPointer.JsonPointer;
using JsonPointerException = StatePocket.JsonPointer.JsonPointerException;

namespace StatePocket.JsonPatch;

public sealed class PatchDocument
{
    private readonly PatchOperation[] _operations;

    internal PatchDocument(IReadOnlyList<PatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = [.. operations];
    }

    public static PatchDocument Parse(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Array)
        {
            throw new JsonPatchException("Patch document must be a JSON array.");
        }
        List<PatchOperation> operations = [];
        foreach (var operation in patch.EnumerateArray())
        {
            if (operation.ValueKind != JsonValueKind.Object)
            {
                throw new JsonPatchException("Patch operation must be a JSON object.");
            }
            var fields = ParseOperationFields(operation);
            var type = ParseOperationType(fields.Op);
            var value = GetValue(type, fields);
            var from = GetFrom(type, fields);
            ValidatePaths(type, fields.Path, from);
            operations.Add(
                new PatchOperation(
                    type,
                    fields.Path,
                    value,
                    from
                )
            );
        }
        return new PatchDocument(operations);
    }

    public JsonElement Apply(JsonElement document)
    {
        if (document.ValueKind == JsonValueKind.Undefined)
        {
            throw new JsonPatchException("Document value is required.");
        }
        var result = Apply(ParseNode(document));
        return ToJsonElement(result);
    }

    internal JsonNode? Apply(JsonNode? document)
    {
        var workingDocument = document?.DeepClone();
        foreach (var operation in _operations)
        {
            workingDocument = ApplyOperation(workingDocument, operation);
        }
        return workingDocument;
    }

    private static JsonNode? ApplyOperation(JsonNode? document, PatchOperation operation)
    {
        return operation.Type switch
        {
            PatchOperationType.Add => ApplyAdd(document, operation),
            PatchOperationType.Remove => ApplyRemove(document, operation),
            PatchOperationType.Replace => ApplyReplace(document, operation),
            PatchOperationType.Move => ApplyMove(document, operation),
            PatchOperationType.Copy => ApplyCopy(document, operation),
            PatchOperationType.Test => ApplyTest(document, operation),
            _ => throw new JsonPatchException($"Operation '{operation.Type}' is not supported yet.")
        };
    }

    private static JsonNode? ApplyMove(JsonNode? document, PatchOperation operation)
    {
        Pointer fromPath = new(GetRequiredFrom(operation));
        Pointer targetPath = new(operation.Path);
        var sourceValue = GetTargetNode(document, fromPath);
        if (PathsEqual(fromPath, targetPath))
        {
            return document;
        }
        if (fromPath.IsPrefixOf(targetPath))
        {
            throw new JsonPatchException("Move operation cannot move a value into its own child path.");
        }
        var value = CloneValue(sourceValue);
        var removedDocument = ApplyRemove(document, new PatchOperation(PatchOperationType.Remove, operation.From!));
        return ApplyAdd(removedDocument, new PatchOperation(PatchOperationType.Add, operation.Path, value));
    }

    private static JsonNode? ApplyCopy(JsonNode? document, PatchOperation operation)
    {
        Pointer fromPath = new(GetRequiredFrom(operation));
        var value = CloneValue(GetTargetNode(document, fromPath));
        return ApplyAdd(document, new PatchOperation(PatchOperationType.Add, operation.Path, value));
    }

    private static JsonNode? ApplyAdd(JsonNode? document, PatchOperation operation)
    {
        Pointer parsedPath = new(operation.Path);
        if (parsedPath.IsRoot)
        {
            return CloneValue(operation.Value);
        }
        var parent = GetParentNode(document, parsedPath);
        var segment = parsedPath.LastSegment
                   ?? throw new JsonPatchException("JSON Pointer path must contain a target segment.");
        switch (parent)
        {
            case JsonObject jsonObject:
                jsonObject[segment] = CloneValue(operation.Value);
                return document;
            case JsonArray jsonArray:
                jsonArray.Insert(ParseArrayInsertIndex(jsonArray, segment), CloneValue(operation.Value));
                return document;
            default:
                throw new JsonPatchException($"Path '{operation.Path}' does not target an object or array.");
        }
    }

    private static JsonNode? ApplyReplace(JsonNode? document, PatchOperation operation)
    {
        Pointer parsedPath = new(operation.Path);
        if (parsedPath.IsRoot)
        {
            return CloneValue(operation.Value);
        }
        var parent = GetParentNode(document, parsedPath);
        var segment = parsedPath.LastSegment
                   ?? throw new JsonPatchException("JSON Pointer path must contain a target segment.");
        switch (parent)
        {
            case JsonObject jsonObject when jsonObject.ContainsKey(segment):
                jsonObject[segment] = CloneValue(operation.Value);
                return document;
            case JsonArray jsonArray:
                jsonArray[ParseExistingArrayIndex(jsonArray, segment)] = CloneValue(operation.Value);
                return document;
            default:
                throw new JsonPatchException($"Path '{operation.Path}' does not exist.");
        }
    }

    private static JsonNode? ApplyRemove(JsonNode? document, PatchOperation operation)
    {
        Pointer parsedPath = new(operation.Path);
        if (parsedPath.IsRoot)
        {
            throw new JsonPatchException("Removing the whole document is not supported.");
        }
        var parent = GetParentNode(document, parsedPath);
        var segment = parsedPath.LastSegment
                   ?? throw new JsonPatchException("JSON Pointer path must contain a target segment.");
        switch (parent)
        {
            case JsonObject jsonObject when jsonObject.Remove(segment):
                return document;
            case JsonArray jsonArray:
                jsonArray.RemoveAt(ParseExistingArrayIndex(jsonArray, segment));
                return document;
            default:
                throw new JsonPatchException($"Path '{operation.Path}' does not exist.");
        }
    }

    private static JsonNode? ApplyTest(JsonNode? document, PatchOperation operation)
    {
        var actual = GetTargetNode(document, new Pointer(operation.Path));
        return JsonNode.DeepEquals(actual, operation.Value)
          ? document
          : throw new JsonPatchException($"Test operation failed at path '{operation.Path}'.");
    }

    private static JsonNode GetParentNode(JsonNode? document, Pointer parsedPath)
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

    private static JsonNode? GetTargetNode(JsonNode? document, Pointer parsedPath)
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

    private static int ParseExistingArrayIndex(JsonArray jsonArray, string segment)
    {
        var index = ParseArrayIndex(segment);
        if (index < 0
         || index >= jsonArray.Count)
        {
            throw new JsonPatchException($"Array index '{segment}' is out of range.");
        }
        return index;
    }

    private static int ParseArrayInsertIndex(JsonArray jsonArray, string segment)
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

    private static JsonNode? CloneValue(JsonNode? value)
    {
        return value?.DeepClone();
    }

    private static string FormatPath(Pointer parsedPath)
    {
        return parsedPath.IsRoot ? "" : $"/{string.Join("/", parsedPath.Segments)}";
    }

    private static OperationFields ParseOperationFields(JsonElement operation)
    {
        string? op = null;
        string? path = null;
        string? from = null;
        JsonNode? value = null;
        var hasValue = false;
        foreach (var property in operation.EnumerateObject())
        {
            switch (property.Name)
            {
                case "op":
                    EnsureUnique(op, "op");
                    op = GetStringPropertyValue(property.Value, "op");
                    break;
                case "path":
                    EnsureUnique(path, "path");
                    path = GetStringPropertyValue(property.Value, "path");
                    break;
                case "from":
                    EnsureUnique(from, "from");
                    from = GetStringPropertyValue(property.Value, "from");
                    break;
                case "value":
                    if (hasValue)
                    {
                        throw new JsonPatchException("Patch operation has duplicate property 'value'.");
                    }
                    value = JsonNode.Parse(property.Value.GetRawText());
                    hasValue = true;
                    break;
            }
        }
        return new OperationFields(
            GetRequiredStringValue(op, "op"),
            GetRequiredStringValue(path, "path"),
            from,
            value,
            hasValue
        );
    }

    private static PatchOperationType ParseOperationType(string op)
    {
        return op switch
        {
            "add" => PatchOperationType.Add,
            "remove" => PatchOperationType.Remove,
            "replace" => PatchOperationType.Replace,
            "move" => PatchOperationType.Move,
            "copy" => PatchOperationType.Copy,
            "test" => PatchOperationType.Test,
            _ => throw new JsonPatchException($"Unsupported patch operation '{op}'.")
        };
    }

    private static string? GetFrom(PatchOperationType type, OperationFields fields)
    {
        if (fields.From is null)
        {
            return type switch
            {
                PatchOperationType.Move or PatchOperationType.Copy => throw new JsonPatchException(
                    $"Patch operation '{type}' must have string property 'from'."
                ),
                _ => null
            };
        }
        return fields.From;
    }

    private static JsonNode? GetValue(PatchOperationType type, OperationFields fields)
    {
        if (!fields.HasValue)
        {
            return type switch
            {
                PatchOperationType.Add or PatchOperationType.Replace or PatchOperationType.Test =>
                    throw new JsonPatchException($"Patch operation '{type}' must have property 'value'."),
                _ => null
            };
        }
        return fields.Value?.DeepClone();
    }

    private static string GetRequiredFrom(PatchOperation operation)
    {
        return operation.From
            ?? throw new JsonPatchException($"Patch operation '{operation.Type}' must have string property 'from'.");
    }

    private static void ValidatePaths(PatchOperationType type, string path, string? from)
    {
        ValidatePath(path);
        if (type is PatchOperationType.Move or PatchOperationType.Copy)
        {
            ValidatePath(
                from ?? throw new JsonPatchException($"Patch operation '{type}' must have string property 'from'.")
            );
        }
    }

    private static bool PathsEqual(Pointer left, Pointer right)
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

    private static void ValidatePath(string path)
    {
        try
        {
            _ = new Pointer(path);
        }
        catch (JsonPointerException exception)
        {
            throw new JsonPatchException(exception.Message);
        }
    }

    private static string GetStringPropertyValue(JsonElement propertyValue, string propertyName)
    {
        if (propertyValue.ValueKind != JsonValueKind.String)
        {
            throw new JsonPatchException($"Patch operation must have string property '{propertyName}'.");
        }
        return propertyValue.GetString()
            ?? throw new JsonPatchException($"Patch operation must have string property '{propertyName}'.");
    }

    private static string GetRequiredStringValue(string? value, string propertyName)
    {
        return value ?? throw new JsonPatchException($"Patch operation must have string property '{propertyName}'.");
    }

    private static void EnsureUnique(string? value, string propertyName)
    {
        if (value is not null)
        {
            throw new JsonPatchException($"Patch operation has duplicate property '{propertyName}'.");
        }
    }

    private static JsonNode? ParseNode(JsonElement value)
    {
        return JsonNode.Parse(value.GetRawText());
    }

    private static JsonElement ToJsonElement(JsonNode? value)
    {
        using var document = JsonDocument.Parse(value?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private readonly record struct OperationFields(
        string Op,
        string Path,
        string? From,
        JsonNode? Value,
        bool HasValue
    );
}
