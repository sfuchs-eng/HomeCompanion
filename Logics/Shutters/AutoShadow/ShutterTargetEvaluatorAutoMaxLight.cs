using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

/// <summary>
/// Shutter target position evaluator for room scene AutoMaxLight specific logic.
/// </summary>
/// <remarks>
/// We put it all into the base class <see cref="ShutterTargetEvaluatorAuto"/> for now.
/// </remarks>
/// <param name="cond"></param>
/// <param name="environmentalsProvider"></param>
/// <param name="timeProvider"></param>
/// <param name="logger"></param> <summary>
public class ShutterTargetEvaluatorAutoMaxLight(
    ShutterConditionsEvaluationResult cond,
    IEnvironmentalsProvider environmentalsProvider,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluatorAutoMaxLight> logger
    ) : ShutterTargetEvaluatorAuto(cond, environmentalsProvider, timeProvider, logger)
{
}