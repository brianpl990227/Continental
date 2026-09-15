using Continental.Core.Model;

namespace Continental.Core.Rules;

public readonly record struct MeldResult(bool Ok, string? Error, List<Card>? Arranged)
{
    public static MeldResult Fail(string error) => new(false, error, null);

    public static MeldResult Success(List<Card> arranged) => new(true, null, arranged);
}

public static class MeldValidator
{
    public static MeldResult TryTrio(IReadOnlyList<Card> cards, GameOptions options)
    {
        if (cards.Count < options.MinTrioSize)
            return MeldResult.Fail($"Un trío necesita al menos {options.MinTrioSize} cartas.");

        var naturals = cards.Where(c => !c.IsJoker).ToList();
        var jokers = cards.Count - naturals.Count;

        if (naturals.Count == 0)
            return MeldResult.Success([.. cards]);

        var rank = naturals[0].Rank;

        if (naturals.Any(c => c.Rank != rank))
            return MeldResult.Fail("Todas las cartas de un trío deben tener el mismo valor.");

        if (options.TrioRequiresDistinctSuits)
        {
            var suits = naturals.Select(c => c.Suit).ToList();

            if (suits.Count != suits.Distinct().Count())
                return MeldResult.Fail("En esta variante un trío no puede repetir palo.");

            if (naturals.Count + jokers > 4)
                return MeldResult.Fail("Un trío no puede superar los cuatro palos.");
        }

        var arranged = new List<Card>(naturals);
        arranged.AddRange(cards.Where(c => c.IsJoker));

        return MeldResult.Success(arranged);
    }

    public static MeldResult TryEscalera(IReadOnlyList<Card> cards, GameOptions options)
    {
        if (cards.Count < options.MinEscaleraSize)
            return MeldResult.Fail($"Una escalera necesita al menos {options.MinEscaleraSize} cartas.");

        var naturals = cards.Where(c => !c.IsJoker).ToList();
        var jokerCards = cards.Where(c => c.IsJoker).ToList();

        if (naturals.Count == 0)
            return MeldResult.Fail("Una escalera necesita al menos una carta natural.");

        var suit = naturals[0].Suit;

        if (naturals.Any(c => c.Suit != suit))
            return MeldResult.Fail("Todas las cartas de una escalera deben ser del mismo palo.");

        foreach (var window in CandidateWindows(cards.Count, options))
        {
            var arranged = TryFit(window, naturals, jokerCards, options);

            if (arranged is not null)
                return MeldResult.Success(arranged);
        }

        return MeldResult.Fail(options.NoTwoAdjacentJokersInEscalera
            ? "No forman una escalera seguida (recuerda: no puede haber dos comodines juntos)."
            : "No forman una escalera seguida.");
    }

    private static IEnumerable<int[]> CandidateWindows(int length, GameOptions options)
    {
        if (options.AceHighAndLow)
        {
            if (length > 13)
                yield break;

            for (var start = 1; start <= 13; start++)
            {
                var window = new int[length];

                for (var i = 0; i < length; i++)
                    window[i] = (start - 1 + i) % 13 + 1;

                yield return window;
            }
        }
        else
        {

            for (var start = 2; start + length - 1 <= 14; start++)
            {
                var window = new int[length];

                for (var i = 0; i < length; i++)
                    window[i] = start + i;

                yield return window;
            }
        }
    }

    private static int SlotValue(Card card, GameOptions options)
        => card.Rank == Rank.Ace && !options.AceHighAndLow ? 14 : (int)card.Rank;

    private static List<Card>? TryFit(int[] window, List<Card> naturals, List<Card> jokers, GameOptions options)
    {
        var slots = new Card?[window.Length];
        var remaining = new List<Card>(naturals);

        for (var i = 0; i < window.Length; i++)
        {
            var match = remaining.FindIndex(c => SlotValue(c, options) == window[i]);

            if (match >= 0)
            {
                slots[i] = remaining[match];
                remaining.RemoveAt(match);
            }
        }

        if (remaining.Count > 0)
            return null;

        var holes = slots.Count(s => s is null);

        if (holes != jokers.Count)
            return null;

        if (options.NoTwoAdjacentJokersInEscalera)
        {
            for (var i = 1; i < slots.Length; i++)
            {
                if (slots[i] is null && slots[i - 1] is null)
                    return null;
            }
        }

        var queue = new Queue<Card>(jokers);
        var arranged = new List<Card>(slots.Length);

        foreach (var slot in slots)
            arranged.Add(slot ?? queue.Dequeue());

        return arranged;
    }

    public static (Meld? Meld, string? Error) Build(string id, string ownerId, MeldKind kind,
                                                    IReadOnlyList<Card> cards, GameOptions options)
    {
        var result = kind == MeldKind.Trio
            ? TryTrio(cards, options)
            : TryEscalera(cards, options);

        if (!result.Ok || result.Arranged is null)
            return (null, result.Error);

        var meld = new Meld
        {
            Id = id,
            OwnerId = ownerId,
            Kind = kind,
            Cards = result.Arranged
        };

        var naturalIndex = result.Arranged.FindIndex(c => !c.IsJoker);

        if (naturalIndex >= 0)
        {
            var firstNatural = result.Arranged[naturalIndex];

            if (kind == MeldKind.Trio)
            {
                meld.TrioRank = firstNatural.Rank;
            }
            else
            {
                meld.EscaleraSuit = firstNatural.Suit;
                meld.LowValue = SlotValue(firstNatural, options) - naturalIndex;
            }
        }

        return (meld, null);
    }

    public static bool CanExtend(Meld meld, Card card, GameOptions options, out int position)
    {
        position = -1;

        if (meld.Kind == MeldKind.Trio)
        {
            if (!card.IsJoker && meld.TrioRank is { } rank && card.Rank != rank)
                return false;

            if (options.TrioRequiresDistinctSuits && !card.IsJoker)
            {
                if (meld.Cards.Any(c => !c.IsJoker && c.Suit == card.Suit))
                    return false;

                if (meld.Size >= 4)
                    return false;
            }

            position = meld.Size;
            return true;
        }

        foreach (var candidate in new[] { 0, meld.Size })
        {
            var trial = new List<Card>(meld.Cards);
            trial.Insert(candidate, card);

            if (TryEscalera(trial, options) is { Ok: true, Arranged: not null } fit
                && fit.Arranged.SequenceEqual(trial))
            {
                position = candidate;
                return true;
            }
        }

        return false;
    }
}
