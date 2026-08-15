using System.Reactive.Subjects;
using HomeCompanion.Base.SignalProcessing;

namespace HomeCompanion.Tests;

[TestFixture]
public class FirstOrderLowPassFilterStateTests
{
    [Test]
    public async Task FirstOrderLowPassFilter_with_seeded_state_uses_internal_state_for_first_step()
    {
        var source = new Subject<float>();
        var outputs = new List<float>();
        var states = new List<FirstOrderLowPassFilterStateFloat>();

        var seed = new FirstOrderLowPassFilterStateFloat
        {
            Previous = 20.0f,
            Current = 21.0f,
        };

        using var sub = source
            .FirstOrderLowPassFilter(
                timeConstant: TimeSpan.FromSeconds(2),
                sampleTime: TimeSpan.FromMilliseconds(20),
                initialState: seed,
                onStateChanged: state => states.Add(state))
            .Subscribe(outputs.Add);

        source.OnNext(25.0f);
        await Task.Delay(80);

        Assert.That(outputs, Is.Not.Empty);
        Assert.That(states, Is.Not.Empty);

        var alpha = 1 - Math.Exp(-0.02 / 2.0);
        var expected = (float)(20.0 * (1.0 - alpha) + 25.0 * alpha);

        Assert.That(outputs[0], Is.EqualTo(expected).Within(0.0001));
        Assert.That(states[0].Previous, Is.EqualTo(21.0f).Within(0.0001));
        Assert.That(states[0].Current, Is.EqualTo(expected).Within(0.0001));
    }

    [Test]
    public async Task FirstOrderLowPassFilter_without_seed_initializes_from_first_sample()
    {
        var source = new Subject<float>();
        var outputs = new List<float>();
        var states = new List<FirstOrderLowPassFilterStateFloat>();

        using var sub = source
            .FirstOrderLowPassFilter(
                timeConstant: TimeSpan.FromSeconds(2),
                sampleTime: TimeSpan.FromMilliseconds(20),
                initialState: null,
                onStateChanged: state => states.Add(state))
            .Subscribe(outputs.Add);

        source.OnNext(23.0f);
        await Task.Delay(80);

        Assert.That(outputs, Is.Not.Empty);
        Assert.That(states, Is.Not.Empty);

        Assert.That(outputs[0], Is.EqualTo(23.0f).Within(0.0001));
        Assert.That(states[0].Previous, Is.EqualTo(23.0f).Within(0.0001));
        Assert.That(states[0].Current, Is.EqualTo(23.0f).Within(0.0001));
    }
}
