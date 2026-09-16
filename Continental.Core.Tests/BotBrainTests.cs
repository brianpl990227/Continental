using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Rules;
using Xunit;

namespace Continental.Core.Tests;

public class BotBrainTests
{
    private static int _id = 1;

    private static Card C(Suit suit, Rank rank) => new(_id++, suit, rank);

    private static Card J() => Card.Joker(_id++);

    private static GameState Table(int roundIndex, RulePreset preset = RulePreset.LatinAmerica)
    {
        var state = new GameState { RoomId = "t", RoomName = "Test", Options = GameOptions.ForPreset(preset), RoundIndex = roundIndex };

        state.Players.Add(new PlayerState { Id = "bot", Name = "Bot", IsBot = true, Seat = 0 });
        state.Players.Add(new PlayerState { Id = "rival", Name = "Rival", Seat = 1 });
        state.Phase = GamePhase.Action;
        state.CurrentPlayerIndex = 0;

        return state;
    }

    private static PlayerState Bot(GameState state) => state.Players[0];

    private static PlayerState Rival(GameState state) => state.Players[1];

    private static void LayTrio(GameState state, PlayerState owner, Rank rank, string id)
    {
        owner.HasLaidDown = true;
        state.Table.Add(new Meld
        {
            Id = id,
            OwnerId = owner.Id,
            Kind = MeldKind.Trio,
            TrioRank = rank,
            Cards = [C(Suit.Clubs, rank), C(Suit.Hearts, rank), C(Suit.Spades, rank)]
        });
    }

    [Fact]
    public void The_planner_counts_how_many_cards_are_still_missing()
    {
        var state = Table(roundIndex: 1);
        var bot = Bot(state);

        bot.Hand.AddRange([
            C(Suit.Spades, Rank.Three), C(Suit.Spades, Rank.Four), C(Suit.Spades, Rank.Six), C(Suit.Spades, Rank.Seven),
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King),
            C(Suit.Diamonds, Rank.Nine)
        ]);

        var knowledge = new TableKnowledge(state, bot);
        var plan = HandPlanner.Plan(bot.Hand, state.Contract, state.Options, knowledge, PlanWeights.Normal);

        Assert.Equal(2, plan.Missing);
        Assert.Equal(2, plan.Melds.Count);
        Assert.Contains(plan.Unused, c => c.Rank == Rank.Nine);

        bot.Hand.Add(J());

        var withJoker = HandPlanner.Plan(bot.Hand, state.Contract, state.Options, new TableKnowledge(state, bot), PlanWeights.Normal);

