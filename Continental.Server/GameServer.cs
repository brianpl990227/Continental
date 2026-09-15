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
    private readonly ConcurrentDictionary<ISocketConnection, string> _players = new();
    private readonly GameRoom _room;
    private readonly string _webRoot;

    private IServerHost? _host;
    private Task? _botPump;
    private int _guestCounter;

    public GameServer(GameRoom room, string webRoot)
    {
        _room = room;
        _webRoot = webRoot;
        _room.StateChanged += () => _ = BroadcastAsync();
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

        if (!_players.TryGetValue(connection, out var playerId))
        {
            await SendAsync(connection, new ServerMessage { Type = MessageType.Error, Error = "Primero tienes que entrar en la sala." });
            return;
        }

        var error = await _room.HandleAsync(playerId, message);

        if (error is not null)
            await SendAsync(connection, new ServerMessage { Type = MessageType.Error, Error = error });
        else
            await SendStateAsync(connection, playerId);
    }

    private async ValueTask JoinAsync(IReactiveConnection connection, ClientMessage message)
    {

        var playerId = message.PlayerId;

        if (playerId is not null && _room.State.Find(playerId) is not null)
        {
            _room.MarkReconnected(playerId);
        }
        else
        {
            playerId = $"guest-{Interlocked.Increment(ref _guestCounter)}";
            var result = _room.AddHumanPlayer(playerId, message.Name ?? "Invitado", isHost: false);

            if (!result.Ok)
            {
                await SendAsync(connection, new ServerMessage { Type = MessageType.Error, Error = result.Error });
                return;
            }
        }

        _players[connection] = playerId;

        await SendAsync(connection, new ServerMessage { Type = MessageType.Welcome, YouId = playerId });
        await BroadcastAsync();
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
        if (_players.TryRemove(connection, out var playerId))
            _room.MarkDisconnected(playerId);
    }

    private async Task BroadcastAsync()
    {
        var targets = _players.ToArray();

        if (targets.Length == 0)
            return;

        var views = await _room.SnapshotAsync(targets.Select(t => t.Value).Distinct());

        foreach (var (connection, playerId) in targets)
        {
            if (!views.TryGetValue(playerId, out var view))
                continue;

            try
            {
                await SendAsync(connection, new ServerMessage { Type = MessageType.State, View = view });
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {

                Drop(connection);
            }
        }
    }

    private async Task SendStateAsync(ISocketConnection connection, string playerId)
    {
        var views = await _room.SnapshotAsync([playerId]);

        await SendAsync(connection, new ServerMessage { Type = MessageType.State, View = views[playerId] });
    }

    private static async Task SendAsync(ISocketConnection connection, ServerMessage message)
    {
        var bytes = Encoding.UTF8.GetBytes(Wire.Serialize(message));

        await connection.WriteAsync(bytes, GenHTTP.Modules.Websockets.Protocol.FrameType.Text);
        await connection.FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
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

        if (_botPump is not null)
        {
            try
            {
                await _botPump;
            }
            catch (Exception)
            {

            }
        }
    }

}
