# Building thermal control model

## Idea

- Pull the weather forecast from an API once a day for the next 3 days
- Predict required room heating valve modulation index, building thermal control profile (`Base.Model.ThermalControlMode`) and whether ventilation via motorized windows is appropriate (on/off, separate scheduling) using an ML model.
- The ML model is trained on historical actual data measured by the home automation system.

## Gemini's answer on it: Predictive Thermal Control System

**Target Stack:** .NET 10 (C#) | ML.NET | InfluxDB v2 (Flux)
**Building Profile:** High Thermal Inertia (2–3 Day Time Constant)
**Actuation Topology:** 3-State Heat Pump (Heat/Off/Cool), 18 Floor-Heating Valves, 7 Thermal Zones (Rooms)

---

### 1. System Overview & Core Philosophy

Standard reactive thermostat logic (e.g., PID controllers) fails in high-inertia buildings due to massive lag, causing constant temperature overshooting and undershooting. 

To overcome this, this system shifts the control paradigm from *reactive* to **predictive**. By decoupling macro-energy strategies from micro-valve actuation, the system uses a machine learning model to calculate a **Global 24-hour Strategy** anchored entirely around a single, highly stable daily baseline measurement.

> **The 05:00 AM Anchor Principle**
> Room air temperature is measured daily at **05:00 AM**. At this specific timestamp, transient human activities (cooking, opening windows, appliances) are minimized, and solar radiation is absent. This measurement represents the building's true baseline thermal equilibrium, serving as the sole optimization target for the ML model.

---

### 2. Tiered Control Architecture

To minimize the machine learning "action space" and prevent unstable hardware oscillations, control responsibilities are strictly split into two layers:

         [ 05:00 AM Trigger ]
                       │
                       ▼
      ┌──────────────────────────────────┐
      │     Tier 1: ML.NET Engine        │
      │   (Macro Energy Forecasting)     │
      └──────────────────────────────────┘
                       │
                       ├─► Output: Global Heat Pump State [Heat, Off, Cool]
                       └─► Output: Target Room Modulations [0 - 100%]
                       │
                       ▼
      ┌──────────────────────────────────┐
      │    Tier 2: C# PWM Modulator      │
      │  (Micro-Actuation Loop / Valves) │
      └──────────────────────────────────┘
                       │
                       ▼ [Maps 7 Rooms to 18 Valves via 24h Duty Cycle]
          [ Physical Valve Relays ]

#### Tier 1: The Macro-Strategy Layer (ML.NET)

* **Execution Interval:** Once daily at 05:05 AM (as soon as InfluxDB data registers).
* **Responsibility:** Evaluates a 48-to-72-hour forecast window to determine the ideal overall Heat Pump operational mode and dictates a target energy allocation percentage (0–100%) for each of the 7 rooms.

#### Tier 2: The Micro-Actuation Layer (C# Native)

* **Execution Interval:** Chronologically distributed throughout the day (e.g., hourly check-ins).
* **Responsibility:** Translates the abstract 0–100% room modulation targets into physical binary `On/Off` signals for the 18 valves using Time-Proportional Pulse Width Modulation (PWM) over a 24-hour window. Master-slave grouping ensures loops in the same zone switch uniformly.

---

### 3. Data Engineering & ML Pipeline

#### Feature Layout (The Daily Flattened Matrix)
Because the building remembers weather patterns from days prior, data cannot look at an isolated hour. Flux queries or C# extraction services must aggregate data into **daily 05:00 AM rows**:

| Feature Category | Variable Name | Description |
| :--- | :--- | :--- |
| **Current State** | `CurrentIndoorTemp` | The 05:00 AM baseline temperature *today*. |
| **Thermal History** | `OutdoorTemp_24h_Avg` | Historical rolling average outdoor temperature. |
| **Thermal History** | `OutdoorTemp_48h_Avg` | Captures long-term cold/warm fronts stored in concrete. |
| **Energy History** | `GlobalModulation_Lag24h` | Proxy for thermal energy currently "trapped" in floor loops. |
| **Forecast** | `Forecast_OutdoorTemp_24h` | Predicted average outdoor temperature for the coming day. |
| **Forecast** | `Forecast_Solar_24h` | Predicted total solar radiation ($W/m^2$); crucial for window gain. |
| **Control Input** | `Proposed_HP_State` | Simulated parameter during optimization loop (`Heat`, `Off`, `Cool`). |
| **Target (Label)** | `NextDayIndoorTemp` | **The Prediction Goal:** The 05:00 AM baseline temperature *tomorrow*. |

#### Training Pipeline Implementation Fragment

Using a regression tree architecture (`FastTreeRegression`) provides excellent accuracy for tabular multi-variable thermal profiles.

```csharp
using Microsoft.ML;

public class ThermalData
{
    public float CurrentIndoorTemp { get; set; }
    public float OutdoorTemp_48h_Avg { get; set; }
    public float Forecast_OutdoorTemp_24h { get; set; }
    public float Forecast_Solar_24h { get; set; }
    public float Proposed_HP_State { get; set; } // Encoded as numerical category
    public float NextDayIndoorTemp { get; set; }  // The Label
}

// Model building flow
var context = new MLContext();
var data = context.Data.LoadFromEnumerable<ThermalData>(historicalInfluxRows);

var pipeline = context.Transforms.CopyColumns("Label", nameof(ThermalData.NextDayIndoorTemp))
    .Append(context.Transforms.Concatenate("Features", 
        nameof(ThermalData.CurrentIndoorTemp), 
        nameof(ThermalData.OutdoorTemp_48h_Avg), 
        nameof(ThermalData.Forecast_OutdoorTemp_24h),
        nameof(ThermalData.Forecast_Solar_24h),
        nameof(ThermalData.Proposed_HP_State)))
    .Append(context.Regression.Trainers.FastTree());

var model = pipeline.Fit(data);
context.Model.Save(model, data.Schema, "thermal_model.zip");
```

### 4. Control Logic Execution Strategy

Every morning at 05:05 AM, a Model Predictive Control (MPC) brute-force simulation loop evaluates potential strategies by projecting them out iteratively through the model.

```csharp
// High-Level conceptual execution framework inside the daily hosted service
public void ExecuteDailyControlLoop()
{
    var scenarios = new[] { HPState.Heat, HPState.Off, HPState.Cool };
    HPState bestState = HPState.Off;
    double optimalScore = double.MaxValue;

    foreach (var state in scenarios)
    {
        // 1. Evaluate trajectory through ML.NET model
        double predicted5AMTempTomorrow = PredictTomorrowTemp(state, weatherForecast, currentMetrics);
        
        // 2. Score the outcome (Penalty = deviation from comfortable setpoint + energy costs)
        double comfortPenalty = Math.Abs(predicted5AMTempTomorrow - TargetSetpoint);
        double energyCost = CalculateStateCost(state);
        double totalPenalty = comfortPenalty + energyCost;

        if (totalPenalty < optimalScore)
        {
            optimalScore = totalPenalty;
            bestState = state;
        }
    }

    // 3. Actuate Macro Assets
    ApplyGlobalHeatPumpState(bestState);
}
```

### 5. Micro PWM Valve Distribution Engine

Once the macro energy goal is set, the C# valve loop uses space-proportional weighting coupled with a daily duty cycle to flick relays.

```csharp
public class ValveModulator
{
    // Tracks physical valve groupings mapping to the 7 core thermal zones
    private readonly Dictionary<string, List<int>> _zoneToValveMap = new()
    {
        { "LivingRoom", new() { 1, 2, 3, 4 } }, // 4 distinct loops in concrete floor
        { "Kitchen",    new() { 5, 6 } },
        { "Bathroom",   new() { 7 } }
    };

    public void EvaluateValveDutyCycle(Dictionary<string, double> roomModulationTargets, int currentHour)
    {
        // Computes current percentage through the 24-hour control frame
        double cycleProgress = (currentHour / 24.0) * 100.0;

        foreach (var zone in _zoneToValveMap)
        {
            double targetModulation = roomModulationTargets[zone.Key]; // Value from 0 to 100
            
            // If the daily target is 50%, valve is true for hours 0-12, false for 12-24
            bool valveState = cycleProgress < targetModulation;

            foreach (var valveId in zone.Value)
            {
                TransmitHardwareSignal(valveId, valveState);
            }
        }
    }
}
```

### 6. System Safety & Operational Guardrails
Because thermal system actions take 48+ hours to correct if corrupted, a deterministic Safety Intercept Layer sits directly between the ML code output and hardware controllers.

```
┌──────────────┐      ┌────────────────┐      ┌─────────────────────────┐      ┌───────────────────┐
│  ML Engine   │ ───► │ Proposed State │ ───► │  Safety Guard Checkers  │ ───► │ Physical Hardware │
└──────────────┘      └────────────────┘      └─────────────────────────┘      └───────────────────┘
                                                           ▲
                                                           │ (Evaluates Live Rules)
                                              [ Hardcoded Critical Limits ]
```

- The Static Dead-Band: If any active room falls below 19°C or exceeds 25°C at any check-in interval, the ML predictive trajectory calculations are overridden completely. Standard deterministic hysteresis rule-sets take over until values re-enter bounds.
- Compressor Cycle Limiting: Minimum hardware lock-times must be maintained in C# to prevent the heat pump from switching between Heat and Cool inside a 12-hour period, protecting compressors from pressure shock.
- Hydronic Flow Protection: The C# valve driver must reject states that attempt to close all 18 valves simultaneously while the heat pump state is actively operating in Heat or Cool mode, preventing pump cavitation and system back-pressure spikes.
