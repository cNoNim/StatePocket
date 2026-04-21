using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed class StrictStringEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(null, false)
    where TEnum : struct, Enum;
