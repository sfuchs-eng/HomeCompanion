using System.Reactive.Subjects;
using System.Reactive.Linq;
using HomeCompanion.Base.Model;
using HomeCompanion.Events;
using HomeCompanion.Logics.Shutters;
using HomeCompanion.Logics.Shutters.AutoShadow;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.Logics.Shutters;

[TestFixture]
public class EnvironmentalsEvaluatorLogicTests
{
    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }

    private sealed class RecordingEventBus : IEventPublisher, IEventSubscriber
    {
        private readonly Dictionary<Type, List<Func<IEvent, CancellationToken, ValueTask>>> handlersByType = [];

        public List<IEvent> PublishedEvents { get; } = [];

        public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
        {
            if (!handlersByType.TryGetValue(typeof(T), out var handlers))
            {
                handlers = [];
                handlersByType[typeof(T)] = handlers;
            }

            handlers.Add((evt, ct) => handler.HandleAsync((T)evt, ct));
        }

        public void Subscribe<T>(EventHandlerDelegate<T> handler) where T : IEvent
        {
            if (!handlersByType.TryGetValue(typeof(T), out var handlers))
            {
                handlers = [];
                handlersByType[typeof(T)] = handlers;
            }

            handlers.Add((evt, ct) => handler((T)evt, ct));
        }

        public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(@event);
            return DispatchAsync(@event, cancellationToken);
        }

        public void Publish(IEvent @event)
        {
            PublishedEvents.Add(@event);
            DispatchAsync(@event, CancellationToken.None).GetAwaiter().GetResult();
        }

        public void Clear()
        {
            PublishedEvents.Clear();
        }

        private async ValueTask DispatchAsync(IEvent @event, CancellationToken cancellationToken)
        {
            Type? eventType = @event.GetType();
            while (eventType is not null && eventType != typeof(object))
            {
                if (handlersByType.TryGetValue(eventType, out var handlers))
                {
                    foreach (var handler in handlers)
                    {
                        await handler(@event, cancellationToken);
                    }
                }
                eventType = eventType.BaseType;
            }
        }
    }

    private sealed class SutContext(
        EnvironmentalsEvaluatorLogic sut,
        ShadowingSpecial special,
        Dictionary<string, IValue> values,
        FakeTimeProvider timeProvider,
        RecordingEventBus eventBus)
    {
        public EnvironmentalsEvaluatorLogic Sut { get; } = sut;
        public ShadowingSpecial Special { get; } = special;
        public Dictionary<string, IValue> Values { get; } = values;
        public FakeTimeProvider TimeProvider { get; } = timeProvider;
        public RecordingEventBus EventBus { get; } = eventBus;
    }

    [Test]
    public async Task GetSunIntensityObservable_ThreeDirectionalSensors_UsesNormalizedRms()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.SunIntensityHysteresisDuration = TimeSpan.FromMilliseconds(30);
            cfg.SunIntensityPUNorm = 1.0f;
        });

        GetFloatValue(ctx.Values, "Float:SunIntensityEast").Write(1.0f);
        GetFloatValue(ctx.Values, "Float:SunIntensitySouth").Write(0.0f);
        GetFloatValue(ctx.Values, "Float:SunIntensityWest").Write(0.0f);

        var value = await WaitForNextValueAsync(ctx.Sut.GetSunIntensityObservable(ctx.Special), TimeSpan.FromSeconds(1));
        var expected = (float)Math.Sqrt(2.0 / 3.0);

        Assert.That(value, Is.EqualTo(expected).Within(0.01f));
    }

    [Test]
    public async Task GetSunIntensityObservable_MissingEast_UsesFirstAvailableDirectionalSensor()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.SunIntensityEastReference = null;
            cfg.SunIntensityHysteresisDuration = TimeSpan.FromMilliseconds(30);
            cfg.SunIntensityPUNorm = 1.0f;
        });

        GetFloatValue(ctx.Values, "Float:SunIntensitySouth").Write(0.25f);
        GetFloatValue(ctx.Values, "Float:SunIntensityWest").Write(0.9f);

        var value = await WaitForNextValueAsync(ctx.Sut.GetSunIntensityObservable(ctx.Special), TimeSpan.FromSeconds(1));

        Assert.That(value, Is.EqualTo(0.25f).Within(0.01f));
    }

    [Test]
    public async Task GetSunIntensityObservable_NoDirectionalSensors_UsesUvFallback()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.SunIntensityEastReference = null;
            cfg.SunIntensitySouthReference = null;
            cfg.SunIntensityWestReference = null;
            cfg.UvIntensityReference = "Float:UvIntensity";
            cfg.SunIntensityHysteresisDuration = TimeSpan.FromMilliseconds(30);
            cfg.UvIntensityPUNorm = 2.0f;
        });

        GetFloatValue(ctx.Values, "Float:UvIntensity").Write(1.0f);

        var value = await WaitForNextValueAsync(ctx.Sut.GetSunIntensityObservable(ctx.Special), TimeSpan.FromSeconds(1));

        Assert.That(value, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public async Task GetSunIntensityObservable_NoDirectionalAndNoUv_FallsBackToZero()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.SunIntensityEastReference = null;
            cfg.SunIntensitySouthReference = null;
            cfg.SunIntensityWestReference = null;
            cfg.UvIntensityReference = null;
            cfg.SunIntensityHysteresisDuration = TimeSpan.FromMilliseconds(30);
        });

        var value = await WaitForNextValueAsync(ctx.Sut.GetSunIntensityObservable(ctx.Special), TimeSpan.FromSeconds(1));

        Assert.That(value, Is.EqualTo(0.0f));
    }

    [Test]
    public async Task GetSunIntensityAboveThresholdObservable_ActivatesImmediately_AndDeactivatesAfterDelay()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.SunIntensityHysteresisDuration = TimeSpan.FromMilliseconds(100);
            cfg.SunIntensityShadowThresholdPU = 0.2f;
            cfg.SunIntensityRelaxationThresholdPU = 0.1f;
        });

        var source = new Subject<float>();
        var emissions = new List<(bool Value, DateTimeOffset Timestamp)>();
        using var subscription = ctx.Sut.GetSunIntensityAboveThresholdObservable(source, ctx.Special)
            .Subscribe(value => emissions.Add((value, DateTimeOffset.UtcNow)));

        source.OnNext(0.25f);
        await WaitUntilAsync(() => emissions.Count >= 1, TimeSpan.FromMilliseconds(80));
        Assert.That(emissions[0].Value, Is.True);

        source.OnNext(0.0f);
        await Task.Delay(TimeSpan.FromMilliseconds(60));
        Assert.That(emissions.Count(e => e.Value == false), Is.EqualTo(0));

        await WaitUntilAsync(() => emissions.Count >= 2, TimeSpan.FromMilliseconds(250));
        Assert.That(emissions[^1].Value, Is.False);
    }

    [Test]
    public async Task GetSunIntensityAboveThresholdObservable_CancelsPendingFalse_WhenBrightReturns()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.SunIntensityHysteresisDuration = TimeSpan.FromMilliseconds(100);
            cfg.SunIntensityShadowThresholdPU = 0.2f;
            cfg.SunIntensityRelaxationThresholdPU = 0.1f;
        });

        var source = new Subject<float>();
        var emissions = new List<bool>();
        using var subscription = ctx.Sut.GetSunIntensityAboveThresholdObservable(source, ctx.Special)
            .Subscribe(emissions.Add);

        source.OnNext(0.25f);
        await WaitUntilAsync(() => emissions.Count >= 1, TimeSpan.FromMilliseconds(80));

        source.OnNext(0.0f);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        source.OnNext(0.25f);

        await Task.Delay(TimeSpan.FromMilliseconds(130));

        Assert.That(emissions.Any(value => value == false), Is.False);
    }

    [Test]
    public async Task GetUvIntensityAboveThresholdObservable_WhenSunAboveHorizon_ActivatesImmediately()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.UvIntensityThresholdPU = 0.2f;
            cfg.UvIntensityRelaxationThresholdPU = 0.1f;
            cfg.UvIntensityHysteresisDuration = TimeSpan.FromMilliseconds(100);
        });

        var uvSource = new Subject<float>();
        var sunAboveHorizonSource = new Subject<bool>();
        var emissions = new List<bool>();

        using var subscription = ctx.Sut.GetUvIntensityAboveThresholdObservable(uvSource, sunAboveHorizonSource, ctx.Special)
            .Subscribe(emissions.Add);

        sunAboveHorizonSource.OnNext(true);
        uvSource.OnNext(0.3f);

        await WaitUntilAsync(() => emissions.Count >= 1, TimeSpan.FromMilliseconds(120));

        Assert.That(emissions[^1], Is.True);
    }

    [Test]
    public async Task GetUvIntensityAboveThresholdObservable_WhenSunSets_EmitsFalseWithoutNewUvSample()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.UvIntensityThresholdPU = 0.2f;
            cfg.UvIntensityRelaxationThresholdPU = 0.1f;
            cfg.UvIntensityHysteresisDuration = TimeSpan.FromMilliseconds(100);
        });

        var uvSource = new Subject<float>();
        var sunAboveHorizonSource = new Subject<bool>();
        var emissions = new List<bool>();

        using var subscription = ctx.Sut.GetUvIntensityAboveThresholdObservable(uvSource, sunAboveHorizonSource, ctx.Special)
            .Subscribe(emissions.Add);

        sunAboveHorizonSource.OnNext(true);
        uvSource.OnNext(0.3f);
        await WaitUntilAsync(() => emissions.Count >= 1, TimeSpan.FromMilliseconds(120));
        Assert.That(emissions[^1], Is.True);

        sunAboveHorizonSource.OnNext(false);
        await WaitUntilAsync(() => emissions.Count >= 2, TimeSpan.FromMilliseconds(120));

        Assert.That(emissions[^1], Is.False);
    }

    [Test]
    public async Task GetSunPositionObservable_WithMissingInputs_EmitsDefaultFallbackVector()
    {
        const double defaultAzimuth = 0.0;
        const double defaultElevation = -10.0;

        var missingAzimuthCtx = CreateContext(cfg =>
        {
            cfg.SunPositionAzimuthReference = null;
            cfg.SunPositionElevationReference = "Float:SunPositionElevation";
        });

        var vectorMissingAzimuth = await WaitForNextValueAsync(
            missingAzimuthCtx.Sut.GetSunPositionObservable(missingAzimuthCtx.Special),
            TimeSpan.FromMilliseconds(300)
        );

        Assert.Multiple(() =>
        {
            Assert.That(vectorMissingAzimuth.Azimuth, Is.EqualTo(defaultAzimuth).Within(0.0001));
            Assert.That(vectorMissingAzimuth.Elevation, Is.Not.EqualTo(defaultElevation).Within(0.0001));
        });

        var missingElevationCtx = CreateContext(cfg =>
        {
            cfg.SunPositionAzimuthReference = "Float:SunPositionAzimuth";
            cfg.SunPositionElevationReference = null;
        });

        var vectorMissingElevation = await WaitForNextValueAsync(
            missingElevationCtx.Sut.GetSunPositionObservable(missingElevationCtx.Special),
            TimeSpan.FromMilliseconds(300)
        );

        Assert.Multiple(() =>
        {
            Assert.That(vectorMissingElevation.Azimuth, Is.Not.EqualTo(defaultAzimuth).Within(0.0001));
            Assert.That(vectorMissingElevation.Elevation, Is.EqualTo(defaultElevation).Within(0.0001));
        });

        var allMissingCtx = CreateContext(cfg =>
        {
            cfg.SunPositionAzimuthReference = null;
            cfg.SunPositionElevationReference = null;
        });

        var vectorAllMissing = await WaitForNextValueAsync(
            allMissingCtx.Sut.GetSunPositionObservable(allMissingCtx.Special),
            TimeSpan.FromMilliseconds(300)
        );

        Assert.Multiple(() =>
        {
            Assert.That(vectorAllMissing.Azimuth, Is.EqualTo(defaultAzimuth).Within(0.0001));
            Assert.That(vectorAllMissing.Elevation, Is.EqualTo(defaultElevation).Within(0.0001));
        });
    }

    [Test]
    public async Task GetSunPositionObservable_WithBothInputs_EmitsConfiguredVector()
    {
        var ctx = CreateContext();

        var azimuth = GetFloatValue(ctx.Values, "Float:SunPositionAzimuth");
        var elevation = GetFloatValue(ctx.Values, "Float:SunPositionElevation");

        azimuth.Write(1.23f);
        elevation.Write(0.45f);

        var vector = await WaitForNextValueAsync(ctx.Sut.GetSunPositionObservable(ctx.Special), TimeSpan.FromMilliseconds(250));

        Assert.Multiple(() =>
        {
            Assert.That(vector.Azimuth, Is.EqualTo(1.23).Within(0.0001));
            Assert.That(vector.Elevation, Is.EqualTo(0.45).Within(0.0001));
        });
    }

    [Test]
    public async Task GetEnergyBalanceObservable_UsesOutdoorTemperatureAndScaling()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.DefaultRoomTemperatureTarget = 22.0;
            cfg.EnergyBalanceTemperatureScalingFactor = 0.5;
        });

        GetFloatValue(ctx.Values, "Float:OutdoorTemperature").Write(18.0f);

        var value = await WaitForNextValueAsync(
            ctx.Sut.GetEnergyBalanceObservable(ctx.Special, TimeSpan.FromMilliseconds(60)),
            TimeSpan.FromMilliseconds(500)
        );

        Assert.That(value, Is.EqualTo(2.0).Within(0.05));
    }

    [Test]
    public async Task GetEnergyBalanceObservable_WithoutOutdoorTemperatureReference_UsesFallbackTemperature()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.OutdoorTemperatureReference = null;
            cfg.DefaultRoomTemperatureTarget = 20.0;
            cfg.EnergyBalanceTemperatureScalingFactor = 0.25;
        });

        var value = await WaitForNextValueAsync(
            ctx.Sut.GetEnergyBalanceObservable(ctx.Special, TimeSpan.FromMilliseconds(60)),
            TimeSpan.FromMilliseconds(500)
        );

        Assert.That(value, Is.EqualTo(2.5).Within(0.05));
    }

    [Test]
    public async Task InitializeAsyncLatched_EnablesLogic()
    {
        var ctx = CreateContext();

        await ctx.Sut.InitializeAsync();

        Assert.That(ctx.Sut.IsEnabled, Is.True);
    }

    [Test]
    public async Task InitializeAsyncLatched_WhenDisabled_DoesNotPublishTriggers()
    {
        var ctx = CreateContext(cfg =>
        {
            cfg.SunIntensityHysteresisDuration = TimeSpan.FromMilliseconds(50);
        });

        await ctx.Sut.InitializeAsync();
        await ctx.Sut.DisableAsync();
        ctx.EventBus.Clear();

        var azimuth = GetFloatValue(ctx.Values, "Float:SunPositionAzimuth");
        azimuth.Write(azimuth.Value + 10.0f);
        await Task.Delay(TimeSpan.FromMilliseconds(120));

        Assert.That(
            ctx.EventBus.PublishedEvents.OfType<ShutterAutomationComputationTriggerEvent>().Any(),
            Is.False
        );
    }

    private static SutContext CreateContext(Action<CfgShadowingSpecial>? configure = null)
    {
        var modelConfig = ShutterAutomationTestFixture.CreateDefaultModelConfiguration();
        var shadowingConfig = modelConfig.Buildings["TestBuilding1"].Specials["DefaultShading"] as CfgShadowingSpecial
            ?? throw new InvalidOperationException("DefaultShading is not configured as CfgShadowingSpecial.");
        configure?.Invoke(shadowingConfig);

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero));

        var values = new Dictionary<string, IValue>();
        ShutterAutomationTestFixture.BuildTestModel(
            modelConfig,
            out var model,
            timeProvider,
            values,
            out var generatedValues
        );

        foreach (var generated in generatedValues)
        {
            values[generated.Key] = generated.Value;
        }

        var special = model.GetAllSpecials<ShadowingSpecial>().Single();
        var eventBus = new RecordingEventBus();
        var sut = new EnvironmentalsEvaluatorLogic(
            eventBus,
            new StubModelProvider(model),
            timeProvider,
            NullLoggerFactory.Instance.CreateLogger<EnvironmentalsEvaluatorLogic>()
        );

        return new SutContext(sut, special, values, timeProvider, eventBus);
    }

    private static ValueBase<float> GetFloatValue(IReadOnlyDictionary<string, IValue> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Missing expected test value reference: {key}");
        }

        return value as ValueBase<float>
            ?? throw new InvalidOperationException($"Value reference {key} is not a ValueBase<float>.");
    }

    private static async Task<T> WaitForNextValueAsync<T>(IObservable<T> observable, TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? subscription = null;
        subscription = observable.Subscribe(
            value =>
            {
                completion.TrySetResult(value);
                subscription?.Dispose();
            },
            ex => completion.TrySetException(ex)
        );

        var completed = await Task.WhenAny(completion.Task, Task.Delay(timeout));
        if (completed != completion.Task)
        {
            subscription.Dispose();
            throw new TimeoutException($"No observable value arrived within {timeout}.");
        }

        return await completion.Task;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        throw new TimeoutException($"Condition was not met within {timeout}.");
    }
}