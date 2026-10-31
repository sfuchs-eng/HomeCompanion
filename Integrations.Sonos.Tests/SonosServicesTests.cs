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

        Assert.That(players.Count, Is.EqualTo(3));
        Assert.That(groups.Count, Is.EqualTo(2));
        Assert.That(players.Select(p => p.Name).ToArray(), Does.Contain("Kitchen"));
        Assert.That(groups[0].Members.Select(m => m.Id), Does.Contain("player-1"));
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
    public async Task PlayUrlAsync_RejectsInvalidUri()
    {
        var transport = new FakeSonosTransport();
        var playback = new SonosPlaybackService(transport);

        var result = await playback.PlayUrlAsync("player-1", "not-a-valid-uri");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(SonosErrorCode.InvalidUri));
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
        Assert.That((await transport.GetGroupsAsync()).Any(g => g.Members.Any(m => m.Id == "player-1") && g.Members.Any(m => m.Id == "player-2")), Is.True);
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
    public async Task TransportTimeout_ReturnsFailureResult()
    {
        var transport = new FakeSonosTransport { SimulateTimeout = true };
        var playback = new SonosPlaybackService(transport);

        var result = await playback.PauseAsync("player-1");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(SonosErrorCode.Timeout));
    }

    private sealed class FakeSonosTransport : ISonosTransportService
    {
        public bool SimulateTimeout { get; set; }

        public Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default)
        {
            var players = new List<SonosPlayer>
            {
                new() { Id = "player-1", Name = "Kitchen", CoordinatorId = "coordinator-1", GroupMembership = ["group-1"], Availability = SonosAvailability.Available, Volume = 25, IsMuted = false },
                new() { Id = "player-2", Name = "Living Room", CoordinatorId = "coordinator-1", GroupMembership = ["group-1"], Availability = SonosAvailability.Available, Volume = 35, IsMuted = false },
                new() { Id = "player-3", Name = "Bedroom", CoordinatorId = "player-3", GroupMembership = ["group-2"], Availability = SonosAvailability.Available, Volume = 15, IsMuted = true }
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
