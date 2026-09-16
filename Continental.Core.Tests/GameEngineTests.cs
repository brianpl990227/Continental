using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Rules;
using Xunit;

namespace Continental.Core.Tests;

public class RulesTests
{
    [Theory]
    [InlineData(RulePreset.LatinAmerica, 0, 7)]
    [InlineData(RulePreset.LatinAmerica, 6, 13)]
    [InlineData(RulePreset.Spain, 0, 6)]
    [InlineData(RulePreset.Spain, 6, 12)]
    public void Deals_grow_by_one_card_per_round(RulePreset preset, int round, int expected)
        => Assert.Equal(expected, GameOptions.ForPreset(preset).CardsForRound(round));

    [Fact]
    public void The_seven_contracts_are_the_classic_ladder()
    {
        var codes = RoundContract.Standard.Select(c => c.Code).ToArray();
        Assert.Equal(["TT", "TE", "EE", "TTT", "TTE", "TEE", "EEE"], codes);
    }

    [Theory]
    [InlineData(RulePreset.LatinAmerica, 30)]
    [InlineData(RulePreset.Spain, 20)]
    public void The_ace_is_worth_what_the_variant_says(RulePreset preset, int expected)
        => Assert.Equal(expected, GameOptions.ForPreset(preset).ValueOf(new Card(0, Suit.Spades, Rank.Ace)));

    [Fact]
    public void A_shoe_is_two_decks_and_six_jokers()
    {
        var deck = Deck.Build(GameOptions.ForPreset(RulePreset.LatinAmerica));

        Assert.Equal(110, deck.Count);
        Assert.Equal(6, deck.Count(c => c.IsJoker));
        Assert.Equal(110, deck.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Every_rule_that_varies_is_shown_to_joining_players()
    {
        var summary = GameOptions.ForPreset(RulePreset.Spain).Summary();

        Assert.Contains(summary, r => r.Label == "Variante" && r.Value == "España");
        Assert.Contains(summary, r => r.Label == "Cartas en la 1ª ronda" && r.Value == "6");
        Assert.Contains(summary, r => r.Label == "Valor del As" && r.Value == "20 pts");
    }
}

public class GameEngineTests
{
    private static GameEngine NewGame(out GameState state, RulePreset preset = RulePreset.LatinAmerica, int seed = 7)
    {
        state = new GameState { RoomId = "t", RoomName = "Test", Options = GameOptions.ForPreset(preset) };
        var engine = new GameEngine(state, new Random(seed));

        engine.AddPlayer("p1", "Ana", false, isHost: true);
        engine.AddPlayer("p2", "Beto", false);

        return engine;
    }

    [Fact]
    public void A_game_needs_two_players()
    {
        var state = new GameState { RoomId = "t", RoomName = "Test" };
        var engine = new GameEngine(state, new Random(1));

        engine.AddPlayer("p1", "Ana", false, isHost: true);

        Assert.False(engine.StartGame().Ok);
    }

    [Fact]
    public void Duplicate_names_are_made_unique()
    {
        var engine = NewGame(out var state);
        engine.AddPlayer("p3", "Ana", false);

        Assert.Equal(["Ana", "Beto", "Ana 2"], state.Players.Select(p => p.Name));
    }

    [Fact]
    public void Starting_deals_the_round_one_hand_and_turns_one_card_up()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        Assert.Equal(GamePhase.Draw, state.Phase);
        Assert.All(state.Players, p => Assert.Equal(7, p.Hand.Count));
        Assert.Single(state.Discard);
    }

    [Fact]
    public void Only_the_player_whose_turn_it_is_may_draw()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        var other = state.Players.First(p => p.Id != state.Current!.Id);

        Assert.False(engine.Draw(other.Id, DrawSource.Stock).Ok);
    }

    [Fact]
    public void Taking_the_discard_moves_straight_to_the_action_phase()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        var player = state.Current!;
        var top = state.DiscardTop!.Value;

