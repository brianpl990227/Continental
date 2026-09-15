using System.Collections.Concurrent;
using System.Text;
using Continental.Core.Engine;
using Continental.Core.Protocol;
using GenHTTP.Api.Infrastructure;
using GenHTTP.Modules.IO;
using GenHTTP.Modules.StaticWebsites;
using GenHTTP.Modules.Websockets;

using GenLayout = GenHTTP.Modules.Layouting.Layout;
using GenHost = GenHTTP.Engine.Internal.Host;

namespace Continental.Server;

public sealed class GameServer : IAsyncDisposable
{
    private sealed class Seat(string playerId)
    {
        public string PlayerId { get; } = playerId;

        // Un websocket no admite dos escrituras a la vez: los frames se entrelazan
        // y el cliente recibe basura. Cada conexión escribe por turnos.
        public SemaphoreSlim Write { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<ISocketConnection, Seat> _players = new();
    private readonly SemaphoreSlim _pending = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly GameRoom _room;
    private readonly string _webRoot;

    private IServerHost? _host;
    private Task? _botPump;
    private Task? _broadcastPump;
    private int _guestCounter;

    public GameServer(GameRoom room, string webRoot)
    {
        _room = room;
        _webRoot = webRoot;
        _room.StateChanged += RequestBroadcast;
    }

    public int Port { get; private set; }

    public async Task StartAsync(int preferredPort)
    {
        var socket = Websocket.Functional()
                              .OnMessage(OnMessageAsync)
                              .OnClose(OnCloseAsync)
                              .OnError(OnErrorAsync);

        var app = new WebAssemblyContentTypes(
            GenLayout.Create()
                     .Add("ws", socket)
                     .Add(StaticWebsite.From(ResourceTree.FromDirectory(_webRoot)))
                     .Build(),
            _webRoot);

        for (var offset = 0; offset < 12; offset++)
        {
            var port = preferredPort + offset;

            try
            {
                _host = GenHost.Create().Handler(app).Port((ushort)port);
                await _host.StartAsync();

                Port = port;
                break;
            }
            catch (Exception)
            {
                _host = null;
            }
        }

        if (_host is null)
            throw new IOException($"No se pudo abrir ningún puerto a partir del {preferredPort}.");

        _botPump = _room.RunAsync();
        _broadcastPump = BroadcastPumpAsync();
    }

    private async ValueTask OnMessageAsync(IReactiveConnection connection, IWebsocketFrame frame)
    {
        string json;

        try
        {
            json = Encoding.UTF8.GetString(frame.Data.ToArray());
        }
        catch (Exception)
        {
            return;
        }

        var message = Wire.ReadClient(json);

        if (message is null)
            return;

        if (message.Type == MessageType.Join)
        {
            await JoinAsync(connection, message);
            return;
        }

        if (!_players.TryGetValue(connection, out var seat))
        {
            await SendRawAsync(connection, new ServerMessage { Type = MessageType.Error, Error = "Primero tienes que entrar en la sala." });
            return;
        }

        if (message.Type == MessageType.Ping)
        {
            await SendAsync(connection, seat, new ServerMessage { Type = MessageType.Pong });
            return;
        }

        var error = await _room.HandleAsync(seat.PlayerId, message);

        if (error is not null)
            await SendAsync(connection, seat, new ServerMessage { Type = MessageType.Error, Error = error });
        else
            await SendStateAsync(connection, seat);
    }

    private async ValueTask JoinAsync(IReactiveConnection connection, ClientMessage message)
    {
        var playerId = message.PlayerId;

        if (playerId is not null && _room.State.Find(playerId) is not null)
        {
            // El socket viejo puede seguir registrado si el corte fue silencioso. Se
            // descarta antes de reactivar el asiento para que su cierre tardío no
            // vuelva a marcar como desconectado a quien acaba de volver.
            DropStaleConnections(playerId, connection);

            _room.MarkReconnected(playerId);
        }
        else
        {
            playerId = $"guest-{Interlocked.Increment(ref _guestCounter)}";
            var result = _room.AddHumanPlayer(playerId, message.Name ?? "Invitado", isHost: false);

            if (!result.Ok)
            {
                await SendRawAsync(connection, new ServerMessage { Type = MessageType.Error, Error = result.Error });
                return;
            }
        }

        var seat = new Seat(playerId);
        _players[connection] = seat;

        await SendAsync(connection, seat, new ServerMessage { Type = MessageType.Welcome, YouId = playerId });

        RequestBroadcast();
    }

    private void DropStaleConnections(string playerId, ISocketConnection keep)
    {
        foreach (var (other, seat) in _players.ToArray())
        {
            if (!ReferenceEquals(other, keep) && seat.PlayerId == playerId)
                _players.TryRemove(other, out _);
        }
    }

    private ValueTask OnCloseAsync(IReactiveConnection connection, IWebsocketFrame frame)
    {
        Drop(connection);
        return ValueTask.CompletedTask;
    }

    private ValueTask<bool> OnErrorAsync(IReactiveConnection connection, GenHTTP.Modules.Websockets.Protocol.FrameError error)
    {
        Drop(connection);

        return ValueTask.FromResult(false);
    }

    private void Drop(ISocketConnection connection)
    {
        if (!_players.TryRemove(connection, out var seat))
            return;

        // Si el jugador ya volvió por otra conexión, este cierre es el del socket
        // viejo y no significa que se haya ido.
        if (_players.Values.Any(other => other.PlayerId == seat.PlayerId))
            return;

        _room.MarkDisconnected(seat.PlayerId);
    }

    private void RequestBroadcast()
    {
        try
        {
            _pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya hay un envío pendiente: el que salga llevará el estado más reciente.
        }
        catch (ObjectDisposedException)
        {

        }
    }

    // Una sola bomba de envío. Antes cada cambio de estado lanzaba su propia tarea
    // y varias podían escribir a la vez en el mismo socket.
    private async Task BroadcastPumpAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _pending.WaitAsync(_cts.Token);
                await BroadcastAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {

            }
        }
    }

