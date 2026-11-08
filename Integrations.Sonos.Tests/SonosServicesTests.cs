using HomeCompanion.Integrations.Sonos;

namespace HomeCompanion.Integrations.Sonos.Tests;

public class SonosServicesTests
{
    [Test]
    public void NormalizePlayerId_StripsUuidAndLowercases()
    {
        var normalized = SonosDiscoveryService.NormalizePlayerId("UUID:Kitchen-Player", "fallback");

        Assert.That(normalized, Is.EqualTo("kitchen-player"));
    }

    [Test]
    public async Task GetPlayersAndGroups_ReturnsNormalizedState()
    {
        var transport = new FakeSonosTransport();
        var system = new SonosSystemManager(transport);

        var players = await system.GetPlayersAsync();
        var groups = await system.GetGroupsAsync();
        var fullSystem = await system.GetSystemAsync();

        Assert.That(players.Count, Is.EqualTo(3));
        Assert.That(groups.Count, Is.EqualTo(2));
        Assert.That(players.Select(p => p.Name).ToArray(), Does.Contain("Kitchen"));
        Assert.That(groups[0].Members.Select(m => m.Id), Does.Contain("player-1"));
        Assert.That(fullSystem.Household, Is.Not.Null);
        Assert.That(fullSystem.Household!.PlayerIds, Does.Contain("player-1"));
        Assert.That(fullSystem.Household.PlayerIds, Does.Contain("player-3"));
        Assert.That(players.Single(player => player.Id == "player-1").CoordinatorId, Is.Not.EqualTo(players.Single(player => player.Id == "player-3").CoordinatorId));
        Assert.That(players.Single(player => player.Id == "player-1").GroupMembership, Is.EquivalentTo(["group-1"]));
        Assert.That(players.Single(player => player.Id == "player-3").GroupMembership, Is.EquivalentTo(["group-2"]));
    }

    [Test]
    public async Task SystemManager_PopulatesHouseholdWhenSingleHouseholdExists()
    {
        var transport = new FakeSonosTransport();
        var manager = new SonosSystemManager(transport);

        var system = await manager.GetSystemAsync();

        Assert.That(system.Household, Is.Not.Null);
        Assert.That(system.Household!.Id, Is.EqualTo("household-1"));
        Assert.That(system.Household.PlayerIds, Does.Contain("player-1"));
        Assert.That(system.Household.PlayerIds, Does.Contain("player-2"));
    }

    [Test]
    public async Task DiscoveryService_WithTransport_PopulatesHouseholdWhenResolvable()
    {
        var transport = new FakeSonosTransport();
        var discovery = new SonosDiscoveryService(transport);

        var system = await discovery.GetSystemAsync();

        Assert.That(system.Household, Is.Not.Null);
        Assert.That(system.Household!.Id, Is.EqualTo("household-1"));
        Assert.That(system.Household.PlayerIds.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task GroupAsync_RejectsSinglePlayer()
    {
        var transport = new FakeSonosTransport();
        var topology = new SonosTopologyService(transport);

        var result = await topology.GroupAsync(["player-1"]);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(SonosErrorCode.GroupRejected));
    }

    [Test]
    public async Task GroupAsync_CombinesPlayersAndRefreshesTopology()
    {
        var transport = new FakeSonosTransport();
        var topology = new SonosTopologyService(transport);

        var result = await topology.GroupAsync(["player-1", "player-2"]);

        Assert.That(result.Success, Is.True);
        Assert.That(result.GroupId, Is.EqualTo("group-1"));
        Assert.That(result.Group?.Members.Select(m => m.Id), Is.EquivalentTo(["player-1", "player-2"]));

        var refreshedGroups = await transport.GetGroupsAsync();
        Assert.That(refreshedGroups.Any(g => g.Members.Any(m => m.Id == "player-1") && g.Members.Any(m => m.Id == "player-2")), Is.True);
    }

    [Test]
    public async Task Transport_GroupAsync_RefreshesTopology()
    {
        var transport = new SonosTransportService();

        var result = await transport.GroupAsync(["player-1", "player-2"]);

        Assert.That(result.Success, Is.True);
        var groups = await transport.GetGroupsAsync();
        Assert.That(groups.Any(g => g.Members.Any(m => m.Id == "player-1") && g.Members.Any(m => m.Id == "player-2")), Is.True);
    }

    [Test]
    public async Task Transport_UngroupAsync_RefreshesTopology()
    {
        var transport = new SonosTransportService();
        await transport.GroupAsync(["player-1", "player-2"]);

        var ungroupResult = await transport.UngroupAsync("player-2");
        var groupsAfterUngroup = await transport.GetGroupsAsync();

        Assert.That(ungroupResult.Success, Is.True);
        Assert.That(groupsAfterUngroup.Any(group => group.Members.Any(member => member.Id == "player-2")), Is.False);
    }

    [Test]
    public async Task PlayUrlAsync_RejectsInvalidUri()
    {
        var transport = new FakeSonosTransport();
        var playback = new SonosPlaybackService(transport);

        var result = await playback.PlayUrlAsync("player-1", "not-a-valid-uri");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(SonosErrorCode.InvalidUri));
    }

