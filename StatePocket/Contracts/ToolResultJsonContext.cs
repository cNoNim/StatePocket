using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(DeleteValueResultData))]
[JsonSerializable(typeof(GetValueResultData))]
[JsonSerializable(typeof(GetValuesResultData))]
[JsonSerializable(typeof(ListKeysResultData))]
[JsonSerializable(typeof(ListNamespacesResultData))]
[JsonSerializable(typeof(PatchValueResultData))]
[JsonSerializable(typeof(QueryValuesResultData))]
[JsonSerializable(typeof(SetValueResultData))]
internal sealed partial class ToolResultJsonContext : JsonSerializerContext;