    private async Task BroadcastAsync()
    {
        var targets = _players.ToArray();

        if (targets.Length == 0)
            return;

        var views = await _room.SnapshotAsync(targets.Select(t => t.Value.PlayerId).Distinct());

        foreach (var (connection, seat) in targets)
        {
            if (!views.TryGetValue(seat.PlayerId, out var view))
                continue;

            try
            {
                await SendAsync(connection, seat, new ServerMessage { Type = MessageType.State, View = view });
            }
            catch (Exception)
            {
                // Un socket muerto no puede cortar el reparto a los demás.
                Drop(connection);
            }
        }
    }

    private async Task SendStateAsync(ISocketConnection connection, Seat seat)
    {
        var views = await _room.SnapshotAsync([seat.PlayerId]);

        await SendAsync(connection, seat, new ServerMessage { Type = MessageType.State, View = views[seat.PlayerId] });
    }

    private static async Task SendAsync(ISocketConnection connection, Seat seat, ServerMessage message)
    {
        var bytes = Encoding.UTF8.GetBytes(Wire.Serialize(message));

        await seat.Write.WaitAsync();

        try
        {
            await connection.WriteAsync(bytes, GenHTTP.Modules.Websockets.Protocol.FrameType.Text);
            await connection.FlushAsync();
        }
        finally
        {
            seat.Write.Release();
        }
    }

    private static async Task SendRawAsync(ISocketConnection connection, ServerMessage message)
    {
        var bytes = Encoding.UTF8.GetBytes(Wire.Serialize(message));

        await connection.WriteAsync(bytes, GenHTTP.Modules.Websockets.Protocol.FrameType.Text);
        await connection.FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _room.StateChanged -= RequestBroadcast;

        await _cts.CancelAsync();

        if (_host is not null)
        {
            try
            {
                await _host.StopAsync();
            }
            catch (Exception)
            {

            }
        }

        _room.Dispose();

        foreach (var pump in new[] { _botPump, _broadcastPump })
        {
            if (pump is null)
                continue;

            try
            {
                await pump;
            }
            catch (Exception)
            {

            }
        }

        _cts.Dispose();
        _pending.Dispose();
    }
}
