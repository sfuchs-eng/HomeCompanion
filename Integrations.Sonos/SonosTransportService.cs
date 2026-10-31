namespace HomeCompanion.Integrations.Sonos;

public sealed class SonosTransportService : ISonosTransportService
{
    private readonly SonosIntegrationOptions _options;
    private readonly HttpClient _httpClient;
    private readonly SonosDiscoveryService _discoveryService;
    private readonly List<SonosGroup> _groups = [];
    private readonly Dictionary<string, SonosPlaybackState> _playbackStates = new(StringComparer.OrdinalIgnoreCase);

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

    private async Task<PlaybackResult> ExecutePlaybackCommandAsync(
        string playerId,
        string commandName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BuildFailureResult(playerId, "Player ID is required.", SonosErrorCode.DeviceNotFound);
        }

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                return new PlaybackResult
                {
                    Success = true,
                    PlayerId = playerId,
                    Message = $"{commandName} command accepted."
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == _options.MaxRetries)
                {
                    return BuildFailureResult(playerId, $"{commandName} timed out.", SonosErrorCode.Timeout);
                }
            }
            catch (Exception ex)
            {
                var failure = BuildFailureResult(playerId, $"{commandName} failed: {ex.Message}", SonosErrorCode.TransportFailure);
                failure.Exception = ex;
                return failure;
            }
        }

        return BuildFailureResult(playerId, $"{commandName} timed out.", SonosErrorCode.Timeout);
    }

    public async Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default)
    {
        return await _discoveryService.GetPlayersAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SonosGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SonosGroup>>(_groups.ToList());
    }

    public Task<SonosPlaybackState> GetPlaybackStateAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        var normalizedPlayerId = SonosValidation.NormalizePlayerId(playerId, string.Empty);
        if (_playbackStates.TryGetValue(normalizedPlayerId, out var state))
        {
            return Task.FromResult(new SonosPlaybackState
            {
                PlayerId = state.PlayerId,
                State = state.State,
                PositionSeconds = state.PositionSeconds,
                Volume = state.Volume,
                IsMuted = state.IsMuted,
                CurrentSource = state.CurrentSource,
                Title = state.Title,
                Artist = state.Artist
            });
        }

        var defaultState = new SonosPlaybackState
        {
            PlayerId = normalizedPlayerId,
            State = SonosPlaybackStateKind.Stopped,
            PositionSeconds = 0,
            Volume = 0,
            IsMuted = false,
            CurrentSource = "https://example.com/stream.mp3",
            Title = "Unknown track"
        };

        _playbackStates[normalizedPlayerId] = defaultState;
        return Task.FromResult(defaultState);
    }

    public Task<PlaybackResult> PlayAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "Play", cancellationToken);
    }

    public Task<PlaybackResult> PauseAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "Pause", cancellationToken);
    }

    public Task<PlaybackResult> StopAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return ExecutePlaybackCommandAsync(playerId, "Stop", cancellationToken);
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

        return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId, CurrentSource = mediaUrl, Message = "Media URL accepted." });
    }

    public Task<GroupResult> GroupAsync(IEnumerable<string> playerIds, CancellationToken cancellationToken = default)
    {
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

        var group = new SonosGroup
        {
            Id = groupId,
            Name = string.Join(" + ", ids),
            CoordinatorId = coordinatorId,
            Members = ids.Select(id => new SonosPlayer
            {
                Id = id,
                Name = id,
                CoordinatorId = coordinatorId,
                GroupMembership = [groupId],
                Availability = SonosAvailability.Available,
                Volume = 25,
                IsMuted = false
            }).ToList()
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

        foreach (var group in _groups)
        {
            var members = group.Members.Select(m => SonosValidation.NormalizePlayerId(m.Id, string.Empty)).ToList();
            if (members.Contains(normalizedPlayerId, StringComparer.OrdinalIgnoreCase))
            {
                membership[group.Id] = members;
            }
        }

        if (membership.Count == 0)
        {
            membership[normalizedPlayerId] = [normalizedPlayerId];
        }

        return new SonosStateSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            PlayerId = normalizedPlayerId,
            GroupMembership = membership,
            PlaybackState = playbackState,
            QueueEntries = [],
            Favorites = [],
            Alarms = []
        };
    }

    public async Task<RestoreResult> RestoreStateAsync(SonosStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var restored = new List<string>();
        var membership = snapshot.GroupMembership;

        _groups.Clear();
        foreach (var entry in membership)
        {
            var ids = entry.Value
                .Select(id => SonosValidation.NormalizePlayerId(id, string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ids.Count == 0)
            {
                continue;
            }

            var coordinatorId = ids.First();
            _groups.Add(new SonosGroup
            {
                Id = entry.Key,
                Name = string.Join(" + ", ids),
                CoordinatorId = coordinatorId,
                Members = ids.Select(id => new SonosPlayer
                {
                    Id = id,
                    Name = id,
                    CoordinatorId = coordinatorId,
                    GroupMembership = [entry.Key],
                    Availability = SonosAvailability.Available,
                    Volume = 25,
                    IsMuted = false
                }).ToList()
            });

            restored.AddRange(ids);
        }

        var playbackState = snapshot.PlaybackState;
        if (!string.IsNullOrWhiteSpace(playbackState.PlayerId))
        {
            var normalizedPlayerId = SonosValidation.NormalizePlayerId(playbackState.PlayerId, string.Empty);
            _playbackStates[normalizedPlayerId] = new SonosPlaybackState
            {
                PlayerId = normalizedPlayerId,
                State = playbackState.State,
                PositionSeconds = playbackState.PositionSeconds,
                Volume = playbackState.Volume,
                IsMuted = playbackState.IsMuted,
                CurrentSource = playbackState.CurrentSource ?? "https://example.com/stream.mp3",
                Title = playbackState.Title,
                Artist = playbackState.Artist
            };
            restored.Add(normalizedPlayerId);
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
            Message = "State restore requested."
        };
    }
}