        Assert.True(engine.Draw(player.Id, DrawSource.Discard).Ok);
        Assert.Equal(GamePhase.Action, state.Phase);
        Assert.Equal(8, player.Hand.Count);
        Assert.Contains(player.Hand, c => c.Id == top.Id);
    }

    [Fact]
    public void Going_to_the_stock_offers_the_discard_to_everyone_else()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        engine.Draw(state.Current!.Id, DrawSource.Stock);

        Assert.Equal(GamePhase.StealWindow, state.Phase);
        Assert.NotNull(state.Steal);
    }

    [Fact]
    public void Stealing_costs_a_penalty_card_and_does_not_grant_a_turn()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        var drawer = state.Current!;
        engine.Draw(drawer.Id, DrawSource.Stock);

        var thief = state.Players.First(p => p.Id != drawer.Id);
        var before = thief.Hand.Count;

        Assert.True(engine.ClaimSteal(thief.Id).Ok);

        Assert.Equal(before + 2, thief.Hand.Count);

        Assert.Equal(GamePhase.Action, state.Phase);
        Assert.Equal(drawer.Id, state.Current!.Id);
    }

    [Fact]
    public void The_player_being_blocked_cannot_steal_from_themselves()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        var drawer = state.Current!;
        engine.Draw(drawer.Id, DrawSource.Stock);

        Assert.False(engine.ClaimSteal(drawer.Id).Ok);
    }

    [Fact]
    public void Laying_down_must_match_the_round_contract_exactly()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        var player = state.Current!;
        engine.Draw(player.Id, DrawSource.Discard);

        var result = engine.LayDown(player.Id, [new MeldSpec(MeldKind.Trio, [player.Hand[0].Id, player.Hand[1].Id, player.Hand[2].Id])]);

        Assert.False(result.Ok);
        Assert.Contains("2 tríos", result.Error);
    }

    [Fact]
    public void You_cannot_lay_down_a_card_you_do_not_hold()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        var player = state.Current!;
        engine.Draw(player.Id, DrawSource.Discard);

        var result = engine.LayDown(player.Id,
        [
            new MeldSpec(MeldKind.Trio, [9001, 9002, 9003]),
            new MeldSpec(MeldKind.Trio, [9004, 9005, 9006])
        ]);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Discarding_passes_the_turn()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        var player = state.Current!;
        engine.Draw(player.Id, DrawSource.Discard);
        engine.Discard(player.Id, player.Hand[0].Id);

        Assert.Equal(GamePhase.Draw, state.Phase);
        Assert.NotEqual(player.Id, state.Current!.Id);
        Assert.Equal(player.Id, state.DiscardOwnerId);
    }

    [Fact]
    public void You_must_lay_down_before_extending()
    {
        var engine = NewGame(out var state);
        engine.StartGame();

        var player = state.Current!;
        engine.Draw(player.Id, DrawSource.Discard);

        Assert.False(engine.Extend(player.Id, "m1", player.Hand[0].Id).Ok);
    }
}

public class JokerSwapTests
{
    private static int _id = 1;

    private static Card C(Suit suit, Rank rank) => new(_id++, suit, rank);

    private static Card J() => Card.Joker(_id++);

    // Mesa montada a mano: Ana bajó 4H-5H-6H-7H con el 5H tapado por un comodín,
    // y es el turno de Beto, que también está bajado.
    private static GameEngine Table(out GameState state, JokerSwap mode, string mover = "p2")
    {
        var options = GameOptions.ForPreset(RulePreset.LatinAmerica) with { JokerSwapMode = mode };

        state = new GameState { RoomId = "t", RoomName = "Test", Options = options };

        var engine = new GameEngine(state, new Random(7));
        engine.AddPlayer("p1", "Ana", false, isHost: true);
        engine.AddPlayer("p2", "Beto", false);

        foreach (var p in state.Players)
            p.HasLaidDown = true;

        state.Phase = GamePhase.Action;
        state.CurrentPlayerIndex = state.Players.FindIndex(p => p.Id == mover);
        state.Players.First(p => p.Id == mover).Hand.Add(C(Suit.Clubs, Rank.Nine));

        state.Table.Add(new Meld
        {
            Id = "m1",
            OwnerId = "p1",
            Kind = MeldKind.Escalera,
            EscaleraSuit = Suit.Hearts,
            Cards = [C(Suit.Hearts, Rank.Four), J(), C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven)]
        });

