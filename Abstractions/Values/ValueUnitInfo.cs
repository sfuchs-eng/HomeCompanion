namespace HomeCompanion.Values;

/// <summary>
/// Unit metadata used by values that represent physical quantities.
/// QuantityName and UnitName use UnitsNet naming conventions (e.g. Temperature, DegreeCelsius).
/// </summary>
public sealed record ValueUnitInfo(string QuantityName, string UnitName, string? UnitSymbol = null)
{
    public string DisplayUnit => string.IsNullOrWhiteSpace(UnitSymbol) ? UnitName : UnitSymbol;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(UnitSymbol)
            ? $"{QuantityName}:{UnitName}"
            : $"{QuantityName}:{UnitName} ({UnitSymbol})";
    }
}
