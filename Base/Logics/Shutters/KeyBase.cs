using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

public abstract class KeyBase() : IEquatable<KeyBase>, IThingKey
{
    public abstract string Key { get; }

    public override bool Equals(object? obj)
    {
        return obj is KeyBase other && Equals(other);
    }

    public bool Equals(KeyBase? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualsByModelObjectReference(other);
    }

    protected abstract bool EqualsByModelObjectReference(KeyBase? other);

    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    public override string ToString() => Key;

    public bool Equals(IThingKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.GetType() != this.GetType()) return false;
        return EqualsByModelObjectReference(other as KeyBase);
    }

    public int CompareTo(IThingKey? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;
        if (other.GetType() != this.GetType()) throw new ArgumentException($"Cannot compare {this.GetType().Name} with {other.GetType().Name}");
        return string.Compare(Key, other.Key, StringComparison.Ordinal);
    }
}
