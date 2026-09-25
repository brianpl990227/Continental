using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Protocol;
using Continental.Core.Rules;
using Xunit;

namespace Continental.Core.Tests;

public class SavedGameTests
{
    private static GameEngine NewGame(out GameState state, params (string Id, bool IsBot)[] rivals)
    {
        state = new GameState { RoomId = "sala", RoomName = "Mesa de Ana", Options = GameOptions.ForPreset(RulePreset.LatinAmerica) };
        var engine = new GameEngine(state, new Random(11));

        engine.AddPlayer("host", "Ana", false, isHost: true);

        foreach (var (id, isBot) in rivals)
            engine.AddPlayer(id, id, isBot);

        engine.StartGame();

        return engine;
    }

    private static void PlayUntil(GameEngine engine, Func<GameState, bool> done, int maxSteps = 4000)
    {
        var state = engine.State;

        for (var step = 0; step < maxSteps && !done(state); step++)
        {
            var player = state.Current!;

            switch (state.Phase)
            {
                case GamePhase.Draw:
                    engine.Draw(player.Id, BotBrain.ChooseDraw(state, player));
                    break;

                case GamePhase.StealWindow:
                    engine.PassSteal();
                    break;

                case GamePhase.Action:
                    if (BotBrain.TryLayDown(state, player) is { } specs && engine.LayDown(player.Id, specs).Ok)
                        break;

                    if (BotBrain.FindPlacements(state, player) is [var (meldId, cardId), ..]
                        && engine.Extend(player.Id, meldId, cardId).Ok)
                        break;

                    engine.Discard(player.Id, BotBrain.ChooseDiscard(state, player));
                    break;

                case GamePhase.RoundEnd:
                    engine.NextRound();
                    break;
            }
        }
    }

    [Fact]
    public void A_game_in_progress_survives_a_round_trip_to_disk()
    {
        var engine = NewGame(out var state, ("bot-1", true), ("bot-2", true));

        PlayUntil(engine, s => s.RoundIndex >= 1 && s.Table.Count > 0 && s.Phase == GamePhase.Draw);

        Assert.True(state.RoundIndex >= 1);
        Assert.NotEmpty(state.Table);

        var json = GameSnapshot.Write(state);
        var restored = GameSnapshot.Read(json);

        Assert.NotNull(restored);
        Assert.Equal(json, GameSnapshot.Write(restored!));
        Assert.Equal(state.RoundIndex, restored!.RoundIndex);
        Assert.Equal(state.CurrentPlayerIndex, restored.CurrentPlayerIndex);
        Assert.Equal(state.Stock.Select(c => c.Id), restored.Stock.Select(c => c.Id));
        Assert.Equal(state.Discard, restored.Discard);

        foreach (var player in state.Players)
        {
            var twin = restored.Find(player.Id)!;

            Assert.Equal(player.Hand, twin.Hand);
            Assert.Equal(player.RoundScores, twin.RoundScores);
            Assert.Equal(player.TotalScore, twin.TotalScore);
            Assert.Equal(player.HasLaidDown, twin.HasLaidDown);
            Assert.Equal(player.Tally.Turns, twin.Tally.Turns);
        }

        Assert.Equal(state.Table.Select(m => (m.Id, m.Kind, m.OwnerId)), restored.Table.Select(m => (m.Id, m.Kind, m.OwnerId)));
        Assert.Equal(state.Table.SelectMany(m => m.Cards), restored.Table.SelectMany(m => m.Cards));
    }

    [Fact]
    public void A_restored_game_keeps_being_playable_to_the_end()
    {
        var engine = NewGame(out var state, ("bot-1", true), ("bot-2", true));

        PlayUntil(engine, s => s.RoundIndex >= 2);

        using var room = GameRoom.Restore(GameSnapshot.Read(GameSnapshot.Write(state))!);
        var resumed = new GameEngine(room.State, new Random(3));

        PlayUntil(resumed, s => s.Phase == GamePhase.GameOver);

        Assert.Equal(GamePhase.GameOver, room.State.Phase);
        Assert.All(room.State.Players, p => Assert.Equal(room.State.TotalRounds, p.RoundScores.Count));
        Assert.All(room.State.Table.GroupBy(m => m.Id), g => Assert.Single(g));
    }

