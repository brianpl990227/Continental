using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Rules;

namespace Continental.Core.Bots;

public static class HandAnalyzer
{

    public static List<List<Card>> FindTrios(IReadOnlyList<Card> hand, GameOptions options)
    {
        var results = new List<List<Card>>();
        var jokers = hand.Where(c => c.IsJoker).ToList();
        var size = options.MinTrioSize;

        foreach (var group in hand.Where(c => !c.IsJoker).GroupBy(c => c.Rank))
        {
            var naturals = group.ToList();

            for (var take = Math.Min(naturals.Count, size); take >= 1; take--)
            {
                var need = size - take;

                if (need > jokers.Count)
                    continue;

                foreach (var combo in Combinations(naturals, take))
                {

                    foreach (var wilds in Combinations(jokers, need))
                    {
                        var cards = new List<Card>(combo);
                        cards.AddRange(wilds);

                        if (MeldValidator.TryTrio(cards, options).Ok)
                            results.Add(cards);
                    }
                }
            }
        }

        return Dedupe(results);
    }

    public static List<List<Card>> FindEscaleras(IReadOnlyList<Card> hand, GameOptions options)
    {
        var results = new List<List<Card>>();
        var jokers = hand.Where(c => c.IsJoker).ToList();
        var size = options.MinEscaleraSize;

        foreach (var group in hand.Where(c => !c.IsJoker).GroupBy(c => c.Suit))
        {
            var bySuit = group.ToList();

            for (var take = Math.Min(bySuit.Count, size); take >= 1; take--)
            {
                var need = size - take;

                if (need > jokers.Count)
                    continue;

                foreach (var combo in Combinations(bySuit, take))
                {
                    foreach (var wilds in Combinations(jokers, need))
                    {
                        var cards = new List<Card>(combo);
                        cards.AddRange(wilds);

                        var fit = MeldValidator.TryEscalera(cards, options);

                        if (fit is { Ok: true, Arranged: not null })
                            results.Add(fit.Arranged);
                    }
                }
            }
        }

        return Dedupe(results);
    }

    public static List<MeldSpec>? FindContract(IReadOnlyList<Card> hand, RoundContract contract, GameOptions options)
        => FindContract(FindTrios(hand, options), FindEscaleras(hand, options), contract);

    public static List<MeldSpec>? FindContract(List<List<Card>> trios, List<List<Card>> escaleras, RoundContract contract)
    {
        if (trios.Count < contract.Trios || escaleras.Count < contract.Escaleras)
            return null;

        var chosen = new List<(MeldKind Kind, List<Card> Cards)>();

        return Search(0, 0, []) ? chosen.Select(c => new MeldSpec(c.Kind, c.Cards.Select(x => x.Id).ToList())).ToList() : null;

        bool Search(int triosDone, int escalerasDone, HashSet<int> used)
        {
            if (triosDone == contract.Trios && escalerasDone == contract.Escaleras)
                return true;

            if (escalerasDone < contract.Escaleras)
            {
                foreach (var candidate in escaleras)
                {
                    if (candidate.Any(c => used.Contains(c.Id)))
                        continue;

                    foreach (var c in candidate) used.Add(c.Id);
                    chosen.Add((MeldKind.Escalera, candidate));

                    if (Search(triosDone, escalerasDone + 1, used))
                        return true;

                    chosen.RemoveAt(chosen.Count - 1);
                    foreach (var c in candidate) used.Remove(c.Id);
                }

                return false;
            }

            foreach (var candidate in trios)
            {
                if (candidate.Any(c => used.Contains(c.Id)))
                    continue;

                foreach (var c in candidate) used.Add(c.Id);
                chosen.Add((MeldKind.Trio, candidate));

                if (Search(triosDone + 1, escalerasDone, used))
                    return true;

                chosen.RemoveAt(chosen.Count - 1);
                foreach (var c in candidate) used.Remove(c.Id);
            }

            return false;
        }
    }

    public static int Usefulness(IReadOnlyList<Card> hand, Card card, GameOptions options)
    {
        if (card.IsJoker)
            return 3;

        var inMeld = FindTrios(hand, options).Concat(FindEscaleras(hand, options))
                                             .Any(m => m.Any(c => c.Id == card.Id));

        return inMeld ? 2 : Potential(hand, card);
    }

    public static int Potential(IReadOnlyList<Card> hand, Card card)
    {
        if (card.IsJoker)
            return 3;

        for (var i = 0; i < hand.Count; i++)
        {
            var other = hand[i];

            if (other.IsJoker || other.Id == card.Id)
                continue;

            if (other.Rank == card.Rank)
                return 1;

            if (other.Suit == card.Suit && Math.Abs((int)other.Rank - (int)card.Rank) <= 2)
                return 1;
        }

        return 0;
    }

    private static List<List<Card>> Dedupe(List<List<Card>> melds)
    {
        var seen = new HashSet<string>();
        var result = new List<List<Card>>();

        foreach (var meld in melds)
        {
            var key = string.Join(",", meld.Select(c => c.Id).OrderBy(x => x));

            if (seen.Add(key))
                result.Add(meld);
        }

        return result;
    }

    private static IEnumerable<List<Card>> Combinations(List<Card> source, int take)
    {
        if (take == 0)
        {
            yield return [];
            yield break;
        }

        for (var i = 0; i <= source.Count - take; i++)
        {
            var head = source[i];

            foreach (var tail in Combinations(source.GetRange(i + 1, source.Count - i - 1), take - 1))
            {
                var combo = new List<Card> { head };
                combo.AddRange(tail);
                yield return combo;
            }
        }
    }
}
