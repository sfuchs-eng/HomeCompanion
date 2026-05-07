using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeCompanion.Persistence;

public class JsonFilesStateStore : IStateStore
{
    readonly StateStoreOptions _options;
    readonly ILogger<JsonFilesStateStore> _logger;
    readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions()
    {
        AllowTrailingCommas = true,
        IgnoreReadOnlyProperties = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true,
    };

    readonly TimeProvider _timeProvider;

    public JsonFilesStateStore(IOptions<StateStoreOptions> options, ILogger<JsonFilesStateStore> logger, TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var di = new DirectoryInfo(_options.Directory);
        if (!di.Exists)
        {
            try
            {
                di.Create();
                _logger.LogInformation("Created state storage directory '{stateDirectory}'", di.FullName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create persistence StateStore in directory '{stateDirectory}'", di.FullName);
            }
        }
        else
            _logger.LogDebug("Storing state in '{stateDirectory}'", di.FullName);
    }

    /// <summary>
    /// Deserializes an object of <typeparamref name="T"/> from the file .../<paramref name="stateSetName"/>.json
    /// if it exists and is not older than <paramref name="maxAge"/>.
    /// The file is also deserialized if too old, but in that case <see langword="false"/> is returned.
    /// </summary>
    /// <returns><see langword="true"/> if the file exists and is recent enough, <see langword="false"/> otherwise</returns>
    public async Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName, TimeSpan maxAge) where T : class, new()
    {
        var fi = GetStateFile<T>(stateSetName);
        var result = new StateLoadingResult<T>();
        if (!fi.Exists)
        {
            _logger.LogInformation("There's no state file '{statePath}' for {stateName}.", fi.FullName, stateSetName);
            result.StateSet = Activator.CreateInstance<T>();
            result.IsSuccess = false;
            result.IsRecent = false;
            return result;
        }

        var isRecent = fi.LastWriteTimeUtc >= _timeProvider.GetUtcNow().UtcDateTime - maxAge;
        result.IsRecent = isRecent;
        if (!isRecent)
        {
            // too old, deserialize nevertheless
            _logger.LogInformation("State information for {stateName} is outdated, written {stateTime}", stateSetName, fi.LastWriteTimeUtc);
        }
        try
        {
            using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read))
                result.StateSet = JsonSerializer.Deserialize<T>(fs, _jsonOptions)
                    ?? throw new JsonException($"Deserialization of state file '{fi.FullName}' for {stateSetName} resulted in null.");
            result.IsSuccess = true;
            _logger.LogTrace("Loaded state file '{statePath}' for {stateName}.", fi.FullName, stateSetName);
        }
        catch (Exception ex )
        {
            _logger.LogWarning(ex, "State loading for {stateName} from file '{statePath}' failed.", stateSetName, fi.FullName);
            result.StateSet = Activator.CreateInstance<T>();
            result.IsSuccess = false;
            result.IsRecent = false;
        }
        return result;
    }

    /// <summary>
    /// Loads states if exist and not older than 30 min.
    /// See <see cref="Load{T}(string, out T, TimeSpan)"/>.
    /// </summary>
    public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName) where T : class, new()
    {
        return LoadAsync<T>(stateSetName, TimeSpan.FromMinutes(30));
    }

    public async Task SaveAsync<T>(string stateSetName, T stateSet, CancellationToken cancellation) where T : class, new()
    {
        if (stateSet == null)
        {
            _logger.LogTrace("Cannot save state to '{stateSetName}' for a null reference of type {stateTypeName}", stateSetName, typeof(T).FullName);
            throw new ArgumentException($"Cannot save state to '{stateSetName}' for a null reference of type {typeof(T).FullName}", nameof(stateSet));
        }
        try
        {
            var fi = GetStateFile<T>(stateSetName);
            using var fs = new FileStream(fi.FullName, FileMode.Create, FileAccess.Write);
            _logger.LogTrace("Saving state info '{stateSetName}' to '{stateFile}'", stateSetName, fi.FullName);
            try
            {
                await JsonSerializer.SerializeAsync(fs, stateSet, stateSet.GetType(), _jsonOptions, cancellation);
                await fs.FlushAsync(cancellation);
                fs.Close();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Serializing state info '{stateSetName}' to '{stateFile}' failed.", stateSetName, fi.FullName);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Saving state information for {stateSetName} was canceled.", stateSetName);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "State information for {stateSetName} could not be serialized to JSON.", stateSetName);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "State set name '{stateSetName}' is invalid for state type {stateTypeName}.", stateSetName, typeof(T).FullName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Saving state information failed for {stateSetName} / type {stateTypeName}", stateSetName, typeof(T).FullName);
        }
    }

    public async Task SaveAsync<T>(string stateSetName, T stateSet, int timeoutSeconds = 30) where T : class, new()
    {
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            await SaveAsync(stateSetName, stateSet, cts.Token);
        }
    }

    FileInfo GetStateFile<T>(string stateSetName)
    {
        if (string.IsNullOrWhiteSpace(stateSetName))
            throw new ArgumentException("State set name cannot be null or whitespace.", nameof(stateSetName));
        // does it contain any path travesal or absolute path characters? If so, reject it by throwing an exception to avoid security issues.
        if (stateSetName.IndexOfAny(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar }) >= 0)
            throw new ArgumentException($"State set name '{stateSetName}' cannot contain path traversal or absolute path characters.", nameof(stateSetName));
        // cover .. too, even if it doesn't contain directory separator chars, to be extra safe against path traversal
        if (stateSetName.Contains(".."))
            throw new ArgumentException($"State set name '{stateSetName}' cannot contain '..' to avoid path traversal.", nameof(stateSetName));

        var typeName = typeof(T).FullName ?? typeof(T).Name;
        var fileName = $"{typeName}_{stateSetName}.json";
        // ensure the file name is valid by replacing invalid characters with underscores
        foreach (var invalidChar in Path.GetInvalidFileNameChars().Append('.'))
        {
            fileName = fileName.Replace(invalidChar, '_');
        }
        return new FileInfo(Path.Combine(new DirectoryInfo(_options.Directory).FullName, fileName));
    }
}
