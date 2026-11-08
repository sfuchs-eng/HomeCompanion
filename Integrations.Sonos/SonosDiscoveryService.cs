using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace HomeCompanion.Integrations.Sonos;

public sealed class SonosDiscoveryService : ISonosDiscoveryService
{
    private readonly ISonosTransportService? _transport;
    private readonly SonosIntegrationOptions _options;
    private readonly HttpClient _httpClient;

    public SonosDiscoveryService()
        : this(new SonosIntegrationOptions())
    {
    }

    public SonosDiscoveryService(SonosIntegrationOptions? options)
    {
        _options = options ?? new SonosIntegrationOptions();
        _httpClient = new HttpClient
        {
            Timeout = _options.RequestTimeout
        };
    }

    public SonosDiscoveryService(ISonosTransportService transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = new SonosIntegrationOptions();
        _httpClient = new HttpClient
        {
            Timeout = _options.RequestTimeout
        };
    }

    public async Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default)
    {
        if (_transport is not null)
        {
            return await _transport.GetPlayersAsync(cancellationToken).ConfigureAwait(false);
        }

        var locations = await DiscoverLocationsAsync(cancellationToken).ConfigureAwait(false);
        var players = new List<SonosPlayer>();

        foreach (var location in locations)
        {
            var player = await LoadPlayerFromDescriptionAsync(location, cancellationToken).ConfigureAwait(false);
            if (player is not null)
            {
                players.Add(player);
            }
        }

        return players;
    }

    public async Task<SonosSystem> GetSystemAsync(CancellationToken cancellationToken = default)
    {
        if (_transport is not null)
        {
            var players = await GetPlayersAsync(cancellationToken).ConfigureAwait(false);
            var groups = await _transport.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
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

        var discoveredPlayers = await GetPlayersAsync(cancellationToken).ConfigureAwait(false);
        var discoveredHousehold = BuildHousehold(discoveredPlayers);
        return new SonosSystem
        {
            Id = "sonos-system",
            Name = discoveredPlayers.Count > 0 ? "Sonos system" : "Sonos (not discovered)",
            Players = discoveredPlayers,
            Groups = [],
            Household = discoveredHousehold
        };
    }

    public static string NormalizePlayerId(string? value, string fallback)
    {
        return SonosValidation.NormalizePlayerId(value, fallback);
    }

    private async Task<IReadOnlyList<string>> DiscoverLocationsAsync(CancellationToken cancellationToken)
    {
        const int ssdpPort = 1900;
        var multicastAddress = IPAddress.Parse("239.255.255.250");
        using var udpClient = new UdpClient();
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
        udpClient.Client.MulticastLoopback = true;
        udpClient.Client.ReceiveTimeout = 2000;
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        foreach (var interfaceAddress in GetDiscoveryInterfaceAddresses())
        {
            try
            {
                udpClient.JoinMulticastGroup(multicastAddress, interfaceAddress);
            }
            catch (SocketException)
            {
                // Some NICs / hosts reject multicast joins; continue with the next interface.
            }
        }

        var search = Encoding.UTF8.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: ssdp:all\r\n\r\n");

        await udpClient.SendAsync(search, search.Length, multicastAddress.ToString(), ssdpPort).ConfigureAwait(false);

        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow.AddSeconds(2.5);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var receiveTask = udpClient.ReceiveAsync(cancellationToken).AsTask();
                var timeoutTask = Task.Delay(250, cancellationToken);
                var completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    break;
                }

                var result = await receiveTask.ConfigureAwait(false);
                var response = Encoding.UTF8.GetString(result.Buffer);
                var location = ParseLocationHeader(response);
                if (!string.IsNullOrWhiteSpace(location) && IsSonosLocation(location, response))
                {
                    locations.Add(location);
                }
            }
            catch (SocketException)
            {
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        return locations.ToList();
    }

    private static IReadOnlyList<IPAddress> GetDiscoveryInterfaceAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.OperationalStatus == OperationalStatus.Up)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToList();
    }

    private static string? ParseLocationHeader(string response)
    {
        var lines = response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line["LOCATION:".Length..].Trim();
            return value.Trim('"');
        }

        return null;
    }

    private static bool IsSonosLocation(string location) => IsSonosLocation(location, response: null);

    private static bool IsSonosLocation(string location, string? response)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var serverHeader = response is null ? null : ParseHeaderValue(response, "SERVER");
        var stHeader = response is null ? null : ParseHeaderValue(response, "ST");
        var usnHeader = response is null ? null : ParseHeaderValue(response, "USN");

        if (serverHeader is not null &&
            (serverHeader.Contains("Sonos", StringComparison.OrdinalIgnoreCase) ||
             serverHeader.Contains("ZonePlayer", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (stHeader is not null &&
            (stHeader.Contains("ZonePlayer", StringComparison.OrdinalIgnoreCase) ||
             stHeader.Contains("MediaRenderer", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (usnHeader is not null && usnHeader.Contains("Sonos", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.Port == 1400 || uri.AbsolutePath.Contains("device_description", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.Host.Contains("sonos", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsoluteUri.Contains("sonos", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseHeaderValue(string response, string headerName)
    {
        var lines = response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (string.Equals(key, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separatorIndex + 1)..].Trim();
            }
        }

        return null;
    }

    private async Task<SonosPlayer?> LoadPlayerFromDescriptionAsync(string descriptionUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(descriptionUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var document = XDocument.Parse(xml);
            var device = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "device");
            if (device is null)
            {
                return null;
            }

            var friendlyName = device.Descendants().FirstOrDefault(x => x.Name.LocalName == "friendlyName")?.Value ?? "Sonos Player";
            var udn = device.Descendants().FirstOrDefault(x => x.Name.LocalName == "UDN")?.Value ?? friendlyName;
            var modelName = device.Descendants().FirstOrDefault(x => x.Name.LocalName == "modelName")?.Value ?? "Sonos";
            var serialNumber = device.Descendants().FirstOrDefault(x => x.Name.LocalName == "serialNumber")?.Value;

            var playerId = SonosValidation.NormalizePlayerId(udn, friendlyName);
            return new SonosPlayer
            {
                Id = playerId,
                Name = friendlyName,
                CoordinatorId = playerId,
                HouseholdId = serialNumber,
                ZoneName = friendlyName,
                GroupMembership = [],
                Availability = SonosAvailability.Available,
                Volume = 0,
                IsMuted = false
            };
        }
        catch (Exception)
        {
            return null;
        }
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
}
