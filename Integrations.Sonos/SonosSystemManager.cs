namespace HomeCompanion.Integrations.Sonos;

public sealed class SonosSystemManager : ISonosSystemService, ISonosStateRestoreService
{
    private readonly ISonosTransportService _transport;

    public SonosSystemManager(ISonosTransportService transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default)
    {
        return _transport.GetPlayersAsync(cancellationToken);
    }

    public Task<IReadOnlyList<SonosGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        return _transport.GetGroupsAsync(cancellationToken);
    }

    public async Task<SonosSystem> GetSystemAsync(CancellationToken cancellationToken = default)
    {
        var players = await GetPlayersAsync(cancellationToken).ConfigureAwait(false);
        var groups = await GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var household = BuildHousehold(players);

        return new SonosSystem
        {
            Id = "sonos-system",
            Name = players.Count > 0 ? "Sonos system" : "Sonos (not discovered)",
            Players = players,
            Groups = groups,
            Household = household
        };
    }

    private static SonosHousehold? BuildHousehold(IReadOnlyList<SonosPlayer> players)
    {
        var normalizedPlayerIds = players
            .Select(player => SonosValidation.NormalizePlayerId(player.Id, string.Empty))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var householdIds = players
            .Select(player => player.HouseholdId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (householdIds.Count != 1)
        {
            return null;
        }

        var householdId = householdIds[0];
        return new SonosHousehold
        {
            Id = householdId,
            Name = $"Sonos Household {householdId}",
            PlayerIds = normalizedPlayerIds
        };
    }

    public Task<SonosStateSnapshot> CaptureStateAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Player ID is required.", nameof(playerId));
        }

        return _transport.CaptureStateAsync(playerId, cancellationToken);
    }

    public Task<RestoreResult> RestoreStateAsync(SonosStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _transport.RestoreStateAsync(snapshot, cancellationToken);
    }
}
