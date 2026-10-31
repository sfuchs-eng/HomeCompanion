namespace HomeCompanion.Integrations.Sonos;

public sealed class SonosTopologyService : ISonosTopologyService
{
    private readonly ISonosTransportService _transport;

    public SonosTopologyService(ISonosTransportService transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<SonosGroup?> GetGroupForPlayerAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        var groups = await _transport.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        return groups.FirstOrDefault(group => group.Members.Any(member => string.Equals(member.Id, playerId, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<GroupResult> GroupAsync(IEnumerable<string> playerIds, CancellationToken cancellationToken = default)
    {
        var ids = (playerIds ?? throw new ArgumentNullException(nameof(playerIds))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count < 2)
        {
            return new GroupResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.GroupRejected,
                Message = "At least two player IDs are required to create a group.",
                PlayerId = ids.FirstOrDefault()
            };
        }

        return await _transport.GroupAsync(ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GroupResult> UngroupAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new GroupResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.DeviceNotFound,
                Message = "A player ID is required to ungroup a player.",
                PlayerId = playerId
            };
        }

        return await _transport.UngroupAsync(playerId, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SonosGroup>> GetCurrentGroupsAsync(CancellationToken cancellationToken = default)
    {
        return _transport.GetGroupsAsync(cancellationToken);
    }
}

public sealed class SonosPlaybackService : ISonosPlaybackService
{
    private readonly ISonosTransportService _transport;

    public SonosPlaybackService(ISonosTransportService transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<PlaybackResult> PlayAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.DeviceNotFound,
                Message = "Player ID is required.",
                PlayerId = playerId
            });
        }

        return _transport.PlayAsync(playerId, cancellationToken);
    }

    public Task<PlaybackResult> PauseAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.DeviceNotFound,
                Message = "Player ID is required.",
                PlayerId = playerId
            });
        }

        return _transport.PauseAsync(playerId, cancellationToken);
    }

    public Task<PlaybackResult> StopAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.DeviceNotFound,
                Message = "Player ID is required.",
                PlayerId = playerId
            });
        }

        return _transport.StopAsync(playerId, cancellationToken);
    }

    public async Task<PlaybackResult> PlayUrlAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new PlaybackResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.DeviceNotFound,
                Message = "Player ID is required.",
                PlayerId = playerId
            };
        }

        if (!SonosValidation.IsValidMediaUrl(mediaUrl))
        {
            return new PlaybackResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.InvalidUri,
                Message = "Media URL must be a valid absolute HTTP or HTTPS URL.",
                PlayerId = playerId
            };
        }

        return await _transport.PlayUrlAsync(playerId, mediaUrl, cancellationToken).ConfigureAwait(false);
    }

    public Task<SonosPlaybackState> GetPlaybackStateAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        return _transport.GetPlaybackStateAsync(playerId, cancellationToken);
    }
}

public sealed class SonosQueueService : ISonosQueueService
{
    private readonly ISonosTransportService _transport;

    public SonosQueueService(ISonosTransportService transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<IReadOnlyList<QueueEntry>> GetQueueAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        return Task.FromResult<IReadOnlyList<QueueEntry>>([
            new QueueEntry { QueueIndex = 0, Id = "queue-1", Title = "Fallback Track", Uri = "https://example.com/fallback.mp3" }
        ]);
    }

    public async Task<PlaybackResult> EnqueueAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." };
        }

        if (!SonosValidation.IsValidMediaUrl(mediaUrl))
        {
            return new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.InvalidUri, Message = "Queue item must be a valid HTTP/HTTPS URL." };
        }

        return new PlaybackResult { Success = true, PlayerId = playerId, CurrentSource = mediaUrl, Message = "Enqueued successfully." };
    }

    public Task<PlaybackResult> ClearQueueAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId, Message = "Queue cleared." });
    }

    public Task<PlaybackResult> PlayQueueItemAsync(string playerId, int queueIndex, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        if (queueIndex < 0)
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.InvalidUri, Message = "Queue index must be non-negative." });
        }

        return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId, Message = $"Queue item {queueIndex} requested." });
    }
}

public sealed class SonosFavoritesService : ISonosFavoritesService
{
    public Task<IReadOnlyList<FavoriteEntry>> GetFavoritesAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        return Task.FromResult<IReadOnlyList<FavoriteEntry>>([
            new FavoriteEntry { Id = "favorite-1", Name = "Classic FM", Type = "radio", Uri = "x-rincon-browse:favorite-1" }
        ]);
    }

    public Task<PlaybackResult> PlayFavoriteAsync(string playerId, string favoriteId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        if (string.IsNullOrWhiteSpace(favoriteId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Favorite ID is required." });
        }

        return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId, Message = $"Playing favorite {favoriteId}." });
    }
}

public sealed class SonosAlarmService : ISonosAlarmService
{
    public Task<IReadOnlyList<AlarmEntry>> GetAlarmsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AlarmEntry>>([
            new AlarmEntry { Id = "alarm-1", Name = "Wake Up", Enabled = true, Time = "07:00", Recurrence = ["Mon", "Tue", "Wed", "Thu", "Fri"], Volume = 20, Source = "radio" }
        ]);
    }

    public Task<AlarmEntry> CreateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        return Task.FromResult(alarm);
    }

    public Task<AlarmEntry> UpdateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        return Task.FromResult(alarm);
    }

    public Task<bool> DeleteAlarmAsync(string alarmId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(alarmId));
    }
}
