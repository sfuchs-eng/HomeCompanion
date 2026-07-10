using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

public class ShutterTargetEvaluatorAutoMaxLight(
    ShutterConditionsEvaluationResult cond,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoMaxLight> logger
    ) : ShutterTargetEvaluatorAuto(cond, timeProvider, logger)
{
}