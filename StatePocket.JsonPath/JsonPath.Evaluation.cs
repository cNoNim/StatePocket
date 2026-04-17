using System.Text.Json;

namespace StatePocket.JsonPath;

public sealed partial class JsonPath
{
    private static List<PathValue> EvaluateSegments(
        JsonElement root,
        JsonElement startValue,
        string startPath,
        IReadOnlyList<SelectorSegment> segments
    )
    {
        var currentValues = new List<PathValue>
        {
            new(startValue, startPath),
        };
        foreach (var segment in segments)
        {
            List<PathValue> nextValues = [];
            foreach (var currentValue in currentValues)
            {
                segment.Apply(root, currentValue, nextValues);
            }
            currentValues = nextValues;
        }
        return currentValues;
    }

    private sealed record PathValue(JsonElement Value, string NormalizedPath);

    private abstract class SelectorSegment
    {
        public virtual bool IsSingular => false;
        public abstract void Apply(JsonElement root, PathValue current, ICollection<PathValue> next);
    }

    private sealed class NameSegment(string name) : SelectorSegment
    {
        public override bool IsSingular => true;

        public override void Apply(JsonElement root, PathValue current, ICollection<PathValue> next)
        {
            if (current.Value.ValueKind == JsonValueKind.Object
             && current.Value.TryGetProperty(name, out var child))
            {
                next.Add(new PathValue(child, $"{current.NormalizedPath}{NormalizeName(name)}"));
            }
        }
    }

    private sealed class WildcardSegment : SelectorSegment
    {
        public override void Apply(JsonElement root, PathValue current, ICollection<PathValue> next)
        {
            switch (current.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in current.Value.EnumerateObject())
                    {
                        next.Add(
                            new PathValue(property.Value, $"{current.NormalizedPath}{NormalizeName(property.Name)}")
                        );
                    }
                    return;
                case JsonValueKind.Array:
                    for (var index = 0; index < current.Value.GetArrayLength(); index++)
                    {
                        next.Add(new PathValue(current.Value[index], $"{current.NormalizedPath}[{index}]"));
                    }
                    return;
                default:
                    return;
            }
        }
    }

    private sealed class IndexSegment(long index) : SelectorSegment
    {
        public override bool IsSingular => true;

        public override void Apply(JsonElement root, PathValue current, ICollection<PathValue> next)
        {
            if (current.Value.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            var arrayLength = current.Value.GetArrayLength();
            var resolvedIndex = index >= 0 ? index : arrayLength + index;
            if (resolvedIndex < 0
             || resolvedIndex >= arrayLength)
            {
                return;
            }
            next.Add(new PathValue(current.Value[(int)resolvedIndex], $"{current.NormalizedPath}[{resolvedIndex}]"));
        }
    }

    private sealed class SliceSegment(long? start, long? end, long? step) : SelectorSegment
    {
        public override void Apply(JsonElement root, PathValue current, ICollection<PathValue> next)
        {
            if (current.Value.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (var index in EvaluateSlice(
                         start,
                         end,
                         step,
                         current.Value.GetArrayLength()
                     ))
            {
                next.Add(new PathValue(current.Value[(int)index], $"{current.NormalizedPath}[{index}]"));
            }
        }
    }

    private sealed class UnionSegment(IReadOnlyList<SelectorSegment> selectors) : SelectorSegment
    {
        public override void Apply(JsonElement root, PathValue current, ICollection<PathValue> next)
        {
            foreach (var selector in selectors)
            {
                selector.Apply(root, current, next);
            }
        }
    }

    private sealed class DescendantSegment(SelectorSegment selector) : SelectorSegment
    {
        public override void Apply(JsonElement root, PathValue current, ICollection<PathValue> next)
        {
            foreach (var descendant in EnumerateSelfAndDescendants(current))
            {
                selector.Apply(root, descendant, next);
            }
        }

        private static IEnumerable<PathValue> EnumerateSelfAndDescendants(PathValue current)
        {
            yield return current;
            switch (current.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in current.Value.EnumerateObject())
                    {
                        foreach (var descendant in EnumerateSelfAndDescendants(
                                     new PathValue(
                                         property.Value,
                                         $"{current.NormalizedPath}{NormalizeName(property.Name)}"
                                     )
                                 ))
                        {
                            yield return descendant;
                        }
                    }
                    yield break;
                case JsonValueKind.Array:
                    for (var index = 0; index < current.Value.GetArrayLength(); index++)
                    {
                        foreach (var descendant in EnumerateSelfAndDescendants(
                                     new PathValue(current.Value[index], $"{current.NormalizedPath}[{index}]")
                                 ))
                        {
                            yield return descendant;
                        }
                    }
                    yield break;
                default:
                    yield break;
            }
        }
    }
}
