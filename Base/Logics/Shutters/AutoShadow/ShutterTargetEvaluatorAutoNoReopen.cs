namespace HomeCompanion.Logics.Shutters.AutoShadow;
using Microsoft.Extensions.Logging;

public class ShutterTargetEvaluatorAutoNoReopen(
    ShutterConditionsEvaluationResult cond,
    IEnvironmentalsProvider environmentalsProvider,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoNoReopen> logger
    ) : ShutterTargetEvaluatorAuto(cond, environmentalsProvider, timeProvider, logger)
{
}