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

    public static List<MeldSpec>? FindExactContract(IReadOnlyList<Card> selection, RoundContract contract, GameOptions options)
    {
        if (selection.Count < MinimumCards(contract, options))
            return null;

        return SplitInSelectionOrder(selection, contract, options)
               ?? SplitAnywhere(selection, contract, options);
    }

    public static int MinimumCards(RoundContract contract, GameOptions options)
        => contract.Trios * options.MinTrioSize + contract.Escaleras * options.MinEscaleraSize;

    public static List<Card>? Arrange(MeldKind kind, IReadOnlyList<Card> cards, GameOptions options)
    {
        if (kind == MeldKind.Trio)
            return MeldValidator.TryTrio(cards, options) is { Ok: true, Arranged: { } trio } ? trio : null;

        if (MeldValidator.IsRunInOrder(cards, options))
            return [.. cards];

        return MeldValidator.TryEscalera(cards, options) is { Ok: true, Arranged: { } run } ? run : null;
    }

    private static List<MeldSpec>? SplitInSelectionOrder(IReadOnlyList<Card> selection, RoundContract contract, GameOptions options)
    {
        var chosen = new List<MeldSpec>();

        return Step(0, contract.Trios, contract.Escaleras) ? chosen : null;

        bool Step(int from, int trios, int escaleras)
        {
            if (from == selection.Count)
                return trios == 0 && escaleras == 0;

            if (trios + escaleras == 0)
                return false;

            for (var take = selection.Count - from; take >= 1; take--)
            {
                var segment = new List<Card>();

                for (var i = from; i < from + take; i++)
                    segment.Add(selection[i]);

                if (trios > 0 && Take(MeldKind.Trio, segment, from + take, trios - 1, escaleras))
                    return true;

                if (escaleras > 0 && Take(MeldKind.Escalera, segment, from + take, trios, escaleras - 1))
                    return true;
            }

            return false;
        }

        bool Take(MeldKind kind, List<Card> segment, int next, int trios, int escaleras)
        {
            if (Arrange(kind, segment, options) is not { } arranged)
                return false;

            chosen.Add(new MeldSpec(kind, arranged.Select(c => c.Id).ToList()));

            if (Step(next, trios, escaleras))
                return true;

            chosen.RemoveAt(chosen.Count - 1);

            return false;
        }
    }

    private sealed record MeldOption(MeldKind Kind, List<int> Slots, List<int> CardIds);

    private static List<MeldSpec>? SplitAnywhere(IReadOnlyList<Card> selection, RoundContract contract, GameOptions options)
    {
        var candidates = MeldsWithin(selection, contract, options);
        var used = new bool[selection.Count];
        var chosen = new List<MeldSpec>();

        return Step(contract.Trios, contract.Escaleras, selection.Count) ? chosen : null;

        bool Step(int trios, int escaleras, int left)
        {
            if (left == 0)
                return trios == 0 && escaleras == 0;

            if (trios + escaleras == 0)
                return false;

            var anchor = Array.IndexOf(used, false);

            foreach (var candidate in candidates)
            {
                var wanted = candidate.Kind == MeldKind.Trio ? trios : escaleras;

                if (wanted == 0 || !candidate.Slots.Contains(anchor) || candidate.Slots.Any(i => used[i]))
                    continue;

                foreach (var slot in candidate.Slots)
                    used[slot] = true;

                chosen.Add(new MeldSpec(candidate.Kind, candidate.CardIds));

                var nextTrios = candidate.Kind == MeldKind.Trio ? trios - 1 : trios;
                var nextEscaleras = candidate.Kind == MeldKind.Escalera ? escaleras - 1 : escaleras;

                if (Step(nextTrios, nextEscaleras, left - candidate.Slots.Count))
                    return true;

                chosen.RemoveAt(chosen.Count - 1);

                foreach (var slot in candidate.Slots)
                    used[slot] = false;
            }

            return false;
        }
    }

    private static List<MeldOption> MeldsWithin(IReadOnlyList<Card> selection, RoundContract contract, GameOptions options)
    {
        var results = new List<MeldOption>();
        var jokers = Slots(selection, c => c.IsJoker);
        var naturals = Slots(selection, c => !c.IsJoker);
        var wilds = Subsets(jokers).ToList();

        if (contract.Trios > 0)
        {
            Collect(MeldKind.Trio, [], options.MinTrioSize);

            foreach (var group in naturals.GroupBy(i => selection[i].Rank))
                Collect(MeldKind.Trio, [.. group], options.MinTrioSize);
        }

        if (contract.Escaleras > 0)
        {
            foreach (var group in naturals.GroupBy(i => selection[i].Suit))
                Collect(MeldKind.Escalera, [.. group], options.MinEscaleraSize);
        }

        return results;

        void Collect(MeldKind kind, List<int> group, int min)
        {
            foreach (var picked in Subsets(group))
            {
                if (picked.Count == 0 && group.Count > 0)
                    continue;

                if (kind == MeldKind.Escalera && picked.Select(i => selection[i].Rank).Distinct().Count() != picked.Count)
                    continue;

                foreach (var wild in wilds)
                {
                    if (picked.Count + wild.Count < min)
                        continue;

                    var slots = new List<int>(picked);
                    slots.AddRange(wild);

                    if (Arrange(kind, slots.Select(i => selection[i]).ToList(), options) is not { } arranged)
                        continue;

                    results.Add(new MeldOption(kind, slots, arranged.Select(c => c.Id).ToList()));
                }
            }
        }
    }

    private static List<int> Slots(IReadOnlyList<Card> selection, Func<Card, bool> keep)
    {
        var slots = new List<int>();

        for (var i = 0; i < selection.Count; i++)
        {
            if (keep(selection[i]))
                slots.Add(i);
        }

        return slots;
    }

    private static IEnumerable<List<int>> Subsets(List<int> source)
    {
        if (source.Count > 16)
            yield break;

        for (var mask = 0; mask < 1 << source.Count; mask++)
        {
            var subset = new List<int>();

            for (var i = 0; i < source.Count; i++)
            {
                if ((mask & 1 << i) != 0)
                    subset.Add(source[i]);
            }

            yield return subset;
        }
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
