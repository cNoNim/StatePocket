using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DeleteValueResult))]
[JsonSerializable(typeof(GetValueResult))]
[JsonSerializable(typeof(GetValuesEntry))]
[JsonSerializable(typeof(GetValuesResult))]
[JsonSerializable(typeof(ListKeysResult))]
[JsonSerializable(typeof(ListNamespacesResult))]
[JsonSerializable(typeof(PatchValueResult))]
[JsonSerializable(typeof(QueryValuesResult))]
[JsonSerializable(typeof(SetValueResult))]
internal sealed partial class ToolResultJsonContext : JsonSerializerContext;
