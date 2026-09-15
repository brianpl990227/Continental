using System.Net.WebSockets;
using System.Text;
using Continental.Core.Protocol;

namespace Continental.Shared.Services;

public sealed class RemoteGameClient : IGameClient
{
    // Cada cuánto se manda un latido y cuánto silencio se tolera antes de dar el
    // socket por muerto. Un corte de WiFi o un NAT que cierra la sesión no avisa:
    // sin latido el socket se queda "abierto" para siempre y nadie se entera.
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SilenceBeforeRetry = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(3);

    private readonly string _address;
    private readonly int _port;
    private readonly string _playerName;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _send = new(1, 1);

    private ClientWebSocket _socket = new();
    private TaskCompletionSource<string> _joined =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _pump;
    private Task? _heartbeat;
    private Task? _supervisor;
    private DateTimeOffset _lastHeard = DateTimeOffset.UtcNow;
    private bool _disposed;

    private RemoteGameClient(string address, int port, string playerName)
    {
        _address = address;
        _port = port;
        _playerName = playerName;
    }

    public string PlayerId { get; private set; } = "";

    public PlayerView? View { get; private set; }

    public bool IsConnected => Connection == ConnectionState.Connected;

    public ConnectionState Connection { get; private set; } = ConnectionState.Connected;

    public int ReconnectAttempt { get; private set; }

    public string? LastError { get; private set; }

    public event Action? Changed;

    public static async Task<RemoteGameClient> ConnectAsync(string address, int port, string playerName,
                                                            string? previousPlayerId = null,
                                                            CancellationToken token = default)
    {
        var client = new RemoteGameClient(address, port, playerName)
        {
            PlayerId = previousPlayerId ?? ""
        };

        await client.OpenAsync(token);

        client._supervisor = client.SuperviseAsync();

        return client;
    }

    private async Task OpenAsync(CancellationToken token)
    {
        var socket = new ClientWebSocket();
        var uri = new Uri($"ws://{_address}:{_port}/ws");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));

        try
        {
            await socket.ConnectAsync(uri, deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException(
                $"No se pudo abrir la conexión con {_address}:{_port}. Comprueba la dirección, " +
                "que estéis en el mismo WiFi y que el cortafuegos del anfitrión deje entrar conexiones.");
        }
        catch (WebSocketException ex)
        {
            socket.Dispose();
            throw new InvalidOperationException($"No hay ninguna sala escuchando en {_address}:{_port}.", ex);
        }

        _socket = socket;
        _joined = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _lastHeard = DateTimeOffset.UtcNow;

        _pump = ReceiveLoopAsync(socket);
        _heartbeat = HeartbeatAsync(socket);

        try
        {
            await SendOnAsync(socket, new ClientMessage
            {
                Type = MessageType.Join,
                Name = _playerName,
                PlayerId = string.IsNullOrEmpty(PlayerId) ? null : PlayerId
            });

            await _joined.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            await CloseSocketAsync(socket);
            throw new TimeoutException("El anfitrión no respondió a tiempo. Comprueba que sigue en la sala.");
        }
        catch (Exception)
        {
            await CloseSocketAsync(socket);
            throw;
        }
    }

    // Vigila el socket vivo. Cuando el bucle de recepción termina sin que nadie
    // haya pedido cerrar, empieza a reintentar.
    private async Task SuperviseAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_pump is not null)
                    await _pump;
            }
            catch (Exception)
            {

            }

            if (_cts.IsCancellationRequested || _disposed)
                return;

            SetConnection(ConnectionState.Reconnecting);

            if (await ReconnectAsync())
            {
                ReconnectAttempt = 0;
                LastError = null;
                SetConnection(ConnectionState.Connected);
                continue;
            }

            LastError = "Se perdió la conexión con el anfitrión y no se pudo recuperar.";
            SetConnection(ConnectionState.Lost);
            return;
        }
    }

    private async Task<bool> ReconnectAsync()
    {
        var wait = TimeSpan.FromSeconds(1);
        var deadline = DateTimeOffset.UtcNow + GiveUpAfter;

        while (!_cts.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            ReconnectAttempt++;
            Changed?.Invoke();

            try
            {
                await OpenAsync(_cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception)
            {

            }

            try
            {
                await Task.Delay(wait, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 1.6, 8));
        }

        return false;
    }

    private async Task HeartbeatAsync(ClientWebSocket socket)
    {
        try
        {
            while (!_cts.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(PingEvery, _cts.Token);

                if (DateTimeOffset.UtcNow - _lastHeard > SilenceBeforeRetry)
                {
                    // Nadie contesta: se corta a mano para que el supervisor reintente
                    // en lugar de esperar a que el sistema note el socket muerto.
                    socket.Abort();
                    return;
                }

                await SendOnAsync(socket, new ClientMessage { Type = MessageType.Ping });
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception)
        {
            socket.Abort();
        }
    }

    public async Task SendAsync(ClientMessage message)
    {
        var socket = _socket;

        if (Connection != ConnectionState.Connected || socket.State != WebSocketState.Open)
        {
            LastError = Connection == ConnectionState.Reconnecting
                ? "Reconectando con el anfitrión…"
                : "Se perdió la conexión con el anfitrión.";

            Changed?.Invoke();
            return;
        }

        try
        {
            await SendOnAsync(socket, message);
        }
        catch (Exception)
        {
            // Que falle el envío es la primera señal de que el socket está muerto.
            socket.Abort();
        }
    }

    private async Task SendOnAsync(ClientWebSocket socket, ClientMessage message)
    {
        var bytes = Encoding.UTF8.GetBytes(Wire.Serialize(message));

        await _send.WaitAsync(_cts.Token);

        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, _cts.Token);
        }
        finally
        {
            _send.Release();
        }
    }

    public void ClearError()
    {
        LastError = null;
        Changed?.Invoke();
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket)
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var builder = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer, _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                _lastHeard = DateTimeOffset.UtcNow;

                Handle(builder.ToString());
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception)
        {

        }
        finally
        {
            _joined.TrySetException(new InvalidOperationException(
                LastError ?? "Se cerró la conexión antes de entrar en la sala."));

            Changed?.Invoke();
        }
    }

    private void Handle(string json)
    {
        var message = Wire.ReadServer(json);

        if (message is null)
            return;

        switch (message.Type)
        {
            case MessageType.Welcome:
                PlayerId = message.YouId ?? "";
                _joined.TrySetResult(PlayerId);
                break;

            case MessageType.State:
                View = message.View;
                break;

            case MessageType.Pong:
                return;

            case MessageType.Error:
                LastError = message.Error;

                _joined.TrySetException(new InvalidOperationException(message.Error ?? "El anfitrión rechazó la conexión."));
                break;
        }

        Changed?.Invoke();
    }

    private void SetConnection(ConnectionState state)
    {
        Connection = state;
        Changed?.Invoke();
    }

    private async Task CloseSocketAsync(ClientWebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (Exception)
        {

        }

        socket.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        await _cts.CancelAsync();

        await CloseSocketAsync(_socket);

        foreach (var task in new[] { _pump, _heartbeat, _supervisor })
        {
            if (task is null)
                continue;

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception)
            {

            }
        }

        _cts.Dispose();
        _send.Dispose();
    }
}
