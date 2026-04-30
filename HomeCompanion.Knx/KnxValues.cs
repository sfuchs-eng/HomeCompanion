using HomeCompanion.Base.Values;

namespace HomeCompanion.Knx;

/// <summary>
/// KNX group address values container, with properties generated from the ETS group address export by <c>HomeCompanion.Knx.CodeGen</c>.
/// </summary>
/// <remarks>
/// Properties are emitted by the source generator. To enable generation on your machine, create a
/// git-ignored <c>KnxValues.local.cs</c> in this project with the path to your ETS export:
/// <code>
/// namespace HomeCompanion.Knx;
/// [KnxValuesFromEtsExport("/path/to/GroupAddressExport.xml")]
/// partial class KnxValues { }
/// </code>
/// The path may be absolute or relative to the project directory.
/// Without the attribute file the class compiles without any generated properties (CI-safe).
/// </remarks>
public partial class KnxValues : IValuesContainer { }
