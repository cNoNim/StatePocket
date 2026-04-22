using System.Text.Json.Serialization;

namespace StatePocket.Serialization;

internal sealed class StrictStringEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(null, false)
    where TEnum : struct, Enum;
