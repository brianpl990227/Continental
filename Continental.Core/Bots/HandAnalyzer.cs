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
        => FindContracts(trios, escaleras, contract, 1).FirstOrDefault();

    public static List<List<MeldSpec>> FindContracts(IReadOnlyList<Card> hand, RoundContract contract, GameOptions options, int limit = 64)
        => FindContracts(FindTrios(hand, options), FindEscaleras(hand, options), contract, limit);

    public static List<List<MeldSpec>> FindContracts(List<List<Card>> trios, List<List<Card>> escaleras, RoundContract contract, int limit)
    {
        var solutions = new List<List<MeldSpec>>();

        if (trios.Count < contract.Trios || escaleras.Count < contract.Escaleras)
            return solutions;

        var chosen = new List<(MeldKind Kind, List<Card> Cards)>();
        var used = new HashSet<int>();

        Search(0, 0, 0, 0);

        return solutions;

        void Search(int triosDone, int escalerasDone, int escaleraFrom, int trioFrom)
        {
            if (solutions.Count >= limit)
                return;

            if (triosDone == contract.Trios && escalerasDone == contract.Escaleras)
            {
                solutions.Add(chosen.Select(c => new MeldSpec(c.Kind, c.Cards.Select(x => x.Id).ToList())).ToList());
                return;
            }

            if (escalerasDone < contract.Escaleras)
            {
                for (var i = escaleraFrom; i < escaleras.Count; i++)
                {
                    var candidate = escaleras[i];

                    if (candidate.Any(c => used.Contains(c.Id)))
                        continue;

                    foreach (var c in candidate) used.Add(c.Id);
                    chosen.Add((MeldKind.Escalera, candidate));

                    Search(triosDone, escalerasDone + 1, i + 1, trioFrom);

                    chosen.RemoveAt(chosen.Count - 1);
                    foreach (var c in candidate) used.Remove(c.Id);
                }

                return;
            }

            for (var i = trioFrom; i < trios.Count; i++)
            {
                var candidate = trios[i];

                if (candidate.Any(c => used.Contains(c.Id)))
                    continue;

                foreach (var c in candidate) used.Add(c.Id);
                chosen.Add((MeldKind.Trio, candidate));

                Search(triosDone + 1, escalerasDone, escaleraFrom, i + 1);

                chosen.RemoveAt(chosen.Count - 1);
                foreach (var c in candidate) used.Remove(c.Id);
            }
        }
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
