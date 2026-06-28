using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public class ShutterRuntime(
    ShutterKey shutterKey,
    IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder,
    ILogger<ShutterRuntime> logger
) : RuntimeBase(logger)
{
    public ShutterKey ShutterKey { get; } = shutterKey;
    private readonly ILogger<ShutterRuntime> logger = logger;
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder = queueFeeder;

    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Register event handlers for shutter level inputs.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var shutter = ShutterKey.Shutter;
        var shutterConfig = ShutterKey.ShutterConfig;

        shutter.AngleValue?.Written += HandleShutterCommanded;
        shutter.PositionValue?.Written += HandleShutterCommanded;
        shutter.OpenCloseValue?.Written += HandleShutterCommanded;

        await Task.CompletedTask;
    }

    public event EventHandler<ShutterExternalOverrideEventArgs>? ShutterExternalOverride;

    private void HandleShutterCommanded(object? sender, ValueWrittenEventArgs e)
    {
        if (sender is ShutterRuntime shutterRuntime && ReferenceEquals(shutterRuntime, this))
        {
            // we're not handling this as it's coming from ourselves.
            return;
        }
        ShutterExternalOverride?.Invoke(this, new ShutterExternalOverrideEventArgs(ShutterKey, e));
    }

    /// <summary>
    /// Stop the shutter runtime and unregister event handlers for all relevant inputs.
    /// </summary>
    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        var shutter = ShutterKey.Shutter;

        shutter.AngleValue?.Written -= HandleShutterCommanded;
        shutter.PositionValue?.Written -= HandleShutterCommanded;
        shutter.OpenCloseValue?.Written -= HandleShutterCommanded;

        return Task.CompletedTask;
    }

    internal async Task ExecuteShutterTargetAsync(ShutterTarget shutterTarget)
    {
        if ( !shutterTarget.ShutterKey.Equals(this.ShutterKey) )
            throw new ArgumentException($"The provided shutter target {shutterTarget} does not match the shutter key {this.ShutterKey} of this runtime.", nameof(shutterTarget));
        var shutter = ShutterKey.Shutter;
        // switch:      ShutterType.OpenClose => shutter.OpenCloseValue.TryWriteNumeric(shutterTarget.OpenClose, shutterTarget.Duration),

        bool success = true;
        switch (shutter.Configuration.Type)
        {
            case ShutterType.VenetianBlind:
                success = success && (shutter.PositionValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                success = success && (shutter.AngleValue?.TryWriteNumeric(shutterTarget.TargetPosition.TiltAngle, this, logger) ?? false);
                break;
            case ShutterType.Positional:
                success = success && (shutter.PositionValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                break;
            case ShutterType.OpenClose:
                success = success && (shutter.OpenCloseValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                break;
            default:
                logger.LogWarning("Unsupported shutter type {ShutterType} for shutter {ShutterKey}.", shutter.Configuration.Type, ShutterKey);
                break;
        }
        if ( !success )
        {
            logger.LogWarning("Failed to write target {ShutterTarget} to shutter {ShutterKey} of type {ShutterType}.", shutterTarget, ShutterKey, shutter.Configuration.Type);
        }
    }

    internal static Dictionary<ShutterKey, ShutterRuntime> Create(RuntimeCreationContext<ShutterKey, ShutterRuntime> runtimeCreationContext)
    {
        var model = runtimeCreationContext.Model;
        var existingRuntimes = runtimeCreationContext.ExistingRuntimes;
        var queueFeeder = runtimeCreationContext.ComputationTriggerQueueFeeder;
        var loggerFactory = runtimeCreationContext.LoggerFactory;

        var newRuntimes = new Dictionary<ShutterKey, ShutterRuntime>();

        foreach (var shutterKey in model.EnumerateShutterKeys())
        {
            if (existingRuntimes != null && existingRuntimes.ContainsKey(shutterKey))
            {
                continue;
            }

            var runtime = new ShutterRuntime(shutterKey, queueFeeder, loggerFactory.CreateLogger<ShutterRuntime>());
            newRuntimes[shutterKey] = runtime;
        }

        return newRuntimes;
    }
}

public class ShutterExternalOverrideEventArgs(ShutterKey shutterKey, ValueWrittenEventArgs valueWrittenEventArgs) : EventArgs
{
    public ShutterKey ShutterKey { get; } = shutterKey;
    public ValueWrittenEventArgs ValueWrittenEventArgs { get; } = valueWrittenEventArgs;
}