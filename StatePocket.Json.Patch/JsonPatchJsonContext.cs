using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

[JsonSerializable(typeof(JsonPatch))]
[JsonSerializable(typeof(IReadOnlyList<JsonPatchOperation>))]
[JsonSerializable(typeof(JsonPatchOperation[]))]
[JsonSerializable(typeof(JsonPatchOperation))]
[JsonSerializable(typeof(ValueOperation))]
[JsonSerializable(typeof(FromOperation))]
[JsonSerializable(typeof(AddOperation))]
[JsonSerializable(typeof(RemoveOperation))]
[JsonSerializable(typeof(ReplaceOperation))]
[JsonSerializable(typeof(MoveOperation))]
[JsonSerializable(typeof(CopyOperation))]
[JsonSerializable(typeof(TestOperation))]
[JsonSerializable(typeof(JsonPatchOperationType))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonPointer), TypeInfoPropertyName = "JsonPointerTypeInfo")]
public sealed partial class JsonPatchJsonContext : JsonSerializerContext;