    [Fact]
    public void New_melds_after_a_restore_never_reuse_an_id()
    {
        var engine = NewGame(out var state, ("bot-1", true));
        var id = 500;

        PlayUntil(engine, s => s.Table.Count > 0 && s.Phase == GamePhase.Draw);

        var restored = GameSnapshot.Read(GameSnapshot.Write(state))!;
        var resumed = new GameEngine(restored, new Random(5));
        var player = restored.Current!;

        player.HasLaidDown = false;
        Assert.True(resumed.Draw(player.Id, DrawSource.Discard).Ok);

        player.Hand.Clear();

        var contract = restored.Contract;

        for (var t = 0; t < contract.Trios; t++)
            player.Hand.AddRange([new Card(id++, Suit.Clubs, Rank.Five + t), new Card(id++, Suit.Hearts, Rank.Five + t), new Card(id++, Suit.Spades, Rank.Five + t)]);

        for (var e = 0; e < contract.Escaleras; e++)
            player.Hand.AddRange(Enumerable.Range(0, 4).Select(i => new Card(id++, Suit.Diamonds + e, Rank.Two + i)));

        player.Hand.Add(new Card(id++, Suit.Spades, Rank.King));

        var specs = HandAnalyzer.FindExactContract(player.Hand.Take(player.Hand.Count - 1).ToList(), contract, restored.Options);

        Assert.NotNull(specs);
        Assert.True(resumed.LayDown(player.Id, specs!).Ok);
        Assert.All(restored.Table.GroupBy(m => m.Id), g => Assert.Single(g));
    }

