using Continental.Core.Model;
using Continental.Core.Rules;

namespace Continental.Core.Bots;

public readonly record struct Hole(Suit Suit, Rank Rank, int Outs);

public sealed class PlanMeld
{
    public required MeldKind Kind { get; init; }

    public required List<Card> Cards { get; init; }

    public required List<Hole> Holes { get; init; }
}

public sealed class HandPlan
{
    public required IReadOnlyList<PlanMeld> Melds { get; init; }

    public required IReadOnlyList<Card> Unused { get; init; }

    public required int Holes { get; init; }

    public required int Missing { get; init; }

    public required int Deadwood { get; init; }

    public required double Score { get; init; }

    private HashSet<int>? _used;

    public bool Uses(int cardId)
    {
        _used ??= Melds.SelectMany(m => m.Cards).Select(c => c.Id).ToHashSet();
        return _used.Contains(cardId);
    }
}

public readonly record struct PlanWeights(double MissingCost, double DeadwoodWeight, double MeldWeight)
{
    public static readonly PlanWeights Normal = new(300, 1, 0);
}

public static class HandPlanner
{
    private const int MaxRunCandidates = 36;

    public static HandPlan Plan(IReadOnlyList<Card> hand, RoundContract contract, GameOptions options,
                                TableKnowledge knowledge, PlanWeights weights)
    {
        var jokers = hand.Where(c => c.IsJoker).ToList();
        var naturals = hand.Where(c => !c.IsJoker).ToList();

        var pool = new List<Card>[5, 14];

        foreach (var card in naturals)
            (pool[(int)card.Suit, (int)card.Rank] ??= []).Add(card);

        var runs = contract.Escaleras > 0 ? RunCandidates(pool, jokers.Count, options, knowledge) : [];
        var trioRanks = contract.Trios > 0
            ? naturals.Select(c => c.Rank).Distinct().OrderBy(r => r).ToList()
            : [];

        var search = new Search(pool, jokers, contract, options, knowledge, weights, runs, trioRanks);
        search.Run();

        if (search.Best is { } best)
            return best;

        var holes = contract.Trios * options.MinTrioSize + contract.Escaleras * options.MinEscaleraSize;
        var missing = Math.Max(0, holes - jokers.Count);
        var deadwood = hand.Sum(options.ValueOf);

        return new HandPlan
        {
            Melds = [],
            Unused = [.. hand],
            Holes = holes,
            Missing = missing,
            Deadwood = deadwood,
            Score = -missing * weights.MissingCost - deadwood * weights.DeadwoodWeight - hand.Count * 2
        };
    }

    public static double Difficulty(int outs) => outs switch
    {
        <= 0 => 250,
        1 => 70,
        2 => 30,
        3 => 12,
        _ => 0
    };

    private sealed class RunCandidate(Suit suit, Rank[] ranks)
    {
        public Suit Suit { get; } = suit;

        public Rank[] Ranks { get; } = ranks;
    }

    private static List<RunCandidate> RunCandidates(List<Card>[,] pool, int jokers, GameOptions options, TableKnowledge knowledge)
    {
        var scored = new List<(RunCandidate Run, int Holes, int Outs)>();
        var min = options.MinEscaleraSize;

        for (var suit = 1; suit <= 4; suit++)
        {
            var inSuit = 0;

            for (var rank = 1; rank <= 13; rank++)
                inSuit += pool[suit, rank]?.Count ?? 0;

            if (inSuit == 0)
                continue;

            var maxLen = Math.Min(13, inSuit + jokers + 2);

            foreach (var ranks in Windows(min, maxLen, options))
            {
                var present = 0;
                var outs = 0;

                foreach (var rank in ranks)
                {
                    if (pool[suit, (int)rank] is { Count: > 0 })
                        present++;
                    else
                        outs += knowledge.Live((Suit)suit, rank);
                }

                var holes = ranks.Length - present;

                if (present == 0 || holes > jokers + 2)
                    continue;

                if (ranks.Length > min && (pool[suit, (int)ranks[0]] is not { Count: > 0 }
                                           || pool[suit, (int)ranks[^1]] is not { Count: > 0 }))
                    continue;

                scored.Add((new RunCandidate((Suit)suit, ranks), holes, outs));
            }
        }

        return scored.OrderBy(s => s.Holes)
                     .ThenByDescending(s => s.Run.Ranks.Length)
                     .ThenByDescending(s => s.Outs)
                     .Take(MaxRunCandidates)
                     .Select(s => s.Run)
                     .ToList();
    }

