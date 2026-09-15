using Continental.Core.Engine;
using Continental.Core.Protocol;

namespace Continental.Shared.Services;

public sealed class LocalGameClient : IGameClient
{
    private readonly GameRoom _room;

    public LocalGameClient(GameRoom room, string playerId)
    {
        _room = room;
        PlayerId = playerId;
        _room.StateChanged += OnRoomChanged;

        View = _room.ViewFor(playerId);
    }

    public string PlayerId { get; }

    public PlayerView? View { get; private set; }

    public bool IsConnected => true;

    public ConnectionState Connection => ConnectionState.Connected;

    public int ReconnectAttempt => 0;

    public string? LastError { get; private set; }

    public event Action? Changed;

    public async Task SendAsync(ClientMessage message)
    {
        LastError = await _room.HandleAsync(PlayerId, message);
        Refresh();
    }

    public void ClearError()
    {
        LastError = null;
        Changed?.Invoke();
    }

    private void OnRoomChanged() => Refresh();

    private void Refresh()
    {
        View = _room.ViewFor(PlayerId);
        Changed?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        _room.StateChanged -= OnRoomChanged;
        return ValueTask.CompletedTask;
    }
}
