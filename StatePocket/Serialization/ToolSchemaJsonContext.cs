using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace StatePocket.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(Tool))]
internal sealed partial class ToolSchemaJsonContext : JsonSerializerContext;
