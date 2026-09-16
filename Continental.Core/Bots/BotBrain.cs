using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Rules;

namespace Continental.Core.Bots;

public sealed record JokerSwapMove(string MeldId, int CardId, string? TargetMeldId, int Position);

public static class BotBrain
{
    private const double GiftPenalty = 150;
    private const double GiftToNextPlayerPenalty = 60;
    private const double GiftNearCloserPenalty = 120;
    private const double RankInterestPenalty = 70;
    private const double SuitInterestPenalty = 45;
    private const double MaxInterestPenalty = 140;
    private const double JokerDiscardPenalty = 400;

    public static PlanWeights WeightsFor(GameState state, PlayerState bot)
    {
        var rivals = state.Players.Where(p => p.Id != bot.Id && p.IsConnected).ToList();
        var someoneLaid = rivals.Any(p => p.HasLaidDown);
        var fewest = rivals.Count == 0 ? int.MaxValue : rivals.Min(p => p.Hand.Count);
        var stockDying = state.StockRecycles >= state.Options.MaxStockRecycles;

        if (someoneLaid && fewest <= 3)
            return new PlanWeights(200, 3, 1.5);

        if (someoneLaid || stockDying)
            return new PlanWeights(300, 2, 0.5);

        return PlanWeights.Normal;
    }

    public static HandPlan PlanFor(GameState state, PlayerState bot, IReadOnlyList<Card> hand, TableKnowledge knowledge)
    {
        var weights = WeightsFor(state, bot);
        var plan = HandPlanner.Plan(hand, state.Contract, state.Options, knowledge, weights);

        if (weights.DeadwoodWeight >= 3 && plan.Missing >= 3)
            plan = HandPlanner.Plan(hand, state.Contract, state.Options, knowledge, new PlanWeights(20, 4, 4));

        return plan;
    }

    public static DrawSource ChooseDraw(GameState state, PlayerState bot)
    {
        if (state.DiscardTop is not { } top)
            return DrawSource.Stock;

        if (bot.HasLaidDown)
            return CanPlaceSomewhere(state, bot, top) ? DrawSource.Discard : DrawSource.Stock;

        var knowledge = new TableKnowledge(state, bot);
        var current = PlanFor(state, bot, bot.Hand, knowledge);
        var with = PlanFor(state, bot, [.. bot.Hand, top], knowledge);

        if (with.Missing < current.Missing)
            return DrawSource.Discard;

        if (current.Missing == 0 && (top.IsJoker || CanPlaceSomewhere(state, bot, top)))
            return DrawSource.Discard;

        return DrawSource.Stock;
    }

    public static bool WantsSteal(GameState state, PlayerState bot)
    {
        if (state.Steal is not { } offer || bot.HasLaidDown)
            return false;

        var knowledge = new TableKnowledge(state, bot);
        var current = PlanFor(state, bot, bot.Hand, knowledge);
        var with = PlanFor(state, bot, [.. bot.Hand, offer.Card], knowledge);

        if (with.Missing >= current.Missing)
            return false;

        if (with.Missing == 0)
            return true;

        var rivalAboutToClose = state.Players.Any(p => p.Id != bot.Id && p.HasLaidDown && p.Hand.Count <= 3);

        return with.Missing == 1 && !rivalAboutToClose;
    }

    public static List<MeldSpec>? TryLayDown(GameState state, PlayerState bot)
    {
        if (bot.HasLaidDown)
            return null;

        var solutions = HandAnalyzer.FindContracts(bot.Hand, state.Contract, state.Options);

        if (solutions.Count == 0)
            return null;

        List<MeldSpec>? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var solution in solutions)
        {
            var used = solution.SelectMany(s => s.CardIds).ToHashSet();
            var leftover = bot.Hand.Where(c => !used.Contains(c.Id)).ToList();
            var table = state.Table.Select(m => m.Clone()).ToList();
            var sequence = 0;

            foreach (var spec in solution)
            {
                var cards = spec.CardIds.Select(id => bot.Hand.First(c => c.Id == id)).ToList();
                var (meld, _) = MeldValidator.Build($"plan{++sequence}", bot.Id, spec.Kind, cards, state.Options);

                if (meld is not null)
                    table.Add(meld);
            }

            var placeable = CountPlaceable(table, leftover, bot.Id, state.Options);
            var stuck = leftover.Count - placeable;
            var stuckPoints = leftover.Where(c => !FitsAnywhere(table, c, bot.Id, state.Options)).Sum(state.Options.ValueOf);

            var score = used.Count * 10 + placeable * 10 - stuck * 10 - stuckPoints / 10.0;

            if (score > bestScore)
            {
                bestScore = score;
                best = solution;
            }
        }

