using HomeCompanion.Diagnostics;
using HomeCompanion.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

/// <summary>
/// Follows environmental measurements and aggregates them to intermediate results for the shutter target evaluation logic.
/// Normally implemented as a singleton <see cref="ILogic"/> that is registered in the DI container and can be injected into other logics that need to access environmental measurements.
/// Standard implementation is <see cref="EnvironmentalsEvaluatorLogic"/>.
/// </summary>
/// <remarks>
/// It's a current state representation of event based environmental measurements, e.g. sun intensity, UV intensity, sun position, outdoor temperature, etc.
/// processed such that it can be used by the shutter target evaluation logic to determine the appropriate shutter target position.
/// It encapsulates actual measurements and may provide forecasts.
/// </remarks>
public interface IEnvironmentalsProvider
{
    double OutdoorTemperature { get; }

    /// <summary>
    /// Sun intensity in p.u. (per unit) scaled to the configured reference value of the <see cref="CfgShadowingSpecial"/>.
    /// The reference value is typically the maximum expected sun intensity for the location and sensor type.
    /// It's configured in the <see cref="CfgShadowingSpecial"/> as <see cref="CfgShadowingSpecial.SunIntensityPUNorm"/>.
    /// </summary>
    double SunIntensityPU { get; }

    /// <summary>
    /// Time &amp; value threshold based hysteresis evaluation of <see cref="SunIntensityPU"/>.
    /// <para>
    /// Rationale:
    /// <list type="bullet">
    /// <item>True: immediately when sun intensity is above threshold</item>
    /// <item>False: if the intensity remained below hysteresis threshold for the specified time</item>
    /// </list>
    /// </para>
    /// </summary>
    bool SunIntensityAboveThreshold { get; }

    bool IsSunAboveHorizon { get; }

    /// <summary>
    /// Ultraviolet (UV) intensity in p.u. (per unit) scaled to the configured reference value of the <see cref="CfgShadowingSpecial"/>.
    /// The reference value is typically the maximum expected UV intensity for the location and sensor type.
    /// It's configured in the <see cref="CfgShadowingSpecial"/> as <see cref="CfgShadowingSpecial.UvIntensityPUNorm"/>.
    /// </summary>
    double UvIntensityPU { get; }

    bool UvIntensityAboveThreshold { get; }

    /// <summary>
    /// Gets the daily net energy balance in p.u. (per unit). The p.u. normalization must be chosen to land a suitable range for shadowing thresholds and decisions.
    /// This value is computed from the daily average temperature difference between indoor target and outdoor actual temperature.
    /// </summary>
    /// <param name="index">Optional index to specify the day (e.g., 0 for today, -1 for past, 1 for forecast).</param>
    /// <returns>The estimated daily net energy balance in p.u. for the specified day, ignoring shutter/sunlight impact, or null if not available.</returns>
    double EnergyBalancePU24hActual { get; }

    /// <summary>
    /// Indicates whether the cautious shadowing energy balance limit has been exceeded and shutters should be closed to prevent excessive heating.
    /// </summary>
    /// <value><c>true</c> if the limit has been exceeded; otherwise, <c>false</c>.</value>
    bool CautiousShadowingEnergyBalanceLimitExceeded { get; }

    SphericVector SunPosition { get; }
}

/// <summary>
/// Follows environmental measurements and aggregates them to intermediate results for the shutter target evaluation logic.
/// It encapsulates actual measurements and my provide forecasts.
/// It is responsible to ensure that shutter target evaluation logic is not called too often, e.g. by providing a minimum time interval between evaluations, yet whenever appropriate it is triggered.
/// </summary>
/// <typeparam name="EnvironmentalsEvaluatorLogic"></typeparam>
public class EnvironmentalsEvaluatorLogic : LogicBase, IEnvironmentalsProvider, IDisposable, IDiagnosable
{
    private readonly IModelProvider modelProvider;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<EnvironmentalsEvaluatorLogic> logger;
    private readonly IEventPublisher eventPublisher;

