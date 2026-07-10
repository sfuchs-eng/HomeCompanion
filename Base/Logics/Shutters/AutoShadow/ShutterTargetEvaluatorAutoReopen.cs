using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

public class ShutterTargetEvaluatorAutoReopen(
    ShutterConditionsEvaluationResult cond,
    IEnvironmentalsProvider environmentalsProvider,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoReopen> logger
    ) : ShutterTargetEvaluatorAuto(cond, environmentalsProvider, timeProvider, logger)
{
}