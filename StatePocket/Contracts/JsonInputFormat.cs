using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

[JsonConverter(typeof(StrictStringEnumConverter<JsonInputFormat>))]
internal enum JsonInputFormat
{
    [JsonStringEnumMemberName("text")]
    Text,
    [JsonStringEnumMemberName("json")]
    Json
}
