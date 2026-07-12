using System.Numerics;

namespace HomeCompanion.Base.Utilities;

public class CachedValue<T>
{
    private T cachedValue;

    private bool isValueCached = false;
    private readonly Func<T> ValueProvider;

    public CachedValue(T dflt, Func<T> valueProvider)
    {
        cachedValue = dflt;
        isValueCached = false;
        ValueProvider = valueProvider;
    }

    public T Value
    {
        get
        {
            if (!isValueCached)
            {
                Value = ValueProvider();
            }
            return cachedValue;
        }
        set
        {
            cachedValue = value;
            isValueCached = true;
        }
    }
    
    public void Invalidate()
    {
        isValueCached = false;
    }
}