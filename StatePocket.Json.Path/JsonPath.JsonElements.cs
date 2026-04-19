using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace StatePocket.Json.Path;

public sealed partial class JsonPath
{
    private static class JsonElementEqualityComparer
    {
        public static bool Equals(JsonElement left, JsonElement right)
        {
            if (left.ValueKind != right.ValueKind)
            {
                return left.ValueKind is JsonValueKind.Number
                    && right.ValueKind is JsonValueKind.Number
                    && JsonNumber.Parse(left) == JsonNumber.Parse(right);
            }
            return left.ValueKind switch
            {
                JsonValueKind.Null => true,
                JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
                JsonValueKind.String => left.GetString() == right.GetString(),
                JsonValueKind.Number => JsonNumber.Parse(left) == JsonNumber.Parse(right),
                JsonValueKind.Array => ArraysEqual(left, right),
                JsonValueKind.Object => ObjectsEqual(left, right),
                _ => false
            };
        }

        private static bool ArraysEqual(JsonElement left, JsonElement right)
        {
            var leftItems = left.EnumerateArray()
                                .ToArray();
            var rightItems = right.EnumerateArray()
                                  .ToArray();
            if (leftItems.Length != rightItems.Length)
            {
                return false;
            }
            for (var index = 0; index < leftItems.Length; index++)
            {
                if (!Equals(leftItems[index], rightItems[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ObjectsEqual(JsonElement left, JsonElement right)
        {
            var leftProperties = left.EnumerateObject()
                                     .ToArray();
            var rightProperties = right.EnumerateObject()
                                       .ToArray();
            if (leftProperties.Length != rightProperties.Length)
            {
                return false;
            }
            Dictionary<string, List<JsonElement>> rightByName = new(StringComparer.Ordinal);
            foreach (var property in rightProperties)
            {
                if (!rightByName.TryGetValue(property.Name, out var values))
                {
                    values = [];
                    rightByName[property.Name] = values;
                }
                values.Add(property.Value);
            }
            foreach (var property in leftProperties)
            {
                if (!rightByName.TryGetValue(property.Name, out var values))
                {
                    return false;
                }
                var matchIndex = values.FindIndex(value => Equals(property.Value, value));
                if (matchIndex < 0)
                {
                    return false;
                }
                values.RemoveAt(matchIndex);
                if (values.Count == 0)
                {
                    rightByName.Remove(property.Name);
                }
            }
            return rightByName.Count == 0;
        }
    }

    private static class JsonElementComparisonComparer
    {
        public static bool TryCompare(JsonElement left, JsonElement right, out int comparison)
        {
            comparison = 0;
            if (left.ValueKind != right.ValueKind)
            {
                return false;
            }
            switch (left.ValueKind)
            {
                case JsonValueKind.String:
                    comparison = string.CompareOrdinal(left.GetString(), right.GetString());
                    return true;
                case JsonValueKind.Number:
                    comparison = JsonNumber.Parse(left)
                                           .CompareTo(JsonNumber.Parse(right));
                    return true;
                case JsonValueKind.Null:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    comparison = JsonElementEqualityComparer.Equals(left, right) ? 0 : 1;
                    return true;
                default:
                    return false;
            }
        }
    }

    private readonly record struct JsonNumber(BigInteger Significand, BigInteger Exponent) : IComparable<JsonNumber>
    {
        public int CompareTo(JsonNumber other)
        {
            if (Significand.Sign != other.Significand.Sign)
            {
                return Significand.Sign.CompareTo(other.Significand.Sign);
            }
            if (Significand.IsZero)
            {
                return 0;
            }
            var comparison = CompareAbsolute(other);
            return Significand.Sign < 0 ? -comparison : comparison;
        }

        public static JsonNumber Parse(JsonElement value)
        {
            return Parse(value.GetRawText());
        }

        private static JsonNumber Parse(string rawText)
        {
            var index = 0;
            var negative = false;
            if (rawText[index] == '-')
            {
                negative = true;
                index++;
            }
            var integerStart = index;
            if (rawText[index] == '0')
            {
                index++;
            }
            else
            {
                while (index < rawText.Length
                    && char.IsAsciiDigit(rawText[index]))
                {
                    index++;
                }
            }
            var integerPart = rawText[integerStart..index];
            var fractionPart = "";
            if (index < rawText.Length
             && rawText[index] == '.')
            {
                index++;
                var fractionStart = index;
                while (index < rawText.Length
                    && char.IsAsciiDigit(rawText[index]))
                {
                    index++;
                }
                fractionPart = rawText[fractionStart..index];
            }
            var explicitExponent = BigInteger.Zero;
            if (index < rawText.Length
             && rawText[index] is 'e' or 'E')
            {
                explicitExponent = BigInteger.Parse(rawText[(index + 1)..], CultureInfo.InvariantCulture);
            }
            var digits = (integerPart + fractionPart).TrimStart('0');
            if (digits.Length == 0)
            {
                return new JsonNumber(BigInteger.Zero, BigInteger.Zero);
            }
            var significand = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            if (negative)
            {
                significand = -significand;
            }
            var exponent = explicitExponent - fractionPart.Length;
            return Normalize(significand, exponent);
        }

        private int CompareAbsolute(JsonNumber other)
        {
            var magnitude = DigitCount(Significand) + Exponent;
            var otherMagnitude = DigitCount(other.Significand) + other.Exponent;
            var magnitudeComparison = magnitude.CompareTo(otherMagnitude);
            if (magnitudeComparison != 0)
            {
                return magnitudeComparison;
            }
            var exponentDelta = Exponent - other.Exponent;
            return exponentDelta.Sign switch
            {
                > 0 => ScaleByPowerOfTen(BigInteger.Abs(Significand), exponentDelta)
                   .CompareTo(BigInteger.Abs(other.Significand)),
                < 0 => BigInteger.Abs(Significand)
                                 .CompareTo(
                                      ScaleByPowerOfTen(
                                          BigInteger.Abs(other.Significand),
                                          BigInteger.Negate(exponentDelta)
                                      )
                                  ),
                _ => BigInteger.Abs(Significand)
                               .CompareTo(BigInteger.Abs(other.Significand))
            };
        }

        private static BigInteger DigitCount(BigInteger value)
        {
            return value.IsZero
              ? BigInteger.One
              : BigInteger.Abs(value)
                          .ToString(CultureInfo.InvariantCulture)
                          .Length;
        }

        private static JsonNumber Normalize(BigInteger significand, BigInteger exponent)
        {
            if (significand.IsZero)
            {
                return new JsonNumber(BigInteger.Zero, BigInteger.Zero);
            }
            var sign = significand.Sign;
            var absolute = BigInteger.Abs(significand);
            while (absolute % 10 == 0)
            {
                absolute /= 10;
                exponent += BigInteger.One;
            }
            return new JsonNumber(sign < 0 ? -absolute : absolute, exponent);
        }

        private static BigInteger ScaleByPowerOfTen(BigInteger value, BigInteger exponent)
        {
            return exponent.IsZero ? value : value * BigInteger.Pow(10, checked((int)exponent));
        }
    }
}
