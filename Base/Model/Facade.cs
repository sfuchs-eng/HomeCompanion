using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Base.Model;

/// <summary>
/// Configuration for a facade.
/// </summary>
public class CfgFacade : CfgEntity
{
    /// <summary>
    /// The azimuth angle in deg (360) of the facade normal, from North clock-wise.
    /// </summary>
    public double Azimuth { get; set; }

    /// <summary>
    /// The elevation angle in deg (360) of the facade normal, from horizontal up. A flat roof is 90 deg, a vertical facade is 0 deg.
    /// </summary>
    public double Elevation { get; set; }
}

/// <summary>
/// Runtime representation of a facade.
/// </summary>
public class Facade(string name, CfgFacade configuration) : ModelEntityWithConfig<CfgFacade>(name, configuration), IConfigBackedModelEntity
{
    /// <summary>
    /// Facade orientation in radians.
    /// </summary>
    public SphericVector OrientationRad => SphericVector.FromDegrees(Configuration.Azimuth, Configuration.Elevation);

    CfgEntity IConfigBackedModelEntity.Configuration => Configuration;
}
