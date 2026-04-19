using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StatePocket.JsonPatch;

[JsonSerializable(typeof(PatchDocument))]
[JsonSerializable(typeof(IReadOnlyList<PatchOperation>))]
[JsonSerializable(typeof(PatchOperation[]))]
[JsonSerializable(typeof(PatchOperation))]
[JsonSerializable(typeof(ValueOperation))]
[JsonSerializable(typeof(FromOperation))]
[JsonSerializable(typeof(AddOperation))]
[JsonSerializable(typeof(RemoveOperation))]
[JsonSerializable(typeof(ReplaceOperation))]
[JsonSerializable(typeof(MoveOperation))]
[JsonSerializable(typeof(CopyOperation))]
[JsonSerializable(typeof(TestOperation))]
[JsonSerializable(typeof(PatchOperationType))]
[JsonSerializable(typeof(JsonNode))]
public sealed partial class JsonPatchJsonContext : JsonSerializerContext;
