using HomeCompanion.Values;
using UnitsNet;

namespace HomeCompanion.Base.Model;

public class CfgRainSensor : CfgBuildingSpecial
{
    public string? RainInputReference { get; set; }

    public string? RainActual24hReference { get; set; }

    public string? RainActual48hReference { get; set; }

    public string? RainActual72hReference { get; set; }

    public string? RainActual1wReference { get; set; }

    public string? RainActual2wReference { get; set; }
}

public class RainSensor(string name, CfgRainSensor config) : Special<CfgRainSensor>(name, config), IBuildingSpecial
{
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRainSensor.RainInputReference))]
    public IValue<Length>? RainInput { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRainSensor.RainActual24hReference))]
    public IValue<Length>? RainActual24h { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRainSensor.RainActual48hReference))]
    public IValue<Length>? RainActual48h { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRainSensor.RainActual72hReference))]
    public IValue<Length>? RainActual72h { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRainSensor.RainActual1wReference))]
    public IValue<Length>? RainActual1w { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRainSensor.RainActual2wReference))]
    public IValue<Length>? RainActual2w { get; set; }
}
