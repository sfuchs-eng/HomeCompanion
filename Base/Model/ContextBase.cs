using System.Diagnostics.CodeAnalysis;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Base.Model;

public abstract class ContextBase<TKey>(TKey key) : IThingContext, IThingKey where TKey : IThingKey
{
    public TKey Key { get; } = key;

    public IThingContext? ParentContext { get; init; }

    IThingKey IThingContext.Key => Key;

    string IThingKey.Key => Key.Key;

    /// <summary>
    /// Compares via key
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(IThingContext? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;
        if (other.GetType() != this.GetType()) throw new ArgumentException($"Cannot compare {this.GetType().Name} with {other.GetType().Name}");
        return string.Compare(Key.Key, other.Key.Key, StringComparison.Ordinal);
    }

    public int CompareTo(IThingKey? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;
        if (other.GetType() != this.GetType()) throw new ArgumentException($"Cannot compare {this.GetType().Name} with {other.GetType().Name}");
        return string.Compare(Key.Key, other.Key, StringComparison.Ordinal);
    }

    /// <summary>
    /// Equates by default via key, but can be overridden to equate by model object reference instead.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public virtual bool Equals(IThingContext? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.GetType() != this.GetType()) return false;
        return Key.Equals(other.Key);
    }

    public virtual bool Equals(IThingKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.GetType() != this.GetType()) return false;
        return Key.Equals(other.Key);
    }

    public virtual bool Equals(IThingContext? x, IThingContext? y)
    {
        if (x is null) return y is null;
        return x.Equals(y);
    }

    public virtual bool Equals(IThingKey? x, IThingKey? y)
    {
        if (x is null) return y is null;
        return x.Equals(y);
    }

    /// <summary>
    /// If we equate by Key, then we can use the Key's hash code. If we equate by model object reference, then we should override this method to use the model object's hash code instead.
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public virtual int GetHashCode([DisallowNull] IThingContext obj)
    {
        return obj.Key.GetHashCode();
    }

    public virtual int GetHashCode([DisallowNull] IThingKey obj)
    {
        return obj.Key.GetHashCode();
    }

    public override string ToString() => Key?.ToString() ?? $"null {nameof(IThingContext)} of type {GetType().Name}";
}