using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Continental.Core.Protocol;
using Continental.Shared.Services;

namespace Continental.Server;

public static class NetworkInfo
{

    public static string? LocalAddress()
    {
        var candidates = new List<(int Score, string Address)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var score = nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => 3,
                NetworkInterfaceType.Ethernet => 2,
                _ => 1
            };

            if (nic.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                || nic.Description.Contains("hyper-v", StringComparison.OrdinalIgnoreCase)
                || nic.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase))
            {
                score = 0;
            }

            foreach (var ip in nic.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                if (IPAddress.IsLoopback(ip.Address))
                    continue;

                candidates.Add((score, ip.Address.ToString()));
            }
        }

        return candidates.OrderByDescending(c => c.Score).Select(c => c.Address).FirstOrDefault();
    }
}

public sealed class RoomAnnouncer : IAsyncDisposable
{
    public const int DiscoveryPort = 45678;

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public void Start(Func<RoomAnnounce> snapshot) => _loop = RunAsync(snapshot, _cts.Token);

    private static async Task RunAsync(Func<RoomAnnounce> snapshot, CancellationToken token)
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var target = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(Wire.Serialize(snapshot()));
                    await udp.SendAsync(bytes, bytes.Length, target);
                }
                catch (SocketException)
                {

                }

                await Task.Delay(2000, token);
            }
        }
        catch (OperationCanceledException)
        {

        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (Exception)
            {

            }
        }

        _cts.Dispose();
    }
}

public sealed class UdpRoomDiscovery : IRoomDiscovery
{
    private readonly Dictionary<string, RoomAnnounce> _rooms = [];
    private readonly Lock _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public bool IsSupported => true;

    public IReadOnlyList<RoomAnnounce> Rooms
    {
        get
        {
            lock (_gate)
            {

                var cutoff = DateTimeOffset.UtcNow.AddSeconds(-6);

                foreach (var stale in _rooms.Where(r => r.Value.SeenAt < cutoff).Select(r => r.Key).ToList())
                    _rooms.Remove(stale);

                return _rooms.Values.OrderBy(r => r.RoomName).ToList();
            }
        }
    }

    public event Action? Changed;

    public Task StartAsync()
    {
        if (_loop is not null)
            return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);

        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken token)
    {
        UdpClient? udp = null;

        try
        {
            udp = new UdpClient
            {
                ExclusiveAddressUse = false
            };

            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, RoomAnnouncer.DiscoveryPort));

            while (!token.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(token);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var announce = Wire.ReadAnnounce(json);

                if (announce is null || string.IsNullOrEmpty(announce.RoomId))
                    continue;

                announce.SeenAt = DateTimeOffset.UtcNow;

                if (string.IsNullOrEmpty(announce.Address))
                    announce.Address = result.RemoteEndPoint.Address.ToString();

                lock (_gate)
                    _rooms[announce.RoomId] = announce;

                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (SocketException)
        {

        }
        finally
        {
            udp?.Dispose();
        }
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        await _cts.CancelAsync();

        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (Exception)
            {

            }
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }
}
