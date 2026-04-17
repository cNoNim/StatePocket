using System.Text.Json;
using System.Text.RegularExpressions;

namespace StatePocket.JsonPath;

public sealed partial class JsonPath
{
    private abstract class FilterExpression
    {
        public abstract bool Evaluate(JsonElement root, JsonElement current);
    }

    private sealed class FilterExpressionParser(string expression, string selector)
    {
        private int _index;

        public FilterExpression Parse()
        {
            var parsed = ParseOr();
            SkipWhitespace();
            return _index == expression.Length
              ? parsed
              : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }

        private FilterExpression ParseOr()
        {
            var left = ParseAnd();
            while (true)
            {
                SkipWhitespace();
                if (!TryConsume("||"))
                {
                    return left;
                }
                left = new BinaryLogicalExpression(left, ParseAnd(), BinaryLogicalOperator.Or);
            }
        }

        private FilterExpression ParseAnd()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (!TryConsume("&&"))
                {
                    return left;
                }
                left = new BinaryLogicalExpression(left, ParseUnary(), BinaryLogicalOperator.And);
            }
        }

        private FilterExpression ParseUnary()
        {
            SkipWhitespace();
            if (TryConsume("!"))
            {
                return new NotExpression(ParseUnary());
            }
            return ParsePrimary();
        }

        private FilterExpression ParsePrimary()
        {
            SkipWhitespace();
            if (TryConsume("("))
            {
                var inner = ParseOr();
                SkipWhitespace();
                return TryConsume(")")
                  ? inner
                  : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
            }
            return ParseAtomic();
        }

        private FilterExpression ParseAtomic()
        {
            var start = _index;
            var inString = false;
            var quote = '\0';
            var nestedBracketCount = 0;
            var nestedParenthesisCount = 0;
            while (_index < expression.Length)
            {
                var current = expression[_index];
                if (inString)
                {
                    if (current == '\\')
                    {
                        _index += 2;
                        continue;
                    }
                    if (current == quote)
                    {
                        inString = false;
                    }
                    _index++;
                    continue;
                }
                switch (current)
                {
                    case '"' or '\'':
                        inString = true;
                        quote = current;
                        _index++;
                        continue;
                    case '[':
                        nestedBracketCount++;
                        _index++;
                        continue;
                    case ']':
                        nestedBracketCount--;
                        _index++;
                        continue;
                    case '(':
                        nestedParenthesisCount++;
                        _index++;
                        continue;
                    case ')':
                        if (nestedParenthesisCount == 0)
                        {
                            return ParseAtomicExpression(expression[start.._index]);
                        }
                        nestedParenthesisCount--;
                        _index++;
                        continue;
                }
                if (nestedBracketCount == 0
                 && nestedParenthesisCount == 0
                 && ((_index + 1 < expression.Length && expression.AsSpan(_index, 2) is "&&" or "||")
                  || current == ')'))
                {
                    return ParseAtomicExpression(expression[start.._index]);
                }
                _index++;
            }
            return ParseAtomicExpression(expression[start.._index]);
        }

        private FilterExpression ParseAtomicExpression(string content)
        {
            var trimmedContent = TrimWhitespace(content);
            if (trimmedContent.Length == 0)
            {
                throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
            }
            var comparisonOperatorIndex = FindComparisonOperator(trimmedContent);
            if (comparisonOperatorIndex >= 0)
            {
                var operatorLength = trimmedContent[comparisonOperatorIndex + 1] == '=' ? 2 : 1;
                var left = ParseFilterOperand(TrimWhitespace(trimmedContent[..comparisonOperatorIndex]), selector);
                var comparisonOperator = trimmedContent.Substring(comparisonOperatorIndex, operatorLength);
                var right = ParseFilterOperand(
                    TrimWhitespace(trimmedContent[(comparisonOperatorIndex + operatorLength)..]),
                    selector
                );
                if (left is QueryOperand
                    {
                        Query.IsSingular: false,
                    }
                 || right is QueryOperand
                    {
                        Query.IsSingular: false,
                    })
                {
                    throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
                }
                return new ComparisonExpression(left, comparisonOperator, right);
            }
            if (TryParseFunctionExpression(trimmedContent, selector, out var functionExpression))
            {
                return functionExpression;
            }
            return trimmedContent[0] switch
            {
                '@' or '$' => new QueryExistenceExpression(ParseFilterQuery(trimmedContent, selector), false),
                _ => throw new JsonPathException($"Invalid JSONPath selector '{selector}'."),
            };
        }

        private void SkipWhitespace()
        {
            while (_index < expression.Length
                && expression[_index] is ' ' or '\t' or '\n' or '\r')
            {
                _index++;
            }
        }

        private bool TryConsume(string token)
        {
            if (_index + token.Length > expression.Length
             || !expression.AsSpan(_index, token.Length)
                           .SequenceEqual(token))
            {
                return false;
            }
            _index += token.Length;
            return true;
        }
    }

    private enum FilterValueKind
    {
        NodeList,
        Json,
        Nothing,
    }

    private readonly record struct FilterValue(FilterValueKind Kind, JsonElement[] Values)
    {
        public static FilterValue Nothing => new(FilterValueKind.Nothing, []);
        public bool IsEmptyNodeList => Kind == FilterValueKind.NodeList && Values.Length == 0;

        public static FilterValue FromNodeList(JsonElement[] values)
        {
            return new FilterValue(FilterValueKind.NodeList, values);
        }

        public static FilterValue FromJson(JsonElement value)
        {
            return new FilterValue(FilterValueKind.Json, [value]);
        }
    }

    private abstract class FilterOperand
    {
        public abstract FilterValue Evaluate(JsonElement root, JsonElement current);
    }

    private sealed class QueryOperand(PathQuery query) : FilterOperand
    {
        public PathQuery Query => query;

        public override FilterValue Evaluate(JsonElement root, JsonElement current)
        {
            return FilterValue.FromNodeList(
                [
                    .. query.Evaluate(root, current)
                            .Select(static value => value.Value.Clone()),
                ]
            );
        }
    }

    private sealed class LiteralOperand(JsonElement value) : FilterOperand
    {
        public JsonElement Value => value;

        public override FilterValue Evaluate(JsonElement root, JsonElement current)
        {
            return FilterValue.FromJson(value.Clone());
        }
    }

    private sealed class CountOperand(PathQuery query) : FilterOperand
    {
        public override FilterValue Evaluate(JsonElement root, JsonElement current)
        {
            return FilterValue.FromJson(
                ToJsonInt32Element(
                    query.Evaluate(root, current)
                         .Count
                )
            );
        }
    }

    private sealed class LengthOperand(FilterOperand operand) : FilterOperand
    {
        public override FilterValue Evaluate(JsonElement root, JsonElement current)
        {
            if (!TryGetSingularValue(operand.Evaluate(root, current), out var value))
            {
                return FilterValue.Nothing;
            }
            return value.ValueKind switch
            {
                JsonValueKind.String => FilterValue.FromJson(
                    ToJsonInt32Element(
                        value.GetString()!.EnumerateRunes()
                             .Count()
                    )
                ),
                JsonValueKind.Array => FilterValue.FromJson(ToJsonInt32Element(value.GetArrayLength())),
                _ => FilterValue.Nothing,
            };
        }
    }

    private sealed class ValueOperand(PathQuery query) : FilterOperand
    {
        public override FilterValue Evaluate(JsonElement root, JsonElement current)
        {
            var matches = query.Evaluate(root, current);
            return matches.Count == 1
              ? FilterValue.FromJson(
                    matches[0]
                       .Value.Clone()
                )
              : FilterValue.Nothing;
        }
    }

    private sealed class QueryExistenceExpression(PathQuery query, bool negated) : FilterExpression
    {
        public override bool Evaluate(JsonElement root, JsonElement current)
        {
            var exists = query.HasAnyMatch(root, current);
            return negated ? !exists : exists;
        }
    }

    private sealed class RegexMatchExpression(
        FilterOperand input,
        FilterOperand pattern,
        bool requireFullMatch,
        string selector
    ) : FilterExpression
    {
        private readonly Regex? _constantRegex = TryCreateConstantRegex(pattern, requireFullMatch, selector);

        public override bool Evaluate(JsonElement root, JsonElement current)
        {
            try
            {
                if (!TryGetSingularString(input.Evaluate(root, current), out var inputValue))
                {
                    return false;
                }
                if (_constantRegex is not null)
                {
                    return _constantRegex.IsMatch(inputValue);
                }
                if (!TryGetSingularString(pattern.Evaluate(root, current), out var patternValue))
                {
                    return false;
                }
                return CreateRegex(patternValue, requireFullMatch, selector)
                   .IsMatch(inputValue);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
            }
        }
    }

    private enum BinaryLogicalOperator
    {
        And,
        Or,
    }

    private sealed class BinaryLogicalExpression(
        FilterExpression left,
        FilterExpression right,
        BinaryLogicalOperator operation
    ) : FilterExpression
    {
        public override bool Evaluate(JsonElement root, JsonElement current)
        {
            return operation switch
            {
                BinaryLogicalOperator.And => left.Evaluate(root, current) && right.Evaluate(root, current),
                BinaryLogicalOperator.Or => left.Evaluate(root, current) || right.Evaluate(root, current),
                _ => throw new InvalidOperationException($"Unsupported logical operator '{operation}'."),
            };
        }
    }

    private sealed class NotExpression(FilterExpression inner) : FilterExpression
    {
        public override bool Evaluate(JsonElement root, JsonElement current)
        {
            return !inner.Evaluate(root, current);
        }
    }

    private sealed class ComparisonExpression(FilterOperand left, string comparisonOperator, FilterOperand right)
        : FilterExpression
    {
        public override bool Evaluate(JsonElement root, JsonElement current)
        {
            var leftValues = left.Evaluate(root, current);
            var rightValues = right.Evaluate(root, current);
            var isEqual = AreEqual(leftValues, rightValues);
            return comparisonOperator switch
            {
                "==" => isEqual,
                "!=" => !isEqual,
                "<" => Compare(leftValues, rightValues, static value => value < 0),
                "<=" => Compare(leftValues, rightValues, static value => value <= 0),
                ">" => Compare(leftValues, rightValues, static value => value > 0),
                ">=" => Compare(leftValues, rightValues, static value => value >= 0),
                _ => throw new InvalidOperationException($"Unsupported operator '{comparisonOperator}'."),
            };
        }

        private static bool AreEqual(FilterValue left, FilterValue right)
        {
            if (left.Kind == FilterValueKind.Nothing
             || right.Kind == FilterValueKind.Nothing)
            {
                return left.Kind switch
                {
                    FilterValueKind.Nothing when right.Kind == FilterValueKind.Nothing => true,
                    FilterValueKind.Nothing => right.IsEmptyNodeList,
                    _ => left.IsEmptyNodeList,
                };
            }
            if (left.Values.Length != right.Values.Length)
            {
                return false;
            }
            for (var index = 0; index < left.Values.Length; index++)
            {
                if (!JsonElementEqualityComparer.Equals(left.Values[index], right.Values[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool Compare(FilterValue left, FilterValue right, Func<int, bool> predicate)
        {
            if (!TryGetSingularValue(left, out var leftValue)
             || !TryGetSingularValue(right, out var rightValue))
            {
                return false;
            }
            return JsonElementComparisonComparer.TryCompare(leftValue, rightValue, out var comparison)
                && predicate(comparison);
        }
    }

    private sealed class PathQuery(char rootSymbol, SelectorSegment[] segments)
    {
        public bool IsSingular { get; } = segments.All(static segment => segment.IsSingular);

        public List<PathValue> Evaluate(JsonElement root, JsonElement current)
        {
            var startValue = rootSymbol == '$' ? root : current;
            var startPath = rootSymbol == '$' ? "$" : "@";
            return EvaluateSegments(
                root,
                startValue,
                startPath,
                segments
            );
        }

        public bool HasAnyMatch(JsonElement root, JsonElement current)
        {
            return Evaluate(root, current)
                      .Count
                != 0;
        }
    }

    private sealed class FilterSegment(FilterExpression expression) : SelectorSegment
    {
        public override void Apply(JsonElement root, PathValue current, ICollection<PathValue> next)
        {
            switch (current.Value.ValueKind)
            {
                case JsonValueKind.Array:
                    for (var index = 0; index < current.Value.GetArrayLength(); index++)
                    {
                        var candidate = new PathValue(current.Value[index], $"{current.NormalizedPath}[{index}]");
                        if (expression.Evaluate(root, candidate.Value))
                        {
                            next.Add(candidate);
                        }
                    }
                    return;
                case JsonValueKind.Object:
                    foreach (var property in current.Value.EnumerateObject())
                    {
                        var candidate = new PathValue(
                            property.Value,
                            $"{current.NormalizedPath}{NormalizeName(property.Name)}"
                        );
                        if (expression.Evaluate(root, candidate.Value))
                        {
                            next.Add(candidate);
                        }
                    }
                    return;
                default:
                    return;
            }
        }
    }
}
