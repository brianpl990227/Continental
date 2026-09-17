using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Rules;
using Xunit;

namespace Continental.Core.Tests;

public class BotBanterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static GameState Table(params string[] bots)
    {
        var state = new GameState { RoomId = "t", RoomName = "Test", Options = GameOptions.ForPreset(RulePreset.LatinAmerica) };

        state.Players.Add(new PlayerState { Id = "human", Name = "Brian", Seat = 0 });

        var seat = 1;

        foreach (var name in bots)
            state.Players.Add(new PlayerState { Id = name.ToLowerInvariant(), Name = name, IsBot = true, Seat = seat++ });

        state.Phase = GamePhase.Action;
        state.CurrentPlayerIndex = 0;

        return state;
    }

    [Fact]
    public void Every_bot_name_has_a_voice_and_every_voice_greets()
    {
        foreach (var name in new[] { "Chelo", "Marta", "Rubén", "Vicky", "Sombra", "Tiburón", "Águila", "Lola", "Pepa", "Nino", "Cualquiera" })
        {
            var state = Table(name);
            var banter = new BotBanter(new Random(1));

            banter.React(state, new GameEvent(GameEventKind.GameStarted), T0);

            Assert.Contains(banter.Pending, l => l.BotId == name.ToLowerInvariant());
        }
    }

    [Fact]
    public void Lines_are_delivered_only_when_they_are_due_and_bots_show_as_typing_first()
    {
        var state = Table("Marta");
        var banter = new BotBanter(new Random(3));

        banter.React(state, new GameEvent(GameEventKind.GameStarted), T0);

        var line = Assert.Single(banter.Pending.Where(l => l.BotId == "marta"));

        Assert.True(line.DueAt > T0 + TimeSpan.FromSeconds(1));
        Assert.Empty(banter.Due(T0));
        Assert.Contains("marta", banter.Typing(line.DueAt - TimeSpan.FromSeconds(1)));
        Assert.Contains(banter.Due(line.DueAt), l => l.Text == line.Text);
        Assert.Empty(banter.Due(line.DueAt));
    }

    [Fact]
    public void A_bot_complains_when_someone_takes_its_joker()
    {
        var hits = 0;

        for (var seed = 0; seed < 20; seed++)
        {
            var state = Table("Vicky", "Rubén");
            var banter = new BotBanter(new Random(seed));

            banter.React(state, new GameEvent(GameEventKind.JokerSwapped, "human", "vicky", new Card(1, Suit.Hearts, Rank.Five)), T0);

            if (banter.Pending.Any(l => l.BotId == "vicky"))
                hits++;
        }

        Assert.True(hits >= 15, $"la víctima solo protestó en {hits} de 20 partidas");
    }

    [Fact]
    public void Placeholders_never_leak_into_the_text()
    {
        var state = Table("Chelo", "Lola", "Tiburón");
        var banter = new BotBanter(new Random(11));

        var events = new[]
        {
            new GameEvent(GameEventKind.GameStarted),
            new GameEvent(GameEventKind.LaidDown, "human"),
            new GameEvent(GameEventKind.Stole, "chelo", "human", new Card(1, Suit.Spades, Rank.King)),
            new GameEvent(GameEventKind.JokerSwapped, "lola", "chelo", new Card(2, Suit.Hearts, Rank.Nine)),
            new GameEvent(GameEventKind.Discarded, "human", null, Card.Joker(3)),
            new GameEvent(GameEventKind.ChatSaid, "human", null, null, "hola a todos"),
            new GameEvent(GameEventKind.ChatSaid, "human", null, null, "jajaja"),
            new GameEvent(GameEventKind.ChatSaid, "human", null, null, "¿quién va ganando?")
        };

        var now = T0;
        var seen = new List<string>();

        foreach (var e in events)
        {
            for (var i = 0; i < 6; i++)
            {
                banter.React(state, e, now);
                now += TimeSpan.FromSeconds(20);
                seen.AddRange(banter.Due(now).Select(l => l.Text));
            }
        }

        Assert.NotEmpty(seen);
        Assert.All(seen, t => Assert.DoesNotContain("{", t));
    }

    [Fact]
    public void A_human_who_dawdles_is_nudged_exactly_once_per_turn()
    {
        var state = Table("Pepa", "Nino");
        state.Phase = GamePhase.Draw;
        state.TurnStartedAt = T0;

        var banter = new BotBanter(new Random(5));

        banter.Tick(state, T0 + TimeSpan.FromSeconds(10));
        Assert.Empty(banter.Pending);

        banter.Tick(state, T0 + TimeSpan.FromSeconds(40));
        banter.Tick(state, T0 + TimeSpan.FromSeconds(50));
        banter.Tick(state, T0 + TimeSpan.FromSeconds(90));

        Assert.Single(banter.Pending, l => l.Text.Contains("Brian"));

        state.TurnStartedAt = T0 + TimeSpan.FromSeconds(100);
        banter.Due(T0 + TimeSpan.FromSeconds(200));
        banter.Tick(state, T0 + TimeSpan.FromSeconds(140));
        banter.Tick(state, T0 + TimeSpan.FromSeconds(150));

        Assert.Single(banter.Pending, l => l.Text.Contains("Brian"));
    }

    [Fact]
    public void Bots_stay_quiet_when_it_is_a_bots_turn()
    {
        var state = Table("Pepa");
        state.Phase = GamePhase.Draw;
        state.CurrentPlayerIndex = 1;
        state.TurnStartedAt = T0;

        var banter = new BotBanter(new Random(5));
        banter.Tick(state, T0 + TimeSpan.FromMinutes(5));

        Assert.Empty(banter.Pending);
    }

    [Theory]
    [InlineData(RulePreset.LatinAmerica, 1234)]
    [InlineData(RulePreset.Spain, 9876)]
    [InlineData(RulePreset.LatinAmerica, 77)]
    public void Chatter_over_a_full_game_stays_at_a_human_pace(RulePreset preset, int seed)
    {
        var random = new Random(seed);
        var state = new GameState { RoomId = "sim", RoomName = "Sim", Options = GameOptions.ForPreset(preset) };
        var engine = new GameEngine(state, random);
        var banter = new BotBanter(new Random(seed + 1));

        var clock = T0;
        var delivered = new List<BanterLine>();

        engine.Happened += e => banter.React(state, e, clock);

        foreach (var name in new[] { "Chelo", "Marta", "Rubén", "Vicky" })
            engine.AddPlayer(name.ToLowerInvariant(), name, isBot: true);

        Assert.True(engine.StartGame().Ok);

        var steps = 0;

        while (state.Phase != GamePhase.GameOver && steps++ < 40_000)
        {
            clock += TimeSpan.FromSeconds(2.5);
            delivered.AddRange(banter.Due(clock));

            switch (state.Phase)
            {
                case GamePhase.StealWindow:
                    engine.PassSteal();
                    break;

                case GamePhase.Draw:
                {
                    var bot = state.Current!;
                    engine.Draw(bot.Id, BotBrain.ChooseDraw(state, bot));
                    break;
                }

                case GamePhase.Action:
                {
                    var bot = state.Current!;

                    if (BotBrain.TryLayDown(state, bot) is { } specs)
                    {
                        engine.LayDown(bot.Id, specs);
                        break;
                    }

                    if (BotBrain.FindJokerSwap(state, bot) is { } swap
                        && engine.SwapJoker(bot.Id, swap.MeldId, swap.CardId, swap.TargetMeldId, swap.Position).Ok)
                        break;

                    if (BotBrain.FindPlacements(state, bot).Any(p => engine.Extend(bot.Id, p.MeldId, p.CardId).Ok))
                        break;

                    engine.Discard(bot.Id, BotBrain.ChooseDiscard(state, bot));
                    break;
                }

                case GamePhase.RoundEnd:
                    clock += TimeSpan.FromSeconds(8);
                    delivered.AddRange(banter.Due(clock));
                    engine.NextRound();
                    break;
            }
        }

        clock += TimeSpan.FromSeconds(10);
        delivered.AddRange(banter.Due(clock));

        Assert.Equal(GamePhase.GameOver, state.Phase);

        var minutes = (clock - T0).TotalMinutes;
        var perMinute = delivered.Count / minutes;

        Assert.True(perMinute >= 0.8, $"demasiado silencio: {perMinute:F2} líneas por minuto en {minutes:F1} minutos");
        Assert.True(perMinute <= 7, $"demasiado ruido: {perMinute:F2} líneas por minuto en {minutes:F1} minutos");

        var ordered = delivered.OrderBy(l => l.DueAt).ToList();

        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i].DueAt - ordered[i - 1].DueAt >= TimeSpan.FromSeconds(2.4),
                        $"dos líneas casi a la vez: '{ordered[i - 1].Text}' y '{ordered[i].Text}'");

        Assert.True(delivered.Select(l => l.BotId).Distinct().Count() >= 3, "casi siempre habla el mismo bot");
        Assert.True(delivered.Select(l => l.Text).Distinct().Count() > delivered.Count * 0.6, "se repiten demasiado");
    }
}
