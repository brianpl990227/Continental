using Continental.Core.Protocol;
using Continental.Core.Rules;
using Microsoft.JSInterop;

namespace Continental.Shared.Services;

public sealed class AppState(IJSRuntime js)
{
    private const string NameKey = "continental.playerName";

    public string PlayerName { get; private set; } = "";

    public IGameClient? Client { get; private set; }

    public GameOptions PreferredOptions { get; set; } = GameOptions.ForPreset(RulePreset.LatinAmerica);

    public string? LastPlayerId { get; private set; }

    public (string Address, int Port)? LastRoom { get; private set; }

    public event Action? Changed;

    public async Task LoadAsync()
    {
        try
        {
            PlayerName = await js.InvokeAsync<string>("localStorage.getItem", NameKey) ?? "";
        }
        catch (Exception)
        {

            PlayerName = "";
        }

        Changed?.Invoke();
    }

    public async Task SetNameAsync(string name)
    {
        PlayerName = name.Trim();

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", NameKey, PlayerName);
        }
        catch (Exception)
        {

        }

        Changed?.Invoke();
    }

    public void SetClient(IGameClient? client, string? address = null, int port = 0)
    {
        Client = client;

        if (client is not null)
        {
            LastPlayerId = client.PlayerId;
            LastRoom = address is null ? null : (address, port);
        }

        Changed?.Invoke();
    }

    public void ForgetSeat()
    {
        LastPlayerId = null;
        LastRoom = null;
    }

    public async Task LeaveAsync()
    {
        if (Client is null)
            return;

        try
        {
            await Client.SendAsync(new ClientMessage { Type = MessageType.Leave });
        }
        catch (Exception)
        {

        }

        await Client.DisposeAsync();
        Client = null;
        Changed?.Invoke();
    }
}

public sealed class WebSocketJoiner : IGameJoiner
{
    public async Task<IGameClient> JoinAsync(string address, int port, string playerName,
                                             string? previousPlayerId = null, CancellationToken token = default)
        => await RemoteGameClient.ConnectAsync(address, port, playerName, previousPlayerId, token);
}

public sealed class NoRoomDiscovery : IRoomDiscovery
{
    public IReadOnlyList<RoomAnnounce> Rooms => [];

    public bool IsSupported => false;

    public event Action? Changed { add { } remove { } }

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;
}

public sealed class NoGameHost : IGameHost
{
    public bool IsRunning => false;

    public string? JoinUrl => null;

    public bool WebClientReady => false;

    public Continental.Core.Engine.GameRoom? Room => null;

    public Task<IGameClient> StartAsync(string roomName, string playerName, GameOptions options)
        => throw new NotSupportedException("Desde el navegador solo puedes unirte a una sala, no crearla.");

    public Task StopAsync() => Task.CompletedTask;
}
