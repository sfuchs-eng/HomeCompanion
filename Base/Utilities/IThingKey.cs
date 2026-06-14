namespace HomeCompanion.Base.Utilities;

public interface IThingKey : IEquatable<IThingKey>
{
    string Key { get; }
}