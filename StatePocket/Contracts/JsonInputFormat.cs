using System.Text.Json.Serialization;
using StatePocket.Serialization;

namespace StatePocket.Contracts;

[JsonConverter(typeof(StrictStringEnumConverter<JsonInputFormat>))]
internal enum JsonInputFormat
{
    [JsonStringEnumMemberName("text")]
    Text,
    [JsonStringEnumMemberName("json")]
    Json
}
