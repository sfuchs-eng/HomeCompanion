using HomeCompanion.Values;
using MQTTnet;

namespace HomeCompanion.Integrations.Mqtt;

internal enum MqttRouteKind
{
    State,
    Command,
}

internal sealed record MqttValueMapping(IValue Value, MqttBusEndpointMapping Mapping, int RegistrationOrder);

internal sealed record MqttTopicMatchResult(
    string TopicFilter,
    bool IsExact,
    int FixedSegmentCount,
    int WildcardCount,
    IReadOnlyDictionary<string, string> Parameters);

internal sealed record MqttRouteSelection(
    IValue Value,
    MqttBusEndpointMapping Mapping,
    MqttRouteKind RouteKind,
    MqttTopicMatchResult Match);

internal sealed class MqttTopicRouter
{
    private readonly List<RouteEntry> _entries;

    public MqttTopicRouter(IEnumerable<MqttValueMapping> mappings)
    {
        _entries = [];

        foreach (var mapping in mappings)
        {
            var config = mapping.Mapping.Config ?? new MqttBusMappingConfiguration();

            foreach (var stateFilter in mapping.Mapping.GetAllStateTopicFilters())
            {
                _entries.Add(new RouteEntry(
                    mapping.Value,
                    mapping.Mapping,
                    MqttRouteKind.State,
                    stateFilter,
                    config.Priority,
                    mapping.RegistrationOrder));
            }

            if (!string.IsNullOrWhiteSpace(mapping.Mapping.CommandTopic))
            {
                _entries.Add(new RouteEntry(
                    mapping.Value,
                    mapping.Mapping,
                    MqttRouteKind.Command,
                    mapping.Mapping.CommandTopic!,
                    config.Priority,
                    mapping.RegistrationOrder));
            }
        }
    }

    public bool TryResolve(string topic, out MqttRouteSelection? selection)
    {
        selection = null;

        var matches = _entries
            .Select(entry => TryMatch(topic, entry))
            .Where(m => m is not null)
            .Cast<RouteMatch>()
            .OrderByDescending(m => m.Match.IsExact)
            .ThenByDescending(m => m.Match.FixedSegmentCount)
            .ThenBy(m => m.Match.WildcardCount)
            .ThenByDescending(m => m.Entry.Priority)
            .ThenBy(m => m.Entry.RegistrationOrder)
            .ToList();

        if (matches.Count == 0)
            return false;

        var best = matches[0];
        selection = new MqttRouteSelection(best.Entry.Value, best.Entry.Mapping, best.Entry.RouteKind, best.Match);
        return true;
    }

    private static RouteMatch? TryMatch(string topic, RouteEntry entry)
    {
        if (MqttTopicFilterComparer.Compare(topic, entry.TopicFilter) != MqttTopicFilterCompareResult.IsMatch)
            return null;

        var (fixedSegments, wildcardSegments, isExact) = ComputeSpecificity(entry.TopicFilter);
        var parameters = ExtractParameters(topic, entry.TopicFilter, entry.Mapping.Config?.TopicParameters);

        return new RouteMatch(
            entry,
            new MqttTopicMatchResult(
                entry.TopicFilter,
                isExact,
                fixedSegments,
                wildcardSegments,
                parameters));
    }

    private static (int FixedSegments, int WildcardSegments, bool IsExact) ComputeSpecificity(string filter)
    {
        var segments = SplitSegments(filter);
        var fixedSegments = 0;
        var wildcardSegments = 0;

        foreach (var segment in segments)
        {
            if (segment == "+" || segment == "#")
                wildcardSegments++;
            else
                fixedSegments++;
        }

        return (fixedSegments, wildcardSegments, wildcardSegments == 0);
    }

    private static IReadOnlyDictionary<string, string> ExtractParameters(
        string topic,
        string filter,
        IReadOnlyList<string>? parameterNames)
    {
        if (parameterNames is null || parameterNames.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var values = new List<string>();
        var topicSegments = SplitSegments(topic);
        var filterSegments = SplitSegments(filter);

        var topicIndex = 0;
        for (var i = 0; i < filterSegments.Length && topicIndex <= topicSegments.Length; i++)
        {
            var segment = filterSegments[i];
            if (segment == "+")
            {
                if (topicIndex < topicSegments.Length)
                    values.Add(topicSegments[topicIndex]);
                topicIndex++;
                continue;
            }

            if (segment == "#")
            {
                var rest = topicIndex < topicSegments.Length
                    ? string.Join('/', topicSegments.Skip(topicIndex))
                    : string.Empty;
                values.Add(rest);
                break;
            }

            topicIndex++;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var count = Math.Min(parameterNames.Count, values.Count);
        for (var i = 0; i < count; i++)
        {
            var name = parameterNames[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            result[name] = values[i];
        }

        return result;
    }

    private static string[] SplitSegments(string value)
    {
        return value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.None);
    }

    private sealed record RouteEntry(
        IValue Value,
        MqttBusEndpointMapping Mapping,
        MqttRouteKind RouteKind,
        string TopicFilter,
        int Priority,
        int RegistrationOrder);

    private sealed record RouteMatch(RouteEntry Entry, MqttTopicMatchResult Match);
}
