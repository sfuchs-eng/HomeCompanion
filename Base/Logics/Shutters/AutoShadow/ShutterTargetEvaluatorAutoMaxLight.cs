using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

public class ShutterTargetEvaluatorAutoMaxLight(
    ShutterConditionsEvaluationResult cond,
    IEnvironmentalsProvider environmentalsProvider,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoMaxLight> logger
    ) : ShutterTargetEvaluatorAuto(cond, environmentalsProvider, timeProvider, logger)
{
}