        Assert.Equal(1, withJoker.Missing);
    }

    [Fact]
    public void The_planner_prefers_holes_that_can_still_be_filled()
    {
        var state = Table(roundIndex: 2);
        var bot = Bot(state);

        bot.Hand.AddRange([
            C(Suit.Spades, Rank.Three), C(Suit.Spades, Rank.Four), C(Suit.Spades, Rank.Six), C(Suit.Spades, Rank.Seven),
            C(Suit.Hearts, Rank.Nine), C(Suit.Hearts, Rank.Ten), C(Suit.Hearts, Rank.Queen), C(Suit.Hearts, Rank.King)
        ]);

        var alive = HandPlanner.Plan(bot.Hand, state.Contract, state.Options, new TableKnowledge(state, bot), PlanWeights.Normal);

        state.Discard.AddRange([C(Suit.Spades, Rank.Five), C(Suit.Spades, Rank.Five)]);

        var dead = HandPlanner.Plan(bot.Hand, state.Contract, state.Options, new TableKnowledge(state, bot), PlanWeights.Normal);

        Assert.Equal(alive.Missing, dead.Missing);
        Assert.True(dead.Score < alive.Score);
    }

    [Fact]
    public void A_bot_takes_the_discard_that_completes_a_meld_and_ignores_a_useless_one()
    {
        var state = Table(roundIndex: 0);
        var bot = Bot(state);
        state.Phase = GamePhase.Draw;

        bot.Hand.AddRange([
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King),
            C(Suit.Clubs, Rank.Five), C(Suit.Hearts, Rank.Five), C(Suit.Spades, Rank.Five),
            C(Suit.Diamonds, Rank.Nine), C(Suit.Spades, Rank.Two)
        ]);

        state.Discard.Add(C(Suit.Spades, Rank.King));
        Assert.Equal(DrawSource.Discard, BotBrain.ChooseDraw(state, bot));

        state.Discard.Clear();
        state.Discard.Add(C(Suit.Diamonds, Rank.Eight));
        Assert.Equal(DrawSource.Stock, BotBrain.ChooseDraw(state, bot));
    }

    [Fact]
    public void A_bot_does_not_hand_a_rival_a_card_that_fits_their_meld()
    {
        var state = Table(roundIndex: 0);
        var bot = Bot(state);

        LayTrio(state, Rival(state), Rank.King, "m1");

        bot.Hand.AddRange([
            C(Suit.Spades, Rank.King),
            C(Suit.Diamonds, Rank.Nine), C(Suit.Clubs, Rank.Four), C(Suit.Hearts, Rank.Seven),
            C(Suit.Spades, Rank.Two), C(Suit.Diamonds, Rank.Jack), C(Suit.Clubs, Rank.Five), C(Suit.Hearts, Rank.Ten)
        ]);

        var chosen = bot.Hand.First(c => c.Id == BotBrain.ChooseDiscard(state, bot));

        Assert.NotEqual(Rank.King, chosen.Rank);

        state.Table.Clear();
        Rival(state).HasLaidDown = false;

        var free = bot.Hand.First(c => c.Id == BotBrain.ChooseDiscard(state, bot));

        Assert.Equal(Rank.King, free.Rank);
    }

    [Fact]
    public void A_bot_dumps_points_when_a_rival_is_about_to_close()
    {
        var state = Table(roundIndex: 0);
        var bot = Bot(state);
        var rival = Rival(state);

        LayTrio(state, rival, Rank.Seven, "m1");
        rival.Hand.Add(C(Suit.Clubs, Rank.Two));

        bot.Hand.AddRange([
            C(Suit.Spades, Rank.Ace), C(Suit.Hearts, Rank.Ace),
            C(Suit.Clubs, Rank.Three), C(Suit.Diamonds, Rank.Four), C(Suit.Hearts, Rank.Six),
            C(Suit.Spades, Rank.Eight), C(Suit.Clubs, Rank.Nine), C(Suit.Diamonds, Rank.Jack)
        ]);

        var chosen = bot.Hand.First(c => c.Id == BotBrain.ChooseDiscard(state, bot));

        Assert.Equal(Rank.Ace, chosen.Rank);
    }

    [Fact]
    public void A_bot_never_throws_a_joker_while_it_has_another_card()
    {
        var state = Table(roundIndex: 0);
        var bot = Bot(state);

        bot.Hand.AddRange([J(), C(Suit.Clubs, Rank.Two), C(Suit.Hearts, Rank.Nine), C(Suit.Spades, Rank.Queen)]);

        var chosen = bot.Hand.First(c => c.Id == BotBrain.ChooseDiscard(state, bot));

        Assert.False(chosen.IsJoker);
    }

    [Fact]
    public void A_bot_swaps_a_joker_when_it_holds_the_card_underneath()
    {
        var state = Table(roundIndex: 1);
        var bot = Bot(state);
        var rival = Rival(state);
        var engine = new GameEngine(state, new Random(1));

        rival.HasLaidDown = true;
        bot.HasLaidDown = true;

        state.Table.Add(new Meld
        {
            Id = "m1",
            OwnerId = rival.Id,
            Kind = MeldKind.Escalera,
            EscaleraSuit = Suit.Hearts,
            Cards = [C(Suit.Hearts, Rank.Four), J(), C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven)]
        });

        var five = C(Suit.Hearts, Rank.Five);
        bot.Hand.AddRange([five, C(Suit.Clubs, Rank.Nine)]);

        var move = BotBrain.FindJokerSwap(state, bot);

        Assert.NotNull(move);

        var result = engine.SwapJoker(bot.Id, move!.MeldId, move.CardId, move.TargetMeldId, move.Position);

        Assert.True(result.Ok, result.Error);
        Assert.DoesNotContain(bot.Hand, c => c.Id == five.Id);
        Assert.Contains(state.Table[0].Cards, c => c.Id == five.Id);
    }

    [Fact]
    public void A_joker_swap_is_refused_when_it_would_leave_no_card_to_discard()
    {
        var state = Table(roundIndex: 1);
        var bot = Bot(state);
        var engine = new GameEngine(state, new Random(1));

        bot.HasLaidDown = true;

        state.Table.Add(new Meld
        {
            Id = "m1",
            OwnerId = bot.Id,
            Kind = MeldKind.Escalera,
            EscaleraSuit = Suit.Hearts,
            Cards = [C(Suit.Hearts, Rank.Four), J(), C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven)]
        });

        var five = C(Suit.Hearts, Rank.Five);
        bot.Hand.Add(five);

        Assert.Null(BotBrain.FindJokerSwap(state, bot));
        Assert.False(engine.SwapJoker(bot.Id, "m1", five.Id, null, null).Ok);
    }

    [Fact]
    public void A_bot_picks_the_laydown_that_leaves_the_fewest_cards_stuck_in_hand()
    {
        var state = Table(roundIndex: 0);
        var bot = Bot(state);

        bot.Hand.AddRange([
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King), C(Suit.Diamonds, Rank.King),
            C(Suit.Clubs, Rank.Five), C(Suit.Hearts, Rank.Five), C(Suit.Spades, Rank.Five),
            C(Suit.Diamonds, Rank.Nine)
        ]);

        var specs = BotBrain.TryLayDown(state, bot);

        Assert.NotNull(specs);

        var used = specs!.SelectMany(s => s.CardIds).ToHashSet();
        var leftover = bot.Hand.Where(c => !used.Contains(c.Id)).ToList();

        Assert.Equal(2, leftover.Count);
        Assert.Contains(leftover, c => c.Rank == Rank.King);
    }

    [Theory]
    [InlineData(RulePreset.LatinAmerica, 11)]
    [InlineData(RulePreset.LatinAmerica, 12)]
    [InlineData(RulePreset.Spain, 13)]
    [InlineData(RulePreset.Spain, 14)]
    public void Bots_place_before_discarding_and_rarely_feed_rivals(RulePreset preset, int seed)
    {
        var random = new Random(seed);
        var state = new GameState { RoomId = "sim", RoomName = "Sim", Options = GameOptions.ForPreset(preset) };
        var engine = new GameEngine(state, random);

        foreach (var name in new[] { "Ana", "Beto", "Caro", "Dani" })
            engine.AddPlayer(name.ToLowerInvariant(), name, isBot: true);

        Assert.True(engine.StartGame().Ok);

        var discards = 0;
        var avoidableGifts = 0;
        var steps = 0;

        while (state.Phase != GamePhase.GameOver && steps++ < 60_000)
        {
            switch (state.Phase)
            {
                case GamePhase.StealWindow:
                {
                    var offer = state.Steal!;
                    var thief = state.Players.FirstOrDefault(p => p.Id != offer.BlockedPlayerId && p.Id != offer.DiscarderId
                                                                  && BotBrain.WantsSteal(state, p));

                    if (thief is null)
                        engine.PassSteal();
                    else
                        Assert.True(engine.ClaimSteal(thief.Id).Ok);

                    break;
                }

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

                    if (BotBrain.FindJokerSwap(state, bot) is { } swap)
                    {
                        Assert.True(engine.SwapJoker(bot.Id, swap.MeldId, swap.CardId, swap.TargetMeldId, swap.Position).Ok);
                        break;
                    }

                    if (BotBrain.FindPlacements(state, bot).Any(p => engine.Extend(bot.Id, p.MeldId, p.CardId).Ok))
                        break;

                    var card = bot.Hand.First(c => c.Id == BotBrain.ChooseDiscard(state, bot));

                    Assert.False(bot.HasLaidDown && bot.Hand.Count > 1 && Fits(state, bot, card), $"{card} se tiró pudiendo colocarse");

                    discards++;

                    if (Giftable(state, bot, card) && bot.Hand.Any(c => c.Id != card.Id && !c.IsJoker && !Giftable(state, bot, c)))
                        avoidableGifts++;

                    Assert.True(engine.Discard(bot.Id, card.Id).Ok);
                    break;
                }

                case GamePhase.RoundEnd:
                    engine.NextRound();
                    break;
            }
        }

        Assert.Equal(GamePhase.GameOver, state.Phase);
        Assert.True(avoidableGifts <= Math.Max(2, discards / 100), $"{avoidableGifts} regalos evitables en {discards} descartes");
    }

    private static bool Fits(GameState state, PlayerState bot, Card card)
        => state.Table.Any(m => (state.Options.CanExtendOpponentMelds || m.OwnerId == bot.Id)
                                && MeldValidator.CanExtend(m, card, state.Options, out _));

    private static bool Giftable(GameState state, PlayerState bot, Card card)
        => state.Players.Any(p => p.Id != bot.Id && p.HasLaidDown
                                  && state.Table.Any(m => (state.Options.CanExtendOpponentMelds || m.OwnerId == p.Id)
                                                          && MeldValidator.CanExtend(m, card, state.Options, out _)));
}
