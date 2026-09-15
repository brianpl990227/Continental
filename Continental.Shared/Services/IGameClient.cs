using Continental.Core.Engine;
using Continental.Core.Protocol;
using Continental.Core.Rules;

namespace Continental.Shared.Services;

public interface IGameClient : IAsyncDisposable
{
    string PlayerId { get; }

    PlayerView? View { get; }

    bool IsConnected { get; }

    string? LastError { get; }

    event Action? Changed;

    Task SendAsync(ClientMessage message);

    void ClearError();
}

public interface IGameHost
{
    bool IsRunning { get; }

    string? JoinUrl { get; }

    bool WebClientReady { get; }

    GameRoom? Room { get; }

    Task<IGameClient> StartAsync(string roomName, string playerName, GameOptions options);

    Task StopAsync();
}

public interface IRoomDiscovery
{
    IReadOnlyList<RoomAnnounce> Rooms { get; }

    bool IsSupported { get; }

    event Action? Changed;

    Task StartAsync();

    Task StopAsync();
}

public interface IGameJoiner
{

    Task<IGameClient> JoinAsync(string address, int port, string playerName,
                                string? previousPlayerId = null, CancellationToken token = default);
}
