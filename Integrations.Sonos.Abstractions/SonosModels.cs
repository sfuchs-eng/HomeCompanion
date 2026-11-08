namespace HomeCompanion.Integrations.Sonos;

public enum SonosAvailability
{
    Unknown = 0,
    Available = 1,
    Offline = 2
}

public enum SonosPlaybackStateKind
{
    Unknown = 0,
    Playing = 1,
    Paused = 2,
    Stopped = 3,
    Transitioning = 4
}

public enum SonosErrorCode
{
    None = 0,
    DeviceNotFound = 1,
    RoomNotFound = 2,
    PlayerUnavailable = 3,
    GroupRejected = 4,
    InvalidUri = 5,
    Timeout = 6,
    MalformedResponse = 7,
    TransportFailure = 8,
    Cancelled = 9,
    Unknown = 10
}

public sealed class SonosPlayer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CoordinatorId { get; set; } = string.Empty;
    public string? HouseholdId { get; set; }
    public string? ZoneName { get; set; }
    public IReadOnlyList<string> GroupMembership { get; set; } = [];
    public SonosAvailability Availability { get; set; } = SonosAvailability.Unknown;
    public int Volume { get; set; }
    public bool IsMuted { get; set; }
    public string? RoomLabel => Name;
}

public sealed class SonosGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CoordinatorId { get; set; } = string.Empty;
    public IReadOnlyList<SonosPlayer> Members { get; set; } = [];
}

public sealed class SonosHousehold
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> PlayerIds { get; set; } = [];
}

public sealed class SonosSystem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<SonosPlayer> Players { get; set; } = [];
    public IReadOnlyList<SonosGroup> Groups { get; set; } = [];
    public SonosHousehold? Household { get; set; }
}

public sealed class SonosPlaybackState
{
    public string PlayerId { get; set; } = string.Empty;
    public SonosPlaybackStateKind State { get; set; } = SonosPlaybackStateKind.Unknown;
    public double PositionSeconds { get; set; }
    public int Volume { get; set; }
    public bool IsMuted { get; set; }
    public string? CurrentSource { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
}

public sealed class QueueEntry
{
    public int QueueIndex { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Uri { get; set; } = string.Empty;
    public string? Metadata { get; set; }
}

public sealed class FavoriteEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Description { get; set; }
    public string? Uri { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed class AlarmEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Time { get; set; }
    public IReadOnlyList<string> Recurrence { get; set; } = [];
    public int Volume { get; set; }
    public string? Source { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed class SonosStateSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PlayerId { get; set; } = string.Empty;
    public string? SystemId { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GroupMembership { get; set; } = new Dictionary<string, IReadOnlyList<string>>();
    public SonosPlaybackState PlaybackState { get; set; } = new();
    public IReadOnlyList<QueueEntry> QueueEntries { get; set; } = [];
    public IReadOnlyList<FavoriteEntry> Favorites { get; set; } = [];
    public IReadOnlyList<AlarmEntry> Alarms { get; set; } = [];
}

public abstract class SonosOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SonosErrorCode ErrorCode { get; set; } = SonosErrorCode.None;
    public Exception? Exception { get; set; }
}

public sealed class PlaybackResult : SonosOperationResult
{
    public string? PlayerId { get; set; }
    public string? CurrentSource { get; set; }
}

public sealed class GroupResult : SonosOperationResult
{
    public string? PlayerId { get; set; }
    public string? GroupId { get; set; }
    public SonosGroup? Group { get; set; }
}

public sealed class RestoreResult : SonosOperationResult
{
    public string? SnapshotId { get; set; }
    public IReadOnlyList<string> RestoredPlayerIds { get; set; } = [];
}

public interface ISonosTransportService
{
    Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SonosGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QueueEntry>> GetQueueAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> EnqueueAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default);
    Task<PlaybackResult> ClearQueueAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PlayQueueItemAsync(string playerId, int queueIndex, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FavoriteEntry>> GetFavoritesAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PlayFavoriteAsync(string playerId, string favoriteId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlarmEntry>> GetAlarmsAsync(CancellationToken cancellationToken = default);
    Task<AlarmEntry> CreateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default);
    Task<AlarmEntry> UpdateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default);
    Task<bool> DeleteAlarmAsync(string alarmId, CancellationToken cancellationToken = default);
    Task<SonosPlaybackState> GetPlaybackStateAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PlayAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PauseAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> StopAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> NextAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PreviousAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> SeekAsync(string playerId, double positionSeconds, CancellationToken cancellationToken = default);
    Task<PlaybackResult> SetVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default);
    Task<PlaybackResult> SetMuteAsync(string playerId, bool isMuted, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PlayUrlAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default);
    Task<GroupResult> GroupAsync(IEnumerable<string> playerIds, CancellationToken cancellationToken = default);
    Task<GroupResult> UngroupAsync(string playerId, CancellationToken cancellationToken = default);
    Task<SonosStateSnapshot> CaptureStateAsync(string playerId, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreStateAsync(SonosStateSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface ISonosDiscoveryService
{
    Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default);
    Task<SonosSystem> GetSystemAsync(CancellationToken cancellationToken = default);
}

public interface ISonosSystemService : ISonosDiscoveryService
{
    Task<IReadOnlyList<SonosGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<SonosStateSnapshot> CaptureStateAsync(string playerId, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreStateAsync(SonosStateSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface ISonosTopologyService
{
    Task<SonosGroup?> GetGroupForPlayerAsync(string playerId, CancellationToken cancellationToken = default);
    Task<GroupResult> GroupAsync(IEnumerable<string> playerIds, CancellationToken cancellationToken = default);
    Task<GroupResult> UngroupAsync(string playerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SonosGroup>> GetCurrentGroupsAsync(CancellationToken cancellationToken = default);
}

public interface ISonosPlaybackService
{
    Task<PlaybackResult> PlayAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PauseAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> StopAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> NextAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PreviousAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> SeekAsync(string playerId, double positionSeconds, CancellationToken cancellationToken = default);
    Task<PlaybackResult> SetVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default);
    Task<PlaybackResult> SetMuteAsync(string playerId, bool isMuted, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PlayUrlAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default);
    Task<SonosPlaybackState> GetPlaybackStateAsync(string playerId, CancellationToken cancellationToken = default);
}

public interface ISonosQueueService
{
    Task<IReadOnlyList<QueueEntry>> GetQueueAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> EnqueueAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default);
    Task<PlaybackResult> ClearQueueAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PlayQueueItemAsync(string playerId, int queueIndex, CancellationToken cancellationToken = default);
}

public interface ISonosFavoritesService
{
    Task<IReadOnlyList<FavoriteEntry>> GetFavoritesAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PlayFavoriteAsync(string playerId, string favoriteId, CancellationToken cancellationToken = default);
}

public interface ISonosAlarmService
{
    Task<IReadOnlyList<AlarmEntry>> GetAlarmsAsync(CancellationToken cancellationToken = default);
    Task<AlarmEntry> CreateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default);
    Task<AlarmEntry> UpdateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default);
    Task<bool> DeleteAlarmAsync(string alarmId, CancellationToken cancellationToken = default);
}

public interface ISonosStateRestoreService
{
    Task<SonosStateSnapshot> CaptureStateAsync(string playerId, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreStateAsync(SonosStateSnapshot snapshot, CancellationToken cancellationToken = default);
}

public sealed class SonosIntegrationOptions
{
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxRetries { get; set; } = 2;
    public string? Hostname { get; set; }
    public string? NetworkInterface { get; set; }
}
