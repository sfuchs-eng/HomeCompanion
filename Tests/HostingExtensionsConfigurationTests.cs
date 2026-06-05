using HomeCompanion.Core;
using Microsoft.Extensions.Configuration;

namespace HomeCompanion.Tests;

[TestFixture]
public class HostingExtensionsConfigurationTests
{
    [Test]
    public void EnumerateJsonFilesInDirectory_OnlyTopLevelJsonFiles_SortedByFileName()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "b.json"), "{}");
            File.WriteAllText(Path.Combine(root, "a.json"), "{}");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "ignored");

            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "c.json"), "{}");

            var result = HostingExtensions.EnumerateJsonFilesInDirectory(
                root,
                Directory.Exists,
                path => Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly));

            Assert.That(result.Select(Path.GetFileName).ToArray(), Is.EqualTo(new[] { "a.json", "b.json" }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ResolveHomeCompanionJsonConfigurationPaths_UsesDeterministicOrder_AndDeduplicatesPaths()
    {
        var xdgConfigHome = "/tmp/xdg-config";
        var homeDir = "/tmp/home-user";
        var homeDotConfig = Path.Combine(homeDir, ".config");

        var knownDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            HostingExtensions.NormalizePath("/etc/homecompanion"),
            HostingExtensions.NormalizePath(Path.Combine(xdgConfigHome, "homecompanion")),
            HostingExtensions.NormalizePath(Path.Combine(homeDotConfig, "homecompanion")),
        };

        var filesByDirectory = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [HostingExtensions.NormalizePath("/etc/homecompanion")] =
            [
                "/etc/homecompanion/90-override.json",
                "/etc/homecompanion/20-base.json",
                "/etc/homecompanion/HomeCompanion.json",
                "/etc/homecompanion/readme.txt",
            ],
            [HostingExtensions.NormalizePath(Path.Combine(xdgConfigHome, "homecompanion"))] =
            [
                Path.Combine(xdgConfigHome, "homecompanion", "10-user.json"),
                Path.Combine(xdgConfigHome, "homecompanion", "ignore.md"),
            ],
            [HostingExtensions.NormalizePath(Path.Combine(homeDotConfig, "homecompanion"))] =
            [
                Path.Combine(homeDotConfig, "homecompanion", "20-fallback.json"),
            ],
        };

        var result = HostingExtensions.ResolveHomeCompanionJsonConfigurationPaths(
            directoryExists: path => knownDirs.Contains(HostingExtensions.NormalizePath(path)),
            enumerateFiles: path => filesByDirectory[HostingExtensions.NormalizePath(path)],
            xdgConfigHomeAccessor: () => xdgConfigHome,
            appDataPathAccessor: () => "/tmp/appdata",
            homePathAccessor: () => homeDir);

        var expected = new[]
        {
            "/etc/HomeCompanion.json",
            "/etc/homecompanion/HomeCompanion.json",
            Path.Combine(xdgConfigHome, "HomeCompanion.json"),
            Path.Combine(homeDotConfig, "HomeCompanion.json"),
            "/etc/homecompanion/20-base.json",
            "/etc/homecompanion/90-override.json",
            Path.Combine(xdgConfigHome, "homecompanion", "10-user.json"),
            Path.Combine(homeDotConfig, "homecompanion", "20-fallback.json"),
        }
        .Select(HostingExtensions.NormalizePath)
        .ToArray();

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveHomeCompanionJsonConfigurationPaths_DeduplicatesXdgAndHomeDotConfigLocations()
    {
        var homeDir = "/tmp/home-user";
        var xdgConfigHome = Path.Combine(homeDir, ".config");
        var xdgDirectory = HostingExtensions.NormalizePath(Path.Combine(xdgConfigHome, "homecompanion"));

        var result = HostingExtensions.ResolveHomeCompanionJsonConfigurationPaths(
            directoryExists: path => string.Equals(HostingExtensions.NormalizePath(path), xdgDirectory, StringComparison.OrdinalIgnoreCase),
            enumerateFiles: path =>
            [
                Path.Combine(HostingExtensions.NormalizePath(path), "01-base.json"),
                Path.Combine(HostingExtensions.NormalizePath(path), "99-local.json"),
            ],
            xdgConfigHomeAccessor: () => xdgConfigHome,
            appDataPathAccessor: () => "/tmp/appdata",
            homePathAccessor: () => homeDir);

        Assert.That(result.Where(path => path.EndsWith("HomeCompanion.json", StringComparison.OrdinalIgnoreCase)).Count(), Is.EqualTo(3));
        Assert.That(result.Count(path => string.Equals(Path.GetDirectoryName(path), xdgDirectory, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(2));
    }

    [Test]
    public void ResolveHomeCompanionJsonConfigurationPaths_IncludesConfiguredConfigDirectories()
    {
        var configuredDirectory = HostingExtensions.NormalizePath("/workspace/Config");
        var knownDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            configuredDirectory,
        };

        var result = HostingExtensions.ResolveHomeCompanionJsonConfigurationPaths(
            directoryExists: path => knownDirs.Contains(HostingExtensions.NormalizePath(path)),
            enumerateFiles: path =>
            [
                Path.Combine(HostingExtensions.NormalizePath(path), "20-model.json"),
                Path.Combine(HostingExtensions.NormalizePath(path), "10-values.json"),
                Path.Combine(HostingExtensions.NormalizePath(path), "notes.txt"),
            ],
            xdgConfigHomeAccessor: () => "/tmp/xdg",
            appDataPathAccessor: () => "/tmp/appdata",
            homePathAccessor: () => "/tmp/home",
            configuredConfigDirectories: [configuredDirectory]);

        Assert.That(result, Does.Contain(HostingExtensions.NormalizePath(Path.Combine(configuredDirectory, "10-values.json"))));
        Assert.That(result, Does.Contain(HostingExtensions.NormalizePath(Path.Combine(configuredDirectory, "20-model.json"))));
    }

    [Test]
    public void ResolveConfiguredConfigDirectories_LoadsConventionAndConfiguredEntries_Deduplicated()
    {
        var configurationData = new Dictionary<string, string?>
        {
            ["HomeCompanion:ConfigDirectory"] = "/opt/homecompanion/config",
            ["HomeCompanion:ConfigDirectories:0"] = "../Config",
            ["HomeCompanion:ConfigDirectories:1"] = "/etc/homecompanion",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var result = HostingExtensions.ResolveConfiguredConfigDirectories(configuration, "/workspace/Server");

        Assert.That(result, Does.Contain(HostingExtensions.NormalizePath("/workspace/Config")));
        Assert.That(result, Does.Contain(HostingExtensions.NormalizePath("/opt/homecompanion/config")));
        Assert.That(result, Does.Contain(HostingExtensions.NormalizePath("/etc/homecompanion")));
        Assert.That(result.Count(path => string.Equals(path, HostingExtensions.NormalizePath("/workspace/Config"), StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
