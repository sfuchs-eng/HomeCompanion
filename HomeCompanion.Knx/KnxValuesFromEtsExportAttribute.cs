namespace HomeCompanion.Knx;

/// <summary>
/// Instructs the <c>HomeCompanion.Knx.CodeGen</c> source generator to generate
/// <see cref="HomeCompanion.Base.Values.ValueBase{T}"/> properties on the decorated
/// <see langword="partial"/> class from an ETS group address export XML file.
/// </summary>
/// <remarks>
/// Apply to a <c>partial class KnxValues</c> to enable generation. The path is resolved as
/// absolute, or relative to the project directory when not rooted. When no file exists at the
/// given path the generator emits a HCKNX001 warning and produces no output, keeping builds
/// clean on machines without a local ETS export (e.g. CI). Create a git-ignored
/// <c>KnxValues.local.cs</c> to keep developer-local paths out of version control:
/// <code>
/// namespace HomeCompanion.Knx;
/// [KnxValuesFromEtsExport("/path/to/GroupAddressExport.xml")]
/// partial class KnxValues { }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class KnxValuesFromEtsExportAttribute : Attribute
{
    /// <summary>Absolute or project-relative path to the ETS group address export XML file.</summary>
    public string ExportFilePath { get; }

    /// <param name="exportFilePath">Absolute or project-relative path to <c>GroupAddressExport.xml</c>.</param>
    public KnxValuesFromEtsExportAttribute(string exportFilePath) => ExportFilePath = exportFilePath;
}
