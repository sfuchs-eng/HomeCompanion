namespace HomeCompanion.Knx;

/// <summary>
/// Marker attribute indicating that this <see langword="partial"/> class participates in
/// <c>KnxValues.generated.cs</c> code generation via <c>srf-network-cli kc --home-companion-code-gen</c>.
/// </summary>
/// <remarks>
/// Apply this attribute in a git-ignored <c>KnxValues.local.cs</c> to signal intent.
/// The file path parameters are accepted for forward compatibility but are not currently consumed;
/// the output path is configured via <c>HomeCompanionCodeGenFile</c> in <c>SRF.Network.json</c>.
/// <code>
/// namespace HomeCompanion.Knx;
/// [KnxValuesFromEtsExport]
/// partial class KnxValues { }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class KnxValuesFromEtsExportAttribute : Attribute
{
    /// <summary>Accepted for forward compatibility; currently unused.</summary>
    public string? ExportFilePath { get; }

    /// <summary>Accepted for forward compatibility; currently unused.</summary>
    public string? AutoGenFilePath { get; }

    /// <param name="exportFilePath">Accepted for forward compatibility; currently unused.</param>
    /// <param name="autoGenFilePath">Accepted for forward compatibility; currently unused.</param>
    public KnxValuesFromEtsExportAttribute(string? exportFilePath = null, string? autoGenFilePath = null)
    {
        ExportFilePath = exportFilePath;
        AutoGenFilePath = autoGenFilePath;
    }
}
