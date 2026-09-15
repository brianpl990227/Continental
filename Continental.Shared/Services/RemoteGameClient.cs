using System.Net.WebSockets;
using System.Text;
using Continental.Core.Protocol;

namespace Continental.Shared.Services;

public sealed class RemoteGameClient : IGameClient
{
    private readonly ClientWebSocket _socket;
    private readonly CancellationTokenSource _cts = new();

    private readonly TaskCompletionSource<string> _joined =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _pump;

    private RemoteGameClient(ClientWebSocket socket) => _socket = socket;

    public string PlayerId { get; private set; } = "";

    public PlayerView? View { get; private set; }

    public bool IsConnected => _socket.State == WebSocketState.Open;

    public string? LastError { get; private set; }

    public event Action? Changed;

    public static async Task<RemoteGameClient> ConnectAsync(string address, int port, string playerName,
                                                            string? previousPlayerId = null,
                                                            CancellationToken token = default)
    {
        var socket = new ClientWebSocket();
        var uri = new Uri($"ws://{address}:{port}/ws");

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
                $"No se pudo abrir la conexión con {address}:{port}. Comprueba la dirección, " +
                "que estéis en el mismo WiFi y que el cortafuegos del anfitrión deje entrar conexiones.");
        }
        catch (WebSocketException ex)
        {
            socket.Dispose();
            throw new InvalidOperationException($"No hay ninguna sala escuchando en {address}:{port}.", ex);
        }

        var client = new RemoteGameClient(socket);
        client._pump = client.ReceiveLoopAsync();

        try
        {
            await client.SendAsync(new ClientMessage
            {
                Type = MessageType.Join,
                Name = playerName,
                PlayerId = previousPlayerId
            });

            await client._joined.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            await client.DisposeAsync();
            throw new TimeoutException("El anfitrión no respondió a tiempo. Comprueba que sigue en la sala.");
        }
        catch (Exception)
        {
            await client.DisposeAsync();
            throw;
        }

        return client;
    }

    public async Task SendAsync(ClientMessage message)
    {
        if (_socket.State != WebSocketState.Open)
        {
            LastError = "Se perdió la conexión con el anfitrión.";
            Changed?.Invoke();
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(Wire.Serialize(message));

        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, _cts.Token);
    }

    public void ClearError()
    {
        LastError = null;
        Changed?.Invoke();
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var builder = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await _socket.ReceiveAsync(buffer, _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                Handle(builder.ToString());
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (WebSocketException)
        {
            LastError = "Se perdió la conexión con el anfitrión.";
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

            case MessageType.Error:
                LastError = message.Error;

                _joined.TrySetException(new InvalidOperationException(message.Error ?? "El anfitrión rechazó la conexión."));
                break;
        }

        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (WebSocketException)
        {

        }

        _socket.Dispose();
        _cts.Dispose();

        if (_pump is not null)
            await _pump.ConfigureAwait(false);
    }
}
