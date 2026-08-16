using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;

namespace HomeCompanion.Base.SignalProcessing;

public sealed record FirstOrderLowPassFilterStateDouble
{
    public int Version { get; init; } = 1;
    public double Previous { get; init; } = double.NaN;
    public double Current { get; init; } = double.NaN;
}


public sealed record FirstOrderLowPassFilterStateFloat
{
    public int Version { get; init; } = 1;
    public float Previous { get; init; } = float.NaN;
    public float Current { get; init; } = float.NaN;
}

/// <summary>
/// <para>IValue helpers to convert IValue to IObservable&lt;T&gt; using System.Reactive.</para>
/// <para>See https://github.com/dotnet/reactive</para>
/// <para>Contains also further signal processing convenience methods for IObservable&lt;T&gt; such as time-weighted average and hysteresis filtering.</para>
/// </summary>
public static class IValueReactiveExtensions
{
    /// <summary>
    /// Converts the IValue to an IObservable&lt;T&gt; that applies a time-weighted average and hysteresis filtering.
    /// E.g. for a light intensity sensor, this can be used to smooth out the readings and avoid triggering logic or actuators for insignificant changes.
    /// </summary>
    public static IObservable<T> AsFilteredObservable<T>(this IValue<T> value, TimeSpan timeWeightedAverageWindow, double hysteresisThreshold) where T : struct, INumber<T>, IConvertible
    {
        return value.AsObservable<T>()
            .TimeWeightedAverage(timeWeightedAverageWindow)
            .DistinctUntilChangedWithHysteresis(hysteresisThreshold);
    }

    public static IObservable<T> AsObservable<T>(this IValue value) where T : struct, INumber<T>, IConvertible
    {
        if (value is not IValue<T> typedValue)
        {
            throw new InvalidOperationException($"Cannot convert IValue of type {value.GetType().Name} to IObservable<{typeof(T).Name}>. The value is not of the expected type.");
        }

        if (((T?)value.OValue) is null)
        {
            throw new InvalidOperationException($"Cannot convert IValue of type {value.GetType().Name} to IObservable<{typeof(T).Name}>. The value is null.");
        }

        return Observable.FromEventPattern<ValueChangedEventArgs>(
            h => value.Changed += h,
            h => value.Changed -= h
        )
        // Extract the value and cast it to the expected type
        .Select(e => (T)(e.EventArgs.NewValue.GetNumericValue<T>() ?? throw new InvalidOperationException($"Cannot convert IValue of type {value.GetType().Name} to IObservable<{typeof(T).Name}>. The value is null.")))
        .StartWith((T)value.OValue); // Ensure the stream starts with current state
    }

    public static IObservable<T> TimeWeightedAverage<T>(this IObservable<T> source, TimeSpan window) where T : struct, INumber<T>
    {
        return source
            .Buffer(window)
            .Select(values =>
            {
                if (values.Count == 0) return default(T);

                // Calculate the time-weighted average
                double totalWeight = values.Count;
                double weightedSum = values.Sum(v => Convert.ToDouble(v));
                return (T)Convert.ChangeType(weightedSum / totalWeight, typeof(T));
            });
    }

    public static IObservable<T> DistinctUntilChangedWithHysteresis<T>(this IObservable<T> source, double threshold) where T : struct, INumber<T>
    {
        return source
            .DistinctUntilChanged()
            .Scan((previous: default(T), current: default(T)), (acc, current) =>
            {
                if (acc.previous.Equals(default(T)) || Math.Abs(Convert.ToDouble(current) - Convert.ToDouble(acc.previous)) > threshold)
                {
                    return (previous: acc.current, current: current);
                }
                return acc;
            })
            .Select(acc => acc.current);
    }

    /// <summary>
    /// Yields true if the value has gone above the threshold, and false if it has gone below the threshold, with a hysteresis of the given amount
    /// </summary>
    public static IObservable<bool> ThresholdWithHysteresis<T>(this IObservable<T> source, double threshold, double hysteresis) where T : struct, INumber<T>
    {
        bool? lastState = null;
        return source.Select(value =>
        {
            double numericValue = Convert.ToDouble(value);
            bool currentState;

            if (lastState == null)
            {
                currentState = numericValue > threshold;
            }
            else if (lastState == true)
            {
                currentState = numericValue > (threshold - hysteresis);
            }
            else
            {
                currentState = numericValue > (threshold + hysteresis);
            }

            lastState = currentState;
            return currentState;
        });
    }

    public static IObservable<double> FirstOrderLowPassFilter(this IObservable<double> source, TimeSpan timeConstant, TimeSpan sampleTime)
    {
        return source.FirstOrderLowPassFilter(timeConstant, sampleTime, null, null);
    }

    public static IObservable<double> FirstOrderLowPassFilter(
        this IObservable<double> source,
        TimeSpan timeConstant,
        TimeSpan sampleTime,
        FirstOrderLowPassFilterStateDouble? initialState,
        Action<FirstOrderLowPassFilterStateDouble>? onStateChanged = null)
    {
        if (timeConstant <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeConstant), "Time constant must be greater than zero.");
        }

        var dt = sampleTime.TotalSeconds;
        var alpha = 1 - Math.Exp(-dt / timeConstant.TotalSeconds);

        var seed = initialState ?? new FirstOrderLowPassFilterStateDouble();

        return source
            .Sample(sampleTime)
            .Scan(seed, (state, current) =>
                {
                    if (double.IsNaN(state.Previous) || double.IsNaN(state.Current))
                    {
                        return state with { Previous = current, Current = current };
                    }
                    var filteredValue = state.Previous * (1.0 - alpha) + current * alpha;
                    return state with { Previous = state.Current, Current = filteredValue };
                })
            .Do(state => onStateChanged?.Invoke(state))
            .Select(state => state.Current);
    }

    public static IObservable<float> FirstOrderLowPassFilter(this IObservable<float> source, TimeSpan timeConstant, TimeSpan sampleTime)
    {
        return source.FirstOrderLowPassFilter(timeConstant, sampleTime, null, null);
    }

    public static IObservable<float> FirstOrderLowPassFilter(
        this IObservable<float> source,
        TimeSpan timeConstant,
        TimeSpan sampleTime,
        FirstOrderLowPassFilterStateFloat? initialState,
        Action<FirstOrderLowPassFilterStateFloat>? onStateChanged = null)
    {
        if (timeConstant <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeConstant), "Time constant must be greater than zero.");
        }

        var dt = sampleTime.TotalSeconds;
        var alpha = 1 - Math.Exp(-dt / timeConstant.TotalSeconds);

        var seed = initialState ?? new FirstOrderLowPassFilterStateFloat();

        return source
            .Sample(sampleTime)
            .Scan(seed, (state, current) =>
                {
                    if (float.IsNaN(state.Previous) || float.IsNaN(state.Current))
                    {
                        return state with { Previous = current, Current = current };
                    }
                    var filteredValue = (float)(state.Previous * (1.0 - alpha) + current * alpha);
                    return state with { Previous = state.Current, Current = filteredValue };
                })
            .Do(state => onStateChanged?.Invoke(state))
            .Select(state => state.Current);
    }
}