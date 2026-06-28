namespace HomeCompanion.Base.Utilities;

public interface IThingKey : IEquatable<IThingKey>, IComparable<IThingKey>, IEqualityComparer<IThingKey>
{
    string Key { get; }
}
