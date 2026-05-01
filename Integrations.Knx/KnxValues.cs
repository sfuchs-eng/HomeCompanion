using HomeCompanion.Base.Values;

namespace HomeCompanion.Integrations.Knx;

/// <summary>
/// KNX group address values container. Properties are generated into <c>KnxValues.generated.cs</c>
/// by running <c>srf-network-cli kc --home-companion-code-gen</c>.
/// </summary>
/// <remarks>
/// The generated file is git-ignored and machine-local. To enable generation on your machine:
/// <list type="number">
///   <item><description>
///     Configure <c>HomeCompanionCodeGenFile</c> in your local <c>SRF.Network.json</c> to the
///     absolute path of <c>HomeCompanion.Knx/KnxValues.generated.cs</c>.
///   </description></item>
///   <item><description>
///     Run <c>srf-network-cli kc --home-companion-code-gen</c> from your KNX config working directory.
///   </description></item>
/// </list>
/// Without the generated file the class compiles without any properties (CI-safe).
/// </remarks>
public partial class KnxValues : IValuesContainer { }
