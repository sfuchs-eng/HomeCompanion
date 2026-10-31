using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Garden;

public enum RainAggregateWindow
{
    Hours24,
    Hours48,
    Hours72,
    Days7,
    Days14,
}

public static class RainAggregateWindowExtensions
{
    public static TimeSpan Duration(this RainAggregateWindow window) => window switch
    {
        RainAggregateWindow.Hours24 => TimeSpan.FromHours(24),
        RainAggregateWindow.Hours48 => TimeSpan.FromHours(48),
        RainAggregateWindow.Hours72 => TimeSpan.FromHours(72),
        RainAggregateWindow.Days7 => TimeSpan.FromDays(7),
        RainAggregateWindow.Days14 => TimeSpan.FromDays(14),
        _ => TimeSpan.Zero,
    };
}

public sealed class RainEventArgs(double quantity, DateTimeOffset when) : EventArgs
{
    public double Quantity { get; } = quantity;
    public DateTimeOffset When { get; } = when;
}

public sealed record RainQuant(double QuantityMm, DateTimeOffset When);

public interface IRainService
{
    event EventHandler<RainEventArgs>? RainTick;
    IReadOnlyDictionary<RainAggregateWindow, double> RollingAggregates { get; }
    double GetAggregate(RainAggregateWindow window);
    void ProcessRainTick(double quantityMm, DateTimeOffset? when = null);
    void RecomputeAggregates(DateTimeOffset? now = null);
    int AcceptedTickCount { get; }
    int RejectedTickCount { get; }
    int QuantCount { get; }
    RainEventArgs? LastTick { get; }
}

public sealed class RainService(TimeProvider? timeProvider = null, ILogger<RainService>? logger = null) : IRainService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger<RainService>? _logger = logger;
    private readonly List<RainQuant> _quantities = [];
    private readonly Dictionary<RainAggregateWindow, double> _aggregates = new();
    private readonly object _sync = new();

    public event EventHandler<RainEventArgs>? RainTick;

    public double CorrectionFactor { get; set; } = 1.0;

    public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromDays(14);

    public TimeSpan AggregatePublishInterval { get; set; } = TimeSpan.FromMinutes(15);

    public int AcceptedTickCount { get; private set; }

    public int RejectedTickCount { get; private set; }

    public int QuantCount => _quantities.Count;

    public RainEventArgs? LastTick { get; private set; }

    public IReadOnlyDictionary<RainAggregateWindow, double> RollingAggregates
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<RainAggregateWindow, double>(_aggregates);
            }
        }
    }

    public double GetAggregate(RainAggregateWindow window) => RollingAggregates.TryGetValue(window, out var value) ? value : 0d;

    public void ProcessRainTick(double quantityMm, DateTimeOffset? when = null)
    {
        var timestamp = when ?? _timeProvider.GetUtcNow();
        var adjusted = quantityMm * CorrectionFactor;

        if (!double.IsFinite(adjusted) || adjusted <= 0d)
        {
            RejectedTickCount++;
            return;
        }

        lock (_sync)
        {
            _quantities.Add(new RainQuant(adjusted, timestamp));
            PruneHistory(timestamp);
            RecomputeAggregatesCore(timestamp);
            AcceptedTickCount++;
            LastTick = new RainEventArgs(adjusted, timestamp);
        }

        var handlers = RainTick?.GetInvocationList().Cast<EventHandler<RainEventArgs>>().ToArray() ?? [];
        foreach (var handler in handlers)
        {
            try
            {
                handler(this, LastTick!);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Rain tick subscriber failed for quantity {Quantity} mm at {When}.", LastTick!.Quantity, LastTick.When);
            }
        }
    }

    public void RecomputeAggregates(DateTimeOffset? now = null)
    {
        lock (_sync)
        {
            var timestamp = now ?? _timeProvider.GetUtcNow();
            RecomputeAggregatesCore(timestamp);
        }
    }

    private void RecomputeAggregatesCore(DateTimeOffset now)
    {
        var windows = Enum.GetValues<RainAggregateWindow>();
        foreach (var window in windows)
        {
            var cutoff = now - window.Duration();
            var sum = _quantities
                .Where(q => q.When >= cutoff && q.When <= now)
                .Sum(q => q.QuantityMm);
            _aggregates[window] = sum;
        }
    }

    private void PruneHistory(DateTimeOffset now)
    {
        var cutoff = now - RetentionWindow;
        _quantities.RemoveAll(q => q.When < cutoff);
    }
}
