using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

/// <summary>
/// Shutter target position evaluator for room scene AutoReopen specific logic.
/// </summary>
/// <remarks>
/// We put it all into the base class <see cref="ShutterTargetEvaluatorAuto"/> for now.
/// </remarks>
public class ShutterTargetEvaluatorAutoReopen(
    ShutterConditionsEvaluationResult cond,
    IEnvironmentalsProvider environmentalsProvider,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoReopen> logger
    ) : ShutterTargetEvaluatorAuto(cond, environmentalsProvider, timeProvider, logger)
{
}