    public EnvironmentalsEvaluatorLogic(
        IEventPublisher eventPublisher,
        IModelProvider modelProvider,
        TimeProvider timeProvider,
        ILogger<EnvironmentalsEvaluatorLogic> logger
    ) : base(logger)
    {
        this.eventPublisher = eventPublisher;
        this.modelProvider = modelProvider;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public double OutdoorTemperature { get; private set; } = double.NaN;

    /// <summary>
    /// Sun intensity in p.u. (per unit) scaled to the configured reference value of the <see cref="ShadowingSpecial"/>.
    /// The reference value is typically the maximum expected sun intensity for the location and sensor type.
    /// It's configured in the <see cref="ShadowingSpecial"/> as <see cref="ShadowingSpecial.SunIntensityPUNorm"/>.
    /// </summary>
    public double SunIntensityPU { get; private set; } = double.NaN;

    /// <summary>
    /// Time &amp; value threshold based hysteresis evaluation of <see cref="SunIntensityPU"/>.
    /// <para>
    /// Rationale:
    /// <list type="bullet">
    /// <item>True: immediately when sun intensity is above threshold</item>
    /// <item>False: if the intensity remained below hysteresis threshold for the specified time</item>
    /// </list>
    /// </para>
    /// </summary>
    public bool SunIntensityAboveThreshold { get; private set; } = false;
    public double UvIntensityPU { get; private set; } = double.NaN;
    public SphericVector SunPosition { get; private set; } = new(0.0, -10.0); // below horizon by default

    public double EnergyBalancePU24hActual { get; private set; } = 0.0;
    public bool CautiousShadowingEnergyBalanceLimitExceeded { get; private set; } = false;

    public bool UvIntensityAboveThreshold { get; private set; } = false;

    public bool IsSunAboveHorizon { get; private set; } = false;

    TimeSpan temperatureAveragingWindow = TimeSpan.FromMinutes(15);
    float temperatureHysteresis = 0.5f;

    protected override Task<DiagnosticResultNode> PopulateDiagnosticResultsAsync(DiagnosticResultNode parentNode, CancellationToken cancellationToken)
    {
        var node = parentNode.AddChild("EnvironmentalsEvaluatorLogic");
        node.Records = [
            OutdoorTemperature.AsDiagnosticRecord("OutdoorTemperature", (v) => $"{v:F1} °C"),
            SunIntensityPU.AsDiagnosticRecord("SunIntensityPU", (v) => $"{v:F3} p.u."),
            SunIntensityAboveThreshold.AsDiagnosticRecord("SunIntensityAboveThreshold", (v) => v.ToString()),
            UvIntensityPU.AsDiagnosticRecord("UvIntensityPU", (v) => $"{v:F3} p.u."),
            UvIntensityAboveThreshold.AsDiagnosticRecord("UvIntensityAboveThreshold", (v) => v.ToString()),
            SunPosition.AsDiagnosticRecord("SunPosition", (v) => v.ToString()),
            IsSunAboveHorizon.AsDiagnosticRecord("IsSunAboveHorizon", (v) => v.ToString()),
            EnergyBalancePU24hActual.AsDiagnosticRecord("EnergyBalancePU24hActual", (v) => $"{v:F3} p.u."),
            CautiousShadowingEnergyBalanceLimitExceeded.AsDiagnosticRecord("CautiousShadowingEnergyBalanceLimitExceeded", (v) => v.ToString())
        ];
        return Task.FromResult(node);
    }

    List<IDisposable> subscriptions = [];

    protected void RegisterSubscriptions(IDisposable? subscription)
    {
        if (subscription is null)
        {
            return;
        }
        subscriptions.Add(subscription);
    }

    internal IObservable<bool> GetSunAboveHorizonObservable(IObservable<SphericVector> sunPosition, ShadowingSpecial s)
    {
        return sunPosition.Select(sv => sv.Elevation > 0.0)
            .DistinctUntilChanged();
    }

    internal IObservable<float> GetSunIntensityObservable(ShadowingSpecial s)
    {
        IObservable<float> sunIntensityObs;

        // 3 directional sun intensity sensors are preferred
        if (s.SunIntensityEast is not null && s.SunIntensitySouth is not null && s.SunIntensityWest is not null)
        {
            sunIntensityObs = Observable.CombineLatest(
                s.SunIntensityEast.AsObservable<float>(),
                s.SunIntensitySouth.AsObservable<float>(),
                s.SunIntensityWest.AsObservable<float>(),
                // use p.u. scaled RMS value of the 3 directional sensors E, S, W as a good approximation of the sun intensity
                (east, south, west) => (float)(Math.Sqrt((east * east + south * south + west * west) / 3.0f) * Math.Sqrt(2.0f) / s.Configuration.SunIntensityPUNorm)
            );
        }
        else
        {
            // if we don't have 3, ...
            var usedSensor = new List<(string, IValue?)>
            {
                ("SunIntensityEast", s.SunIntensityEast),
                ("SunIntensitySouth", s.SunIntensitySouth),
                ("SunIntensityWest", s.SunIntensityWest)
            }.FirstOrDefault(p => p.Item2 is not null);
            if (usedSensor.Item2 is null)
            {
                // if we don't have any, try uv intensity, otherwise fallback to 0.0f
                logger.LogWarning("ShadowingSpecial {ShadowingSpecialKey} does not provide any sun intensity measurement. Sun intensity is routed from UV intensity if available, otherwise it is set to 0.", s.Name);
                sunIntensityObs = s.UvIntensity?.AsObservable<float>().Select(uv => uv / s.Configuration.UvIntensityPUNorm) ?? Observable.Return(0.0f);
            }
            else
            {
                // if we have 1 or 2, use the first available one
                logger.LogTrace("ShadowingSpecial {ShadowingSpecialKey} does not provide all 3 directional sun intensity measurements. Sun intensity is routed from {SensorName}.", s.Name, usedSensor.Item1);
                sunIntensityObs = usedSensor.Item2.AsObservable<float>().Select(v => v / s.Configuration.SunIntensityPUNorm);
            }
        }

        sunIntensityObs = sunIntensityObs
            .TimeWeightedAverage(s.Configuration.SunIntensityHysteresisDuration)
            .DistinctUntilChangedWithHysteresis(0.05);

        return sunIntensityObs;
    }

    internal IObservable<bool> GetSunIntensityAboveThresholdObservable(IObservable<float> sunIntensity, ShadowingSpecial s)
    {
        // activation: immediately when sun intensity is above threshold
        // deactivation: if the intensity remained below hysteresis threshold for the specified time

        return sunIntensity

            // 1. Maintain hysteresis state: 
            // Only flip to TRUE if above upper, only flip to FALSE if below lower.
            .Scan(false, (currentState, intensity) =>
            {
                if (intensity >= s.Configuration.SunIntensityShadowThresholdPU) return true;
                if (intensity < s.Configuration.SunIntensityRelaxationThresholdPU) return false;
                return currentState;
            })
            .DistinctUntilChanged()

            // 2. Handle the "delayed-off" logic
            // We transform the boolean into a stream that dictates the state
            .Select(isBright => isBright
                ? Observable.Return(true)
                : Observable.Return(false).Delay(s.Configuration.SunIntensityHysteresisDuration)
            )

            // 3. Switch cancels any pending 'false' timer if we return to 'true' 
            // before the 30 minutes expire.
            .Switch()
            .DistinctUntilChanged();
    }

    internal IObservable<bool> GetUvIntensityAboveThresholdObservable(IObservable<float> uvIntensity, IObservable<bool> sunAboveHorizon, ShadowingSpecial s)
    {
        // activation: immediately when UV intensity is above threshold
        // deactivation: if the intensity remained below hysteresis threshold for the specified time

        var uvHysteresisState = uvIntensity

            // 1. Maintain hysteresis state:
            // Only flip to TRUE if above upper, only flip to FALSE if below lower.
            .Scan(false, (currentState, intensity) =>
            {
                if (intensity >= s.Configuration.UvIntensityThresholdPU) return true;
                if (intensity < s.Configuration.UvIntensityRelaxationThresholdPU) return false;
                return currentState;
            })
            .DistinctUntilChanged();

        return Observable.CombineLatest(
                uvHysteresisState,
                sunAboveHorizon.StartWith(false),
                (isBright, isSunAboveHorizon) => (isBright, isSunAboveHorizon)
            )

            // 2. Handle the "delayed-off" logic
            // We transform the boolean into a stream that dictates the state; we don't need the delayed-off while the sun is above horizon.
            .Select(sample => sample.isBright
                ? (sample.isSunAboveHorizon ? Observable.Return(true) : Observable.Return(false))
                : (sample.isSunAboveHorizon ? Observable.Return(false) : Observable.Return(false).Delay(s.Configuration.UvIntensityHysteresisDuration))
            )

            // 3. Switch cancels any pending 'false' timer if we return to 'true'
            .Switch()
            .DistinctUntilChanged();
    }

    internal IObservable<SphericVector> GetSunPositionObservable(ShadowingSpecial s)
    {
        var subAziObs = s.SunPositionAzimuth?.AsObservable<float>() ?? Observable.Return(0.0f);
        var subEleObs = s.SunPositionElevation?.AsObservable<float>() ?? Observable.Return(-10.0f);

        var sunObs = Observable.CombineLatest(
            subAziObs,
            subEleObs,
            (azimuth, elevation) => new SphericVector(azimuth, elevation)
        )
            .DistinctUntilChanged(
                new SphericVectorComparer()
                    .SetToleranceDeg(2.0f)
                    .UseSphericEquality() // 2° tolerance for sun position changes
            );

        return sunObs;
    }

    internal IObservable<double> GetEnergyBalanceObservable(ShadowingSpecial s, TimeSpan? averagingWindow = null)
    {
        // Compute the daily net energy balance in p.u. (per unit)
        // This value is computed from the daily average temperature difference between indoor target room temp and outdoor temperature, normalized by a reference value.
        var window = averagingWindow ?? TimeSpan.FromHours(24);

        var energyBalanceObs = (s.OutdoorTemperature?.AsObservable<float>().Select(f => (double)f) ?? Observable.Return(10.0))
            // energy balance = (target room temp - outdoor temp) * scaling factor
            .Select(outdoorTemp => (s.Configuration.DefaultRoomTemperatureTarget - outdoorTemp) * s.Configuration.EnergyBalanceTemperatureScalingFactor)
            // 24h average
            .TimeWeightedAverage(window)
            .DistinctUntilChangedWithHysteresis(0.01);

        return energyBalanceObs;
    }

    protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        var m = modelProvider.GetModel();
        var shadowingSpecial = m.GetAllSpecials<ShadowingSpecial>().SingleOrDefault()
            ?? throw new InvalidOperationException("Code is built to handle exactly one ShadowingSpecial in the model. Found none or more than one.");

        var s = shadowingSpecial;

        // set up reactive pipelines to follow environmental measurements and trigger shutter automation computations as needed.

        RegisterSubscriptions(
            s.OutdoorTemperature?
                .AsFilteredObservable(temperatureAveragingWindow, temperatureHysteresis)
                .Subscribe(temp => { OutdoorTemperature = temp; PublishGlobalShutterAutomationComputationTrigger(s, s.OutdoorTemperature); })
        );

        // Sun brightness / irraditaion / intensity
        IObservable<float> sunIntensityObs = GetSunIntensityObservable(s)
            .Publish() // Hot instead of cold observable, so that multiple subscribers share the same source and don't trigger multiple subscriptions to the underlying observables.
            .RefCount(); // Automatically connects when the first subscriber subscribes and disconnects when the last subscriber unsubscribes.

        RegisterSubscriptions(
            sunIntensityObs
                // needed at all?
                .Subscribe(brightness => { SunIntensityPU = brightness; PublishGlobalShutterAutomationComputationTrigger(s, s.SunIntensitySouth ?? s.SunIntensityEast ?? s.SunIntensityWest); })
        );

        RegisterSubscriptions(
                GetSunIntensityAboveThresholdObservable(sunIntensityObs, s)
                .Subscribe(aboveThreshold => { SunIntensityAboveThreshold = aboveThreshold; PublishGlobalShutterAutomationComputationTrigger(s, s.SunIntensitySouth ?? s.SunIntensityEast ?? s.SunIntensityWest); })
        );

        // sun position
        var sunObs = GetSunPositionObservable(s)
            .Publish() // Hot instead of cold observable, so that multiple subscribers share the same source and don't trigger multiple subscriptions to the underlying observables.
            .RefCount(); // Automatically connects when the first subscriber subscribes and disconnects when the last subscriber unsubscribes.

        RegisterSubscriptions(
            sunObs.Subscribe(sv => { SunPosition = sv; PublishGlobalShutterAutomationComputationTrigger(s, s.SunPositionAzimuth ?? s.SunPositionElevation); })
        );
        RegisterSubscriptions(
            GetSunAboveHorizonObservable(sunObs, s)
                .Subscribe(aboveHorizon => { IsSunAboveHorizon = aboveHorizon; PublishGlobalShutterAutomationComputationTrigger(s, s.SunPositionAzimuth ?? s.SunPositionElevation); })
        );

        // energy balance from daily average indoor target vs. outdoor actual temperature difference
        var energyBalanceObs = GetEnergyBalanceObservable(s)
            .Publish() // Hot instead of cold observable, so that multiple subscribers share the same source and don't trigger multiple subscriptions to the underlying observables.
            .RefCount(); // Automatically connects when the first subscriber subscribes and disconnects when the last subscriber unsubscribes.
        RegisterSubscriptions(
            energyBalanceObs.Subscribe(eb => { EnergyBalancePU24hActual = eb; PublishGlobalShutterAutomationComputationTrigger(s, s.OutdoorTemperature); })
        );
        // the energy balance with threshold on cautious shadowing we're handling here too for global consistency.
        RegisterSubscriptions(
            energyBalanceObs
                // threshold and hysteresis for cautious shadowing: CautiousShadowingEnergyBalanceThresholdPU, CautiousShadowingEnergyBalanceThresholdHysteresisPU
                .Select(eb => eb >= s.Configuration.CautiousShadowingEnergyBalanceThresholdPU
                    ? true
                    : eb <= s.Configuration.CautiousShadowingEnergyBalanceThresholdPU - s.Configuration.CautiousShadowingEnergyBalanceThresholdHysteresisPU
                        ? false
                        : (bool?)null // no change
                )
                .Where(eb => eb.HasValue)
                .Select(eb => eb!.Value)
                .DistinctUntilChanged()
                .Subscribe(eb => { CautiousShadowingEnergyBalanceLimitExceeded = eb; PublishGlobalShutterAutomationComputationTrigger(s, s.OutdoorTemperature); })
        );

        // UV intensity, or if not available, route from sun intensity
        if (s.UvIntensity is not null)
        {
            RegisterSubscriptions(
                s.UvIntensity?
                    .AsFilteredObservable(s.Configuration.SunIntensityHysteresisDuration, 0.05)
                    .Subscribe(uv => { UvIntensityPU = uv; PublishGlobalShutterAutomationComputationTrigger(s, s.UvIntensity); })
            );
        }
        else
        {
            logger.LogTrace("ShadowingSpecial {ShadowingSpecialKey} does not provide UV intensity measurement. UV intensity is routed from sun intensity", s.Name);
            RegisterSubscriptions(
                sunObs.Subscribe(sv => { UvIntensityPU = SunIntensityPU; })
            );
        }

        return Task.CompletedTask;
    }

