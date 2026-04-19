using System.Text.Json.Serialization;

namespace StatePocket.JsonPatch;

[JsonConverter(typeof(JsonStringEnumConverter<PatchOperationType>))]
public enum PatchOperationType
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
