using System.Text.Json.Serialization;
using StatePocket.Contracts;

namespace StatePocket.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true
)]
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
