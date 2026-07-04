using System.Collections;
using System.Diagnostics.CodeAnalysis;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Base.Model;

/// <summary>
/// string based model object identifier base class.
/// See <see cref="Key"/> for formatting details.
/// </summary>
public abstract class KeyBase() : IEquatable<KeyBase>, IThingKey
{
    /// <summary>
    /// <para>Key format: {Token1}/{Token2}/.../{TokenN}</para>
    /// <para>where each token is a string identifier of a model object formatted `{TypeName}:{Identifier}`, e.g. "Building:MyHome", "Shutter:SouthEast1Left"</para>
    /// <para>The tokens are separated by a forward slash.</para>
    /// <para>The type identifiers are the names of the model object types, e.g. Building, Floor, Room, Shutter.</para>
    /// </summary>
    public abstract string Key { get; }

    public override bool Equals(object? obj)
    {
        return obj is KeyBase other && Equals(other);
    }

    public bool Equals(KeyBase? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Key == other.Key;
    }

    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    public override string ToString() => Key;

    public virtual bool Equals(IThingKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.GetType() != this.GetType()) return false;
        return Key == other.Key;
    }

    public virtual int CompareTo(IThingKey? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;
        if (other.GetType() != this.GetType()) throw new ArgumentException($"Cannot compare {this.GetType().Name} with {other.GetType().Name}");
        return string.Compare(Key, other.Key, StringComparison.Ordinal);
    }

    public bool Equals(IThingKey? x, IThingKey? y)
    {
        if (x is null) return y is null;
        return x.Equals(y);
    }

    public int GetHashCode([DisallowNull] IThingKey obj)
    {
        return obj.Key.GetHashCode();
    }
}
