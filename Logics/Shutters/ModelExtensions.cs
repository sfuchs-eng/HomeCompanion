using HomeCompanion.Logics.Shutters.AutoShadow;

namespace HomeCompanion.Logics.Shutters;

public static class ModelExtensions
{
    public static double GetCutoverAngle(this CfgDynamicCutoverAngleRule coaRule, double roomTemperature, ShadowingPolicy shadowingPolicy)
    {
        return shadowingPolicy switch
        {
            ShadowingPolicy.AggressiveShadowing => coaRule.CutoverAngle,// no interpolation, stick to minimum
                                                                        // Use the base cut-over angle for aggressive shadowing
            ShadowingPolicy.AvoidShadowing => coaRule.CutoverAngleMax ?? coaRule.CutoverAngle,// no interpolation, stick to maximum if defined, otherwise use base
                                                                                              // Use the maximum cut-over angle for avoid shadowing
            _ => coaRule.GetCutoverAngle(roomTemperature),// No change for default policy
        };
    }
}
