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
        var model = new Model(config);
        model.Buildings = config.Buildings.ToDictionary(
            kv => kv.Key,
            kv => CreateBuilding(new BuildingCreationContext(model), kv.Key, kv.Value));

        model.Specials = config.Specials.ToDictionary(
            kv => kv.Key,
            kv => CreateSpecial(new SpecialCreationContext(model, null), kv.Key, kv.Value));

        return model;
    }

    public virtual Building CreateBuilding(BuildingCreationContext context, string name, CfgBuilding config)
    {
        var building = new Building(name, config);

        building.Facades = config.Facades.ToDictionary(
            kv => kv.Key,
            kv => CreateFacade(new FacadeCreationContext(context.Model, building), kv.Key, kv.Value));

        building.Floors = config.Floors.ToDictionary(
            kv => kv.Key,
            kv => CreateFloor(new FloorCreationContext(context.Model, building), kv.Key, kv.Value));

        building.Specials = config.Specials.ToDictionary(
            kv => kv.Key,
            kv => CreateSpecial(new SpecialCreationContext(context.Model, building), kv.Key, kv.Value) as IBuildingSpecial ?? throw new InvalidOperationException(
                $"Special '{kv.Key}' in building '{name}' must implement '{typeof(IBuildingSpecial).FullName}' to be added to the building's specials collection."));

        BindFacades(building);

        return building;
    }

    public virtual Facade CreateFacade(FacadeCreationContext context, string name, CfgFacade config)
    {
        _ = context;
        return CreateEntityFromConfig(name, config, () => new Facade(name, config), typeof(Facade));
    }

    /// <summary>
    /// Link <see cref="Shutter.Facade"/> to their respective facades.
    /// Normally this would be done in the shutter creation, but since the facade reference is only a string in the config, we need to defer it until all facades are created. This also allows for more flexible configuration ordering.
    /// </summary>
    /// <param name="model"></param>
    public virtual void BindFacades(Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            BindFacades(building);
        }
    }

    /// <summary>
    /// Link <see cref="Shutter.Facade"/> to their respective facades for a specific building.
    /// For <see cref="IValue"/> properties binding, see <see cref="HomeCompanion.Core.ModelValueBinder"/> 
    /// </summary>
    /// <param name="building"></param>
    public virtual void BindFacades(Building building)
    {
        foreach (var floor in building.Floors.Values)
        {
            foreach (var room in floor.Rooms.Values)
            {
                foreach (var shutter in room.Shutters.Values)
                {
                    // Lookup facade reference from shutter config and link to facade instance. This is required for the shutter to determine its orientation and for scene controls to find other shutters on the same facade.
                    if (string.IsNullOrWhiteSpace(shutter.Configuration.FacadeReference))
                    {
                        throw new InvalidOperationException(
                            $"Shutter '{shutter.Name}' in room '{room.Name}' on floor '{floor.Name}' in building '{building.Name}' has no facade reference configured.");
                    }

                    if (!building.Facades.TryGetValue(shutter.Configuration.FacadeReference, out var facade))
                    {
                        throw new InvalidOperationException(
                            $"Shutter '{shutter.Name}' in room '{room.Name}' on floor '{floor.Name}' in building '{building.Name}' references unknown facade '{shutter.Configuration.FacadeReference}'.");
                    }

                    shutter.Facade = facade;
                }
            }
        }
    }

    public virtual Floor CreateFloor(FloorCreationContext context, string name, CfgFloor config)
    {
        var floor = new Floor(name, config);

        floor.Rooms = config.Rooms.ToDictionary(
            kv => kv.Key,
            kv => CreateRoom(new RoomCreationContext(context.Model, context.Building, floor), kv.Key, kv.Value));

        return floor;
    }

    public virtual Room CreateRoom(RoomCreationContext context, string name, CfgRoom config)
    {
        var room = CreateEntityFromConfig(name, config, () => new Room(name, config), typeof(Room));

        foreach (var shutterConfig in config.Shutters)
        {
            var shutter = CreateShutter(new ShutterCreationContext(context.Model, context.Building, context.Floor, room), shutterConfig.Key, shutterConfig.Value);
            room.Shutters[shutterConfig.Key] = shutter;
        }

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

    public virtual ISpecial CreateSpecial(SpecialCreationContext context, string name, CfgSpecial config)
    {
        // Use the type indication from the config to determine the runtime type to instantiate.
        // Check whether the type is derived from ISpecial and has a constructor (string name, CfgSpecial config).
        if ( string.IsNullOrWhiteSpace(config.Kind) )
        {
            throw new ArgumentException(
                $"Special '{name}' in building '{context.Building?.Name}' has no kind specified. " +
                $"The 'Kind' property is required to determine the runtime type to instantiate for this special.");
        }

        var specialType = FindDerivedTypeByKind(typeof(ISpecial), config.Kind, configPrefix: "Cfg")
                ?? throw new InvalidOperationException(
                    $"Unsupported kind '{config.Kind}' at configuration path '{name}'. " +
                    $"Expected a loaded type derived from '{typeof(ISpecial).Name}' with a matching name.");

        if (specialType.GetConstructor([typeof(string), config.GetType()]) is null)
        {
            throw new InvalidOperationException(
                $"Type '{specialType.FullName}' for kind '{config.Kind}' at configuration path '{name}' must provide a public constructor '(string name, {config.GetType().Name} config)'.");
        }

        var inst = Activator.CreateInstance(specialType, [name, config]);

        if (inst is not ISpecial special)
        {
            throw new InvalidOperationException(
                $"Type '{specialType.FullName}' for kind '{config.Kind}' at configuration path '{name}' must implement '{typeof(ISpecial).FullName}'.");
        }

        return special;
    }

    protected virtual TConfig CreateConfigByKind<TConfig>(
        string? kind,
        string configurationPath,
        Func<TConfig> defaultFactory,
        Type defaultConfigType)
        where TConfig : CfgEntity
    {
        if (string.IsNullOrWhiteSpace(kind))
            return defaultFactory();

        var configType = FindDerivedTypeByKind(defaultConfigType, kind, configPrefix: "Cfg") ?? throw new InvalidOperationException(
                $"Unsupported kind '{kind}' at configuration path '{configurationPath}'. " +
                $"Expected a loaded type derived from '{defaultConfigType.Name}' with a matching name.");

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
