# HomeCompanion Model framework

## Overview

The HomeCompanion Model system represents logical or physical entities which typically need an associated configuration and are exposed by `IValue`s to the rest of the system.

The model framework consists of 3 core dimensions:

1. **Configuration**: The configuration system is used to define the model entities and their properties. It supports polymorphism, allowing for different types of entities to be defined and used in the model. The base class `CfgEntity` provides the common properties and methods for all configuration entities, while derived classes can add specific properties and behaviors.
2. **Model**: The model system represents the runtime entities that are created from the configuration. The base class `ModelEntityWithConfig<T>` provides the common properties and methods for all model entities, while derived classes can add specific properties and behaviors. The model entities are typically created during the initialization of the system based on the configuration. They are used for simplied reference to associated `IValue` objects and may provide (normally) state-less functionality closely related to the represented entity.
3. **Keys**: The key system provides a way to uniquely identify and access model entities. The base class `Key` provides the common properties and methods for all keys, while derived classes can add specific properties and behaviors. Keys are used to reference model entities in a type-safe manner, ensuring that the correct entity is accessed based on its type and identifier. Only `Building`, `Room` and `Shutter` entities have a key, while `Facade` and `Floor` entities are always accessed via their parent entity.

Logics and extensions should implement their own dependent runtimes for ralated functionality such as for example state machines or automation logic.
See `HomeCompanion.Logics.Shutters` for an example implementing custom runtimes on top of the model system.

There's an `IValue` binding scheme implemented. See `ModelValueBindingAttribute` and/or the property naming pattern 'ShutterPosition' (IValue<> property in the model entity) / 'ShutterPositionReference' (corresponding configuration property string? with IValue's name).

## Usage

`ILogic` implementations would access the model system by injecting an `IModelProvider` instance and using it to access the model entities and their associated `IValue`s. The `IModelProvider` interface provides methods to retrieve model entities based on their keys, as well as methods to retrieve all entities of a specific type.

```chsharp
public class MyLogic(IModelProvider modelProvider) : LogicBase
{
    private readonly IModelProvider _modelProvider = modelProvider;

    public void DoSomething()
    {
        var model = _modelProvider.GetModel();
        var someShutter = model.Buildings.SelectMany(f => f.Floors).SelectMany(fl => fl.Rooms).SelectMany(r => r.Shutters).First();

        // Access the associated IValue for the shutter
        IValue<float> shutterValue = someShutter.ShutterPosition; // ensure correctly typed IValue<T> property is defined in the model entity class, or use IValue with its flexible value accessors.
        // Do something with the shutter position...
    }
}
```

## Extension points

The model framework can be extended by implementing custom model entities and related configuration entities. Additionally, logics and extensions can implement their own dependent runtimes for related functionality, such as state machines or automation logic. The keys would not be extended and serve also for derived model entities as a unique identifier for the entity type and its associated configuration.

If you implement custom model entities, start with the existing base classes instead of implementing your own base classes from available interfaces.:

- `CfgBuilding`, `CfgFacade`, `CfgFloor`, `CfgRoom`, `CfgShutter`, `CfgSpecial` for configuration entities.
- `Building`, `Facade`, `Floor`, `Room`, `Shutter`, `Special` for model entities.

See e.g. `ShadowingSpecial` for an example which is implemented, despite inclusions in the base model, as a custom model entity with its own configuration entity and runtime logic.