    [Test]
    public async Task PlaybackService_SupportsNextPreviousSeekVolumeAndMute()
    {
        var transport = new SonosTransportService();
        var playback = new SonosPlaybackService(transport);

        var next = await playback.NextAsync("player-1");
        var previous = await playback.PreviousAsync("player-1");
        var seek = await playback.SeekAsync("player-1", 95);
        var volume = await playback.SetVolumeAsync("player-1", 41);
        var mute = await playback.SetMuteAsync("player-1", true);
        var state = await playback.GetPlaybackStateAsync("player-1");

        Assert.That(next.Success, Is.True);
        Assert.That(previous.Success, Is.True);
        Assert.That(seek.Success, Is.True);
        Assert.That(volume.Success, Is.True);
        Assert.That(mute.Success, Is.True);
        Assert.That(state.PositionSeconds, Is.EqualTo(95));
        Assert.That(state.Volume, Is.EqualTo(41));
        Assert.That(state.IsMuted, Is.True);
    }

    [Test]
    public async Task QueueService_SupportsRetrieveEnqueuePlayAndClear()
    {
        var transport = new SonosTransportService();
        var queueService = new SonosQueueService(transport);

        var initialQueue = await queueService.GetQueueAsync("player-1");
        var enqueueResult = await queueService.EnqueueAsync("player-1", "https://example.com/queue/new-track.mp3");
        var updatedQueue = await queueService.GetQueueAsync("player-1");
        var playQueueResult = await queueService.PlayQueueItemAsync("player-1", updatedQueue.Count - 1);
        var playbackState = await transport.GetPlaybackStateAsync("player-1");
        var clearQueueResult = await queueService.ClearQueueAsync("player-1");
        var clearedQueue = await queueService.GetQueueAsync("player-1");

        Assert.That(initialQueue.Count, Is.GreaterThanOrEqualTo(0));
        Assert.That(enqueueResult.Success, Is.True);
        Assert.That(updatedQueue.Count, Is.EqualTo(initialQueue.Count + 1));
        Assert.That(updatedQueue.Select(q => q.QueueIndex), Is.EqualTo(Enumerable.Range(0, updatedQueue.Count)));
        Assert.That(playQueueResult.Success, Is.True);
        Assert.That(playbackState.CurrentSource, Is.EqualTo("https://example.com/queue/new-track.mp3"));
        Assert.That(playbackState.State, Is.EqualTo(SonosPlaybackStateKind.Playing));
        Assert.That(clearQueueResult.Success, Is.True);
        Assert.That(clearedQueue.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task QueueService_ValidatesInvalidUrisAndIndexes()
    {
        var transport = new SonosTransportService();
        var queueService = new SonosQueueService(transport);

        var invalidUriResult = await queueService.EnqueueAsync("player-1", "not-valid-uri");
        var negativeIndexResult = await queueService.PlayQueueItemAsync("player-1", -1);
        var outOfRangeResult = await queueService.PlayQueueItemAsync("player-1", 999);

        Assert.That(invalidUriResult.Success, Is.False);
        Assert.That(invalidUriResult.ErrorCode, Is.EqualTo(SonosErrorCode.InvalidUri));

        Assert.That(negativeIndexResult.Success, Is.False);
        Assert.That(negativeIndexResult.ErrorCode, Is.EqualTo(SonosErrorCode.MalformedResponse));

        Assert.That(outOfRangeResult.Success, Is.False);
        Assert.That(outOfRangeResult.ErrorCode, Is.EqualTo(SonosErrorCode.MalformedResponse));
    }

    [Test]
    public async Task FavoritesService_SupportsListAndPlayByIdOrName()
    {
        var transport = new SonosTransportService();
        var favoritesService = new SonosFavoritesService(transport);

        var favorites = await favoritesService.GetFavoritesAsync("player-1");
        var first = favorites.First();

        var playById = await favoritesService.PlayFavoriteAsync("player-1", first.Id);
        var playByName = await favoritesService.PlayFavoriteAsync("player-1", first.Name);
        var playbackState = await transport.GetPlaybackStateAsync("player-1");

        Assert.That(favorites.Count, Is.GreaterThan(0));
        Assert.That(first.Id, Is.Not.Empty);
        Assert.That(first.Name, Is.Not.Empty);
        Assert.That(first.Uri, Is.Not.Null.And.Not.Empty);

        Assert.That(playById.Success, Is.True);
        Assert.That(playByName.Success, Is.True);
        Assert.That(playByName.Message, Does.Contain("Playing favorite").IgnoreCase);
        Assert.That(playbackState.State, Is.EqualTo(SonosPlaybackStateKind.Playing));
        Assert.That(playbackState.CurrentSource, Is.EqualTo(first.Uri));
    }

    [Test]
    public async Task FavoritesService_ValidatesUnknownFavorite()
    {
        var transport = new SonosTransportService();
        var favoritesService = new SonosFavoritesService(transport);

        var result = await favoritesService.PlayFavoriteAsync("player-1", "missing-favorite");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(SonosErrorCode.DeviceNotFound));
        Assert.That(result.Message, Does.Contain("not found").IgnoreCase);
    }

    [Test]
    public async Task AlarmService_ReturnsMetadataAndSupportsCreateUpdateDelete()
    {
        var transport = new SonosTransportService();
        var alarmService = new SonosAlarmService(transport);

        var initialAlarms = await alarmService.GetAlarmsAsync();
        Assert.That(initialAlarms.Count, Is.GreaterThan(0));

        var created = await alarmService.CreateAlarmAsync(new AlarmEntry
        {
            Name = "Lunch Reminder",
            Enabled = true,
            Time = "12:30",
            Recurrence = ["Mon", "Wed", "Fri"],
            Volume = 30,
            Source = "radio",
            MetadataJson = "{\"kind\":\"test\"}"
        });

        Assert.That(created.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(created.Name, Is.EqualTo("Lunch Reminder"));
        Assert.That(created.Time, Is.EqualTo("12:30"));
        Assert.That(created.Recurrence, Is.EquivalentTo(["Mon", "Wed", "Fri"]));

        var updated = await alarmService.UpdateAlarmAsync(new AlarmEntry
        {
            Id = created.Id,
            Name = "Lunch Reminder Updated",
            Enabled = false,
            Time = "12:45",
            Recurrence = ["Tue"],
            Volume = 22,
            Source = "playlist"
        });

        Assert.That(updated.Name, Is.EqualTo("Lunch Reminder Updated"));
        Assert.That(updated.Enabled, Is.False);
        Assert.That(updated.Time, Is.EqualTo("12:45"));
        Assert.That(updated.Volume, Is.EqualTo(22));

        var alarmsAfterUpdate = await alarmService.GetAlarmsAsync();
        var persisted = alarmsAfterUpdate.Single(alarm => alarm.Id == created.Id);
        Assert.That(persisted.Name, Is.EqualTo("Lunch Reminder Updated"));

        var deleteResult = await alarmService.DeleteAlarmAsync(created.Id);
        Assert.That(deleteResult, Is.True);

        var alarmsAfterDelete = await alarmService.GetAlarmsAsync();
        Assert.That(alarmsAfterDelete.Any(alarm => alarm.Id == created.Id), Is.False);

        var deleteMissing = await alarmService.DeleteAlarmAsync("missing-alarm");
        Assert.That(deleteMissing, Is.False);
    }

    [Test]
    public void AlarmService_RejectsInvalidAlarmDefinition()
    {
        var transport = new SonosTransportService();
        var alarmService = new SonosAlarmService(transport);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await alarmService.CreateAlarmAsync(new AlarmEntry
            {
                Name = "",
                Enabled = true,
                Time = "99:99",
                Recurrence = [],
                Volume = 180,
                Source = "radio"
            }));
    }

    [Test]
    public async Task CaptureStateAsync_StoresTopologyAndPlaybackState()
    {
        var transport = new FakeSonosTransport();
        var manager = new SonosSystemManager(transport);

        var snapshot = await manager.CaptureStateAsync("player-1");

        Assert.That(snapshot.GroupMembership.Count, Is.GreaterThan(0));
        Assert.That(snapshot.PlaybackState.PlayerId, Is.EqualTo("player-1"));
        Assert.That(snapshot.PlaybackState.CurrentSource, Is.Not.Null.And.Not.Empty);
        Assert.That(snapshot.PlaybackState.PositionSeconds, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task RestoreStateAsync_RestoresOriginalTopologyAndPlayback()
    {
        var transport = new FakeSonosTransport();
        var manager = new SonosSystemManager(transport);

        var original = await manager.CaptureStateAsync("player-1");
        var result = await manager.RestoreStateAsync(original);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RestoredPlayerIds, Does.Contain("player-1"));
        Assert.That(result.Message, Does.Contain("restore").IgnoreCase);
    }

    [Test]
    public async Task Transport_CaptureState_TracksGroupTopologyAndSource()
    {
        var transport = new SonosTransportService();
        await transport.GroupAsync(["player-1", "player-2"]);

        var snapshot = await transport.CaptureStateAsync("player-1");

        Assert.That(snapshot.GroupMembership.Values.SelectMany(v => v), Does.Contain("player-1"));
        Assert.That(snapshot.PlaybackState.CurrentSource, Is.Not.Null.And.Not.Empty);
        Assert.That(snapshot.PlaybackState.PositionSeconds, Is.GreaterThanOrEqualTo(0));
        Assert.That(snapshot.QueueEntries.Count, Is.GreaterThan(0));
        Assert.That(snapshot.Favorites.Count, Is.GreaterThan(0));
        Assert.That(snapshot.Alarms.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task Transport_RestoreState_AppliesToplogyAndPlaybackState()
    {
        var transport = new SonosTransportService();
        await transport.GroupAsync(["player-1", "player-2"]);

        var snapshot = await transport.CaptureStateAsync("player-1");
        var restoreResult = await transport.RestoreStateAsync(snapshot);

        Assert.That(restoreResult.Success, Is.True);
        Assert.That(restoreResult.RestoredPlayerIds, Does.Contain("player-1"));
        Assert.That(restoreResult.Message, Does.Contain("verified").IgnoreCase);
        Assert.That((await transport.GetGroupsAsync()).Any(g => g.Members.Any(m => m.Id == "player-1") && g.Members.Any(m => m.Id == "player-2")), Is.True);

        var playbackState = await transport.GetPlaybackStateAsync("player-1");
        Assert.That(playbackState.State, Is.EqualTo(snapshot.PlaybackState.State));
        Assert.That(playbackState.PositionSeconds, Is.EqualTo(snapshot.PlaybackState.PositionSeconds));
        Assert.That(playbackState.Volume, Is.EqualTo(snapshot.PlaybackState.Volume));
        Assert.That(playbackState.IsMuted, Is.EqualTo(snapshot.PlaybackState.IsMuted));
        Assert.That(playbackState.CurrentSource, Is.EqualTo(snapshot.PlaybackState.CurrentSource));
    }

    [Test]
    public async Task Transport_RestoreState_IsDeterministicAndIdempotent()
    {
        var transport = new SonosTransportService();
        await transport.GroupAsync(["player-1", "player-2"]);

        var snapshot = await transport.CaptureStateAsync("player-1");

        await transport.UngroupAsync("player-1");
        await transport.SeekAsync("player-1", 123);
        await transport.SetVolumeAsync("player-1", 52);
        await transport.SetMuteAsync("player-1", true);
        await transport.PlayUrlAsync("player-1", "https://example.com/temporary.mp3");

        var firstRestore = await transport.RestoreStateAsync(snapshot);
        var secondRestore = await transport.RestoreStateAsync(snapshot);

        Assert.That(firstRestore.Success, Is.True);
        Assert.That(secondRestore.Success, Is.True);

        var groups = await transport.GetGroupsAsync();
        Assert.That(groups.Any(g => g.Members.Any(m => m.Id == "player-1") && g.Members.Any(m => m.Id == "player-2")), Is.True);

        var playbackState = await transport.GetPlaybackStateAsync("player-1");
        Assert.That(playbackState.PositionSeconds, Is.EqualTo(snapshot.PlaybackState.PositionSeconds));
        Assert.That(playbackState.Volume, Is.EqualTo(snapshot.PlaybackState.Volume));
        Assert.That(playbackState.IsMuted, Is.EqualTo(snapshot.PlaybackState.IsMuted));
        Assert.That(playbackState.CurrentSource, Is.EqualTo(snapshot.PlaybackState.CurrentSource));
    }

    [Test]
    public async Task Transport_RestoreState_FailsWhenPlaybackSourceIsUnavailable()
    {
        var transport = new SonosTransportService();
        var snapshot = await transport.CaptureStateAsync("player-1");
        snapshot.PlaybackState.CurrentSource = "unavailable://track";

        var restoreResult = await transport.RestoreStateAsync(snapshot);

        Assert.That(restoreResult.Success, Is.False);
        Assert.That(restoreResult.ErrorCode, Is.EqualTo(SonosErrorCode.InvalidUri));
        Assert.That(restoreResult.Message, Does.Contain("no longer available").IgnoreCase);
    }

    [Test]
    public async Task DiscoveryService_UsesDirectNetworkDiscovery()
    {
        var discovery = new SonosDiscoveryService();
        var system = await discovery.GetSystemAsync();

        Assert.That(system.Id, Is.EqualTo("sonos-system"));
        Assert.That(system.Name, Does.StartWith("Sonos"));
    }

    [Test]
    public async Task DiscoveryService_MarksOfflinePlayersAsOffline()
    {
        var transport = new FakeSonosTransport();
        var discovery = new SonosDiscoveryService(transport);

        var players = await discovery.GetPlayersAsync();
        var offlinePlayer = players.Single(player => player.Id == "player-3");

        Assert.That(offlinePlayer.Availability, Is.EqualTo(SonosAvailability.Offline));
        Assert.That(players.Where(player => player.Availability == SonosAvailability.Available).Select(player => player.Id), Does.Not.Contain("player-3"));
    }

    [Test]
    public async Task TransportTimeout_ReturnsFailureResult()
    {
        var transport = new FakeSonosTransport { SimulateTimeout = true };
        var playback = new SonosPlaybackService(transport);

        var result = await playback.PauseAsync("player-1");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(SonosErrorCode.Timeout));
    }

    [Test]
    public async Task ResultErrorSemantics_MapTimeoutMalformedAndCancelled()
    {
        var timeoutTransport = new FakeSonosTransport { SimulateTimeout = true };
        var timeoutPlayback = new SonosPlaybackService(timeoutTransport);
        var timeoutResult = await timeoutPlayback.PauseAsync("player-1");

        var transport = new SonosTransportService();
        var queueService = new SonosQueueService(transport);
        var malformedResult = await queueService.PlayQueueItemAsync("player-1", -1);

        var playback = new SonosPlaybackService(transport);
        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();
        var cancelledResult = await playback.PlayAsync("player-1", cancelledCts.Token);

        Assert.That(timeoutResult.Success, Is.False);
        Assert.That(timeoutResult.ErrorCode, Is.EqualTo(SonosErrorCode.Timeout));

        Assert.That(malformedResult.Success, Is.False);
        Assert.That(malformedResult.ErrorCode, Is.EqualTo(SonosErrorCode.MalformedResponse));

        Assert.That(cancelledResult.Success, Is.False);
        Assert.That(cancelledResult.ErrorCode, Is.EqualTo(SonosErrorCode.Cancelled));
    }

    [Test]
    public async Task PlaybackService_UsesRoomNotFoundForUnknownRoomLabels()
    {
        var transport = new SonosTransportService();
        var playback = new SonosPlaybackService(transport);

        var result = await playback.PlayAsync("Living Room");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(SonosErrorCode.RoomNotFound));
        Assert.That(result.Message, Does.Contain("Room").IgnoreCase);
    }

    private sealed class FakeSonosTransport : ISonosTransportService
    {
        public bool SimulateTimeout { get; set; }
        private readonly Dictionary<string, List<QueueEntry>> _queues =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["player-1"] =
                [
                    new QueueEntry { QueueIndex = 0, Id = "queue-1", Title = "Initial Item", Uri = "https://example.com/queue-item.mp3", Metadata = "{}" }
                ]
            };
        private readonly Dictionary<string, List<FavoriteEntry>> _favorites =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["player-1"] =
                [
                    new FavoriteEntry { Id = "favorite-1", Name = "Classic FM", Type = "radio", Uri = "x-rincon-browse:favorite-1" }
                ]
            };
        private readonly List<AlarmEntry> _alarms =
        [
            new AlarmEntry { Id = "alarm-1", Name = "Wake Up", Enabled = true, Time = "07:00", Recurrence = ["Mon", "Tue", "Wed", "Thu", "Fri"], Volume = 20, Source = "radio" }
        ];

        private static string NormalizePlayerId(string value)
        {
            var sanitized = value.Trim();
            sanitized = sanitized.Replace("uuid:", string.Empty, StringComparison.OrdinalIgnoreCase);
            return sanitized.ToLowerInvariant();
        }

        public Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default)
        {
            var players = new List<SonosPlayer>
            {
                new() { Id = "player-1", Name = "Kitchen", CoordinatorId = "coordinator-1", HouseholdId = "household-1", GroupMembership = ["group-1"], Availability = SonosAvailability.Available, Volume = 25, IsMuted = false },
                new() { Id = "player-2", Name = "Living Room", CoordinatorId = "coordinator-1", HouseholdId = "household-1", GroupMembership = ["group-1"], Availability = SonosAvailability.Available, Volume = 35, IsMuted = false },
                new() { Id = "player-3", Name = "Bedroom", CoordinatorId = "player-3", HouseholdId = "household-1", GroupMembership = ["group-2"], Availability = SonosAvailability.Offline, Volume = 15, IsMuted = true }
            };

            return Task.FromResult<IReadOnlyList<SonosPlayer>>(players);
        }

