using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Logics.Shutters.AutoShadow;

namespace HomeCompanion.Tests.Logics.Shutters;

internal sealed class StubModelProvider : IModelProvider
{
    private readonly Model model;

    public StubModelProvider(Model model)
    {
        this.model = model;
    }

    public bool IsInitialized => true;

    public Model GetModel()
    {
        return model;
    }
}

public class StubEnvironmentalsProvider : IEnvironmentalsProvider
{
    public double OutdoorTemperature { get; set; } = 15.0;

    public double SunIntensityPU { get; set; } = 0.8;

    public bool SunIntensityAboveThreshold { get; set; } = true;

    public double UvIntensityPU { get; set; } = 0.5;

    public SphericVector SunPosition { get; set; } = SphericVector.FromDegrees(190.0, 50.0); // Example sun position in azimuth and elevation
}
