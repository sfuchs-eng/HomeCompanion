using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Base.Logics.MotorizedWindow;

/// <summary>
/// This is the state engine for either the window or the shutter, depending on the context.
/// It is responsible for processing the commands and acknowledgments, managing the state of the window/shutter, and ensuring the correct timing for opening/closing and lock delays.
/// </summary>
internal class ThreeWireControl(
    ThreeWireControlContext context,
    ILogger<ThreeWireControl> logger
)
{
    private readonly ThreeWireControlContext context = context;
    private readonly ILogger<ThreeWireControl> logger = logger;

    private ThreeWireControlState state = ThreeWireControlState.Undefined;

    Task _transitionRunner = Task.CompletedTask;
    CancellationTokenSource? _transitionRunnerTokenSource;
    InspectableAsyncQueue<ThreeWireControlRequest> _commandQueue = new();

    SemaphoreSlim _commandAcknowledgmentSemaphore = new(0);
    bool _commandAcknowledgmentReceived = false;
    bool _forceOpenRequested = false;

    public void SetRequireOpenPriorToOpening(ThreeWireControl? otherControl)
    {
        context.RequireOpenPriorToOpening = otherControl;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _transitionRunnerTokenSource = new CancellationTokenSource();
        _transitionRunner = Task.Run(() => TransitionRunnerAsync(_transitionRunnerTokenSource.Token), CancellationToken.None);

        context.PositionRequest?.Changed += HandlePositionRequestChanged;
        context.CommandAcknowledgment?.Written += HandleCommandAcknowledgment;

        await Task.CompletedTask;
    }

    private void HandleCommandAcknowledgment(object? sender, ValueWrittenEventArgs e)
    {
        if ( context.CommandAcknowledgment.Value )
        {
            _commandAcknowledgmentReceived = true;
            _commandAcknowledgmentSemaphore.Release();
        }
    }

    private void HandlePositionRequestChanged(object? sender, ValueChangedEventArgs e)
    {
        _commandQueue.Enqueue(context.PositionRequest.Value ? ThreeWireControlRequest.Close : ThreeWireControlRequest.Open);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_transitionRunnerTokenSource != null)
        {
            _transitionRunnerTokenSource.Cancel();
            try
            {
                await _transitionRunner;
            }
            catch (OperationCanceledException)
            {
                // expected when the token is cancelled, ignore
            }
        }
    }

    private async Task TransitionRunnerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var result = await _commandQueue.DequeueAsync(TimeSpan.FromSeconds(10), token);
            if (!result.Success)
            {
                continue; // timeout, loop back to check for cancellation
            }

            var request = result.Item;
            switch (request)
            {
                case ThreeWireControlRequest.Open:
                    if (state == ThreeWireControlState.Open)
                    {
                        break; // already open, ignore
                    }
                    if (context.RequireOpenPriorToOpening != null)
                    {
                        await context.RequireOpenPriorToOpening.EnforceOpenAsync(token);
                        if (context.RequireOpenPriorToOpening.state != ThreeWireControlState.ForcedOpen)
                        {
                            logger.LogWarning("Failed to open {Name} because required control {RequiredControlName} could not be force-opened.", context.Name, context.RequireOpenPriorToOpening.context.Name);
                            break; // failed to open the required control, abort
                        }
                    }
                    context.CloseCommand.WriteLocked(false);
                    context.OpenCommand.WriteLocked(true);
                    // wait for acknowledgment or timeout
                    _commandAcknowledgmentReceived = false;
                    await _commandAcknowledgmentSemaphore.WaitAsync(context.Timing.OpenDuration + context.Timing.ExcessTime, token);
                    state = _commandAcknowledgmentReceived ? ThreeWireControlState.Open : ThreeWireControlState.OpeningTimeOut;
                    if (state == ThreeWireControlState.OpeningTimeOut)
                    {
                        logger.LogWarning("Opening of {Name} timed out after {Duration} without acknowledgment.", context.Name, context.Timing.OpenDuration + context.Timing.ExcessTime);
                    }
                    break;
                case ThreeWireControlRequest.Close:
                    if (state == ThreeWireControlState.Closed)
                    {
                        break; // already closed, ignore
                    }
                    if (_forceOpenRequested)
                    {
                        logger.LogTrace("Refusing to close. Release of forced open state for {Name} prior to closing.", context.Name);
                        break; // refuse to close when there is an active forced open request
                    }
                    context.OpenCommand.WriteLocked(false);
                    context.CloseCommand.WriteLocked(true);
                    // wait for acknowledgment or timeout
                    _commandAcknowledgmentReceived = false;
                    await _commandAcknowledgmentSemaphore.WaitAsync(context.Timing.CloseDuration + context.Timing.ExcessTime, token);
                    state = _commandAcknowledgmentReceived ? ThreeWireControlState.Closed : ThreeWireControlState.ClosingTimeOut;
                    if (state == ThreeWireControlState.ClosingTimeOut)
                    {
                        logger.LogWarning("Closing of {Name} timed out after {Duration} without acknowledgment.", context.Name, context.Timing.CloseDuration + context.Timing.ExcessTime);
                    }
                    else if (context.RequireOpenPriorToOpening != null)
                    {
                        // after successfully closing, we can release the required control if it is currently forced open
                        await context.RequireOpenPriorToOpening.ReleaseAsync(token);
                    }
                    break;
                case ThreeWireControlRequest.ForceOpen:
                    _forceOpenRequested = true;
                    if (state == ThreeWireControlState.ForcedOpen)
                    {
                        break; // already forced open, ignore
                    }
                    _commandQueue.Enqueue(ThreeWireControlRequest.Open);
                    break;
                case ThreeWireControlRequest.Release:
                    _forceOpenRequested = false;
                    // go back to the requested position
                    if (context.PositionRequest.IsActive && !context.PositionRequest.Value)
                    {
                        _commandQueue.Enqueue(ThreeWireControlRequest.Open);
                    }
                    else
                    {
                        _commandQueue.Enqueue(ThreeWireControlRequest.Close);
                    }
                    break;
            }
        }
        logger.LogTrace("Transition runner for {Name} is stopping due to cancellation.", context.Name);
    }

    private async Task ReleaseAsync(CancellationToken token)
    {
        _commandQueue.Enqueue(ThreeWireControlRequest.Release);
        // wait until the state is no longer forced open or the transition timeout is reached
        while (!token.IsCancellationRequested)
        {
            if (state != ThreeWireControlState.ForcedOpen)
            {
                return; // successfully released
            }
            if (state == ThreeWireControlState.OpeningTimeOut || state == ThreeWireControlState.ClosingTimeOut)
            {
                logger.LogWarning("Failed to release forced open state for {Name} within the expected time.", context.Name);
                return; // failed to release within the expected time
            }
            await Task.Delay(500, token); // check every 500ms
        }
    }

    /// <summary>
    /// Opens the window/shutter if it is not already open.
    /// If it is currently closing, it will abort the operation and start opening immediately.
    /// Puts the window/shutter into a "forced open" state, which means that it will ignore position request changes on <see cref="ThreeWireControlContext.PositionRequest"/> until it gets released.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> EnforceOpenAsync(CancellationToken cancellationToken = default)
    {
        if (state == ThreeWireControlState.ForcedOpen)
        {
            return true; // already forced open, nothing to do
        }
        _commandQueue.Enqueue(ThreeWireControlRequest.ForceOpen);
        // wait until the state is open or the transition timeout is reached; use the semaphore
        while (!cancellationToken.IsCancellationRequested)
        {
            if (state == ThreeWireControlState.ForcedOpen)
            {
                return true; // successfully opened
            }
            if (state == ThreeWireControlState.OpeningTimeOut)
            {
                return false; // failed to open within the expected time
            }
            await Task.Delay(500, cancellationToken); // check every 500ms
        }
        return false; // operation was cancelled
    }

    public async Task<bool> CloseAsync(CancellationToken cancellationToken = default)
    {
        if (state == ThreeWireControlState.Closed)
        {
            return true; // already closed, nothing to do
        }
        if (_forceOpenRequested)
        {
            logger.LogTrace("Refusing to close {Name} due to active forced open request. Release the forced open state prior to closing.", context.Name);
            return false;
        }
        _commandQueue.Enqueue(ThreeWireControlRequest.Close);
        // wait until the state is closed or the transition timeout is reached; use the semaphore
        while (!cancellationToken.IsCancellationRequested)
        {
            if (state == ThreeWireControlState.Closed)
            {
                return true; // successfully closed
            }
            if (state == ThreeWireControlState.ClosingTimeOut)
            {
                return false; // failed to close within the expected time
            }
            await Task.Delay(500, cancellationToken); // check every 500ms
        }
        return false; // operation was cancelled
    }

    public async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (state == ThreeWireControlState.Open)
        {
            return true; // already open, nothing to do
        }
        _commandQueue.Enqueue(ThreeWireControlRequest.Open);
        // wait until the state is open or the transition timeout is reached; use the semaphore
        while (!cancellationToken.IsCancellationRequested)
        {
            if (state == ThreeWireControlState.Open)
            {
                return true; // successfully opened
            }
            if (state == ThreeWireControlState.OpeningTimeOut)
            {
                return false; // failed to open within the expected time
            }
            await Task.Delay(500, cancellationToken); // check every 500ms
        }
        return false; // operation was cancelled
    }
}

internal class ThreeWireControlContext
{
    public required string Name { get; set; }
    public required IValue<bool> PositionRequest { get; set; }
    public required IValue<bool> PositionStatus { get; set; }
    public required IValue<bool> CloseCommand { get; set; }
    public required IValue<bool> OpenCommand { get; set; }
    public required IValue<bool> CommandAcknowledgment { get; set; }
    public required ThreeWireControlTiming Timing { get; set; }

    /// <summary>
    /// Is there another control that needs to be opened such that this control can be opened, e.g. the shutter needs to be opened prior to opening the window.
    /// </summary>
    /// <value></value>
    public ThreeWireControl? RequireOpenPriorToOpening { get; set; }
}

internal enum ThreeWireControlState
{
    Undefined,
    Closed,
    Open,
    Opening,
    OpeningTimeOut,
    Closing,
    ClosingTimeOut,
    ForcedOpen,
    Released,
}

internal enum ThreeWireControlRequest
{
    Open,
    Close,
    ForceOpen,
    Release,
}