        public Task<IReadOnlyList<SonosGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
        {
            var groups = new List<SonosGroup>
            {
                new() { Id = "group-1", Name = "Kitchen + Living Room", CoordinatorId = "player-1", Members = [
                    new SonosPlayer { Id = "player-1", Name = "Kitchen", CoordinatorId = "coordinator-1", GroupMembership = ["group-1"], Availability = SonosAvailability.Available, Volume = 25, IsMuted = false },
                    new SonosPlayer { Id = "player-2", Name = "Living Room", CoordinatorId = "coordinator-1", GroupMembership = ["group-1"], Availability = SonosAvailability.Available, Volume = 35, IsMuted = false }
                ] },
                new() { Id = "group-2", Name = "Bedroom", CoordinatorId = "player-3", Members = [
                    new SonosPlayer { Id = "player-3", Name = "Bedroom", CoordinatorId = "player-3", GroupMembership = ["group-2"], Availability = SonosAvailability.Available, Volume = 15, IsMuted = true }
                ] }
            };

            return Task.FromResult<IReadOnlyList<SonosGroup>>(groups);
        }

        public Task<IReadOnlyList<QueueEntry>> GetQueueAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException("Player ID is required.", nameof(playerId));
            }

            var normalizedPlayerId = NormalizePlayerId(playerId);
            if (!_queues.TryGetValue(normalizedPlayerId, out var queue))
            {
                _queues[normalizedPlayerId] = [];
                return Task.FromResult<IReadOnlyList<QueueEntry>>([]);
            }

            var cloned = queue
                .Select(entry => new QueueEntry
                {
                    QueueIndex = entry.QueueIndex,
                    Id = entry.Id,
                    Title = entry.Title,
                    Uri = entry.Uri,
                    Metadata = entry.Metadata
                })
                .OrderBy(entry => entry.QueueIndex)
                .ToList();

            return Task.FromResult<IReadOnlyList<QueueEntry>>(cloned);
        }

        public Task<PlaybackResult> EnqueueAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.InvalidUri, Message = "Queue item must be a valid HTTP/HTTPS URL." });
            }

            var normalizedPlayerId = NormalizePlayerId(playerId);
            if (!_queues.TryGetValue(normalizedPlayerId, out var queue))
            {
                queue = [];
                _queues[normalizedPlayerId] = queue;
            }

            queue.Add(new QueueEntry
            {
                QueueIndex = queue.Count,
                Id = $"queue-{queue.Count + 1}",
                Title = mediaUrl,
                Uri = mediaUrl,
                Metadata = "{}"
            });

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = normalizedPlayerId, CurrentSource = mediaUrl, Message = "Enqueued successfully." });
        }

        public Task<PlaybackResult> ClearQueueAsync(string playerId, CancellationToken cancellationToken = default)
        {
            var normalizedPlayerId = NormalizePlayerId(playerId);
            _queues[normalizedPlayerId] = [];
            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = normalizedPlayerId, Message = "Queue cleared." });
        }

        public Task<PlaybackResult> PlayQueueItemAsync(string playerId, int queueIndex, CancellationToken cancellationToken = default)
        {
            var normalizedPlayerId = NormalizePlayerId(playerId);
            if (!_queues.TryGetValue(normalizedPlayerId, out var queue) || queueIndex < 0 || queueIndex >= queue.Count)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.MalformedResponse, Message = "Queue index is out of range." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = normalizedPlayerId, CurrentSource = queue[queueIndex].Uri, Message = "Queue item playback requested." });
        }

        public Task<IReadOnlyList<FavoriteEntry>> GetFavoritesAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException("Player ID is required.", nameof(playerId));
            }

            var normalizedPlayerId = NormalizePlayerId(playerId);
            if (!_favorites.TryGetValue(normalizedPlayerId, out var favorites))
            {
                _favorites[normalizedPlayerId] = [];
                return Task.FromResult<IReadOnlyList<FavoriteEntry>>([]);
            }

            var clone = favorites
                .Select(entry => new FavoriteEntry
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Type = entry.Type,
                    Description = entry.Description,
                    Uri = entry.Uri,
                    MetadataJson = entry.MetadataJson
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<FavoriteEntry>>(clone);
        }

        public async Task<PlaybackResult> PlayFavoriteAsync(string playerId, string favoriteId, CancellationToken cancellationToken = default)
        {
            var normalizedPlayerId = NormalizePlayerId(playerId);
            var favorites = await GetFavoritesAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
            var favorite = favorites.FirstOrDefault(entry =>
                string.Equals(entry.Id, favoriteId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Name, favoriteId, StringComparison.OrdinalIgnoreCase));

            if (favorite is null)
            {
                return new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.DeviceNotFound, Message = "Favorite not found.", PlayerId = normalizedPlayerId };
            }

            return new PlaybackResult
            {
                Success = true,
                PlayerId = normalizedPlayerId,
                CurrentSource = favorite.Uri,
                Message = $"Playing favorite '{favorite.Name}'."
            };
        }

        public Task<IReadOnlyList<AlarmEntry>> GetAlarmsAsync(CancellationToken cancellationToken = default)
        {
            var alarms = _alarms
                .Select(alarm => new AlarmEntry
                {
                    Id = alarm.Id,
                    Name = alarm.Name,
                    Enabled = alarm.Enabled,
                    Time = alarm.Time,
                    Recurrence = alarm.Recurrence.ToList(),
                    Volume = alarm.Volume,
                    Source = alarm.Source,
                    MetadataJson = alarm.MetadataJson
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<AlarmEntry>>(alarms);
        }

        public Task<AlarmEntry> CreateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(alarm);

            if (string.IsNullOrWhiteSpace(alarm.Id))
            {
                alarm.Id = $"alarm-{Guid.NewGuid().ToString("N")[..8]}";
            }

            _alarms.Add(new AlarmEntry
            {
                Id = alarm.Id,
                Name = alarm.Name,
                Enabled = alarm.Enabled,
                Time = alarm.Time,
                Recurrence = alarm.Recurrence.ToList(),
                Volume = alarm.Volume,
                Source = alarm.Source,
                MetadataJson = alarm.MetadataJson
            });

            return Task.FromResult(alarm);
        }

        public Task<AlarmEntry> UpdateAlarmAsync(AlarmEntry alarm, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(alarm);

            var existing = _alarms.FirstOrDefault(entry => string.Equals(entry.Id, alarm.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                throw new InvalidOperationException("Alarm not found.");
            }

            existing.Name = alarm.Name;
            existing.Enabled = alarm.Enabled;
            existing.Time = alarm.Time;
            existing.Recurrence = alarm.Recurrence.ToList();
            existing.Volume = alarm.Volume;
            existing.Source = alarm.Source;
            existing.MetadataJson = alarm.MetadataJson;

            return Task.FromResult(alarm);
        }

        public Task<bool> DeleteAlarmAsync(string alarmId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(alarmId))
            {
                return Task.FromResult(false);
            }

            var deleted = _alarms.RemoveAll(entry => string.Equals(entry.Id, alarmId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(deleted > 0);
        }

        public Task<SonosPlaybackState> GetPlaybackStateAsync(string playerId, CancellationToken cancellationToken = default)
        {
            var state = new SonosPlaybackState
            {
                PlayerId = playerId,
                State = SonosPlaybackStateKind.Playing,
                PositionSeconds = 12,
                Volume = 25,
                IsMuted = false,
                CurrentSource = "https://example.com/audio.mp3"
            };

            return Task.FromResult(state);
        }

        public Task<PlaybackResult> PlayAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (SimulateTimeout)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.Timeout, Message = "Play timed out." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId });
        }

        public Task<PlaybackResult> PauseAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (SimulateTimeout)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.Timeout, Message = "Pause timed out." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId });
        }

        public Task<PlaybackResult> StopAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (SimulateTimeout)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.Timeout, Message = "Stop timed out." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId });
        }

        public Task<PlaybackResult> NextAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (SimulateTimeout)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.Timeout, Message = "Next timed out." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId });
        }

        public Task<PlaybackResult> PreviousAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (SimulateTimeout)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.Timeout, Message = "Previous timed out." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId });
        }

        public Task<PlaybackResult> SeekAsync(string playerId, double positionSeconds, CancellationToken cancellationToken = default)
        {
            if (positionSeconds < 0)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.MalformedResponse, Message = "Position must be non-negative." });
            }

            if (SimulateTimeout)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.Timeout, Message = "Seek timed out." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId });
        }

        public Task<PlaybackResult> SetVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default)
        {
            if (volume < 0 || volume > 100)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.MalformedResponse, Message = "Volume must be between 0 and 100." });
            }

            if (SimulateTimeout)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.Timeout, Message = "SetVolume timed out." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId });
        }

        public Task<PlaybackResult> SetMuteAsync(string playerId, bool isMuted, CancellationToken cancellationToken = default)
        {
            if (SimulateTimeout)
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.Timeout, Message = "SetMute timed out." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId });
        }

        public Task<PlaybackResult> PlayUrlAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Task.FromResult(new PlaybackResult { Success = false, ErrorCode = SonosErrorCode.InvalidUri, Message = "Media URL is invalid." });
            }

            return Task.FromResult(new PlaybackResult { Success = true, PlayerId = playerId, CurrentSource = mediaUrl });
        }

        public Task<GroupResult> GroupAsync(IEnumerable<string> playerIds, CancellationToken cancellationToken = default)
        {
            var list = playerIds.ToList();
            return Task.FromResult(new GroupResult
            {
                Success = true,
                GroupId = "group-1",
                Group = new SonosGroup
                {
                    Id = "group-1",
                    Name = "Grouped Speakers",
                    CoordinatorId = list.FirstOrDefault() ?? "player-1",
                    Members = list.Select(id => new SonosPlayer { Id = id, Name = id, CoordinatorId = id, Availability = SonosAvailability.Available }).ToList()
                }
            });
        }

        public Task<GroupResult> UngroupAsync(string playerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GroupResult { Success = true, GroupId = null, Group = null, PlayerId = playerId });
        }

        public Task<RestoreResult> RestoreStateAsync(SonosStateSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RestoreResult
            {
                Success = true,
                RestoredPlayerIds = snapshot.GroupMembership.Values.SelectMany(v => v).Distinct().ToList(),
                SnapshotId = snapshot.Id,
                Message = "State restore requested."
            });
        }

        public Task<SonosStateSnapshot> CaptureStateAsync(string playerId, CancellationToken cancellationToken = default)
        {
            var snapshot = new SonosStateSnapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                PlayerId = playerId,
                GroupMembership = new Dictionary<string, IReadOnlyList<string>>
                {
                    ["group-1"] = [playerId]
                },
                PlaybackState = new SonosPlaybackState
                {
                    PlayerId = playerId,
                    State = SonosPlaybackStateKind.Playing,
                    PositionSeconds = 12,
                    Volume = 25,
                    IsMuted = false,
                    CurrentSource = "https://example.com/audio.mp3"
                }
            };

            return Task.FromResult(snapshot);
        }
    }
}
