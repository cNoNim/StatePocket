using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace StatePocket.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization, WriteIndented = true)]
[JsonSerializable(typeof(Tool))]
internal sealed partial class ToolSchemaJsonContext : JsonSerializerContext;
