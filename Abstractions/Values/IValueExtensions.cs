using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HomeCompanion.Values;

public static class IValueExtensions
{
    public static bool TryWriteNumeric(this IValue targetValue, double value, object? initiator = null, ILogger? logger = null)
    {
        try
        {
            switch (targetValue)
            {
                case IValue<byte> v:
                    v.Write((byte)ClampToRange(value, byte.MinValue, byte.MaxValue), initiator);
                    return true;
                case IValue<sbyte> v:
                    v.Write((sbyte)ClampToRange(value, sbyte.MinValue, sbyte.MaxValue), initiator);
                    return true;
                case IValue<short> v:
                    v.Write((short)ClampToRange(value, short.MinValue, short.MaxValue), initiator);
                    return true;
                case IValue<ushort> v:
                    v.Write((ushort)ClampToRange(value, ushort.MinValue, ushort.MaxValue), initiator);
                    return true;
                case IValue<int> v:
                    v.Write(ClampToRange(value, int.MinValue, int.MaxValue), initiator);
                    return true;
                case IValue<uint> v:
                    v.Write(ClampToUInt(value), initiator);
                    return true;
                case IValue<long> v:
                    v.Write((long)Math.Round(value, MidpointRounding.AwayFromZero), initiator);
                    return true;
                case IValue<ulong> v:
                    v.Write((ulong)Math.Max(0, Math.Round(value, MidpointRounding.AwayFromZero)), initiator);
                    return true;
                case IValue<float> v:
                    v.Write((float)value, initiator);
                    return true;
                case IValue<double> v:
                    v.Write(value, initiator);
                    return true;
                case IValue<decimal> v:
                    v.Write((decimal)value, initiator);
                    return true;
                case IValue<bool> v:
                    v.Write(Math.Abs(value) >= 0.5, initiator);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed writing numeric command to target value {TargetName}.", targetValue.Name);
            return false;
        }
    }

    private static int ClampToRange(double value, int min, int max)
    {
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, min, max);
    }

    private static uint ClampToUInt(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
            return 0;
        if (rounded >= uint.MaxValue)
            return uint.MaxValue;
        return (uint)rounded;
    }

    /// <summary>
     /// Attempts to retrieve the numeric value from an <see cref="IValue"/> if it is of type <see cref="IValue{double}"/>.
     /// </summary>
     /// <param name="value">The value to retrieve the numeric value from.</param>
     /// <param name="numeric">The output parameter that will contain the numeric value if retrieval is successful; otherwise, it will be set to 0.</param>
     /// <returns>True if the value is of type <see cref="IValue{double}"/> and the numeric value was successfully retrieved; otherwise, false.</returns>
    public static bool TryGetNumericValue(this IValue? value, out double numeric)
    {
        if (value is IValue<double> dblValue)
        {
            numeric = dblValue.Value;
            return true;
        }
        if (value is IValue<int> intValue)
        {
            numeric = intValue.Value;
            return true;
        }
        if (value is IValue<float> floatValue)
        {
            numeric = floatValue.Value;
            return true;
        }

        numeric = 0;

        if (value?.OValue is null)
            return false;

        try
        {
            numeric = Convert.ToDouble(value.OValue, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetValue<T>(this IValue? value, out T typedValue, ILogger? logger = null)
    {
        if (value is IValue<T> typed)
        {
            typedValue = typed.Value;
            return true;
        }

        logger?.LogWarning("Value {ValueName} is of type {ActualType}, expected {ExpectedType}.", value?.Name, value?.ValueType.Name, typeof(T).Name);

        typedValue = default!;
        return false;
    }

    public static bool TryGetIntegralValue<T>(this IValue? value, out T integralValue, ILogger? logger = null) where T : struct, IConvertible
    {
        if (value is IValue<T> typed)
        {
            integralValue = typed.Value;
            return true;
        }
        if (value is IValue<int> intValue && typeof(T) == typeof(int))
        {
            integralValue = (T)(object)intValue.Value;
            return true;
        }
        if (value is IValue<uint> uintValue && typeof(T) == typeof(uint))
        {
            integralValue = (T)(object)uintValue.Value;
            return true;
        }
        if (value is IValue<long> longValue && typeof(T) == typeof(long))
        {
            integralValue = (T)(object)longValue.Value;
            return true;
        }
        if (value is IValue<ulong> ulongValue && typeof(T) == typeof(ulong))
        {
            integralValue = (T)(object)ulongValue.Value;
            return true;
        }

        // is integral type conversion possible? Check bounds and return false if value is out of bounds for target type or not a number.
        if (value?.OValue is not null)
        {
            try
            {
                var converted = Convert.ChangeType(value.OValue, typeof(T), CultureInfo.InvariantCulture);
                if (converted is T typedConverted)
                {
                    integralValue = typedConverted;
                    return true;
                }
            }
            catch
            {
                logger?.LogWarning("Value {ValueName} could not be converted to type {TargetType}.", value?.Name, typeof(T).Name);
            }
        }
        else
        {
            logger?.LogWarning("Value {ValueName} has null OValue, cannot convert to type {TargetType}.", value?.Name, typeof(T).Name);
        }

        integralValue = default!;
        return false;
    }

    public static bool TryGetEnumValue<TEnum>(this IValue? value, out TEnum enumValue, ILogger? logger = null) where TEnum : struct, Enum
    {
        // First try to get the value as the expected enum type directly.
        if (value is IValue<TEnum> typed)
        {
            enumValue = typed.Value;
            return true;
        }

        // if that fails, see whether we can parse the enum's underlying type from IValue<EnumUnderlyingType> and convert to the enum type. Use TryGetIntegralValue to handle various integral underlying types.
        if (TryGetIntegralValue(value, out long integral, logger) && Enum.IsDefined(typeof(TEnum), integral))
        {
            enumValue = (TEnum)Enum.ToObject(typeof(TEnum), integral);
            return true;
        }

        // If that fails, see whether we can parse from IValue<string>
        if (value is IValue<string> strValue && Enum.TryParse<TEnum>(strValue.Value, ignoreCase: true, out var parsedEnum))
        {
            enumValue = parsedEnum;
            return true;
        }

        logger?.LogWarning("Value {ValueName} could not be converted to enum type {TargetType}.", value?.Name, typeof(TEnum).Name);
        enumValue = default!;
        return false;
    }
}