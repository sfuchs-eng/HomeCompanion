namespace HomeCompanion.Base.Utilities;

public interface IThingContext : IEquatable<IThingContext>, IEquatable<IThingKey>, IComparable<IThingContext>, IComparable<IThingKey>, IEqualityComparer<IThingContext>, IEqualityComparer<IThingKey>
{
    IThingContext? ParentContext { get; }
    IThingKey Key { get; }
}