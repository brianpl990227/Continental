using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Protocol;
using Continental.Core.Rules;
using Continental.Server;
using Continental.Shared.Services;
using Xunit;

namespace Continental.Core.Tests;

public class NetworkTests : IAsyncLifetime
{
    private static int _nextPort = 19180;

    private GameRoom _room = null!;
    private GameServer _server = null!;
    private string _webRoot = null!;

    public async Task InitializeAsync()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "continental-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_webRoot);
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), "<html><body>ok</body></html>");

        _room = new GameRoom("test", "Mesa de prueba", GameOptions.ForPreset(RulePreset.LatinAmerica));
        _room.AddHumanPlayer("host", "Anfitrión", isHost: true);

        _server = new GameServer(_room, _webRoot);

        await _server.StartAsync(Interlocked.Add(ref _nextPort, 10));
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();

        try
        {
            Directory.Delete(_webRoot, recursive: true);
        }
        catch (IOException)
        {

        }
    }

    private async Task<IGameClient> ConnectAsync(string name)
        => await RemoteGameClient.ConnectAsync("127.0.0.1", _server.Port, name);

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(25);
        }

        return condition();
    }

    [Fact]
    public async Task A_guest_can_join_over_a_websocket_and_receive_the_table()
    {
        await using var guest = await ConnectAsync("Invitada");

        Assert.True(await Until(() => guest.View is not null), "el invitado nunca recibió el estado");

        var view = guest.View!;

        Assert.Equal("Mesa de prueba", view.RoomName);
        Assert.Equal(GamePhase.Lobby, view.Phase);
        Assert.Contains(view.Players, p => p.Name == "Invitada");
        Assert.Contains(view.Players, p => p.Name == "Anfitrión" && p.IsHost);
        Assert.False(view.YouAreHost);
    }

    [Fact]
    public async Task Joining_players_are_told_which_variant_the_room_uses()
    {
        await using var guest = await ConnectAsync("Invitada");
        await Until(() => guest.View is not null);

        var options = guest.View!.Options;

        Assert.Equal(RulePreset.LatinAmerica, options.Preset);
        Assert.Equal("Latinoamérica", options.DisplayName);
        Assert.Equal(7, options.StartingCards);
        Assert.Equal(30, options.AceValue);
        Assert.Contains(options.Summary(), r => r.Label == "Variante" && r.Value == "Latinoamérica");
    }

    [Fact]
    public async Task The_host_changing_the_variant_reaches_everyone()
    {
        await using var guest = await ConnectAsync("Invitada");
        await Until(() => guest.View is not null);

        await _room.HandleAsync("host", new ClientMessage
        {
            Type = MessageType.SetOptions,
            Options = GameOptions.ForPreset(RulePreset.Spain)
        });

        Assert.True(await Until(() => guest.View?.Options.Preset == RulePreset.Spain),
                    "el invitado no vio el cambio de reglas");

        Assert.Equal(6, guest.View!.Options.StartingCards);
        Assert.Equal(20, guest.View.AceValueOrDefault());
    }

    [Fact]
    public async Task A_guest_never_sees_another_players_cards()
    {
        await using var guest = await ConnectAsync("Invitada");
        await Until(() => guest.View is not null);

        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.AddBot });
        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.Start });

        Assert.True(await Until(() => guest.View?.Phase != GamePhase.Lobby), "la partida no arrancó");

        var view = guest.View!;

        Assert.Equal(7, view.Hand.Count);

        foreach (var other in view.Players.Where(p => p.Id != view.YouId))
            Assert.Equal(7, other.CardCount);

        Assert.Equal(view.YouId, view.Players.Single(p => p.Name == "Invitada").Id);
    }

    [Fact]
    public async Task An_illegal_move_comes_back_as_an_error_and_changes_nothing()
    {
        await using var guest = await ConnectAsync("Invitada");
        await Until(() => guest.View is not null);

        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.AddBot });
        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.Start });
        await Until(() => guest.View?.Phase != GamePhase.Lobby);

        var before = guest.View!.Hand.Count;

        await guest.SendAsync(new ClientMessage { Type = MessageType.Discard, CardId = 99999 });

        Assert.True(await Until(() => guest.LastError is not null), "el servidor aceptó una jugada ilegal");
        Assert.Equal(before, guest.View!.Hand.Count);
    }

    [Fact]
    public async Task Two_guests_share_one_table()
    {
        await using var first = await ConnectAsync("Ana");
        await using var second = await ConnectAsync("Beto");

        Assert.True(await Until(() => first.View?.Players.Count == 3 && second.View?.Players.Count == 3),
                    "los invitados no convergieron en la misma mesa");

        Assert.NotEqual(first.PlayerId, second.PlayerId);
        Assert.Contains(first.View!.Players, p => p.Name == "Beto");
        Assert.Contains(second.View!.Players, p => p.Name == "Ana");
    }

    [Fact]
    public async Task Joining_a_game_in_progress_fails_fast_instead_of_hanging()
    {
        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.AddBot });
        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.Start });

        var clock = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var late = await ConnectAsync("Tarde");
        });

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"tardó {clock.Elapsed}");
    }

    [Fact]
    public async Task A_player_who_drops_can_reclaim_their_seat()
    {
        var first = await ConnectAsync("Ana");
        await Until(() => first.View is not null);

        var seat = first.PlayerId;

        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.AddBot });
        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.Start });
        Assert.True(await Until(() => first.View?.Hand.Count == 7));

        var hand = first.View!.Hand.Select(c => c.Id).OrderBy(x => x).ToList();

        await first.DisposeAsync();
        Assert.True(await Until(() => _room.State.Find(seat)?.IsConnected == false));

        await using var again = await RemoteGameClient.ConnectAsync("127.0.0.1", _server.Port, "Ana", seat);
        Assert.True(await Until(() => again.View is not null));

        Assert.Equal(seat, again.PlayerId);
        Assert.Equal(hand, again.View!.Hand.Select(c => c.Id).OrderBy(x => x));

        Assert.Equal(3, again.View.Players.Count);
    }

    [Fact]
    public async Task A_player_who_lost_their_id_gets_their_seat_back_by_name()
    {
        var first = await ConnectAsync("Ana");
        await Until(() => first.View is not null);

        var seat = first.PlayerId;

        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.AddBot });
        await _room.HandleAsync("host", new ClientMessage { Type = MessageType.Start });
        Assert.True(await Until(() => first.View?.Hand.Count == 7));

        var hand = first.View!.Hand.Select(c => c.Id).OrderBy(x => x).ToList();

        await first.DisposeAsync();
        Assert.True(await Until(() => _room.State.Find(seat)?.IsConnected == false));

        await using var again = await ConnectAsync("ana");
        Assert.True(await Until(() => again.View is not null));

        Assert.Equal(seat, again.PlayerId);
        Assert.Equal(hand, again.View!.Hand.Select(c => c.Id).OrderBy(x => x));
        Assert.True(_room.State.Find(seat)!.IsConnected);
    }

    [Fact]
    public async Task A_guest_leaving_is_noticed_by_the_others()
    {
        await using var stayer = await ConnectAsync("Ana");
        var leaver = await ConnectAsync("Beto");

        Assert.True(await Until(() => stayer.View?.Players.Count == 3));

        await leaver.DisposeAsync();

        Assert.True(await Until(() => stayer.View?.Players.All(p => p.Name != "Beto") ?? false),
                    "el jugador que se fue sigue en la mesa");
    }
}

internal static class ViewAssertions
{

    public static int AceValueOrDefault(this PlayerView view) => view.Options.AceValue;
}