    [Theory]
    [InlineData(GamePhase.Lobby)]
    [InlineData(GamePhase.GameOver)]
    public void Only_games_in_progress_are_offered_to_resume(GamePhase phase)
    {
        NewGame(out var state, ("bot-1", true));
        state.Phase = phase;

        Assert.False(GameSnapshot.IsResumable(state));
        Assert.Null(GameSnapshot.Read(GameSnapshot.Write(state)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ roto")]
    [InlineData("{\"RoomId\":\"x\"}")]
    public void A_broken_save_is_ignored(string json)
        => Assert.Null(GameSnapshot.Read(json));

    [Fact]
    public void Guests_come_back_disconnected_and_their_turn_is_skipped()
    {
        NewGame(out var state, ("guest", false), ("bot-1", true));

        state.CurrentPlayerIndex = state.Players.FindIndex(p => p.Id == "guest");
        state.Phase = GamePhase.Draw;
        state.TypingIds.Add("bot-1");

        using var room = GameRoom.Restore(GameSnapshot.Read(GameSnapshot.Write(state))!);

        Assert.False(room.State.Find("guest")!.IsConnected);
        Assert.True(room.State.Find("host")!.IsConnected);
        Assert.True(room.State.Find("bot-1")!.IsConnected);
        Assert.NotEqual("guest", room.State.Current!.Id);
        Assert.Empty(room.State.TypingIds);
    }

    [Fact]
    public void A_restored_steal_window_gets_a_fresh_countdown()
    {
        var engine = NewGame(out var state, ("bot-1", true), ("bot-2", true));

        state.CurrentPlayerIndex = state.Players.FindIndex(p => p.Id == "host");
        state.Phase = GamePhase.Draw;
        state.DiscardOwnerId = "bot-1";

        Assert.True(engine.Draw("host", DrawSource.Stock).Ok);
        Assert.Equal(GamePhase.StealWindow, state.Phase);

        var json = GameSnapshot.Write(state);
        var before = DateTimeOffset.UtcNow;

        using var room = GameRoom.Restore(GameSnapshot.Read(json)!);

        Assert.Equal(GamePhase.StealWindow, room.State.Phase);
        Assert.True(room.State.Steal!.Deadline >= before.AddSeconds(state.Options.StealWindowSeconds - 1));
    }
}

public class PauseTests
{
    private static GameRoom NewRoom(bool withGuest = false)
    {
        var room = new GameRoom("sala", "Mesa", GameOptions.ForPreset(RulePreset.LatinAmerica));

        room.AddHumanPlayer("host", "Ana", isHost: true);

        if (withGuest)
            room.AddHumanPlayer("guest", "Beto", isHost: false);

        return room;
    }

    private static async Task StartAsync(GameRoom room, int bots = 2)
    {
        for (var i = 0; i < bots; i++)
            await room.HandleAsync("host", new ClientMessage { Type = MessageType.AddBot });

        Assert.Null(await room.HandleAsync("host", new ClientMessage { Type = MessageType.Start }));
    }

    [Fact]
    public async Task Only_a_game_against_bots_can_be_paused()
    {
        using var solo = NewRoom();
        using var mixed = NewRoom(withGuest: true);

        await StartAsync(solo);
        await StartAsync(mixed);

        Assert.True(solo.ViewFor("host").CanPause);
        Assert.False(mixed.ViewFor("host").CanPause);
        Assert.False(mixed.ViewFor("guest").CanPause);
        Assert.NotNull(await mixed.HandleAsync("host", new ClientMessage { Type = MessageType.Pause }));
        Assert.False(mixed.State.Paused);
    }

    [Fact]
    public async Task The_lobby_cannot_be_paused()
    {
        using var room = NewRoom();

        await room.HandleAsync("host", new ClientMessage { Type = MessageType.AddBot });

        Assert.False(room.ViewFor("host").CanPause);
        Assert.NotNull(await room.HandleAsync("host", new ClientMessage { Type = MessageType.Pause }));
    }

    [Fact]
    public async Task While_paused_no_move_is_accepted_and_resuming_restores_play()
    {
        using var room = NewRoom();
        await StartAsync(room);

        var state = room.State;
        state.CurrentPlayerIndex = state.Players.FindIndex(p => p.Id == "host");
        state.Phase = GamePhase.Draw;

        Assert.Null(await room.HandleAsync("host", new ClientMessage { Type = MessageType.Pause }));
        Assert.True(room.ViewFor("host").Paused);

        var error = await room.HandleAsync("host", new ClientMessage { Type = MessageType.Draw, Source = (int)DrawSource.Discard });

        Assert.NotNull(error);
        Assert.Equal(GamePhase.Draw, state.Phase);

        Assert.Null(await room.HandleAsync("host", new ClientMessage { Type = MessageType.Resume }));
        Assert.False(room.ViewFor("host").Paused);
        Assert.Null(await room.HandleAsync("host", new ClientMessage { Type = MessageType.Draw, Source = (int)DrawSource.Discard }));
        Assert.Equal(GamePhase.Action, state.Phase);
    }

    [Fact]
    public async Task Bots_wait_while_the_game_is_paused()
    {
        using var room = NewRoom();
        await StartAsync(room);

        var state = room.State;
        state.CurrentPlayerIndex = state.Players.FindIndex(p => p.IsBot);
        state.Phase = GamePhase.Draw;

        await room.HandleAsync("host", new ClientMessage { Type = MessageType.Pause });

        var hand = state.Current!.Hand.Count;
        _ = room.RunAsync();

        await Task.Delay(1500);

        Assert.Equal(GamePhase.Draw, state.Phase);
        Assert.Equal(hand, state.Current!.Hand.Count);
    }

    [Fact]
    public async Task Bots_wait_until_every_human_has_finished_the_intro()
    {
        using var room = NewRoom(withGuest: true);
        await StartAsync(room);

        var state = room.State;
        state.CurrentPlayerIndex = state.Players.FindIndex(p => p.IsBot);
        state.Phase = GamePhase.Draw;

        var bot = state.Current!;
        var hand = bot.Hand.Count;
        _ = room.RunAsync();

        await Task.Delay(1500);
        Assert.Equal(hand, bot.Hand.Count);

        Assert.Null(await room.HandleAsync("host", new ClientMessage { Type = MessageType.IntroDone }));
        await Task.Delay(1800);
        Assert.Equal(hand, bot.Hand.Count);
        Assert.Equal(GamePhase.Draw, state.Phase);

        Assert.Null(await room.HandleAsync("guest", new ClientMessage { Type = MessageType.IntroDone }));
        await Task.Delay(2200);
        Assert.False(state.Phase == GamePhase.Draw && state.Current == bot);
    }

    [Fact]
    public void The_steal_countdown_freezes_during_a_pause()
    {
        var state = new GameState { RoomId = "t", RoomName = "Mesa", Options = GameOptions.ForPreset(RulePreset.LatinAmerica) };
        var engine = new GameEngine(state, new Random(2));

        engine.AddPlayer("host", "Ana", false, isHost: true);
        engine.AddPlayer("bot-1", "Chelo", true);
        engine.AddPlayer("bot-2", "Lola", true);
        engine.StartGame();

        state.CurrentPlayerIndex = state.Players.FindIndex(p => p.Id == "bot-1");
        state.Phase = GamePhase.Draw;
        state.DiscardOwnerId = "bot-2";

        Assert.True(engine.Draw("bot-1", DrawSource.Stock).Ok);
        Assert.True(engine.Pause("host").Ok);

        state.PausedAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        var deadline = state.Steal!.Deadline;

        Assert.True(PlayerView.For(state, "host").Steal!.SecondsLeft > 0);
        Assert.True(engine.Resume("host").Ok);
        Assert.True(state.Steal!.Deadline >= deadline.AddSeconds(29));
        Assert.False(engine.Tick());
    }

    [Fact]
    public async Task A_paused_game_is_saved_paused()
    {
        using var room = NewRoom();
        await StartAsync(room);

        await room.HandleAsync("host", new ClientMessage { Type = MessageType.Pause });

        Assert.True(room.TryCapture(TimeSpan.FromSeconds(1), out var saved));

        using var restored = GameRoom.Restore(GameSnapshot.Read(saved)!);

        Assert.True(restored.ViewFor("host").Paused);
        Assert.NotNull(restored.State.PausedAt);
    }
}
