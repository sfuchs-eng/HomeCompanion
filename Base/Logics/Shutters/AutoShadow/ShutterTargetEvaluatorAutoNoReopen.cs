namespace HomeCompanion.Logics.Shutters.AutoShadow;
using Microsoft.Extensions.Logging;

public class ShutterTargetEvaluatorAutoNoReopen(
    ShutterConditionsEvaluationResult cond,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoNoReopen> logger
    ) : ShutterTargetEvaluatorAuto(cond, timeProvider, logger)
{
}