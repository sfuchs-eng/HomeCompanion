using System.Numerics;
using System.Reactive.Linq;

namespace HomeCompanion.Base.SignalProcessing;

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
    public static IObservable<T> AsFilteredObservable<T>(this IValue<T> value, TimeSpan timeWeightedAverageWindow, double hysteresisThreshold) where T : struct, INumber<T>
    {
        return value.AsObservable<T>()
            .TimeWeightedAverage(timeWeightedAverageWindow)
            .DistinctUntilChangedWithHysteresis(hysteresisThreshold);
    }

    public static IObservable<T> AsObservable<T>(this IValue value) where T : struct, INumber<T>
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
        .Select(e => (T)e.EventArgs.NewValue)
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
}