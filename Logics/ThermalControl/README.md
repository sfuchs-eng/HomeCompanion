# ThermalControl

The shutter control logic and other functionality bases on an `IValue<byte>`
which essentially reflects a `ThermalControlMode` enum value in my home automation system.
The thermal mode not only directs shutter and roof window automation policies, but also the heat pump operation mode, and it impacts the room heating valve modulation as well.

The actual `ThermalControlLogic` implementation is highly building specific and
hence remains part of a local solution. Correspondingly, there are only a few generic base classes and interfaces in this repository.

The thermal control logic can be realized by static configuration in the consuming entities, or, interfaced via a referenced `IValue<byte>`, by more complex approaches (from manual input to predictive models).
