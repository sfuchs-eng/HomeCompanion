using HomeCompanion.Base.Model;
using HomeCompanion.Base.Quartz;
using HomeCompanion.Logics;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Quartz;
using UnitsNet;

namespace HomeCompanion.Logics.Garden;

public sealed class RainSensorLogic(
    ISchedulerFactory schedulerFactory,
    IModelProvider modelProvider,
    IRainService rainService,
    TimeProvider timeProvider,
    ILogger<RainSensorLogic> logger) : LogicBase(logger)
{
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly IModelProvider _modelProvider = modelProvider;
    private readonly IRainService _rainService = rainService;
    private readonly TimeProvider _timeProvider = timeProvider;
    private RainSensor? _rainSensor;

    protected override async Task InitializeLatchedAsync(CancellationToken cancellationToken = default)
    {
        _rainSensor = _modelProvider.GetModel().GetUniqueSpecial<RainSensor>();

        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = typeof(RainAggregateRefreshJob).GetJobKeyFromType()
            ?? throw new InvalidOperationException($"Could not resolve Quartz job key for {nameof(RainAggregateRefreshJob)}.");

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{nameof(RainAggregateRefreshJob)}_Hourly", jobKey.Group)
            .ForJob(jobKey)
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(trigger, cancellationToken);

        _rainSensor.RainInput?.Changed += OnRainInputChanged;
        PublishAggregates();
    }

    private void OnRainInputChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_rainSensor?.RainInput is null)
            return;

        var rainMm = _rainSensor.RainInput.Value.Millimeters;
        _rainService.ProcessRainTick(rainMm, _timeProvider.GetUtcNow());
        PublishAggregates();
    }

    private void PublishAggregates()
    {
        if (_rainSensor is null)
            return;

        var aggregates = _rainService.RollingAggregates;

        _rainSensor.RainActual24h?.Write(Length.FromMillimeters(aggregates.GetValueOrDefault(RainAggregateWindow.Hours24)));
        _rainSensor.RainActual48h?.Write(Length.FromMillimeters(aggregates.GetValueOrDefault(RainAggregateWindow.Hours48)));
        _rainSensor.RainActual72h?.Write(Length.FromMillimeters(aggregates.GetValueOrDefault(RainAggregateWindow.Hours72)));
        _rainSensor.RainActual1w?.Write(Length.FromMillimeters(aggregates.GetValueOrDefault(RainAggregateWindow.Days7)));
        _rainSensor.RainActual2w?.Write(Length.FromMillimeters(aggregates.GetValueOrDefault(RainAggregateWindow.Days14)));
    }
}