        return engine;
    }

    [Fact]
    public void Anyone_laid_down_can_swap_in_the_latin_variant()
    {
        var engine = Table(out var state, JokerSwap.AnyLaidDownPlayer);

        var beto = state.Players.First(p => p.Id == "p2");
        var five = C(Suit.Hearts, Rank.Five);
        beto.Hand.Add(five);

        var result = engine.SwapJoker("p2", "m1", five.Id, null, null);

        Assert.True(result.Ok, result.Error);

        var meld = state.Table[0];

        // El 5H entra en el hueco y el comodín liberado se recoloca en un extremo,
        // asi que la escalera pasa de cuatro a cinco cartas.
        Assert.Contains(meld.Cards, c => c.Id == five.Id);
        Assert.Single(meld.Cards, c => c.IsJoker);
        Assert.Equal(5, meld.Size);
        Assert.True(MeldValidator.IsRunInOrder(meld.Cards, state.Options));
        Assert.DoesNotContain(beto.Hand, c => c.Id == five.Id);
    }

    [Fact]
    public void Only_the_owner_can_swap_in_the_spanish_variant()
    {
        var engine = Table(out var state, JokerSwap.OwnerOnly);

        var beto = state.Players.First(p => p.Id == "p2");
        var five = C(Suit.Hearts, Rank.Five);
        beto.Hand.Add(five);

        var result = engine.SwapJoker("p2", "m1", five.Id, null, null);

        Assert.False(result.Ok);
        Assert.Contains("dueño", result.Error);
        Assert.True(state.Table[0].Cards[1].IsJoker);
    }

    [Fact]
    public void The_owner_can_always_swap_in_the_spanish_variant()
    {
        var engine = Table(out var state, JokerSwap.OwnerOnly, mover: "p1");

        var ana = state.Players.First(p => p.Id == "p1");
        var five = C(Suit.Hearts, Rank.Five);
        ana.Hand.Add(five);

        Assert.True(engine.SwapJoker("p1", "m1", five.Id, null, null).Ok);
        Assert.Contains(state.Table[0].Cards, c => c.Id == five.Id);
        Assert.True(MeldValidator.IsRunInOrder(state.Table[0].Cards, state.Options));
    }

    [Fact]
    public void Swapping_is_off_when_the_variant_says_so()
    {
        var engine = Table(out var state, JokerSwap.Off, mover: "p1");

        var ana = state.Players.First(p => p.Id == "p1");
        var five = C(Suit.Hearts, Rank.Five);
        ana.Hand.Add(five);

        Assert.False(engine.SwapJoker("p1", "m1", five.Id, null, null).Ok);
    }

    [Fact]
    public void You_must_hand_over_the_exact_card_the_joker_covers()
    {
        var engine = Table(out var state, JokerSwap.AnyLaidDownPlayer);

        var beto = state.Players.First(p => p.Id == "p2");
        var nine = C(Suit.Hearts, Rank.Nine);
        beto.Hand.Add(nine);

        var result = engine.SwapJoker("p2", "m1", nine.Id, null, null);

        Assert.False(result.Ok);
        Assert.Contains("Ningún comodín", result.Error);
    }

    [Fact]
    public void The_freed_joker_can_be_placed_on_either_end()
    {
        foreach (var (slot, expected) in new[] { (0, 0), (4, 4) })
        {
            var engine = Table(out var state, JokerSwap.AnyLaidDownPlayer);

            var beto = state.Players.First(p => p.Id == "p2");
            var five = C(Suit.Hearts, Rank.Five);
            beto.Hand.Add(five);

            Assert.True(engine.SwapJoker("p2", "m1", five.Id, null, slot).Ok);
            Assert.True(state.Table[0].Cards[expected].IsJoker);
        }
    }

    [Fact]
    public void A_joker_cannot_land_next_to_another_joker()
    {
        var engine = Table(out var state, JokerSwap.AnyLaidDownPlayer);

        // Un segundo comodín ocupando el extremo izquierdo.
        state.Table[0].Cards.Insert(0, J());

        var beto = state.Players.First(p => p.Id == "p2");
        var five = C(Suit.Hearts, Rank.Five);
        beto.Hand.Add(five);

        Assert.False(engine.SwapJoker("p2", "m1", five.Id, null, 0).Ok);
    }
}

