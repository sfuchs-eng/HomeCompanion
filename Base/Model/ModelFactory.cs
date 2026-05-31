using System.Collections.Concurrent;
using System.Reflection;

namespace HomeCompanion.Base.Model;

public class ModelFactory : IModelFactory
{
    private static readonly ConcurrentDictionary<(Type BaseType, string TypeName), Type?> TypeByNameCache = new();

    public virtual CfgModel CreateModelConfig() => new();

    public virtual CfgBuilding CreateBuildingConfig(string? kind, string configurationPath)
        => CreateConfigByKind(kind, configurationPath, () => new CfgBuilding(), typeof(CfgBuilding));

    public virtual CfgFacade CreateFacadeConfig(string? kind, string configurationPath)
        => CreateConfigByKind(kind, configurationPath, () => new CfgFacade(), typeof(CfgFacade));

    public virtual CfgFloor CreateFloorConfig(string? kind, string configurationPath)
        => CreateConfigByKind(kind, configurationPath, () => new CfgFloor(), typeof(CfgFloor));

    public virtual CfgRoom CreateRoomConfig(string? kind, string configurationPath)
        => CreateConfigByKind(
            kind,
            configurationPath,
            () => new CfgRoom(),
            typeof(CfgRoom));

    public virtual CfgShutter CreateShutterConfig(string? kind, string configurationPath)
        => CreateConfigByKind(kind, configurationPath, () => new CfgShutter(), typeof(CfgShutter));

    public virtual CfgSpecial CreateSpecialConfig(string? kind, string configurationPath)
        => CreateConfigByKind(kind, configurationPath, () => new CfgSpecial(), typeof(CfgSpecial));

    public virtual Model CreateModel(CfgModel config)
    {
        var model = new Model();
        model.Buildings = config.Buildings.ToDictionary(
            kv => kv.Key,
            kv => CreateBuilding(new BuildingCreationContext(model), kv.Key, kv.Value));

        return model;
    }

    public virtual Building CreateBuilding(BuildingCreationContext context, string name, CfgBuilding config)
    {
        var building = new Building
        {
            Name = name,
        };

        building.Facades = config.Facades.ToDictionary(
            kv => kv.Key,
            kv => CreateFacade(new FacadeCreationContext(context.Model, building), kv.Key, kv.Value));

        building.Floors = config.Floors.ToDictionary(
            kv => kv.Key,
            kv => CreateFloor(new FloorCreationContext(context.Model, building), kv.Key, kv.Value));

        building.Specials = config.Specials.ToDictionary(
            kv => kv.Key,
            kv => CreateSpecial(new SpecialCreationContext(context.Model, building), kv.Key, kv.Value));

        return building;
    }

    public virtual Facade CreateFacade(FacadeCreationContext context, string name, CfgFacade config)
    {
        _ = context;
        return CreateEntityFromConfig(name, config, () => new Facade(name, config), typeof(Facade));
    }

    public virtual Floor CreateFloor(FloorCreationContext context, string name, CfgFloor config)
    {
        var floor = new Floor
        {
            Name = name,
        };

        floor.Rooms = config.Rooms.ToDictionary(
            kv => kv.Key,
            kv => CreateRoom(new RoomCreationContext(context.Model, context.Building, floor), kv.Key, kv.Value));

        return floor;
    }

    public virtual Room CreateRoom(RoomCreationContext context, string name, CfgRoom config)
    {
        var room = CreateEntityFromConfig(name, config, () => new Room(name, config), typeof(Room));

        room.Shutters = config.Shutters.ToDictionary(
            kv => kv.Key,
            kv => CreateShutter(new ShutterCreationContext(context.Model, context.Building, context.Floor, room), kv.Key, kv.Value));

        return room;
    }

    public virtual Shutter CreateShutter(ShutterCreationContext context, string name, CfgShutter config)
    {
        _ = context;
        return CreateEntityFromConfig(name, config, () => new Shutter(name, config), typeof(Shutter));
    }

    public virtual Special CreateSpecial(SpecialCreationContext context, string name, CfgSpecial config)
    {
        _ = context;
        return CreateEntityFromConfig(name, config, () => new Special(name, config), typeof(Special));
    }

    protected virtual TConfig CreateConfigByKind<TConfig>(
        string? kind,
        string configurationPath,
        Func<TConfig> defaultFactory,
        Type defaultConfigType)
        where TConfig : CfgEntity
    {
        if (string.IsNullOrWhiteSpace(kind) || kind.Equals(CfgEntity.KindDefault, StringComparison.OrdinalIgnoreCase))
            return defaultFactory();

        var configType = FindDerivedTypeByKind(defaultConfigType, kind, configPrefix: "Cfg");
        if (configType is null)
        {
            throw new InvalidOperationException(
                $"Unsupported kind '{kind}' at configuration path '{configurationPath}'. " +
                $"Expected a loaded type derived from '{defaultConfigType.Name}' with a matching name.");
        }

        if (configType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Type '{configType.FullName}' for kind '{kind}' at configuration path '{configurationPath}' must provide a public parameterless constructor.");
        }

        return (TConfig)Activator.CreateInstance(configType)!;
    }

    protected virtual TEntity CreateEntityFromConfig<TEntity, TConfig>(
        string name,
        TConfig config,
        Func<TEntity> defaultFactory,
        Type defaultEntityType)
        where TEntity : ModelEntity
        where TConfig : CfgEntity
    {
        if (config.GetType() == typeof(TConfig))
            return defaultFactory();

        var configType = config.GetType();
        var runtimeTypeName = configType.Name.StartsWith("Cfg", StringComparison.Ordinal)
            ? configType.Name[3..]
            : configType.Name;

        var runtimeType = FindTypeByName(defaultEntityType, runtimeTypeName);
        if (runtimeType is null)
        {
            throw new InvalidOperationException(
                $"No runtime model type named '{runtimeTypeName}' derived from '{defaultEntityType.Name}' was found for config type '{configType.FullName}'.");
        }

        var constructor = runtimeType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(string) &&
                       parameters[1].ParameterType.IsAssignableFrom(configType);
            });

        if (constructor is null)
        {
            throw new InvalidOperationException(
                $"Type '{runtimeType.FullName}' must define a public constructor '(string name, {configType.Name} config)' or '(string name, {typeof(TConfig).Name} config)'.");
        }

        return (TEntity)constructor.Invoke([name, config]);
    }

    private static Type? FindDerivedTypeByKind(Type defaultBaseType, string kind, string configPrefix)
    {
        foreach (var candidate in BuildKindCandidates(kind, configPrefix))
        {
            var resolvedType = FindTypeByName(defaultBaseType, candidate);
            if (resolvedType is not null)
                return resolvedType;
        }

        return null;
    }

    private static IEnumerable<string> BuildKindCandidates(string kind, string configPrefix)
    {
        yield return kind;

        if (!kind.StartsWith(configPrefix, StringComparison.OrdinalIgnoreCase))
            yield return $"{configPrefix}{kind}";
    }

    private static Type? FindTypeByName(Type baseType, string typeName)
    {
        var key = (baseType, typeName.ToUpperInvariant());
        return TypeByNameCache.GetOrAdd(key, _ =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
                }

                var match = types.FirstOrDefault(t =>
                    !t.IsAbstract &&
                    baseType.IsAssignableFrom(t) &&
                    t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                    return match;
            }

            return null;
        });
    }
}
