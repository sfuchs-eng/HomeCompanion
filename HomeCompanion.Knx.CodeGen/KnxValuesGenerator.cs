using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using HomeCompanion.Knx.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HomeCompanion.Knx.CodeGen;

/// <summary>
/// Roslyn incremental source generator that produces a <c>partial class KnxValues : IValuesContainer</c>
/// from an ETS group address export XML file. The file path is supplied via
/// <c>[KnxValuesFromEtsExport("path")]</c> on the hand-written partial class declaration.
/// </summary>
[Generator]
public sealed class KnxValuesGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "HomeCompanion.Knx.KnxValuesFromEtsExportAttribute";

    // -------------------------------------------------------------------------
    // Diagnostic descriptors
    // -------------------------------------------------------------------------

    private static readonly DiagnosticDescriptor FileNotFoundDescriptor = new(
        id: "HCKNX001",
        title: "ETS group address export file not found",
        messageFormat: "ETS group address export file not found at '{0}'. No KNX values will be generated.",
        category: "HomeCompanion.Knx.CodeGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FileReadErrorDescriptor = new(
        id: "HCKNX002",
        title: "ETS group address export file could not be read",
        messageFormat: "ETS group address export file '{0}' could not be read: {1}",
        category: "HomeCompanion.Knx.CodeGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingDptDescriptor = new(
        id: "HCKNX003",
        title: "KNX group address has no DPT",
        messageFormat: "Group address '{0}' ({1}) has no DPT defined; generated property will be ValueBase<byte[]>",
        category: "HomeCompanion.Knx.CodeGen",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AutoGenFileNotFoundDescriptor = new(
        id: "HCKNX004",
        title: "HomeCompanion auto-gen file not found",
        messageFormat: "HomeCompanion auto-gen file not found at '{0}'. No KNX values will be generated. Run 'srf-network-cli knx-configuration --generate-homecompanion-autogen' to create it.",
        category: "HomeCompanion.Knx.CodeGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AutoGenFileReadErrorDescriptor = new(
        id: "HCKNX005",
        title: "HomeCompanion auto-gen file could not be read",
        messageFormat: "HomeCompanion auto-gen file '{0}' could not be read or parsed: {1}",
        category: "HomeCompanion.Knx.CodeGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // -------------------------------------------------------------------------
    // IIncrementalGenerator implementation
    // -------------------------------------------------------------------------

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all partial class declarations decorated with [KnxValuesFromEtsExport].
        var exportPaths = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var attr = ctx.Attributes[0];
                    var args = attr.ConstructorArguments;
                    var exportFilePath = args.Length > 0 ? args[0].Value as string : null;
                    var autoGenFilePath = args.Length > 1 ? args[1].Value as string : null;
                    return (ExportFilePath: exportFilePath, AutoGenFilePath: autoGenFilePath);
                })
            .Where(static pair => pair.ExportFilePath is not null || pair.AutoGenFilePath is not null);

        // Obtain the consuming project directory for resolving relative paths.
        var projectDir = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.projectdir", out var dir);
                return dir ?? string.Empty;
            });

        context.RegisterSourceOutput(
            exportPaths.Combine(projectDir),
            static (spc, combined) => Execute(spc, combined.Left.ExportFilePath, combined.Left.AutoGenFilePath, combined.Right));
    }

    // -------------------------------------------------------------------------
    // Execution
    // -------------------------------------------------------------------------

    private static void Execute(SourceProductionContext ctx, string? rawExportPath, string? rawAutoGenPath, string projectDir)
    {
        if (rawAutoGenPath is not null)
        {
            ExecuteFromAutoGen(ctx, rawAutoGenPath, projectDir);
            return;
        }
        if (rawExportPath is not null)
        {
            ExecuteFromEtsXml(ctx, rawExportPath, projectDir);
        }
    }

    private static void ExecuteFromAutoGen(SourceProductionContext ctx, string rawPath, string projectDir)
    {
        var path = ResolvePath(rawPath, projectDir);

        if (!File.Exists(path))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(AutoGenFileNotFoundDescriptor, null, path));
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(AutoGenFileReadErrorDescriptor, null, path, ex.Message));
            return;
        }

        var map = HomeCompanionAutoGenSerializer.Deserialize(json);
        if (map is null)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(AutoGenFileReadErrorDescriptor, null, path, "JSON could not be deserialized."));
            return;
        }

        var entries = new List<GroupAddressEntry>(map.Count);
        foreach (var kvp in map)
        {
            entries.Add(new GroupAddressEntry(kvp.Value.PropertyName, kvp.Key, kvp.Value.Dpt, useRawName: true));
        }

        ctx.AddSource("KnxValues.g.cs", SourceText.From(GenerateSource(ctx, entries), Encoding.UTF8));
    }

    private static void ExecuteFromEtsXml(SourceProductionContext ctx, string rawPath, string projectDir)
    {
        var path = ResolvePath(rawPath, projectDir);

        if (!File.Exists(path))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(FileNotFoundDescriptor, null, path));
            return;
        }

        string xml;
        try
        {
            xml = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(FileReadErrorDescriptor, null, path, ex.Message));
            return;
        }

        var entries = ParseEtsExport(xml);
        ctx.AddSource("KnxValues.g.cs", SourceText.From(GenerateSource(ctx, entries), Encoding.UTF8));
    }

    // -------------------------------------------------------------------------
    // ETS XML parsing
    // -------------------------------------------------------------------------

    private static List<GroupAddressEntry> ParseEtsExport(string xml)
    {
        var result = new List<GroupAddressEntry>();
        try
        {
            var doc = XDocument.Parse(xml);
            if (doc.Root != null)
                CollectGroupAddresses(doc.Root, result);
        }
        catch
        {
            // Malformed XML — return empty list; caller emits no diagnostic
            // because we have no location to attach it to at this point.
        }
        return result;
    }

    private static string ResolvePath(string rawPath, string projectDir)
        => Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.Combine(
                projectDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                rawPath);

    private static void CollectGroupAddresses(XElement element, List<GroupAddressEntry> result)
    {
        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "GroupRange":
                    CollectGroupAddresses(child, result);
                    break;
                case "GroupAddress":
                    var name = child.Attribute("Name")?.Value;
                    var address = child.Attribute("Address")?.Value;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(address))
                        result.Add(new GroupAddressEntry(name!, address!, child.Attribute("DPTs")?.Value, useRawName: false));
                    break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // DPT → C# type mapping  (stable per KNX specification)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a DPT main number to the C# value type used in <c>ValueBase&lt;T&gt;</c>.
    /// DPT strings in the ETS export have the form "DPST-X-Y" or "DPT-X".
    /// </summary>
    private static string GetCSharpType(string? dpts)
    {
        if (string.IsNullOrEmpty(dpts))
            return "byte[]";

        var parts = dpts!.Split('-'); // IsNullOrEmpty guard above; netstandard2.0 lacks [NotNullWhen(false)]
        if (parts.Length < 2 || !int.TryParse(parts[1], out var main))
            return "byte[]";

        switch (main)
        {
            case 1:  return "bool";      // 1-bit switch/boolean
            case 2:  return "byte";      // 2-bit controlled
            case 3:  return "byte";      // 4-bit dimming
            case 4:  return "char";      // 1-byte character
            case 5:  return "byte";      // 1-byte unsigned (0–100 %, angles, …)
            case 6:  return "sbyte";     // 1-byte signed
            case 7:  return "ushort";    // 2-byte unsigned
            case 8:  return "short";     // 2-byte signed
            case 9:  return "float";     // KNX 2-byte float (EIS 5)
            case 10: return "byte[]";    // Time of day (complex)
            case 11: return "byte[]";    // Date (complex)
            case 12: return "uint";      // 4-byte unsigned
            case 13: return "int";       // 4-byte signed
            case 14: return "float";     // IEEE 754 4-byte float
            case 16: return "string";    // ISO 8859-1 character string
            case 17: return "byte";      // Scene number
            case 18: return "byte";      // Scene control
            case 19: return "byte[]";    // Date & time (complex)
            default:
                if (main >= 20 && main <= 29) return "byte";  // Enumeration / status bytes
                return "byte[]";
        }
    }

    // -------------------------------------------------------------------------
    // Identifier sanitization
    // -------------------------------------------------------------------------

    private static string SanitizeToCSharpIdentifier(string name)
    {
        var sb = new StringBuilder();
        var capitalizeNext = true;

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (sb.Length == 0)
            return "Value";

        var result = sb.ToString();
        return char.IsDigit(result[0]) ? "V" + result : result;
    }

    private static string MakeUnique(string name, HashSet<string> used)
    {
        if (used.Add(name))
            return name;

        for (var i = 2; ; i++)
        {
            var candidate = name + "_" + i;
            if (used.Add(candidate))
                return candidate;
        }
    }

    private static string EscapeXmlComment(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // -------------------------------------------------------------------------
    // Code generation
    // -------------------------------------------------------------------------

    private static string GenerateSource(SourceProductionContext ctx, List<GroupAddressEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using HomeCompanion.Base.Values;");
        sb.AppendLine();
        sb.AppendLine("namespace HomeCompanion.Knx;");
        sb.AppendLine();
        sb.AppendLine("partial class KnxValues");
        sb.AppendLine("{");

        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Dpts))
                ctx.ReportDiagnostic(Diagnostic.Create(MissingDptDescriptor, null, entry.Name, entry.Address));

            var csType = GetCSharpType(entry.Dpts);
            var rawPropName = entry.UseRawName ? entry.Name : SanitizeToCSharpIdentifier(entry.Name);
            var propName = MakeUnique(rawPropName, usedNames);

            sb.AppendLine($"    /// <summary>{EscapeXmlComment(entry.Name)} (<c>{entry.Address}</c>)</summary>");
            sb.AppendLine($"    public ValueBase<{csType}> {propName} {{ get; }} = new()");
            sb.AppendLine("    {");
            sb.AppendLine($"        BusMappings = new Dictionary<object, IValueBusEndpointMapping>");
            sb.AppendLine("        {");
            sb.AppendLine($"            [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping(\"{entry.Address}\")");
            sb.AppendLine("        }");
            sb.AppendLine("    };");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Helper types
    // -------------------------------------------------------------------------

    private sealed class GroupAddressEntry
    {
        public GroupAddressEntry(string name, string address, string? dpts, bool useRawName)
        {
            Name = name;
            Address = address;
            Dpts = dpts;
            UseRawName = useRawName;
        }

        public string Name { get; }
        public string Address { get; }
        public string? Dpts { get; }
        /// <summary>When true, <see cref="Name"/> is already a valid C# identifier and must not be sanitized.</summary>
        public bool UseRawName { get; }
    }
}
