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
        // Si la escalera ya viene colocada y vale tal cual, se respeta. Reordenarla
        // siempre convertía 4-5-6-J en 3-4-5-6 aunque el jugador quisiera el 7.
        var result = kind switch
        {
            MeldKind.Trio => TryTrio(cards, options),
            _ when IsRunInOrder(cards, options) => MeldResult.Success([.. cards]),
            _ => TryEscalera(cards, options)
        };

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

    // La carta natural que un comodín está tapando en una escalera ya bajada.
    // Es lo que hay que entregar para canjearlo.
    public static Card? JokerStandsFor(Meld meld, int index, GameOptions options)
    {
        if (meld.Kind != MeldKind.Escalera || index < 0 || index >= meld.Size)
            return null;

        if (!meld.Cards[index].IsJoker)
            return null;

        var anchor = -1;

        for (var i = 0; i < meld.Size; i++)
        {
            if (!meld.Cards[i].IsJoker)
            {
                anchor = i;
                break;
            }
        }

        if (anchor < 0)
            return null;

        var suit = meld.Cards[anchor].Suit;
        var value = SlotValue(meld.Cards[anchor], options) + (index - anchor);

        Rank rank;

        if (options.AceHighAndLow)
        {
            rank = (Rank)(((value - 1) % 13 + 13) % 13 + 1);
        }
        else
        {
            if (value < 2 || value > 14)
                return null;

            rank = value == 14 ? Rank.Ace : (Rank)value;
        }

        return new Card(-1, suit, rank);
    }

    // Comprueba la secuencia TAL CUAL viene, sin reordenarla. TryEscalera devuelve la
    // primera colocación que le cuadra, que para [4 5 6 J] es [J 4 5 6]; preguntarle si
    // una secuencia concreta vale daba siempre que no en cuanto el comodín iba al final.
    public static bool IsRunInOrder(IReadOnlyList<Card> cards, GameOptions options)
    {
        if (cards.Count < options.MinEscaleraSize)
            return false;

        var naturals = cards.Where(c => !c.IsJoker).ToList();

        if (naturals.Count == 0)
            return false;

        var suit = naturals[0].Suit;

        if (naturals.Any(c => c.Suit != suit))
            return false;

        if (options.NoTwoAdjacentJokersInEscalera)
        {
            for (var i = 1; i < cards.Count; i++)
            {
                if (cards[i].IsJoker && cards[i - 1].IsJoker)
                    return false;
            }
        }

        var anchor = 0;

        while (cards[anchor].IsJoker)
            anchor++;

        var baseValue = SlotValue(cards[anchor], options) - anchor;

        if (options.AceHighAndLow)
        {
            if (cards.Count > 13)
                return false;
        }
        else if (baseValue < 2 || baseValue + cards.Count - 1 > 14)
        {
            return false;
        }

        for (var i = 0; i < cards.Count; i++)
        {
            if (cards[i].IsJoker)
                continue;

            var expected = baseValue + i;

            if (options.AceHighAndLow)
                expected = ((expected - 1) % 13 + 13) % 13 + 1;

            if (SlotValue(cards[i], options) != expected)
                return false;
        }

        return true;
    }

    // Cuando los naturales ya van seguidos y hay un solo comodín, este únicamente
    // puede ir a un extremo, y los dos valen. Quién sea la decide el jugador.
    public static (List<Card> Low, List<Card> High)? EndChoiceFor(IReadOnlyList<Card> cards, GameOptions options)
    {
        if (cards.Count(c => c.IsJoker) != 1)
            return null;

        var joker = cards.First(c => c.IsJoker);
        var naturals = cards.Where(c => !c.IsJoker).OrderBy(c => SlotValue(c, options)).ToList();

        if (naturals.Count == 0)
            return null;

        var low = new List<Card>(naturals);
        low.Insert(0, joker);

        var high = new List<Card>(naturals) { joker };

        return IsRunInOrder(low, options) && IsRunInOrder(high, options)
            ? (low, high)
            : null;
    }

    // Un comodín suele encajar por los dos extremos de una escalera. Quien quiera dar
    // a elegir necesita saber cuáles de los dos valen, no solo el primero.
    public static IReadOnlyList<int> ExtendPositions(Meld meld, Card card, GameOptions options)
    {
        if (meld.Kind == MeldKind.Trio)
            return TrioExtendPosition(meld, card, options) is { } only ? [only] : [];

        var fits = new List<int>();

        foreach (var candidate in new[] { 0, meld.Size })
        {
            var trial = new List<Card>(meld.Cards);
            trial.Insert(candidate, card);

            if (IsRunInOrder(trial, options))
                fits.Add(candidate);
        }

        return fits;
    }

    public static bool CanExtendAt(Meld meld, Card card, GameOptions options, int position)
        => ExtendPositions(meld, card, options).Contains(position);

    public static bool CanExtend(Meld meld, Card card, GameOptions options, out int position)
    {
        var fits = ExtendPositions(meld, card, options);

        position = fits.Count > 0 ? fits[0] : -1;

        return fits.Count > 0;
    }

    private static int? TrioExtendPosition(Meld meld, Card card, GameOptions options)
    {
        if (!card.IsJoker && meld.TrioRank is { } rank && card.Rank != rank)
            return null;

        if (options.TrioRequiresDistinctSuits && !card.IsJoker)
        {
            if (meld.Cards.Any(c => !c.IsJoker && c.Suit == card.Suit))
                return null;

            if (meld.Size >= 4)
                return null;
        }

        return meld.Size;
    }
}