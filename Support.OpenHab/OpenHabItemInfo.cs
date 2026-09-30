namespace HomeCompanion.Support.OpenHab;

/// <summary>
/// Proxy class to hold information about an OpenHAB item, used for code generation.
/// Typically populated by deserializing JSON from the OpenHAB REST API / initialized from returned objects.
/// </summary>
public class OpenHabItemInfo
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
}