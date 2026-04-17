using System.Text.Json;

namespace StatePocket.JsonPath;

public sealed record JsonPathMatch(JsonElement Value, string NormalizedPath);