    private void PublishGlobalShutterAutomationComputationTrigger(ShadowingSpecial s, IValue? v)
    {
        if (!IsEnabled)
        {
            logger.LogTrace("EnvironmentalsEvaluatorLogic is disabled. Not publishing shutter automation computation trigger for ShadowingSpecial {ShadowingSpecialKey}.", s.Name);
            return;
        }
        var model = modelProvider.GetModel();
        var triggerContext = new ShutterAutomationComputationTriggerContext(
            thingKeys: model.EnumerateRoomKeys().Select(rk => (IThingKey)rk)
                .Concat(model.EnumerateShutterKeys().Select(sk => (IThingKey)sk))
                .Concat(model.Buildings.Select(b => new BuildingKey(b.Value) as IThingKey))
                .ToList(),
            scope: ShutterAutomationComputationScope.Global,
            triggeringValue: v is null ? [] : [v],
            valueEventArgs: null,
            timestamp: timeProvider.GetLocalNow(),
            urgency: ShutterAutomationComputationTriggerUrgency.Slow
        );
        var computationTrigger = new ShutterAutomationComputationTriggerEvent()
        {
            Context = triggerContext,
            Timestamp = triggerContext.Timestamp
        };
        eventPublisher.Publish(computationTrigger);
    }

    ~EnvironmentalsEvaluatorLogic()
    {
        Dispose();
    }