    private static IEnumerable<Rank[]> Windows(int minLen, int maxLen, GameOptions options)
    {
        for (var len = minLen; len <= maxLen; len++)
        {
            if (options.AceHighAndLow)
            {
                for (var start = 1; start <= 13; start++)
                {
                    var ranks = new Rank[len];

                    for (var i = 0; i < len; i++)
                        ranks[i] = (Rank)((start - 1 + i) % 13 + 1);

                    yield return ranks;
                }
            }
            else
            {
                for (var start = 2; start + len - 1 <= 14; start++)
                {
                    var ranks = new Rank[len];

                    for (var i = 0; i < len; i++)
                    {
                        var value = start + i;
                        ranks[i] = value == 14 ? Rank.Ace : (Rank)value;
                    }

                    yield return ranks;
                }
            }
        }
    }

    private sealed class Search(List<Card>[,] pool, List<Card> jokers, RoundContract contract, GameOptions options,
                                TableKnowledge knowledge, PlanWeights weights, List<RunCandidate> runs, List<Rank> trioRanks)
    {
        private readonly int[,] _taken = new int[5, 14];
        private readonly List<PlanMeld> _chosen = [];

        public HandPlan? Best { get; private set; }

        private double _bestScore = double.NegativeInfinity;

        public void Run() => Step(0, 0);

        private void Step(int runFrom, int trioFrom)
        {
            var runsDone = _chosen.Count(m => m.Kind == MeldKind.Escalera);

            if (runsDone < contract.Escaleras)
            {
                var any = false;

                for (var i = runFrom; i < runs.Count; i++)
                {
                    var meld = TakeRun(runs[i]);

                    if (meld is null)
                        continue;

                    any = true;
                    _chosen.Add(meld);
                    Step(i + 1, trioFrom);
                    _chosen.RemoveAt(_chosen.Count - 1);
                    Release(meld);
                }

                if (!any)
                {
                    var empty = EmptyRun();
                    _chosen.Add(empty);
                    Step(runs.Count, trioFrom);
                    _chosen.RemoveAt(_chosen.Count - 1);
                }

                return;
            }

            var triosDone = _chosen.Count(m => m.Kind == MeldKind.Trio);

            if (triosDone < contract.Trios)
            {
                var any = false;

                for (var i = trioFrom; i < trioRanks.Count; i++)
                {
                    var meld = TakeTrio(trioRanks[i]);

                    if (meld is null)
                        continue;

                    any = true;
                    _chosen.Add(meld);
                    Step(runFrom, i + 1);
                    _chosen.RemoveAt(_chosen.Count - 1);
                    Release(meld);
                }

                if (!any)
                {
                    var empty = EmptyTrio();
                    _chosen.Add(empty);
                    Step(runFrom, trioRanks.Count);
                    _chosen.RemoveAt(_chosen.Count - 1);
                }

                return;
            }

            Evaluate();
        }

        private PlanMeld? TakeRun(RunCandidate run)
        {
            var cards = new List<Card>();
            var holes = new List<Hole>();

            foreach (var rank in run.Ranks)
            {
                var available = pool[(int)run.Suit, (int)rank];
                var used = _taken[(int)run.Suit, (int)rank];

                if (available is not null && used < available.Count)
                {
                    cards.Add(available[used]);
                    _taken[(int)run.Suit, (int)rank]++;
                }
                else
                {
                    holes.Add(new Hole(run.Suit, rank, knowledge.Live(run.Suit, rank)));
                }
            }

            if (cards.Count == 0)
            {
                return null;
            }

            return new PlanMeld { Kind = MeldKind.Escalera, Cards = cards, Holes = holes };
        }

        private PlanMeld? TakeTrio(Rank rank)
        {
            var cards = new List<Card>();
            var suits = new HashSet<Suit>();
            var limit = options.TrioRequiresDistinctSuits ? 4 : Math.Max(options.MinTrioSize, 4);

            for (var suit = 1; suit <= 4 && cards.Count < limit; suit++)
            {
                var available = pool[suit, (int)rank];

                if (available is null)
                    continue;

                for (var i = _taken[suit, (int)rank]; i < available.Count && cards.Count < limit; i++)
                {
                    if (options.TrioRequiresDistinctSuits && !suits.Add((Suit)suit))
                        break;

                    cards.Add(available[i]);
                    _taken[suit, (int)rank]++;
                }
            }

            if (cards.Count == 0)
                return null;

            var holes = new List<Hole>();
            var outs = knowledge.LiveRank(rank, options.TrioRequiresDistinctSuits ? suits : null);

            for (var i = cards.Count; i < options.MinTrioSize; i++)
                holes.Add(new Hole(Suit.None, rank, outs));

            return new PlanMeld { Kind = MeldKind.Trio, Cards = cards, Holes = holes };
        }

        private PlanMeld EmptyRun()
        {
            var holes = new List<Hole>();

            for (var i = 0; i < options.MinEscaleraSize; i++)
                holes.Add(new Hole(Suit.None, Rank.Joker, 8));

            return new PlanMeld { Kind = MeldKind.Escalera, Cards = [], Holes = holes };
        }

        private PlanMeld EmptyTrio()
        {
            var holes = new List<Hole>();

            for (var i = 0; i < options.MinTrioSize; i++)
                holes.Add(new Hole(Suit.None, Rank.Joker, 8));

            return new PlanMeld { Kind = MeldKind.Trio, Cards = [], Holes = holes };
        }

        private void Release(PlanMeld meld)
        {
            foreach (var card in meld.Cards)
                _taken[(int)card.Suit, (int)card.Rank]--;
        }

        private void Evaluate()
        {
            var holes = _chosen.SelectMany(m => m.Holes).OrderBy(h => h.Outs).ToList();
            var covered = Math.Min(jokers.Count, holes.Count);
            var missing = holes.Count - covered;
            var difficulty = 0.0;

            for (var i = covered; i < holes.Count; i++)
                difficulty += Difficulty(holes[i].Outs);

            var meldPoints = 0;
            var deadwood = 0;

            foreach (var meld in _chosen)
                meldPoints += meld.Cards.Sum(options.ValueOf);

            for (var suit = 1; suit <= 4; suit++)
            {
                for (var rank = 1; rank <= 13; rank++)
                {
                    var available = pool[suit, rank];

                    if (available is null)
                        continue;

                    for (var i = _taken[suit, rank]; i < available.Count; i++)
                        deadwood += options.ValueOf(available[i]);
                }
            }

            var spareJokers = jokers.Count - covered;
            deadwood += spareJokers * options.JokerValue;

            var unusedCount = spareJokers;

            for (var suit = 1; suit <= 4; suit++)
            {
                for (var rank = 1; rank <= 13; rank++)
                    unusedCount += (pool[suit, rank]?.Count ?? 0) - _taken[suit, rank];
            }

            var score = -missing * weights.MissingCost
                        - difficulty
                        - deadwood * weights.DeadwoodWeight
                        - meldPoints * weights.MeldWeight
                        - unusedCount * 2;

            if (score <= _bestScore)
                return;

            _bestScore = score;

            var unused = new List<Card>();

            for (var suit = 1; suit <= 4; suit++)
            {
                for (var rank = 1; rank <= 13; rank++)
                {
                    var available = pool[suit, rank];

                    if (available is null)
                        continue;

                    for (var i = _taken[suit, rank]; i < available.Count; i++)
                        unused.Add(available[i]);
                }
            }

            unused.AddRange(jokers.Skip(covered));

            Best = new HandPlan
            {
                Melds = _chosen.Select(m => new PlanMeld { Kind = m.Kind, Cards = [.. m.Cards], Holes = [.. m.Holes] }).ToList(),
                Unused = unused,
                Holes = holes.Count,
                Missing = missing,
                Deadwood = deadwood,
                Score = score
            };
        }
    }
}
