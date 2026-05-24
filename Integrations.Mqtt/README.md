# HomeCompanion MQTT Integration

`HomeCompanion.Integrations.Mqtt` bridges MQTT brokers to HomeCompanion `IValue` instances.

## Configuration

Configure one or more brokers under `Mqtt:Brokers`:

```json
{
  "Mqtt": {
    "Brokers": {
      "main": {
        "Connection": {
          "Host": "mqtt-main.local",
          "ClientID": "homecompanion-main",
          "UseTls": true,
          "User": "hc",
          "Pass": "***"
        },
        "Subscriptions": [
          "home/+/+/state",
          "home/events/#"
        ]
      }
    }
  }
}
```

`Subscriptions` are broker-level ingress filters. They do not define value routing on their own.

## Value Mapping

Map values explicitly via `IValue.BusMappings` using the broker-specific bus id from `MqttBusEndpointMapping.GetBusId(...)`:

```csharp
public ValueBase<float> RoomTemperature { get; } = new(loggerFactory.CreateLogger<ValueBase<float>>())
{
    Name = "RoomTemperature",
    BusMappings =
    {
        [MqttBusEndpointMapping.GetBusId("main")] = new MqttBusEndpointMapping(
            brokerName: "main",
            stateTopicFilter: "home/+/temperature/state",
            commandTopic: "home/living/temperature/set")
        {
            Communication = BusCommunication.Receive | BusCommunication.Transmit,
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.JsonScalar,
                Qos = 1,
              Retain = false
            }
        }
    }
};
```

## Behavior

- One `MqttConnectivityProvider` instance is registered per configured broker.
- Inbound state-topic matches publish `ValueUpdateReceived`.
- Inbound command-topic matches publish `ValueWriteReceived` only.
- Wildcard routing is single-winner only with precedence: exact match, then higher specificity, then configured priority, then stable registration order.
- Outbound publishes require a concrete topic. `CommandTopic` is preferred; `OutboundTopicTemplate` is used as fallback and currently supports the `{ValueName}` placeholder.
- `TopicParameters` are extracted during inbound topic matching and are available to the internal router for match context only.

## Payload Formats

- `RawUtf8`: string and primitive conversion using invariant culture plus boolean synonyms such as `ON/OFF`.
- `JsonScalar`: JSON scalar payloads or a scalar selected via `JsonPath`.
- `Json`: POCO payloads via `System.Text.Json`, lenient by default, strict with `StrictJson = true`.
- Polymorphic JSON is supported through an explicit allow-list in `DerivedTypes`.
