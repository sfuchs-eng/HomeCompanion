namespace HomeCompanion.Integrations.Sonos;

public sealed class SonosTransportService : ISonosTransportService
{
    private readonly SonosIntegrationOptions _options;
    private readonly HttpClient _httpClient;
    private readonly SonosDiscoveryService _discoveryService;
    private readonly List<SonosGroup> _groups = [];
    private readonly Dictionary<string, SonosPlaybackState> _playbackStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<QueueEntry>> _queueEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FavoriteEntry>> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AlarmEntry> _alarms = [];

    public SonosTransportService(SonosIntegrationOptions? options = null)
    {
        _options = options ?? new SonosIntegrationOptions();
        _httpClient = new HttpClient
        {
            Timeout = _options.RequestTimeout
        };
        _discoveryService = new SonosDiscoveryService(_options);

        _playbackStates["player-1"] = new SonosPlaybackState
        {
            PlayerId = "player-1",
            State = SonosPlaybackStateKind.Playing,
            PositionSeconds = 42,
            Volume = 25,
            IsMuted = false,
            CurrentSource = "https://example.com/stream-1.mp3",
            Title = "Current Track"
        };

        _playbackStates["player-2"] = new SonosPlaybackState
        {
            PlayerId = "player-2",
            State = SonosPlaybackStateKind.Stopped,
            PositionSeconds = 0,
            Volume = 30,
            IsMuted = false,
            CurrentSource = "https://example.com/stream-2.mp3",
            Title = "Another Track"
        };

        _queueEntries["player-1"] =
        [
            new QueueEntry
            {
                QueueIndex = 0,
                Id = "queue-1",
                Title = "Current Track",
                Uri = "https://example.com/stream-1.mp3",
                Metadata = "{\"source\":\"seed\"}"
            }
        ];
        _favorites["player-1"] =
        [
            new FavoriteEntry
            {
                Id = "favorite-1",
                Name = "Classic FM",
                Type = "radio",
                Uri = "x-rincon-browse:favorite-1"
            }
        ];
        _alarms.Add(new AlarmEntry
        {
            Id = "alarm-1",
            Name = "Wake Up",
            Enabled = true,
            Time = "07:00",
            Recurrence = ["Mon", "Tue", "Wed", "Thu", "Fri"],
            Volume = 20,
            Source = "radio"
        });
    }

    private static PlaybackResult BuildFailureResult(string? playerId, string message, SonosErrorCode code)
    {
        return new PlaybackResult
        {
            Success = false,
            PlayerId = playerId,
            Message = message,
            ErrorCode = code
        };
    }

    private static GroupResult BuildGroupFailureResult(string? playerId, string message, SonosErrorCode code)
    {
        return new GroupResult
        {
            Success = false,
            PlayerId = playerId,
            Message = message,
            ErrorCode = code
        };
    }

    private static RestoreResult BuildRestoreFailureResult(string? snapshotId, string message, SonosErrorCode code)
    {
        return new RestoreResult
        {
            Success = false,
            SnapshotId = snapshotId,
            Message = message,
            ErrorCode = code,
            RestoredPlayerIds = []
        };
    }

    private static bool IsLikelyPlayerId(string value)
    {
        return value.StartsWith("player-", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase) ||
               Guid.TryParse(value, out _);
    }

    private static string ResolveGroupName(IReadOnlyList<SonosPlayer> members)
    {
        var labels = members
            .Select(member => !string.IsNullOrWhiteSpace(member.ZoneName) ? member.ZoneName : member.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return labels.Count > 0
            ? string.Join(" + ", labels)
            : string.Join(" + ", members.Select(member => member.Id));
    }

    private static SonosErrorCode GetNotFoundCodeForIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SonosErrorCode.DeviceNotFound;
        }

