using Continental.Core.Model;
using Continental.Core.Rules;
using Xunit;

namespace Continental.Core.Tests;

public class MeldValidatorTests
{
    private static readonly GameOptions LatAm = GameOptions.ForPreset(RulePreset.LatinAmerica);
    private static readonly GameOptions Spain = GameOptions.ForPreset(RulePreset.Spain);

    private int _id;

    private Card C(Suit suit, Rank rank) => new(_id++, suit, rank);

    private Card J() => Card.Joker(_id++);

    [Fact]
    public void Three_of_a_kind_is_a_trio()
        => Assert.True(MeldValidator.TryTrio([C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King)], LatAm).Ok);

    [Fact]
    public void Two_of_a_kind_is_too_short()
        => Assert.False(MeldValidator.TryTrio([C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King)], LatAm).Ok);

    [Fact]
    public void Joker_completes_a_trio()
        => Assert.True(MeldValidator.TryTrio([C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), J()], LatAm).Ok);

    [Fact]
    public void Mixed_ranks_are_not_a_trio()
        => Assert.False(MeldValidator.TryTrio([C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.Queen), C(Suit.Spades, Rank.King)], LatAm).Ok);

    [Fact]
    public void All_jokers_form_a_trio()
        => Assert.True(MeldValidator.TryTrio([J(), J(), J()], LatAm).Ok);

    [Fact]
    public void LatinAmerica_allows_a_repeated_suit_in_a_trio()
        => Assert.True(MeldValidator.TryTrio([C(Suit.Clubs, Rank.King), C(Suit.Clubs, Rank.King), C(Suit.Spades, Rank.King)], LatAm).Ok);

    [Fact]
    public void Spain_rejects_a_trio_that_repeats_a_suit_across_the_two_decks()
    {
        var nines = new[] { C(Suit.Clubs, Rank.Nine), C(Suit.Clubs, Rank.Nine), C(Suit.Spades, Rank.Nine) };
        var kings = new[] { C(Suit.Spades, Rank.King), C(Suit.Spades, Rank.King), C(Suit.Hearts, Rank.King) };

        Assert.False(MeldValidator.TryTrio(nines, Spain).Ok);
        Assert.False(MeldValidator.TryTrio(kings, Spain).Ok);

        Assert.Contains("palo", MeldValidator.TryTrio(nines, Spain).Error);

        Assert.True(MeldValidator.TryTrio(nines, LatAm).Ok);
        Assert.True(MeldValidator.TryTrio(kings, LatAm).Ok);
    }

    [Fact]
    public void Spain_caps_a_trio_at_the_four_suits()
    {
        var five = new[]
        {
            C(Suit.Clubs, Rank.Seven), C(Suit.Diamonds, Rank.Seven), C(Suit.Hearts, Rank.Seven),
            C(Suit.Spades, Rank.Seven), J()
        };

        Assert.False(MeldValidator.TryTrio(five, Spain).Ok);
        Assert.True(MeldValidator.TryTrio(five.Take(4).ToArray(), Spain).Ok);
    }

    [Fact]
    public void Spain_rejects_a_repeated_suit_in_a_trio()
        => Assert.False(MeldValidator.TryTrio([C(Suit.Clubs, Rank.King), C(Suit.Clubs, Rank.King), C(Suit.Spades, Rank.King)], Spain).Ok);

    [Fact]
    public void Four_in_sequence_and_suit_is_an_escalera()
        => Assert.True(MeldValidator.TryEscalera(
            [C(Suit.Hearts, Rank.Four), C(Suit.Hearts, Rank.Five), C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven)], LatAm).Ok);

    [Fact]
    public void Three_in_sequence_is_too_short()
        => Assert.False(MeldValidator.TryEscalera(
            [C(Suit.Hearts, Rank.Four), C(Suit.Hearts, Rank.Five), C(Suit.Hearts, Rank.Six)], LatAm).Ok);

    [Fact]
    public void Mixed_suits_are_not_an_escalera()
        => Assert.False(MeldValidator.TryEscalera(
            [C(Suit.Hearts, Rank.Four), C(Suit.Clubs, Rank.Five), C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven)], LatAm).Ok);

    [Fact]
    public void Joker_fills_a_gap()
        => Assert.True(MeldValidator.TryEscalera(
            [C(Suit.Hearts, Rank.Four), J(), C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven)], LatAm).Ok);

    [Fact]
    public void Two_adjacent_jokers_are_rejected()
        => Assert.False(MeldValidator.TryEscalera(
            [C(Suit.Hearts, Rank.Four), J(), J(), C(Suit.Hearts, Rank.Seven)], LatAm).Ok);

    [Fact]
    public void Two_separated_jokers_are_allowed()
        => Assert.True(MeldValidator.TryEscalera(
            [C(Suit.Hearts, Rank.Four), J(), C(Suit.Hearts, Rank.Six), J(), C(Suit.Hearts, Rank.Eight)], LatAm).Ok);

    [Fact]
    public void A_repeated_rank_cannot_sit_in_one_run()
        => Assert.False(MeldValidator.TryEscalera(
            [C(Suit.Hearts, Rank.Four), C(Suit.Hearts, Rank.Four), C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven)], LatAm).Ok);

    [Fact]
    public void Ace_is_high_in_both_variants()
    {
        Assert.True(MeldValidator.TryEscalera(
            [C(Suit.Spades, Rank.Jack), C(Suit.Spades, Rank.Queen), C(Suit.Spades, Rank.King), C(Suit.Spades, Rank.Ace)], LatAm).Ok);
        Assert.True(MeldValidator.TryEscalera(
            [C(Suit.Spades, Rank.Jack), C(Suit.Spades, Rank.Queen), C(Suit.Spades, Rank.King), C(Suit.Spades, Rank.Ace)], Spain).Ok);
    }

    [Fact]
    public void Only_Spain_lets_the_ace_run_low()
    {
        var cards = new[] { C(Suit.Spades, Rank.Ace), C(Suit.Spades, Rank.Two), C(Suit.Spades, Rank.Three), C(Suit.Spades, Rank.Four) };

        Assert.True(MeldValidator.TryEscalera(cards, Spain).Ok);
        Assert.False(MeldValidator.TryEscalera(cards, LatAm).Ok);
    }

    [Fact]
    public void Only_Spain_lets_the_ace_hinge_between_king_and_two()
    {
        var cards = new[] { C(Suit.Spades, Rank.Queen), C(Suit.Spades, Rank.King), C(Suit.Spades, Rank.Ace), C(Suit.Spades, Rank.Two) };

        Assert.True(MeldValidator.TryEscalera(cards, Spain).Ok);
        Assert.False(MeldValidator.TryEscalera(cards, LatAm).Ok);
    }

    [Fact]
    public void Cards_come_back_in_playing_order()
    {
        var result = MeldValidator.TryEscalera(
            [C(Suit.Hearts, Rank.Seven), C(Suit.Hearts, Rank.Four), J(), C(Suit.Hearts, Rank.Six)], LatAm);

        Assert.True(result.Ok);
        Assert.NotNull(result.Arranged);
        Assert.Equal(Rank.Four, result.Arranged![0].Rank);
        Assert.True(result.Arranged[1].IsJoker);
        Assert.Equal(Rank.Six, result.Arranged[2].Rank);
        Assert.Equal(Rank.Seven, result.Arranged[3].Rank);
    }

    [Fact]
    public void An_escalera_extends_at_both_ends_only()
    {
        var (meld, _) = MeldValidator.Build("m1", "p1", MeldKind.Escalera,
            [C(Suit.Hearts, Rank.Four), C(Suit.Hearts, Rank.Five), C(Suit.Hearts, Rank.Six), C(Suit.Hearts, Rank.Seven)], LatAm);

        Assert.NotNull(meld);
        Assert.True(MeldValidator.CanExtend(meld!, C(Suit.Hearts, Rank.Eight), LatAm, out _));
        Assert.True(MeldValidator.CanExtend(meld!, C(Suit.Hearts, Rank.Three), LatAm, out _));
        Assert.False(MeldValidator.CanExtend(meld!, C(Suit.Hearts, Rank.Nine), LatAm, out _));
        Assert.False(MeldValidator.CanExtend(meld!, C(Suit.Clubs, Rank.Eight), LatAm, out _));
    }

    [Fact]
    public void A_trio_takes_a_fourth_card_of_the_same_rank()
    {
        var (meld, _) = MeldValidator.Build("m2", "p1", MeldKind.Trio,
            [C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King)], LatAm);

        Assert.NotNull(meld);
        Assert.True(MeldValidator.CanExtend(meld!, C(Suit.Diamonds, Rank.King), LatAm, out _));
        Assert.True(MeldValidator.CanExtend(meld!, Card.Joker(999), LatAm, out _));
        Assert.False(MeldValidator.CanExtend(meld!, C(Suit.Diamonds, Rank.Queen), LatAm, out _));
    }
}
