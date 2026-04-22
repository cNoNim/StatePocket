using System.Text.Json.Serialization;
using StatePocket.Contracts;

namespace StatePocket.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true
)]
[JsonSerializable(typeof(JsonInputFormat))]
internal sealed partial class ToolArgumentJsonContext : JsonSerializerContext;
