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

    public Task<PlaybackResult> NextAsync(string playerId, CancellationToken cancellationToken = default)
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

        return _transport.NextAsync(playerId, cancellationToken);
    }

    public Task<PlaybackResult> PreviousAsync(string playerId, CancellationToken cancellationToken = default)
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

        return _transport.PreviousAsync(playerId, cancellationToken);
    }

    public Task<PlaybackResult> SeekAsync(string playerId, double positionSeconds, CancellationToken cancellationToken = default)
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

        if (positionSeconds < 0)
        {
            return Task.FromResult(new PlaybackResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.MalformedResponse,
                Message = "Seek position must be greater than or equal to zero.",
                PlayerId = playerId
            });
        }

        return _transport.SeekAsync(playerId, positionSeconds, cancellationToken);
    }

    public Task<PlaybackResult> SetVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default)
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

        if (volume < 0 || volume > 100)
        {
            return Task.FromResult(new PlaybackResult
            {
                Success = false,
                ErrorCode = SonosErrorCode.MalformedResponse,
                Message = "Volume must be between 0 and 100.",
                PlayerId = playerId
            });
        }

        return _transport.SetVolumeAsync(playerId, volume, cancellationToken);
    }

    public Task<PlaybackResult> SetMuteAsync(string playerId, bool isMuted, CancellationToken cancellationToken = default)
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

        return _transport.SetMuteAsync(playerId, isMuted, cancellationToken);
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

        return _transport.GetQueueAsync(playerId, cancellationToken);
    }

    public Task<PlaybackResult> EnqueueAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        if (!SonosValidation.IsValidMediaUrl(mediaUrl))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.InvalidUri, Message = "Queue item must be a valid HTTP/HTTPS URL." });
        }

        return _transport.EnqueueAsync(playerId, mediaUrl, cancellationToken);
    }

    public Task<PlaybackResult> ClearQueueAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        return _transport.ClearQueueAsync(playerId, cancellationToken);
    }

    public Task<PlaybackResult> PlayQueueItemAsync(string playerId, int queueIndex, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        if (queueIndex < 0)
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.MalformedResponse, Message = "Queue index must be non-negative." });
        }

        return _transport.PlayQueueItemAsync(playerId, queueIndex, cancellationToken);
    }
}

public sealed class SonosFavoritesService : ISonosFavoritesService
{
    private readonly ISonosTransportService _transport;

    public SonosFavoritesService(ISonosTransportService transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<IReadOnlyList<FavoriteEntry>> GetFavoritesAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        return _transport.GetFavoritesAsync(playerId, cancellationToken);
    }

    public Task<PlaybackResult> PlayFavoriteAsync(string playerId, string favoriteId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Player ID is required." });
        }

        if (string.IsNullOrWhiteSpace(favoriteId))
        {
            return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Favorite ID or name is required." });
        }

        return _transport.PlayFavoriteAsync(playerId, favoriteId, cancellationToken);
    }
}

public sealed class SonosAlarmService : ISonosAlarmService
{
    private readonly ISonosTransportService _transport;

    public SonosAlarmService(ISonosTransportService transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<IReadOnlyList<AlarmEntry>> GetAlarmsAsync(CancellationToken cancellationToken = default)
    {
        return _transport.GetAlarmsAsync(cancellationToken);
    }

    public Task<AlarmEntry> CreateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        return _transport.CreateAlarmAsync(alarm, cancellationToken);
    }

    public Task<AlarmEntry> UpdateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        return _transport.UpdateAlarmAsync(alarm, cancellationToken);
    }

    public Task<bool> DeleteAlarmAsync(string alarmId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return Task.FromResult(false);
        }

        return _transport.DeleteAlarmAsync(alarmId, cancellationToken);
    }
}
