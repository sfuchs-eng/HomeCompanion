using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

public class ShutterTargetEvaluatorAutoReopen(
    ShutterConditionsEvaluationResult cond,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoReopen> logger
    ) : ShutterTargetEvaluatorAuto(cond, timeProvider, logger)
{
}