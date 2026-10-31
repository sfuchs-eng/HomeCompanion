using System.Reactive.Subjects;
using HomeCompanion.Base.SignalProcessing;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnitsNet;
using UnitsNet.Units;

namespace HomeCompanion.Tests.Base.SignalProcessing;

[TestFixture]
public class IValueReactiveExtensionsTests
{
    [Test]
    public async Task TimeWeightedAverage_IgnoresEmptyWindows_AndPreservesLastValidValue()
    {
        var source = new Subject<double>();
        var values = new List<double>();

        using var subscription = source
            .TimeWeightedAverage(TimeSpan.FromMilliseconds(80))
            .Subscribe(values.Add);

        source.OnNext(5.0);
        await Task.Delay(120);
        source.OnNext(7.0);
        await Task.Delay(120);

        Assert.That(values, Is.EqualTo(new[] { 5.0, 7.0 }).Within(0.01));
    }

    [Test]
    public void AsObservable_EmitsCurrentValueAndSubsequentChanges()
    {
        var value = new ValueBase<float>(NullLoggerFactory.Instance.CreateLogger<ValueBase<float>>());
        value.Write(1.5f);

        var values = new List<float>();
        using var subscription = value.AsObservable().Subscribe(values.Add);

        Assert.That(values, Is.EqualTo(new[] { 1.5f }));

        value.Write(2.5f);
        Assert.That(values, Is.EqualTo(new[] { 1.5f, 2.5f }));
    }

    [Test]
    public void AsObservable_WithConverter_UsesCurrentValueAndConvertedScalar()
    {
        var value = new ValueBase<Temperature>(NullLoggerFactory.Instance.CreateLogger<ValueBase<Temperature>>());
        value.Write(Temperature.FromDegreesCelsius(5.0));

        var values = new List<double>();
        using var subscription = value.AsObservable(t => t.As(TemperatureUnit.DegreeCelsius)).Subscribe(values.Add);

        Assert.That(values, Is.EqualTo(new[] { 5.0 }).Within(0.0001));

        value.Write(Temperature.FromDegreesCelsius(8.0));
        Assert.That(values.Last(), Is.EqualTo(8.0).Within(0.0001));
    }

    [Test]
    public async Task AsFilteredObservable_UsesWindowedAverageAndHysteresis()
    {
        var value = new ValueBase<float>(NullLoggerFactory.Instance.CreateLogger<ValueBase<float>>());
        value.Write(10.0f);

        var values = new List<float>();
        using var subscription = value
            .AsFilteredObservable(TimeSpan.FromMilliseconds(30), 0.5)
            .Subscribe(values.Add);

        value.Write(10.2f);
        value.Write(11.0f);

        await Task.Delay(150);

        Assert.That(values, Is.Not.Empty);
        Assert.That(values.All(v => double.IsFinite(v) && v >= 10.0f && v <= 11.0f), Is.True);
    }

    [Test]
    public async Task DistinctUntilChangedWithHysteresis_OnlyEmitsWhenThresholdIsCrossed()
    {
        var source = new Subject<double>();
        var values = new List<double>();

        using var subscription = source
            .DistinctUntilChangedWithHysteresis(0.5)
            .Subscribe(values.Add);

        source.OnNext(1.0);
        source.OnNext(1.2);
        source.OnNext(1.6);
        source.OnNext(1.7);
        source.OnNext(1.0);

        await Task.Delay(50);

        Assert.That(values, Is.EqualTo(new[] { 1.0, 1.6, 1.0 }));
    }

    [Test]
    public void ThresholdWithHysteresis_TracksAboveAndBelowThresholdTransitions()
    {
        var source = new Subject<double>();
        var values = new List<bool>();

        using var subscription = source
            .ThresholdWithHysteresis(10.0, 2.0)
            .Subscribe(values.Add);

        source.OnNext(9.0);
        source.OnNext(13.0);
        source.OnNext(9.0);
        source.OnNext(7.0);

        Assert.That(values, Is.EqualTo(new[] { false, true, true, false }));
    }

    [Test]
    public async Task FirstOrderLowPassFilter_Double_SmoothsTransitionsAndStaysWithinBounds()
    {
        var source = new Subject<double>();
        var values = new List<double>();

        using var subscription = source
            .FirstOrderLowPassFilter(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(1))
            .Subscribe(values.Add);

        source.OnNext(10.0);
        source.OnNext(20.0);

        await Task.Delay(50);

        Assert.That(values, Is.Not.Empty);
        Assert.That(values.All(double.IsFinite), Is.True);
        Assert.That(values.All(v => v >= 10.0 && v <= 20.0), Is.True);
    }

    [Test]
    public async Task FirstOrderLowPassFilter_Float_SmoothsTransitionsAndStaysWithinBounds()
    {
        var source = new Subject<float>();
        var values = new List<float>();

        using var subscription = source
            .FirstOrderLowPassFilter(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(1))
            .Subscribe(values.Add);

        source.OnNext(10.0f);
        source.OnNext(20.0f);

        await Task.Delay(50);

        Assert.That(values, Is.Not.Empty);
        Assert.That(values.All(float.IsFinite), Is.True);
        Assert.That(values.All(v => v >= 10.0f && v <= 20.0f), Is.True);
    }
}
