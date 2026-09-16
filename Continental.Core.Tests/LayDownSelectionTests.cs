using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Protocol;
using Continental.Core.Rules;
using Xunit;

namespace Continental.Core.Tests;

public class LayDownSelectionTests
{
    private static readonly GameOptions LatAm = GameOptions.ForPreset(RulePreset.LatinAmerica);
    private static readonly GameOptions Spain = GameOptions.ForPreset(RulePreset.Spain);

    private int _id;

    private Card C(Suit suit, Rank rank) => new(_id++, suit, rank);

    private Card J() => Card.Joker(_id++);

    private static List<Card> Cards(List<MeldSpec> specs, IReadOnlyList<Card> pool, MeldKind kind)
        => specs.Where(s => s.Kind == kind)
                .SelectMany(s => s.CardIds)
                .Select(id => pool.First(c => c.Id == id))
                .ToList();

    private static void UsesEverything(List<MeldSpec>? specs, IReadOnlyList<Card> selection)
    {
        Assert.NotNull(specs);

        var used = specs!.SelectMany(s => s.CardIds).ToList();

        Assert.Equal(selection.Count, used.Count);
        Assert.Equal(selection.Select(c => c.Id).OrderBy(x => x), used.OrderBy(x => x));
    }

    [Fact]
    public void A_fourth_king_goes_down_with_the_trio()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King), C(Suit.Diamonds, Rank.King),
            C(Suit.Clubs, Rank.Four), C(Suit.Hearts, Rank.Four), C(Suit.Spades, Rank.Four)
        };

        var specs = HandAnalyzer.FindExactContract(selection, new RoundContract(2, 0), LatAm);

        UsesEverything(specs, selection);
        Assert.Equal(2, specs!.Count);
        Assert.Contains(specs, s => s.CardIds.Count == 4);
    }

    [Fact]
    public void A_two_to_eight_run_goes_down_whole()
    {
        var selection = new List<Card>
        {
            C(Suit.Hearts, Rank.Two), C(Suit.Hearts, Rank.Three), C(Suit.Hearts, Rank.Four), C(Suit.Hearts, Rank.Five),
            C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven), C(Suit.Hearts, Rank.Eight)
        };

        var specs = HandAnalyzer.FindExactContract(selection, new RoundContract(0, 1), LatAm);

        UsesEverything(specs, selection);
        Assert.Single(specs!);
        Assert.Equal(MeldKind.Escalera, specs![0].Kind);
    }

    [Fact]
    public void The_run_keeps_the_order_it_was_selected_in()
    {
        var selection = new List<Card>
        {
            C(Suit.Spades, Rank.Nine), C(Suit.Spades, Rank.Ten), C(Suit.Spades, Rank.Jack),
            C(Suit.Spades, Rank.Queen), C(Suit.Spades, Rank.King)
        };

        var specs = HandAnalyzer.FindExactContract(selection, new RoundContract(0, 1), LatAm);

        Assert.NotNull(specs);
        Assert.Equal(selection.Select(c => c.Id), specs![0].CardIds);
    }

    [Fact]
    public void Two_groups_are_split_where_the_player_selected_them()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King), C(Suit.Diamonds, Rank.King),
            C(Suit.Clubs, Rank.Four), C(Suit.Clubs, Rank.Five), C(Suit.Clubs, Rank.Six), C(Suit.Clubs, Rank.Seven)
        };

        var specs = HandAnalyzer.FindExactContract(selection, new RoundContract(1, 1), LatAm);

        UsesEverything(specs, selection);

        var trio = specs!.First(s => s.Kind == MeldKind.Trio);

        Assert.Equal(4, trio.CardIds.Count);
        Assert.Equal(selection.Take(4).Select(c => c.Id).OrderBy(x => x), trio.CardIds.OrderBy(x => x));
    }

    [Fact]
    public void A_shared_card_goes_where_it_makes_everything_fit()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.Seven), C(Suit.Hearts, Rank.Seven), C(Suit.Spades, Rank.Seven), C(Suit.Diamonds, Rank.Seven),
            C(Suit.Diamonds, Rank.Four), C(Suit.Diamonds, Rank.Five), C(Suit.Diamonds, Rank.Six), C(Suit.Diamonds, Rank.Eight)
        };

        var specs = HandAnalyzer.FindExactContract(selection, new RoundContract(1, 1), LatAm);

        UsesEverything(specs, selection);

        Assert.Equal(3, specs!.First(s => s.Kind == MeldKind.Trio).CardIds.Count);
        Assert.Equal(5, specs!.First(s => s.Kind == MeldKind.Escalera).CardIds.Count);
    }

    [Fact]
    public void Cards_selected_out_of_order_still_go_down_together()
    {
        var selection = new List<Card>
        {
            C(Suit.Diamonds, Rank.Six), C(Suit.Clubs, Rank.Seven), C(Suit.Diamonds, Rank.Four),
            C(Suit.Hearts, Rank.Seven), C(Suit.Diamonds, Rank.Eight), C(Suit.Spades, Rank.Seven),
            C(Suit.Diamonds, Rank.Five), C(Suit.Diamonds, Rank.Seven)
        };

        var specs = HandAnalyzer.FindExactContract(selection, new RoundContract(1, 1), LatAm);

        UsesEverything(specs, selection);

        var run = Cards(specs!, selection, MeldKind.Escalera);

        Assert.Equal(5, run.Count);
        Assert.True(MeldValidator.IsRunInOrder(run, LatAm));
    }

    [Fact]
    public void Interleaved_picks_are_regrouped_into_two_trios()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.King), C(Suit.Clubs, Rank.Four), C(Suit.Hearts, Rank.King),
            C(Suit.Hearts, Rank.Four), C(Suit.Spades, Rank.King), C(Suit.Spades, Rank.Four)
        };

        var specs = HandAnalyzer.FindExactContract(selection, new RoundContract(2, 0), LatAm);

        UsesEverything(specs, selection);

        Assert.All(specs!, m => Assert.Equal(3, m.CardIds.Count));
        Assert.All(specs!, m => Assert.Single(m.CardIds
                                               .Select(id => selection.First(c => c.Id == id).Rank)
                                               .Distinct()));
    }

    [Fact]
    public void A_trio_of_jokers_alone_still_counts()
    {
        var selection = new List<Card> { J(), J(), J() };

        UsesEverything(HandAnalyzer.FindExactContract(selection, new RoundContract(1, 0), LatAm), selection);
    }

    [Fact]
    public void A_spare_card_blocks_the_lay_down_instead_of_being_left_behind()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King),
            C(Suit.Clubs, Rank.Four), C(Suit.Hearts, Rank.Four), C(Suit.Spades, Rank.Four),
            C(Suit.Diamonds, Rank.Nine)
        };

        Assert.Null(HandAnalyzer.FindExactContract(selection, new RoundContract(2, 0), LatAm));
        Assert.NotNull(HandAnalyzer.FindContract(selection, new RoundContract(2, 0), LatAm));
    }

    [Fact]
    public void Spain_refuses_a_fourth_king_that_repeats_a_suit()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King), C(Suit.Spades, Rank.King)
        };

        Assert.Null(HandAnalyzer.FindExactContract(selection, new RoundContract(1, 0), Spain));
        Assert.NotNull(HandAnalyzer.FindExactContract([.. selection.Take(3)], new RoundContract(1, 0), Spain));
    }

    [Fact]
    public void Spain_caps_the_trio_at_four_cards()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King),
            C(Suit.Diamonds, Rank.King), J()
        };

        Assert.Null(HandAnalyzer.FindExactContract(selection, new RoundContract(1, 0), Spain));
        Assert.NotNull(HandAnalyzer.FindExactContract(selection, new RoundContract(1, 0), LatAm));
    }

    [Fact]
    public void Spain_lays_down_a_long_run_that_turns_on_the_ace()
    {
        var selection = new List<Card>
        {
            C(Suit.Hearts, Rank.Jack), C(Suit.Hearts, Rank.Queen), C(Suit.Hearts, Rank.King),
            C(Suit.Hearts, Rank.Ace), C(Suit.Hearts, Rank.Two), C(Suit.Hearts, Rank.Three)
        };

        UsesEverything(HandAnalyzer.FindExactContract(selection, new RoundContract(0, 1), Spain), selection);
        Assert.Null(HandAnalyzer.FindExactContract(selection, new RoundContract(0, 1), LatAm));
    }

    [Fact]
    public void A_long_run_may_not_leave_two_jokers_side_by_side()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.Four), C(Suit.Clubs, Rank.Five), J(), J(), C(Suit.Clubs, Rank.Eight)
        };

        Assert.Null(HandAnalyzer.FindExactContract(selection, new RoundContract(0, 1), LatAm));

        var spread = new List<Card>
        {
            C(Suit.Clubs, Rank.Four), J(), C(Suit.Clubs, Rank.Six), J(), C(Suit.Clubs, Rank.Eight)
        };

        UsesEverything(HandAnalyzer.FindExactContract(spread, new RoundContract(0, 1), LatAm), spread);
    }

    [Fact]
    public void A_run_cannot_repeat_a_card_from_the_second_deck()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.Four), C(Suit.Clubs, Rank.Five), C(Suit.Clubs, Rank.Five), C(Suit.Clubs, Rank.Six)
        };

        Assert.Null(HandAnalyzer.FindExactContract(selection, new RoundContract(0, 1), LatAm));
    }

    [Fact]
    public void Three_runs_of_different_lengths_all_go_down()
    {
        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.Two), C(Suit.Clubs, Rank.Three), C(Suit.Clubs, Rank.Four), C(Suit.Clubs, Rank.Five),
            C(Suit.Hearts, Rank.Seven), C(Suit.Hearts, Rank.Eight), C(Suit.Hearts, Rank.Nine), C(Suit.Hearts, Rank.Ten),
            C(Suit.Spades, Rank.Nine), C(Suit.Spades, Rank.Ten), C(Suit.Spades, Rank.Jack), C(Suit.Spades, Rank.Queen),
            C(Suit.Spades, Rank.King)
        };

        var specs = HandAnalyzer.FindExactContract(selection, new RoundContract(0, 3), LatAm);

        UsesEverything(specs, selection);
        Assert.Equal(3, specs!.Count);
        Assert.Contains(specs, s => s.CardIds.Count == 5);
    }

    [Fact]
    public void The_whole_hand_never_goes_down_at_once()
    {
        var state = new GameState { RoomId = "t", RoomName = "Test", Options = LatAm };
        var engine = new GameEngine(state, new Random(3));

        engine.AddPlayer("p1", "Ana", false, isHost: true);
        engine.AddPlayer("p2", "Beto", false);
        engine.StartGame();

        var player = state.Current!;
        engine.Draw(player.Id, DrawSource.Discard);

        player.Hand.Clear();
        player.Hand.AddRange(
        [
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King),
            C(Suit.Clubs, Rank.Four), C(Suit.Hearts, Rank.Four), C(Suit.Spades, Rank.Four)
        ]);

        var full = engine.LayDown(player.Id,
        [
            new MeldSpec(MeldKind.Trio, [player.Hand[0].Id, player.Hand[1].Id, player.Hand[2].Id]),
            new MeldSpec(MeldKind.Trio, [player.Hand[3].Id, player.Hand[4].Id, player.Hand[5].Id])
        ]);

        Assert.False(full.Ok);
        Assert.Contains("una carta", full.Error);

        player.Hand.Add(C(Suit.Diamonds, Rank.Nine));

        Assert.True(engine.LayDown(player.Id,
        [
            new MeldSpec(MeldKind.Trio, [player.Hand[0].Id, player.Hand[1].Id, player.Hand[2].Id]),
            new MeldSpec(MeldKind.Trio, [player.Hand[3].Id, player.Hand[4].Id, player.Hand[5].Id])
        ]).Ok);
    }

    [Fact]
    public async Task The_client_path_lays_down_the_whole_selection_and_closes()
    {
        using var room = new GameRoom("t", "Test", LatAm);

        room.AddHumanPlayer("p1", "Ana", isHost: true);
        room.AddHumanPlayer("p2", "Beto", isHost: false);

        Assert.Null(await room.HandleAsync("p1", new ClientMessage { Type = MessageType.Start }));

        var state = room.State;
        var me = state.Current!;

        Assert.Null(await room.HandleAsync(me.Id, new ClientMessage { Type = MessageType.Draw, Source = (int)DrawSource.Discard }));

        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.Nine), C(Suit.Hearts, Rank.Nine), C(Suit.Spades, Rank.Nine), C(Suit.Diamonds, Rank.Nine),
            C(Suit.Hearts, Rank.Four), C(Suit.Clubs, Rank.Four), C(Suit.Spades, Rank.Four)
        };

        var spare = C(Suit.Spades, Rank.Two);

        me.Hand.Clear();
        me.Hand.AddRange(selection);
        me.Hand.Add(spare);

        var view = room.ViewFor(me.Id);
        var contract = RoundContract.Standard[view.RoundIndex];
        var specs = HandAnalyzer.FindExactContract(selection, contract, view.Options);

        Assert.NotNull(specs);

        var error = await room.HandleAsync(me.Id, new ClientMessage
        {
            Type = MessageType.LayDown,
            Melds = specs!.Select(m => new MeldSpecDto((int)m.Kind, m.CardIds.ToList())).ToList()
        });

        Assert.Null(error);

        var table = room.ViewFor(me.Id).Table;

        Assert.Equal(2, table.Count);
        Assert.All(table, m => Assert.Equal(me.Id, m.OwnerId));
        Assert.Contains(table, m => m.Kind == MeldKind.Trio && m.Cards.Count == 4);
        Assert.Contains(table, m => m.Kind == MeldKind.Trio && m.Cards.Count == 3);

        Assert.Null(await room.HandleAsync(me.Id, new ClientMessage { Type = MessageType.Discard, CardId = spare.Id }));

        Assert.Equal(GamePhase.RoundEnd, state.Phase);
        Assert.Equal(me.Id, state.LastCloserId);
        Assert.Equal(LatAm.CloseSameTurnBonus, me.RoundScores[^1]);
    }

    [Fact]
    public void The_engine_accepts_the_oversized_melds_the_table_shows()
    {
        var state = new GameState { RoomId = "t", RoomName = "Test", Options = LatAm };
        var engine = new GameEngine(state, new Random(5));

        engine.AddPlayer("p1", "Ana", false, isHost: true);
        engine.AddPlayer("p2", "Beto", false);
        engine.StartGame();

        var player = state.Current!;
        engine.Draw(player.Id, DrawSource.Discard);

        var selection = new List<Card>
        {
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King), C(Suit.Diamonds, Rank.King),
            C(Suit.Clubs, Rank.Four), C(Suit.Hearts, Rank.Four), C(Suit.Spades, Rank.Four)
        };

        player.Hand.Clear();
        player.Hand.AddRange(selection);
        player.Hand.Add(C(Suit.Diamonds, Rank.Nine));

        var specs = HandAnalyzer.FindExactContract(selection, state.Contract, state.Options);

        Assert.NotNull(specs);
        Assert.True(engine.LayDown(player.Id, specs!).Ok);
        Assert.Single(player.Hand);
        Assert.Equal(2, state.Table.Count);
        Assert.Contains(state.Table, m => m.Size == 4);
    }
}
