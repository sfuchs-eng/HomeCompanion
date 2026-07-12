namespace HomeCompanion.Logics.Shutters.AutoShadow;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shutter target position evaluator for room scene AutoNoReopen specific logic.
/// </summary>
/// <remarks>
/// We put it all into the base class <see cref="ShutterTargetEvaluatorAuto"/> for now.
/// </remarks>
public class ShutterTargetEvaluatorAutoNoReopen(
    ShutterConditionsEvaluationResult cond,
    IEnvironmentalsProvider environmentalsProvider,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoNoReopen> logger
    ) : ShutterTargetEvaluatorAuto(cond, environmentalsProvider, timeProvider, logger)
{
}