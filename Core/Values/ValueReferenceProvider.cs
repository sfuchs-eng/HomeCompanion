using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HomeCompanion.Values;

/// <summary>
/// Default reference resolver for configured value references.
/// </summary>
internal sealed class ValueReferenceProvider(
    IEnumerable<IValuesContainer> containers,
    ILogger<ValueReferenceProvider> logger)
    : IValueReferenceProvider
{
    private readonly IEnumerable<IValuesContainer> _containers = containers;
    private readonly ILogger<ValueReferenceProvider> _logger = logger;
    private readonly object _sync = new();
    private Dictionary<string, List<IValue>> _index = new(StringComparer.OrdinalIgnoreCase);
    private bool _indexInitialized;

    public IValue Resolve(string reference)
    {
        if (TryResolveCore(reference, allowRefresh: true, out var value, out var error) && value is not null)
            return value;

        throw new InvalidOperationException(error ?? $"Unable to resolve value reference '{reference}'.");
    }

    public bool TryResolve(string reference, out IValue? value)
        => TryResolveCore(reference, allowRefresh: true, out value, out _);

    public bool TryResolve<T>(string reference, out IValue<T>? value)
    {
        value = null;
        if (!TryResolve(reference, out var resolved) || resolved is null)
            return false;

        if (resolved is IValue<T> typed)
        {
            value = typed;
            return true;
        }

        return false;
    }

    private bool TryResolveCore(string reference, bool allowRefresh, out IValue? value, out string? error)
    {
        value = null;
        error = null;

        if (!TryNormalizeReference(reference, out var normalized))
        {
            error = $"Value reference '{reference}' is invalid.";
            return false;
        }

        EnsureIndexInitialized();

        if (TryResolveFromIndex(normalized, out value, out error))
            return true;

        if (!allowRefresh)
            return false;

        RebuildIndex();
        return TryResolveCore(reference, allowRefresh: false, out value, out error);
    }

    private void EnsureIndexInitialized()
    {
        if (_indexInitialized)
            return;

        lock (_sync)
        {
            if (_indexInitialized)
                return;

            _index = BuildIndex();
            _indexInitialized = true;
        }
    }

    private void RebuildIndex()
    {
        lock (_sync)
        {
            _index = BuildIndex();
            _indexInitialized = true;
        }
    }

    private bool TryResolveFromIndex(string normalized, out IValue? value, out string? error)
    {
        lock (_sync)
        {
            if (!_index.TryGetValue(normalized, out var candidates) || candidates.Count == 0)
            {
                value = null;
                error = $"No value found for reference '{normalized}'.";
                return false;
            }

            var distinct = candidates.Distinct(ReferenceEqualityComparer<IValue>.Instance).ToList();
            if (distinct.Count > 1)
            {
                value = null;
                error = $"Reference '{normalized}' is ambiguous and matched {distinct.Count} values.";
                return false;
            }

            value = distinct[0];
            error = null;
            return true;
        }
    }

    private Dictionary<string, List<IValue>> BuildIndex()
    {
        var index = new Dictionary<string, List<IValue>>(StringComparer.OrdinalIgnoreCase);

        foreach (var container in _containers)
        {
            IndexContainer(index, container);
        }

        _logger.LogDebug("Built value reference index with {EntryCount} key(s).", index.Count);
        return index;
    }

    private void IndexContainer(Dictionary<string, List<IValue>> index, IValuesContainer container)
    {
        var containerType = container.GetType();
        var containerTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            containerType.Name,
        };
        if (!string.IsNullOrWhiteSpace(containerType.FullName))
            containerTypeNames.Add(containerType.FullName);

        var containerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            containerType.Name,
        };
        if (TryGetContainerName(container, out var explicitContainerName))
            containerNames.Add(explicitContainerName);

        var namesByValue = DiscoverValueNames(container);
        foreach (var (value, valueNames) in namesByValue)
        {
            foreach (var alias in BuildAliases(containerTypeNames, containerNames, valueNames))
            {
                if (!TryNormalizeReference(alias, out var normalized))
                    continue;

                if (!index.TryGetValue(normalized, out var list))
                {
                    list = [];
                    index[normalized] = list;
                }

                list.Add(value);
            }
        }
    }

    private static IEnumerable<string> BuildAliases(
        IEnumerable<string> containerTypeNames,
        IEnumerable<string> containerNames,
        IEnumerable<string> valueNames)
    {
        foreach (var valueName in valueNames)
        {
            if (string.IsNullOrWhiteSpace(valueName))
                continue;

            yield return valueName;

            foreach (var typeName in containerTypeNames)
            {
                yield return $"{typeName}:{valueName}";
                yield return $"{typeName}.{valueName}";

                foreach (var containerName in containerNames)
                {
                    yield return $"{typeName}[{containerName}]:{valueName}";
                    yield return $"{typeName}.{containerName}.{valueName}";
                }
            }
        }
    }

    private static Dictionary<IValue, HashSet<string>> DiscoverValueNames(IValuesContainer container)
    {
        var namesByValue = new Dictionary<IValue, HashSet<string>>(ReferenceEqualityComparer<IValue>.Instance);

        foreach (var value in container.GetValues())
        {
            if (!namesByValue.TryGetValue(value, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                namesByValue[value] = names;
            }

            if (!string.IsNullOrWhiteSpace(value.Name))
                names.Add(value.Name);
        }

        var properties = container.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && typeof(IValue).IsAssignableFrom(p.PropertyType));

        foreach (var property in properties)
        {
            if (property.GetValue(container) is not IValue value)
                continue;

            if (!namesByValue.TryGetValue(value, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                namesByValue[value] = names;
            }

            names.Add(property.Name);
        }

        return namesByValue;
    }

    private static bool TryGetContainerName(IValuesContainer container, out string name)
    {
        name = string.Empty;

        var property = container.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        if (property?.PropertyType != typeof(string) || !property.CanRead)
            return false;

        if (property.GetValue(container) is not string value || string.IsNullOrWhiteSpace(value))
            return false;

        name = value;
        return true;
    }

    internal static bool TryNormalizeReference(string reference, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var raw = reference.Trim();
        if (!TryParseReference(raw, out var parsed))
            return false;

        normalized = parsed.ContainerType is null
            ? parsed.ValueName
            : parsed.ContainerName is null
                ? $"{parsed.ContainerType}:{parsed.ValueName}"
                : $"{parsed.ContainerType}[{parsed.ContainerName}]:{parsed.ValueName}";

        return true;
    }

    private static bool TryParseReference(string raw, out ParsedReference parsed)
    {
        parsed = default;

        if (raw.Contains(':'))
        {
            var parts = raw.Split(':', 2);
            if (parts.Length != 2)
                return false;

            var left = parts[0].Trim();
            var valueName = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(valueName))
                return false;

            if (left.Contains('['))
            {
                var open = left.IndexOf('[');
                var close = left.LastIndexOf(']');
                if (open <= 0 || close <= open)
                    return false;

                var containerType = left[..open].Trim();
                var containerName = left[(open + 1)..close].Trim();
                if (string.IsNullOrWhiteSpace(containerType) || string.IsNullOrWhiteSpace(containerName))
                    return false;

                parsed = new ParsedReference(containerType, containerName, valueName);
                return true;
            }

            if (string.IsNullOrWhiteSpace(left))
                return false;

            parsed = new ParsedReference(left, null, valueName);
            return true;
        }

        var segments = raw.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        if (segments.Length == 1)
        {
            parsed = new ParsedReference(null, null, segments[0]);
            return true;
        }

        if (segments.Length == 2)
        {
            parsed = new ParsedReference(segments[0], null, segments[1]);
            return true;
        }

        var dottedContainerType = string.Join('.', segments[..^2]);
        var dottedContainerName = segments[^2];
        var dottedValueName = segments[^1];

        parsed = new ParsedReference(dottedContainerType, dottedContainerName, dottedValueName);
        return true;
    }

    private readonly record struct ParsedReference(string? ContainerType, string? ContainerName, string ValueName);

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static ReferenceEqualityComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
