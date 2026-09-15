using System.IO.Compression;
using Continental.Core.Engine;
using Continental.Core.Protocol;
using Continental.Core.Rules;
using Continental.Server;
using Continental.Shared.Services;

namespace Continental.Services;

public sealed class MauiGameHost : IGameHost
{
    private const int DefaultPort = 8080;
    private const string HostPlayerId = "host";

    private GameServer? _server;
    private RoomAnnouncer? _announcer;

    public bool IsRunning => _server is not null;

    public string? JoinUrl { get; private set; }

    public bool WebClientReady { get; private set; }

    public GameRoom? Room { get; private set; }

    public async Task<IGameClient> StartAsync(string roomName, string playerName, GameOptions options)
    {
        await StopAsync();

        var room = new GameRoom(Guid.NewGuid().ToString("N")[..8], roomName, options);
        room.AddHumanPlayer(HostPlayerId, playerName, isHost: true);

        var webRoot = await PrepareWebClientAsync();
        var server = new GameServer(room, webRoot);

        await server.StartAsync(DefaultPort);

        Room = room;
        _server = server;

        var address = NetworkInfo.LocalAddress();
        JoinUrl = address is null ? null : $"http://{address}:{server.Port}";

        _announcer = new RoomAnnouncer();
        _announcer.Start(() => new RoomAnnounce
        {
            RoomId = room.State.RoomId,
            RoomName = room.State.RoomName,
            HostName = playerName,
            Address = address ?? "",
            Port = server.Port,
            Players = room.State.Players.Count,
            MaxPlayers = 6,
            InProgress = room.State.Phase != GamePhase.Lobby,
            Ruleset = room.State.Options.DisplayName,
            StartingCards = room.State.Options.StartingCards
        });

        return new LocalGameClient(room, HostPlayerId);
    }

    public async Task StopAsync()
    {
        if (_announcer is not null)
        {
            await _announcer.DisposeAsync();
            _announcer = null;
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }

        Room = null;
        JoinUrl = null;
        WebClientReady = false;
    }

    private async Task<string> PrepareWebClientAsync()
    {
        var target = Path.Combine(FileSystem.AppDataDirectory, "webclient");
        var stamp = Path.Combine(target, ".bundle");

        try
        {
            var fingerprint = await BundleFingerprintAsync();

            if (fingerprint is null)
                throw new FileNotFoundException(WebClientAsset);

            if (File.Exists(stamp) && await File.ReadAllTextAsync(stamp) == fingerprint
                && Directory.Exists(Path.Combine(target, "_framework")))
            {
                WebClientReady = true;
                return target;
            }

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);

            Directory.CreateDirectory(target);

            await using (var packed = await FileSystem.OpenAppPackageFileAsync(WebClientAsset))
            using (var archive = new ZipArchive(packed, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(target, overwriteFiles: true);
            }

            await File.WriteAllTextAsync(stamp, fingerprint);

            WebClientReady = Directory.Exists(Path.Combine(target, "_framework"));
        }
        catch (Exception)
        {

            WebClientReady = false;

            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "index.html"), FallbackPage);
        }

        return target;
    }

    private static async Task<string?> BundleFingerprintAsync()
    {
        try
        {
            await using var packed = await FileSystem.OpenAppPackageFileAsync(WebClientAsset);
            using var sha = System.Security.Cryptography.SHA256.Create();

            var hash = await sha.ComputeHashAsync(packed);

            return Convert.ToHexString(hash);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private const string WebClientAsset = "webclient.zip";

    private const string FallbackPage = """
        <!doctype html>
        <html lang="es">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Continental</title>
        <style>
        body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px;
             font:15px/1.55 system-ui,sans-serif;color:#E6EFEB;
             background:radial-gradient(120% 70% at 50% 30%,#1A3B33,#0A1A17 60%,#05100E)}
        .box{max-width:420px;text-align:center}
        h1{font-size:32px;letter-spacing:-.035em;margin:0 0 14px;color:#F2B33D}
        p{color:#8CA39C;margin:0 0 12px}
        code{font-family:ui-monospace,monospace;color:#F2B33D}
        </style>
        </head>
        <body><div class="box">
        <h1>Continental</h1>
        <p>La sala está levantada y aceptando jugadores.</p>
        <p>El cliente para navegador no viene en esta compilación. Ejecuta
        <code>dotnet publish Continental.WebClient</code> y vuelve a compilar la app
        para poder jugar desde aquí.</p>
        <p>Mientras tanto, únete desde la app instalada.</p>
        </div></body>
        </html>
        """;
}
