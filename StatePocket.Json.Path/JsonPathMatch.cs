using System.Text.Json;

namespace StatePocket.Json.Path;

public sealed record JsonPathMatch(JsonElement Value, string NormalizedPath);
