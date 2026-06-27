namespace HomeCompanion.Base.Utilities;

public interface IThingKey : IEquatable<IThingKey>, IComparable<IThingKey>
{
    string Key { get; }
}