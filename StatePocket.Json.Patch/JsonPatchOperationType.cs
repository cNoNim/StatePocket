using System.Text.Json.Serialization;

namespace StatePocket.Json.Patch;

[JsonConverter(typeof(JsonStringEnumConverter<JsonPatchOperationType>))]
public enum JsonPatchOperationType
{
    [JsonStringEnumMemberName("add")]
    Add,
    [JsonStringEnumMemberName("remove")]
    Remove,
    [JsonStringEnumMemberName("replace")]
    Replace,
    [JsonStringEnumMemberName("move")]
    Move,
    [JsonStringEnumMemberName("copy")]
    Copy,
    [JsonStringEnumMemberName("test")]
    Test
}