public class BotTests
{

    [Theory]
    [InlineData(RulePreset.LatinAmerica, 1234)]
    [InlineData(RulePreset.LatinAmerica, 4242)]
    [InlineData(RulePreset.Spain, 9876)]
    [InlineData(RulePreset.Spain, 555)]
    public void A_full_game_of_bots_always_finishes(RulePreset preset, int seed)
    {
        var random = new Random(seed);
        var state = new GameState { RoomId = "sim", RoomName = "Sim", Options = GameOptions.ForPreset(preset) };
        var engine = new GameEngine(state, random);

        foreach (var name in new[] { "Ana", "Beto", "Caro", "Dani" })
            engine.AddPlayer(name.ToLowerInvariant(), name, isBot: true);

        Assert.True(engine.StartGame().Ok);

        var steps = 0;

        while (state.Phase != GamePhase.GameOver && steps++ < 40_000)
        {
            switch (state.Phase)
            {
                case GamePhase.StealWindow:
                    engine.PassSteal();
                    break;

                case GamePhase.Draw:
                {
                    var bot = state.Current!;
                    Assert.True(engine.Draw(bot.Id, BotBrain.ChooseDraw(state, bot)).Ok);
                    break;
                }

                case GamePhase.Action:
                {
                    var bot = state.Current!;

                    if (BotBrain.TryLayDown(state, bot) is { } specs)
                    {
                        Assert.True(engine.LayDown(bot.Id, specs).Ok);
                        break;
                    }

                    var placed = BotBrain.FindPlacements(state, bot)
                                         .Any(p => engine.Extend(bot.Id, p.MeldId, p.CardId).Ok);

                    if (placed)
                        break;

                    var discard = BotBrain.ChooseDiscard(state, bot);
                    Assert.True(engine.Discard(bot.Id, discard).Ok);
                    break;
                }

                case GamePhase.RoundEnd:
                    engine.NextRound();
                    break;
            }
        }

        Assert.Equal(GamePhase.GameOver, state.Phase);
        Assert.All(state.Players, p => Assert.Equal(7, p.RoundScores.Count));
        Assert.All(state.Players, p => Assert.Equal(p.RoundScores.Sum(), p.TotalScore));
    }

    [Fact]
    public void A_bot_finds_two_trios_when_they_are_there()
    {
        var options = GameOptions.ForPreset(RulePreset.LatinAmerica);
        var id = 0;

        List<Card> hand =
        [
            new(id++, Suit.Clubs, Rank.King), new(id++, Suit.Hearts, Rank.King), new(id++, Suit.Spades, Rank.King),
            new(id++, Suit.Clubs, Rank.Five), new(id++, Suit.Hearts, Rank.Five), new(id++, Suit.Spades, Rank.Five),
            new(id, Suit.Diamonds, Rank.Nine)
        ];

        var specs = HandAnalyzer.FindContract(hand, RoundContract.Standard[0], options);

        Assert.NotNull(specs);
        Assert.Equal(2, specs!.Count);
        Assert.All(specs, s => Assert.Equal(MeldKind.Trio, s.Kind));
    }

    [Fact]
    public void Two_melds_can_each_claim_a_different_joker()
    {
        var options = GameOptions.ForPreset(RulePreset.LatinAmerica);
        var id = 0;

        List<Card> hand =
        [
            new(id++, Suit.Clubs, Rank.King), new(id++, Suit.Hearts, Rank.King), Card.Joker(id++),
            new(id++, Suit.Clubs, Rank.Five), new(id++, Suit.Hearts, Rank.Five), Card.Joker(id++),
            new(id, Suit.Diamonds, Rank.Nine)
        ];

        var specs = HandAnalyzer.FindContract(hand, RoundContract.Standard[0], options);

        Assert.NotNull(specs);
        Assert.Equal(2, specs!.Count);

        var used = specs.SelectMany(s => s.CardIds).ToList();
        Assert.Equal(used.Count, used.Distinct().Count());
    }
}
