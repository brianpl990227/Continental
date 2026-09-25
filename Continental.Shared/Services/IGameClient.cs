using Continental.Core.Engine;
using Continental.Core.Protocol;
using Continental.Core.Rules;

namespace Continental.Shared.Services;

public enum ConnectionState
{
    Connected,
    Reconnecting,
    Lost
}

public interface IGameClient : IAsyncDisposable
{
    string PlayerId { get; }

    PlayerView? View { get; }

    bool IsConnected { get; }

    ConnectionState Connection { get; }

    int ReconnectAttempt { get; }

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

    Task<SavedGame?> FindSavedAsync();

    Task<IGameClient?> ResumeSavedAsync();

    Task ForgetSavedAsync();

    void SaveNow();
}

public sealed record SavedGame(
    string RoomName,
    int RoundIndex,
    int TotalRounds,
    IReadOnlyList<string> Rivals,
    bool OnlyBots);

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