        return best;
    }

    private static int CountPlaceable(List<Meld> table, List<Card> leftover, string botId, GameOptions options)
    {
        var placed = 0;
        var remaining = new List<Card>(leftover);
        var progress = true;

        while (progress && remaining.Count > 1)
        {
            progress = false;

            foreach (var card in remaining.Where(c => !c.IsJoker).Concat(remaining.Where(c => c.IsJoker)).ToList())
            {
                var target = table.FirstOrDefault(m => (options.CanExtendOpponentMelds || m.OwnerId == botId)
                                                       && MeldValidator.CanExtend(m, card, options, out _));

                if (target is null)
                    continue;

                MeldValidator.CanExtend(target, card, options, out var position);
                target.Cards.Insert(position, card);
                remaining.Remove(card);
                placed++;
                progress = true;
                break;
            }
        }

        return placed;
    }

    private static bool FitsAnywhere(List<Meld> table, Card card, string botId, GameOptions options)
        => table.Any(m => (options.CanExtendOpponentMelds || m.OwnerId == botId)
                          && MeldValidator.CanExtend(m, card, options, out _));

    public static JokerSwapMove? FindJokerSwap(GameState state, PlayerState bot)
    {
        if (!bot.HasLaidDown || bot.Hand.Count <= 1 || state.Options.JokerSwapMode == JokerSwap.Off)
            return null;

        foreach (var source in state.Table.Where(m => m.Kind == MeldKind.Escalera))
        {
            if (state.Options.JokerSwapMode == JokerSwap.OwnerOnly && source.OwnerId != bot.Id)
                continue;

            for (var i = 0; i < source.Size; i++)
            {
                if (MeldValidator.JokerStandsFor(source, i, state.Options) is not { } stands)
                    continue;

                var natural = bot.Hand.FirstOrDefault(c => !c.IsJoker && c.Suit == stands.Suit && c.Rank == stands.Rank);

                if (natural == default)
                    continue;

                var joker = source.Cards[i];

                foreach (var target in JokerTargets(state, bot, source))
                {
                    var probe = target.Clone();

                    if (ReferenceEquals(target, source))
                        probe.Cards[i] = natural;

                    var fits = MeldValidator.ExtendPositions(probe, joker, state.Options);

                    if (fits.Count == 0)
                        continue;

                    return new JokerSwapMove(source.Id, natural.Id, ReferenceEquals(target, source) ? null : target.Id, fits[0]);
                }
            }
        }

        return null;
    }

    private static IEnumerable<Meld> JokerTargets(GameState state, PlayerState bot, Meld source)
    {
        var allowed = state.Table.Where(m => state.Options.CanExtendOpponentMelds || m.OwnerId == bot.Id).ToList();

        foreach (var meld in allowed.Where(m => m.Kind == MeldKind.Trio))
            yield return meld;

        foreach (var meld in allowed.Where(m => m.Kind == MeldKind.Escalera && m.OwnerId == bot.Id && !ReferenceEquals(m, source)))
            yield return meld;

        if (allowed.Contains(source))
            yield return source;

        foreach (var meld in allowed.Where(m => m.Kind == MeldKind.Escalera && m.OwnerId != bot.Id && !ReferenceEquals(m, source)))
            yield return meld;
    }

    public static List<(string MeldId, int CardId)> FindPlacements(GameState state, PlayerState bot)
    {
        var placements = new List<(string, int)>();

        if (!bot.HasLaidDown)
            return placements;

        var table = state.Table.Select(m => m.Clone()).ToList();
        var hand = new List<Card>(bot.Hand);

        while (hand.Count > 1)
        {
            var move = BestPlacement(state, bot, table, hand);

            if (move is null)
                break;

            var (meld, card, position) = move.Value;
            meld.Cards.Insert(position, card);
            hand.Remove(card);
            placements.Add((meld.Id, card.Id));
        }

        return placements;
    }

    private static (Meld Meld, Card Card, int Position)? BestPlacement(GameState state, PlayerState bot, List<Meld> table, List<Card> hand)
    {
        (Meld Meld, Card Card, int Position)? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var card in hand)
        {
            foreach (var meld in table)
            {
                if (!state.Options.CanExtendOpponentMelds && meld.OwnerId != bot.Id)
                    continue;

                if (!MeldValidator.CanExtend(meld, card, state.Options, out var position))
                    continue;

                var score = card.IsJoker ? -100 : state.Options.ValueOf(card);

                if (card.IsJoker && meld.Kind == MeldKind.Trio)
                    score += 30;

                if (card.IsJoker && meld.OwnerId == bot.Id)
                    score += 20;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = (meld, card, position);
                }
            }
        }

        return best;
    }

    private static bool CanPlaceSomewhere(GameState state, PlayerState bot, Card card)
        => FitsAnywhere(state.Table, card, bot.Id, state.Options);

    public static int ChooseDiscard(GameState state, PlayerState bot)
    {
        if (bot.Hand.Count == 0)
            return -1;

        if (bot.Hand.Count == 1)
            return bot.Hand[0].Id;

        var knowledge = new TableKnowledge(state, bot);
        var weights = WeightsFor(state, bot);
        var plan = bot.HasLaidDown ? null : PlanFor(state, bot, bot.Hand, knowledge);

        var best = bot.Hand[0];
        var bestScore = double.NegativeInfinity;

        foreach (var card in bot.Hand)
        {
            double score;

            if (plan is null)
            {
                score = state.Options.ValueOf(card);
            }
            else if (plan.Uses(card.Id))
            {
                var rest = bot.Hand.Where(c => c.Id != card.Id).ToList();
                score = PlanFor(state, bot, rest, knowledge).Score;
            }
            else
            {
                score = plan.Score + state.Options.ValueOf(card) * weights.DeadwoodWeight + 2;
            }

            if (card.IsJoker)
                score -= JokerDiscardPenalty;

            score -= Danger(state, bot, card);
            score += Safety(state, bot, card, knowledge);

            if (score > bestScore)
            {
                bestScore = score;
                best = card;
            }
        }

        return best.Id;
    }

    public static double Danger(GameState state, PlayerState bot, Card card)
    {
        var rivals = state.Players.Where(p => p.Id != bot.Id && p.IsConnected).ToList();
        var danger = 0.0;

        var takers = rivals.Where(p => p.HasLaidDown && state.Table.Any(m => (state.Options.CanExtendOpponentMelds || m.OwnerId == p.Id)
                                                                              && MeldValidator.CanExtend(m, card, state.Options, out _)))
                           .ToList();

        if (takers.Count > 0)
        {
            danger += GiftPenalty;

            if (NextPlayer(state, bot) is { } next && takers.Any(t => t.Id == next.Id))
                danger += GiftToNextPlayerPenalty;

            if (takers.Min(t => t.Hand.Count) <= 3)
                danger += GiftNearCloserPenalty;
        }

        if (card.IsJoker)
            return danger;

        var interest = 0.0;

        foreach (var rival in rivals.Where(p => !p.HasLaidDown))
        {
            var rankHit = false;
            var suitHit = false;

            foreach (var move in state.History)
            {
                if (move.PlayerId != rival.Id || move.Kind == MoveKind.Discarded || move.Card.IsJoker)
                    continue;

                if (move.Card.Rank == card.Rank)
                    rankHit = true;
                else if (move.Card.Suit == card.Suit && Math.Abs((int)move.Card.Rank - (int)card.Rank) <= 2)
                    suitHit = true;
            }

            if (rankHit)
                interest += RankInterestPenalty;

            if (suitHit)
                interest += SuitInterestPenalty;
        }

        return danger + Math.Min(interest, MaxInterestPenalty);
    }

    public static double Safety(GameState state, PlayerState bot, Card card, TableKnowledge knowledge)
    {
        if (card.IsJoker)
            return 0;

        var safety = 0.0;

        if (knowledge.LiveRank(card.Rank) <= 2)
            safety += 15;

        var next = NextPlayer(state, bot);

        if (next is not null && !next.HasLaidDown
            && state.History.Any(m => m.PlayerId == next.Id && m.Kind == MoveKind.Discarded && m.Card.Rank == card.Rank))
            safety += 10;

        return safety;
    }

    private static PlayerState? NextPlayer(GameState state, PlayerState bot)
    {
        var count = state.Players.Count;
        var index = state.Players.IndexOf(bot);

        for (var step = 1; step < count; step++)
        {
            var candidate = state.Players[(index + step) % count];

            if (candidate.IsConnected)
                return candidate;
        }

        return null;
    }
}