    private bool disposedValue = false; // To detect redundant calls

    public void Dispose()
    {
        if (!disposedValue)
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
            subscriptions.Clear();
            disposedValue = true;
            GC.SuppressFinalize(this);
        }
    }
}

public class EnvironmentalsEvaluatorExtension : IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        context.Builder.Services.AddSingleton<IEnvironmentalsProvider>(s => s.GetRequiredService<EnvironmentalsEvaluatorLogic>());
    }
}

/*
// Flux query to look at my wheather station's brightness sensor values. It's an MDT SCN-WS3HW.01 running since 10 years.
// Looks like the RMS value of the 3 directional sensors E, S, W is a reasonably good approximation of the measured brightness yet without directional sensitivity.

import "math"

sensors = from(bucket: "OpenHabItems")
  |> range(start: v.timeRangeStart, stop: v.timeRangeStop)
  |> filter(fn: (r) => 
      r["_measurement"] == "Helligkeit.E" or 
      r["_measurement"] == "Helligkeit.S" or 
      r["_measurement"] == "Helligkeit.W"
  )
  |> aggregateWindow(every: 1h, fn: max, createEmpty: false)
  |> map(fn: (r) => ({ r with
        _value: r._value / 100000.0
  }))

sensRms = sensors
  |> group()
  |> pivot(columnKey: ["item"], rowKey: ["_time"], valueColumn: "_value")
  |> filter(fn: (r) => exists r.Helligkeit_E and exists r.Helligkeit_S and exists r.Helligkeit_W)
  |> map(fn: (r) => ({ r with
      _value: math.sqrt(x: (math.pow(x: r.Helligkeit_E, y: 2.0) + math.pow(x: r.Helligkeit_S, y: 2.0) + math.pow(x: r.Helligkeit_W, y: 2.0)) / 3.0) * math.sqrt(x: 2.0)
  }))

azimuth = from(bucket: "OpenHabItems")
  |> range(start: v.timeRangeStart, stop: v.timeRangeStop)
  |> filter(fn: (r) => 
      r["_measurement"] == "Sun.LocalPosition.Azimuth"
  )
  |> aggregateWindow(every: 1h, fn: max, createEmpty: false)
  |> map(fn: (r) => ({ r with
        _value: (math.cos(x: (r._value - 170.0) / 360.0 * (2.0 * math.pi)) + 1.0 ) / 2.0,
  }))

union(tables: [sensors, azimuth, sensRms])

*/

/*
// same story for the difference between daily average indoor and outdoor temperature.

import "math"

temps = from(bucket: "OpenHabItems")
  |> range(start: v.timeRangeStart, stop: v.timeRangeStop)
  |> filter(fn: (r) => r["_measurement"] == "gAussentempMin" or r["_measurement"] == "Raumtemperatur.OG.Zimmer.III")
  |> aggregateWindow(every: 1d, fn: mean, createEmpty: false)
  |> yield(name: "mean")

tempDiff = temps
  |> group()
  |> pivot(columnKey: ["item"], rowKey: ["_time"], valueColumn: "_value")
  |> filter(fn: (r) => exists r.gAussentempMin and exists r.Raumtemperatur_OG_Zimmer_III)
  |> map(fn: (r) => ({ r with
      _value: r.Raumtemperatur_OG_Zimmer_III - r.gAussentempMin
  }))

tempDiff

*/