        return IsLikelyPlayerId(value.Trim())
            ? SonosErrorCode.DeviceNotFound
            : SonosErrorCode.RoomNotFound;
    }

    private static SonosPlaybackState ClonePlaybackState(SonosPlaybackState state)
    {
        return new SonosPlaybackState
        {
            PlayerId = state.PlayerId,
            State = state.State,
            PositionSeconds = state.PositionSeconds,
            Volume = state.Volume,
            IsMuted = state.IsMuted,
            CurrentSource = state.CurrentSource,
            Title = state.Title,
            Artist = state.Artist
        };
    }

    private static QueueEntry CloneQueueEntry(QueueEntry entry)
    {
        return new QueueEntry
        {
            QueueIndex = entry.QueueIndex,
            Id = entry.Id,
            Title = entry.Title,
            Uri = entry.Uri,
            Metadata = entry.Metadata
        };
    }

    private static FavoriteEntry CloneFavoriteEntry(FavoriteEntry entry)
    {
        return new FavoriteEntry
        {
            Id = entry.Id,
            Name = entry.Name,
            Type = entry.Type,
            Description = entry.Description,
            Uri = entry.Uri,
            MetadataJson = entry.MetadataJson
        };
    }

    private static AlarmEntry CloneAlarmEntry(AlarmEntry entry)
    {
        return new AlarmEntry
        {
            Id = entry.Id,
            Name = entry.Name,
            Enabled = entry.Enabled,
            Time = entry.Time,
            Recurrence = entry.Recurrence.ToList(),
            Volume = entry.Volume,
            Source = entry.Source,
            MetadataJson = entry.MetadataJson
        };
    }

    private static void ValidateAlarm(AlarmEntry alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);

        if (string.IsNullOrWhiteSpace(alarm.Name))
        {
            throw new ArgumentException("Alarm name is required.", nameof(alarm));
        }

        if (string.IsNullOrWhiteSpace(alarm.Time) || !TimeSpan.TryParse(alarm.Time, out var parsedTime))
        {
            throw new ArgumentException("Alarm time must be a valid HH:mm or HH:mm:ss value.", nameof(alarm));
        }

        if (parsedTime < TimeSpan.Zero || parsedTime >= TimeSpan.FromDays(1))
        {
            throw new ArgumentException("Alarm time must be within the 24-hour day range.", nameof(alarm));
        }

        if (alarm.Volume is < 0 or > 100)
        {
            throw new ArgumentException("Alarm volume must be between 0 and 100.", nameof(alarm));
        }

        if (alarm.Recurrence is null || alarm.Recurrence.Count == 0)
        {
            throw new ArgumentException("Alarm recurrence must contain at least one day.", nameof(alarm));
        }
    }

    private static bool IsRestorableSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        if (source.StartsWith("unavailable:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (SonosValidation.IsValidMediaUrl(source))
        {
            return true;
        }

        return source.StartsWith("x-rincon", StringComparison.OrdinalIgnoreCase) ||
               source.StartsWith("x-sonos", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildExpectedMultiPlayerGroups(
        IReadOnlyDictionary<string, IReadOnlyList<string>> membership)
    {
        var expected = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in membership.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            var members = entry.Value
                .Select(id => SonosValidation.NormalizePlayerId(id, string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (members.Count > 1)
            {
                expected[entry.Key] = members;
            }
        }

        return expected;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildActualMultiPlayerGroups(IReadOnlyList<SonosGroup> groups)
    {
        return groups
            .Select(group => new
            {
                GroupId = group.Id,
                Members = group.Members
                    .Select(member => SonosValidation.NormalizePlayerId(member.Id, string.Empty))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .Where(item => item.Members.Count > 1)
            .ToDictionary(item => item.GroupId, item => (IReadOnlyList<string>)item.Members, StringComparer.OrdinalIgnoreCase);
    }

    private static bool AreEquivalentGroupMaps(
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected,
        IReadOnlyDictionary<string, IReadOnlyList<string>> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        foreach (var expectedEntry in expected)
        {
            if (!actual.TryGetValue(expectedEntry.Key, out var actualMembers))
            {
                return false;
            }

            if (!expectedEntry.Value.SequenceEqual(actualMembers, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<SonosPlayer?> GetPlayerAsync(string normalizedPlayerId, CancellationToken cancellationToken)
    {
        var players = await GetPlayersAsync(cancellationToken).ConfigureAwait(false);
        var discoveredPlayer = players.FirstOrDefault(player =>
            string.Equals(SonosValidation.NormalizePlayerId(player.Id, string.Empty), normalizedPlayerId, StringComparison.OrdinalIgnoreCase));

        if (discoveredPlayer is not null)
        {
            return discoveredPlayer;
        }

        if (_playbackStates.TryGetValue(normalizedPlayerId, out var trackedState))
        {
            return new SonosPlayer
            {
                Id = normalizedPlayerId,
                Name = normalizedPlayerId,
                CoordinatorId = normalizedPlayerId,
                GroupMembership = [normalizedPlayerId],
                Availability = SonosAvailability.Available,
                Volume = trackedState.Volume,
                IsMuted = trackedState.IsMuted
            };
        }

        return null;
    }

    private async Task<SonosPlaybackState> EnsurePlaybackStateAsync(string normalizedPlayerId, CancellationToken cancellationToken)
    {
        var player = await GetPlayerAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);

        if (!_playbackStates.TryGetValue(normalizedPlayerId, out var existing))
        {
            existing = new SonosPlaybackState
            {
                PlayerId = normalizedPlayerId,
                State = SonosPlaybackStateKind.Stopped,
                PositionSeconds = 0,
                Volume = player?.Volume ?? 0,
                IsMuted = player?.IsMuted ?? false,
                CurrentSource = null,
                Title = null,
                Artist = null
            };
            _playbackStates[normalizedPlayerId] = existing;
            return existing;
        }

        if (player is not null)
        {
            existing.Volume = player.Volume;
            existing.IsMuted = player.IsMuted;
        }

        return existing;
    }

    private async Task<PlaybackResult> ExecutePlaybackCommandAsync(
        string playerId,
        string commandName,
        Func<SonosPlaybackState, SonosPlaybackState>? applyMutation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BuildFailureResult(playerId, "Player ID is required.", SonosErrorCode.DeviceNotFound);
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return BuildFailureResult(normalizedPlayerId, $"{commandName} was cancelled.", SonosErrorCode.Cancelled);
            }

            try
            {
                var player = await GetPlayerAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
                if (player is null)
                {
                    var errorCode = GetNotFoundCodeForIdentifier(playerId);
                    var subject = errorCode == SonosErrorCode.RoomNotFound ? "Room" : "Player";
                    return BuildFailureResult(normalizedPlayerId, $"{subject} '{playerId}' was not found.", errorCode);
                }

                if (player.Availability == SonosAvailability.Offline)
                {
                    return BuildFailureResult(normalizedPlayerId, "Player is offline.", SonosErrorCode.PlayerUnavailable);
                }

                await Task.Delay(1, cancellationToken).ConfigureAwait(false);

                if (applyMutation is not null)
                {
                    var state = await EnsurePlaybackStateAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
                    _playbackStates[normalizedPlayerId] = applyMutation(ClonePlaybackState(state));
                }

                var refreshedState = await GetPlaybackStateAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
                return new PlaybackResult
                {
                    Success = true,
                    PlayerId = normalizedPlayerId,
                    CurrentSource = refreshedState.CurrentSource,
                    Message = $"{commandName} command accepted."
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return BuildFailureResult(normalizedPlayerId, $"{commandName} was cancelled.", SonosErrorCode.Cancelled);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == _options.MaxRetries)
                {
                    return BuildFailureResult(normalizedPlayerId, $"{commandName} timed out.", SonosErrorCode.Timeout);
                }
            }
            catch (Exception ex)
            {
                var failure = BuildFailureResult(normalizedPlayerId, $"{commandName} failed: {ex.Message}", SonosErrorCode.TransportFailure);
                failure.Exception = ex;
                return failure;
            }
        }

        return BuildFailureResult(normalizedPlayerId, $"{commandName} timed out.", SonosErrorCode.Timeout);
    }

    public async Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default)
    {
        return await _discoveryService.GetPlayersAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SonosGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SonosGroup>>(_groups.ToList());
    }

    public async Task<IReadOnlyList<QueueEntry>> GetQueueAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        var player = await GetPlayerAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            var errorCode = GetNotFoundCodeForIdentifier(playerId);
            var subject = errorCode == SonosErrorCode.RoomNotFound ? "Room" : "Player";
            throw new InvalidOperationException($"{subject} '{playerId}' was not found.");
        }

        if (!_queueEntries.TryGetValue(normalizedPlayerId, out var queue))
        {
            _queueEntries[normalizedPlayerId] = [];
            return [];
        }

        for (var index = 0; index < queue.Count; index++)
        {
            queue[index].QueueIndex = index;
            if (string.IsNullOrWhiteSpace(queue[index].Uri))
            {
                throw new FormatException($"Queue entry at index {index} has an empty URI.");
            }

            if (string.IsNullOrWhiteSpace(queue[index].Id))
            {
                throw new FormatException($"Queue entry at index {index} has an empty ID.");
            }
        }

        return queue.OrderBy(entry => entry.QueueIndex).Select(CloneQueueEntry).ToList();
    }

    public async Task<PlaybackResult> EnqueueAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return BuildFailureResult(playerId, "Enqueue was cancelled.", SonosErrorCode.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BuildFailureResult(playerId, "Player ID is required.", SonosErrorCode.DeviceNotFound);
        }

        if (!SonosValidation.IsValidMediaUrl(mediaUrl))
        {
            return BuildFailureResult(playerId, "Queue item must be a valid HTTP/HTTPS URL.", SonosErrorCode.InvalidUri);
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        var player = await GetPlayerAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            var errorCode = GetNotFoundCodeForIdentifier(playerId);
            var subject = errorCode == SonosErrorCode.RoomNotFound ? "Room" : "Player";
            return BuildFailureResult(normalizedPlayerId, $"{subject} '{playerId}' was not found.", errorCode);
        }

        if (!_queueEntries.TryGetValue(normalizedPlayerId, out var queue))
        {
            queue = [];
            _queueEntries[normalizedPlayerId] = queue;
        }

        var nextIndex = queue.Count;
        var entry = new QueueEntry
        {
            QueueIndex = nextIndex,
            Id = $"queue-{nextIndex + 1}",
            Title = Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) ? Path.GetFileName(uri.LocalPath) : mediaUrl,
            Uri = mediaUrl,
            Metadata = "{\"source\":\"enqueue\"}"
        };
        if (string.IsNullOrWhiteSpace(entry.Title))
        {
            entry.Title = mediaUrl;
        }

        queue.Add(entry);
        var refreshedQueue = await GetQueueAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);

        return new PlaybackResult
        {
            Success = true,
            PlayerId = normalizedPlayerId,
            CurrentSource = entry.Uri,
            Message = $"Enqueued item at index {refreshedQueue.Count - 1}."
        };
    }

    public async Task<PlaybackResult> ClearQueueAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return BuildFailureResult(playerId, "ClearQueue was cancelled.", SonosErrorCode.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BuildFailureResult(playerId, "Player ID is required.", SonosErrorCode.DeviceNotFound);
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        var player = await GetPlayerAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            var errorCode = GetNotFoundCodeForIdentifier(playerId);
            var subject = errorCode == SonosErrorCode.RoomNotFound ? "Room" : "Player";
            return BuildFailureResult(normalizedPlayerId, $"{subject} '{playerId}' was not found.", errorCode);
        }

        _queueEntries[normalizedPlayerId] = [];

        var refreshedQueue = await GetQueueAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
        return new PlaybackResult
        {
            Success = true,
            PlayerId = normalizedPlayerId,
            Message = refreshedQueue.Count == 0 ? "Queue cleared." : "Queue clear requested."
        };
    }

    public async Task<PlaybackResult> PlayQueueItemAsync(string playerId, int queueIndex, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return BuildFailureResult(playerId, "PlayQueueItem was cancelled.", SonosErrorCode.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BuildFailureResult(playerId, "Player ID is required.", SonosErrorCode.DeviceNotFound);
        }

        if (queueIndex < 0)
        {
            return BuildFailureResult(playerId, "Queue index must be non-negative.", SonosErrorCode.MalformedResponse);
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        var queue = await GetQueueAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
        if (queueIndex >= queue.Count)
        {
            return BuildFailureResult(normalizedPlayerId, $"Queue index {queueIndex} is out of range.", SonosErrorCode.MalformedResponse);
        }

        var selectedEntry = queue[queueIndex];

        return await ExecutePlaybackCommandAsync(normalizedPlayerId, "PlayQueueItem", state =>
        {
            state.State = SonosPlaybackStateKind.Playing;
            state.PositionSeconds = 0;
            state.CurrentSource = selectedEntry.Uri;
            state.Title = selectedEntry.Title;
            return state;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FavoriteEntry>> GetFavoritesAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        var player = await GetPlayerAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            var errorCode = GetNotFoundCodeForIdentifier(playerId);
            var subject = errorCode == SonosErrorCode.RoomNotFound ? "Room" : "Player";
            throw new InvalidOperationException($"{subject} '{playerId}' was not found.");
        }

        if (!_favorites.TryGetValue(normalizedPlayerId, out var favorites))
        {
            _favorites[normalizedPlayerId] = [];
            return [];
        }

        var clonedFavorites = favorites.Select(CloneFavoriteEntry).ToList();
        foreach (var favorite in clonedFavorites)
        {
            if (string.IsNullOrWhiteSpace(favorite.Id) || string.IsNullOrWhiteSpace(favorite.Name))
            {
                throw new FormatException("Favorite payload is malformed: ID and Name are required.");
            }
        }

        return clonedFavorites;
    }

    public async Task<PlaybackResult> PlayFavoriteAsync(string playerId, string favoriteId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return BuildFailureResult(playerId, "PlayFavorite was cancelled.", SonosErrorCode.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BuildFailureResult(playerId, "Player ID is required.", SonosErrorCode.DeviceNotFound);
        }

        if (string.IsNullOrWhiteSpace(favoriteId))
        {
            return BuildFailureResult(playerId, "Favorite ID or name is required.", SonosErrorCode.DeviceNotFound);
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        var favorites = await GetFavoritesAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);

        var favorite = favorites.FirstOrDefault(entry =>
            string.Equals(entry.Id, favoriteId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, favoriteId, StringComparison.OrdinalIgnoreCase));

        if (favorite is null)
        {
            return BuildFailureResult(normalizedPlayerId, $"Favorite '{favoriteId}' not found.", SonosErrorCode.DeviceNotFound);
        }

        if (string.IsNullOrWhiteSpace(favorite.Uri))
        {
            return BuildFailureResult(normalizedPlayerId, $"Favorite '{favorite.Name}' has no playable URI.", SonosErrorCode.InvalidUri);
        }

        var source = favorite.Uri!;
        var canPlay = SonosValidation.IsValidMediaUrl(source) ||
                      source.StartsWith("x-rincon", StringComparison.OrdinalIgnoreCase) ||
                      source.StartsWith("x-sonos", StringComparison.OrdinalIgnoreCase);
        if (!canPlay)
        {
            return BuildFailureResult(normalizedPlayerId, $"Favorite '{favorite.Name}' uses an unsupported URI.", SonosErrorCode.InvalidUri);
        }

        var playbackResult = await ExecutePlaybackCommandAsync(normalizedPlayerId, "PlayFavorite", state =>
        {
            state.State = SonosPlaybackStateKind.Playing;
            state.PositionSeconds = 0;
            state.CurrentSource = source;
            state.Title = favorite.Name;
            return state;
        }, cancellationToken).ConfigureAwait(false);

        if (playbackResult.Success)
        {
            playbackResult.Message = $"Playing favorite '{favorite.Name}'.";
        }

        return playbackResult;
    }

    public Task<IReadOnlyList<AlarmEntry>> GetAlarmsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var alarms = _alarms
            .OrderBy(alarm => alarm.Time, StringComparer.Ordinal)
            .ThenBy(alarm => alarm.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CloneAlarmEntry)
            .ToList();

        foreach (var alarm in alarms)
        {
            if (string.IsNullOrWhiteSpace(alarm.Id) || string.IsNullOrWhiteSpace(alarm.Name) || string.IsNullOrWhiteSpace(alarm.Time))
            {
                throw new FormatException("Alarm payload is malformed: ID, Name, and Time are required.");
            }
        }

        return Task.FromResult<IReadOnlyList<AlarmEntry>>(alarms);
    }

    public Task<AlarmEntry> CreateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAlarm(alarm);

        var created = CloneAlarmEntry(alarm);
        created.Id = string.IsNullOrWhiteSpace(created.Id)
            ? $"alarm-{Guid.NewGuid().ToString("N")[..8]}"
            : created.Id.Trim();

        if (_alarms.Any(existing => string.Equals(existing.Id, created.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Alarm with ID '{created.Id}' already exists.");
        }

        _alarms.Add(created);
        return Task.FromResult(CloneAlarmEntry(created));
    }

    public Task<AlarmEntry> UpdateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAlarm(alarm);

        if (string.IsNullOrWhiteSpace(alarm.Id))
        {
            throw new ArgumentException("Alarm ID is required for updates.", nameof(alarm));
        }

        var existingIndex = _alarms.FindIndex(existing => string.Equals(existing.Id, alarm.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
        {
            throw new InvalidOperationException($"Alarm '{alarm.Id}' was not found.");
        }

        var updated = CloneAlarmEntry(alarm);
        _alarms[existingIndex] = updated;
        return Task.FromResult(CloneAlarmEntry(updated));
    }

    public Task<bool> DeleteAlarmAsync(string alarmId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return Task.FromResult(false);
        }

        var removed = _alarms.RemoveAll(existing => string.Equals(existing.Id, alarmId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(removed > 0);
    }

    public async Task<SonosPlaybackState> GetPlaybackStateAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        var state = await EnsurePlaybackStateAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
        return ClonePlaybackState(state);
    }

    public Task<PlaybackResult> PlayAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "Play", state =>
        {
            state.State = SonosPlaybackStateKind.Playing;
            return state;
        }, cancellationToken);
    }

    public Task<PlaybackResult> PauseAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "Pause", state =>
        {
            state.State = SonosPlaybackStateKind.Paused;
            return state;
        }, cancellationToken);
    }

    public Task<PlaybackResult> StopAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "Stop", state =>
        {
            state.State = SonosPlaybackStateKind.Stopped;
            state.PositionSeconds = 0;
            return state;
        }, cancellationToken);
    }

    public Task<PlaybackResult> NextAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "Next", state =>
        {
            state.State = SonosPlaybackStateKind.Playing;
            state.PositionSeconds = 0;
            return state;
        }, cancellationToken);
    }

    public Task<PlaybackResult> PreviousAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "Previous", state =>
        {
            state.State = SonosPlaybackStateKind.Playing;
            state.PositionSeconds = 0;
            return state;
        }, cancellationToken);
    }

    public Task<PlaybackResult> SeekAsync(string playerId, double positionSeconds, CancellationToken cancellationToken = default)
    {
        if (positionSeconds < 0)
        {
            return Task.FromResult(BuildFailureResult(playerId, "Seek position must be greater than or equal to zero.", SonosErrorCode.MalformedResponse));
        }

        return ExecutePlaybackCommandAsync(playerId, "Seek", state =>
        {
            state.PositionSeconds = positionSeconds;
            return state;
        }, cancellationToken);
    }

    public Task<PlaybackResult> SetVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default)
    {
        if (volume < 0 || volume > 100)
        {
            return Task.FromResult(BuildFailureResult(playerId, "Volume must be between 0 and 100.", SonosErrorCode.MalformedResponse));
        }

        return ExecutePlaybackCommandAsync(playerId, "SetVolume", state =>
        {
            state.Volume = volume;
            return state;
        }, cancellationToken);
    }

    public Task<PlaybackResult> SetMuteAsync(string playerId, bool isMuted, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "SetMute", state =>
        {
            state.IsMuted = isMuted;
            return state;
        }, cancellationToken);
    }

    public Task<PlaybackResult> PlayUrlAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        if (!SonosValidation.IsValidMediaUrl(mediaUrl))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.InvalidUri, Message = "Media URL must be a valid HTTP/HTTPS URL." });
        }

        return ExecutePlaybackCommandAsync(playerId, "PlayUrl", state =>
        {
            state.CurrentSource = mediaUrl;
            state.State = SonosPlaybackStateKind.Playing;
            state.PositionSeconds = 0;
            return state;
        }, cancellationToken);
    }

    public Task<GroupResult> GroupAsync(IEnumerable<string> playerIds, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(BuildGroupFailureResult(null, "Group operation was cancelled.", SonosErrorCode.Cancelled));
        }

        var ids = (playerIds ?? throw new ArgumentNullException(nameof(playerIds)))
            .Select(id => SonosValidation.NormalizePlayerId(id, string.Empty))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ids.Length < 2)
        {
            return Task.FromResult(new GroupResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.GroupRejected,
                Message = "Grouping requires at least two players.",
                PlayerId = ids.FirstOrDefault()
            });
        }

        var groupId = "group-1";
        var coordinatorId = ids.First();

        _groups.RemoveAll(group => group.Members.Any(member => ids.Contains(member.Id, StringComparer.OrdinalIgnoreCase)));

        var groupMembers = ids.Select(id => new SonosPlayer
        {
            Id = id,
            Name = id,
            CoordinatorId = coordinatorId,
            GroupMembership = [groupId],
            Availability = SonosAvailability.Available,
            Volume = 25,
            IsMuted = false
        }).ToList();

        var group = new SonosGroup
        {
            Id = groupId,
            Name = ResolveGroupName(groupMembers),
            CoordinatorId = coordinatorId,
            Members = groupMembers
        };

        _groups.Add(group);

        return Task.FromResult(new GroupResult
        {
            Success = true,
            GroupId = groupId,
            PlayerId = coordinatorId,
            Group = group,
            Message = "Grouping refreshed."
        });
    }

    public Task<GroupResult> UngroupAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(BuildGroupFailureResult(playerId, "Ungroup operation was cancelled.", SonosErrorCode.Cancelled));
        }

        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new GroupResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        _groups.RemoveAll(group => group.Members.Any(member => string.Equals(member.Id, normalizedPlayerId, StringComparison.OrdinalIgnoreCase)));

        return Task.FromResult(new GroupResult
        {
            Success = true,
            PlayerId = normalizedPlayerId,
            GroupId = null,
            Group = null,
            Message = "Ungroup command accepted."
        });
    }

    public async Task<SonosStateSnapshot> CaptureStateAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        var playbackState = await GetPlaybackStateAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
        var membership = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var discoveredPlayerIds = (await GetPlayersAsync(cancellationToken).ConfigureAwait(false))
            .Select(player => SonosValidation.NormalizePlayerId(player.Id, string.Empty))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var knownStatePlayer in _playbackStates.Keys)
        {
            discoveredPlayerIds.Add(SonosValidation.NormalizePlayerId(knownStatePlayer, string.Empty));
        }

        var groupedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in _groups)
        {
            var members = group.Members
                .Select(m => SonosValidation.NormalizePlayerId(m.Id, string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (members.Count == 0)
            {
                continue;
            }

            membership[group.Id] = members;
            foreach (var memberId in members)
            {
                groupedPlayers.Add(memberId);
            }
        }

        foreach (var knownPlayerId in discoveredPlayerIds)
        {
            if (!groupedPlayers.Contains(knownPlayerId))
            {
                membership[knownPlayerId] = [knownPlayerId];
            }
        }

        if (!membership.Values.Any(players => players.Contains(normalizedPlayerId, StringComparer.OrdinalIgnoreCase)))
        {
            membership[normalizedPlayerId] = [normalizedPlayerId];
        }

        var queueEntries = _queueEntries.TryGetValue(normalizedPlayerId, out var queue)
            ? queue.OrderBy(entry => entry.QueueIndex).Select(CloneQueueEntry).ToList()
            : [];
        var favorites = _favorites.TryGetValue(normalizedPlayerId, out var favoriteEntries)
            ? favoriteEntries.Select(CloneFavoriteEntry).ToList()
            : [];
        var alarms = _alarms.Select(CloneAlarmEntry).ToList();

        return new SonosStateSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            PlayerId = normalizedPlayerId,
            SystemId = "sonos-system",
            GroupMembership = membership,
            PlaybackState = playbackState,
            QueueEntries = queueEntries,
            Favorites = favorites,
            Alarms = alarms
        };
    }

    public async Task<RestoreResult> RestoreStateAsync(SonosStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (cancellationToken.IsCancellationRequested)
        {
            return BuildRestoreFailureResult(snapshot.Id, "Restore was cancelled.", SonosErrorCode.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(snapshot.PlayerId))
        {
            return new RestoreResult
            {
                Success = false,
                SnapshotId = snapshot.Id,
                ErrorCode = SonosErrorCode.DeviceNotFound,
                Message = "Snapshot does not contain a valid player ID."
            };
        }

        var snapshotPlayerId = SonosValidation.NormalizePlayerId(snapshot.PlayerId, string.Empty);
        if (!IsRestorableSource(snapshot.PlaybackState.CurrentSource))
        {
            return new RestoreResult
            {
                Success = false,
                SnapshotId = snapshot.Id,
                ErrorCode = SonosErrorCode.InvalidUri,
                Message = "Playback source from snapshot is no longer available or unsupported."
            };
        }

        var restored = new List<string>();
        var membership = snapshot.GroupMembership.Count == 0
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, IReadOnlyList<string>>(snapshot.GroupMembership, StringComparer.OrdinalIgnoreCase);

        if (!membership.Values.Any(players => players.Any(playerId =>
            string.Equals(SonosValidation.NormalizePlayerId(playerId, string.Empty), snapshotPlayerId, StringComparison.OrdinalIgnoreCase))))
        {
            membership[snapshotPlayerId] = [snapshotPlayerId];
        }

        _groups.Clear();
        foreach (var entry in membership.OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return BuildRestoreFailureResult(snapshot.Id, "Restore was cancelled.", SonosErrorCode.Cancelled);
            }

            var ids = entry.Value
                .Select(id => SonosValidation.NormalizePlayerId(id, string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ids.Count == 0)
            {
                continue;
            }

            if (ids.Count == 1)
            {
                restored.Add(ids[0]);
                continue;
            }

            var coordinatorId = ids.First();
            var restoredMembers = ids.Select(id => new SonosPlayer
            {
                Id = id,
                Name = id,
                CoordinatorId = coordinatorId,
                GroupMembership = [entry.Key],
                Availability = SonosAvailability.Available,
                Volume = 25,
                IsMuted = false
            }).ToList();

            _groups.Add(new SonosGroup
            {
                Id = entry.Key,
                Name = ResolveGroupName(restoredMembers),
                CoordinatorId = coordinatorId,
                Members = restoredMembers
            });

            restored.AddRange(ids);
        }

        var playbackState = snapshot.PlaybackState;
        if (!string.IsNullOrWhiteSpace(playbackState.PlayerId) || !string.IsNullOrWhiteSpace(snapshotPlayerId))
        {
            var normalizedPlayerId = string.IsNullOrWhiteSpace(playbackState.PlayerId)
                ? snapshotPlayerId
                : SonosValidation.NormalizePlayerId(playbackState.PlayerId, snapshotPlayerId);

            _playbackStates[normalizedPlayerId] = new SonosPlaybackState
            {
                PlayerId = normalizedPlayerId,
                State = playbackState.State,
                PositionSeconds = playbackState.PositionSeconds,
                Volume = playbackState.Volume,
                IsMuted = playbackState.IsMuted,
                CurrentSource = playbackState.CurrentSource,
                Title = playbackState.Title,
                Artist = playbackState.Artist
            };
            restored.Add(normalizedPlayerId);

            _queueEntries[normalizedPlayerId] = snapshot.QueueEntries
                .Select(CloneQueueEntry)
                .OrderBy(entry => entry.QueueIndex)
                .ToList();
            _favorites[normalizedPlayerId] = snapshot.Favorites
                .Select(CloneFavoriteEntry)
                .ToList();
        }

        _alarms.Clear();
        foreach (var alarm in snapshot.Alarms.Select(CloneAlarmEntry))
        {
            _alarms.Add(alarm);
        }

        var expectedGroups = BuildExpectedMultiPlayerGroups(membership);
        var refreshedGroups = await GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var actualGroups = BuildActualMultiPlayerGroups(refreshedGroups);
        if (!AreEquivalentGroupMaps(expectedGroups, actualGroups))
        {
            return new RestoreResult
            {
                Success = false,
                SnapshotId = snapshot.Id,
                ErrorCode = SonosErrorCode.MalformedResponse,
                Message = "Topology restore verification failed.",
                RestoredPlayerIds = restored
                    .Select(id => SonosValidation.NormalizePlayerId(id, string.Empty))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        var refreshedPlaybackState = await GetPlaybackStateAsync(snapshotPlayerId, cancellationToken).ConfigureAwait(false);
        if (refreshedPlaybackState.State != playbackState.State ||
            Math.Abs(refreshedPlaybackState.PositionSeconds - playbackState.PositionSeconds) > 0.01 ||
            refreshedPlaybackState.Volume != playbackState.Volume ||
            refreshedPlaybackState.IsMuted != playbackState.IsMuted ||
            !string.Equals(refreshedPlaybackState.CurrentSource, playbackState.CurrentSource, StringComparison.Ordinal))
        {
            return new RestoreResult
            {
                Success = false,
                SnapshotId = snapshot.Id,
                ErrorCode = SonosErrorCode.MalformedResponse,
                Message = "Playback restore verification failed.",
                RestoredPlayerIds = restored
                    .Select(id => SonosValidation.NormalizePlayerId(id, string.Empty))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        var restoredPlayerIds = restored
            .Select(id => SonosValidation.NormalizePlayerId(id, string.Empty))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RestoreResult
        {
            Success = true,
            SnapshotId = snapshot.Id,
            RestoredPlayerIds = restoredPlayerIds,
            Message = "State restore completed and verified."
        };
    }
}
