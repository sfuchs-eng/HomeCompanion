using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Tests;

[TestFixture]
public class JsonFilesStateStoreTests
{
    private sealed class SutContext : IDisposable
    {
        public string StateDirectory { get; }
        public JsonFilesStateStore Sut { get; }

        public SutContext()
        {
            StateDirectory = new DirectoryInfo(Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                ".tmp",
                $"json-state-store-tests-{Guid.NewGuid():N}")).FullName;
            Directory.CreateDirectory(StateDirectory);
            var options = Options.Create(new StateStoreOptions { Directory = StateDirectory });
            Sut = new JsonFilesStateStore(options, NullLogger<JsonFilesStateStore>.Instance);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(StateDirectory))
                {
                    Directory.Delete(StateDirectory, recursive: true);
                }
            }
            catch
            {
                // Keep cleanup resilient when files are still in use by the test host.
            }
        }
    }

    private static SutContext CreateContext() => new();

    [Test]
    public async Task LoadAsync_when_file_does_not_exist_returns_new_state_and_failure_flags()
    {
        using var context = CreateContext();
        var result = await context.Sut.LoadAsync<JsonFilesStateStoreSampleState>("missing-state", TimeSpan.FromMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(result.StateSet, Is.Not.Null);
            Assert.That(result.StateSet.Counter, Is.EqualTo(0));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.IsRecent, Is.False);
        });
    }

    [Test]
    public async Task LoadAsync_when_json_is_malformed_returns_new_state_and_failure_flags()
    {
        using var context = CreateContext();
        var stateSetName = "broken-state";
        await File.WriteAllTextAsync(GetExpectedStorePath<JsonFilesStateStoreSampleState>(context.StateDirectory, stateSetName), "{ invalid json }");

        var result = await context.Sut.LoadAsync<JsonFilesStateStoreSampleState>(stateSetName, TimeSpan.FromMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(result.StateSet, Is.Not.Null);
            Assert.That(result.StateSet.Counter, Is.EqualTo(0));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.IsRecent, Is.False);
        });
    }

    [Test]
    public async Task LoadAsync_when_file_is_stale_loads_state_data()
    {
        using var context = CreateContext();
        var stateSetName = "stale-state";
        var path = GetExpectedStorePath<JsonFilesStateStoreSampleState>(context.StateDirectory, stateSetName);
        await File.WriteAllTextAsync(path, """
            {
                            "Counter": 42,
                            "Ratio": 1.5,
                            "Name": "stale"
            }
            """);

        File.SetLastWriteTime(path, DateTime.Now.Subtract(TimeSpan.FromHours(2)));

        var result = await context.Sut.LoadAsync<JsonFilesStateStoreSampleState>(stateSetName, TimeSpan.FromMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(result.StateSet.Counter, Is.EqualTo(42));
            Assert.That(result.StateSet.Ratio, Is.EqualTo(1.5));
            Assert.That(result.StateSet.Name, Is.EqualTo("stale"));
            Assert.That(result.IsRecent, Is.False);
        });
    }

    [Test]
    public async Task LoadAsync_allows_trailing_commas_and_numbers_read_from_strings()
    {
        using var context = CreateContext();
        var stateSetName = "lenient-json";
        await File.WriteAllTextAsync(GetExpectedStorePath<JsonFilesStateStoreSampleState>(context.StateDirectory, stateSetName), """
            {
                            "Counter": "7",
                            "Ratio": "2.5",
                            "Name": "ok",
            }
            """);

        var result = await context.Sut.LoadAsync<JsonFilesStateStoreSampleState>(stateSetName, TimeSpan.FromHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(result.StateSet.Counter, Is.EqualTo(7));
            Assert.That(result.StateSet.Ratio, Is.EqualTo(2.5));
            Assert.That(result.StateSet.Name, Is.EqualTo("ok"));
        });
    }

    [Test]
    public async Task SaveAsync_when_state_is_null_throws_argument_exception()
    {
        using var context = CreateContext();
        JsonFilesStateStoreSampleState? state = null;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await context.Sut.SaveAsync("null-state", state!, CancellationToken.None));

        Assert.That(ex!.ParamName, Is.EqualTo("stateSet"));
    }

    [Test]
    public async Task SaveAsync_when_state_is_valid_writes_json_file()
    {
        using var context = CreateContext();
        var stateSetName = "saved-state";
        var state = new JsonFilesStateStoreSampleState { Counter = 5, Ratio = 0.75, Name = "persisted" };

        await context.Sut.SaveAsync(stateSetName, state, CancellationToken.None);

        var path = GetExpectedStorePath<JsonFilesStateStoreSampleState>(context.StateDirectory, stateSetName);
        Assert.That(File.Exists(path), Is.True);

        var json = await File.ReadAllTextAsync(path);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"Counter\": 5"));
            Assert.That(json, Does.Contain("\"Ratio\": 0.75"));
            Assert.That(json, Does.Contain("\"Name\": \"persisted\""));
        });
    }

    [Test]
    public async Task SaveAsync_timeout_overload_writes_json_file()
    {
        using var context = CreateContext();
        var stateSetName = "saved-with-timeout";
        var state = new JsonFilesStateStoreSampleState { Counter = 9, Ratio = double.NaN, Name = "timeout" };

        await context.Sut.SaveAsync(stateSetName, state, timeoutSeconds: 5);

        var result = await context.Sut.LoadAsync<JsonFilesStateStoreSampleState>(stateSetName, TimeSpan.FromMinutes(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.StateSet.Counter, Is.EqualTo(9));
            Assert.That(double.IsNaN(result.StateSet.Ratio), Is.True);
            Assert.That(result.StateSet.Name, Is.EqualTo("timeout"));
        });
    }

    [Test]
    [Explicit("Hardening test: enable when success/recent flags are implemented for successful loads.")]
    public async Task Hardening_recent_valid_load_should_report_success_and_recent()
    {
        using var context = CreateContext();
        var stateSetName = "recent-state";
        await File.WriteAllTextAsync(GetExpectedStorePath<JsonFilesStateStoreSampleState>(context.StateDirectory, stateSetName), """
            {
                            "Counter": 1,
                            "Ratio": 1.0,
                            "Name": "recent"
            }
            """);

        var result = await context.Sut.LoadAsync<JsonFilesStateStoreSampleState>(stateSetName, TimeSpan.FromHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, "Successful deserialization should mark IsSuccess=true.");
            Assert.That(result.IsRecent, Is.True, "Recent state file should mark IsRecent=true.");
        });
    }

    [Test]
    [Explicit("Hardening test: enable when stateSetName path traversal is rejected or sanitized.")]
    public async Task Hardening_state_set_name_should_not_escape_storage_directory()
    {
        using var context = CreateContext();
        var escapedName = Path.Combine("..", "outside");

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await context.Sut.SaveAsync(escapedName, new JsonFilesStateStoreSampleState { Counter = 1 }, CancellationToken.None));
    }

    [Test]
    [Explicit("Hardening test: enable when JsonFilesStateStore supports deterministic time source injection.")]
    public void Hardening_constructor_should_accept_time_provider()
    {
        var ctor = typeof(JsonFilesStateStore).GetConstructor(
        [
            typeof(IOptions<StateStoreOptions>),
            typeof(Microsoft.Extensions.Logging.ILogger<JsonFilesStateStore>),
            typeof(TimeProvider),
        ]);

        Assert.That(ctor, Is.Not.Null, "Expected constructor overload with TimeProvider for deterministic recency testing.");
    }

    private static string GetExpectedStorePath<T>(string stateDirectory, string stateSetName)
    {
        var typeName = typeof(T).FullName ?? typeof(T).Name;
        var fileName = $"{typeName}_{stateSetName}.json";
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }
        return Path.Combine(stateDirectory, fileName);
    }

}

public sealed class JsonFilesStateStoreSampleState
{
    public int Counter { get; set; }
    public double Ratio { get; set; }
    public string Name { get; set; } = string.Empty;
}
