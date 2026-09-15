using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Rules;

namespace Continental.Core.Bots;

public enum BotLevel
{
    Easy = 0,
    Normal = 1,
    Hard = 2
}

public static class BotBrain
{

    public static int Evaluate(IReadOnlyList<Card> hand, RoundContract contract, GameOptions options)
    {

        var trios = HandAnalyzer.FindTrios(hand, options);
        var escaleras = HandAnalyzer.FindEscaleras(hand, options);

        if (HandAnalyzer.FindContract(trios, escaleras, contract) is not null)
            return 100_000 - hand.Sum(options.ValueOf);

        var used = new HashSet<int>();
        var escaleraCount = 0;

        foreach (var candidate in escaleras)
        {
            if (escaleraCount >= contract.Escaleras)
                break;

            if (candidate.Any(c => used.Contains(c.Id)))
                continue;

            foreach (var c in candidate) used.Add(c.Id);
            escaleraCount++;
        }

        var trioCount = 0;

        foreach (var candidate in trios)
        {
            if (trioCount >= contract.Trios)
                break;

            if (candidate.Any(c => used.Contains(c.Id)))
                continue;

            foreach (var c in candidate) used.Add(c.Id);
            trioCount++;
        }

        var deadwood = 0;
        var nearMiss = 0;

        foreach (var card in hand)
        {
            if (used.Contains(card.Id))
                continue;

            deadwood += options.ValueOf(card);

            if (HandAnalyzer.Potential(hand, card) >= 1)
                nearMiss++;
        }

        return (escaleraCount + trioCount) * 1_000 + nearMiss * 25 - deadwood;
    }

    public static DrawSource ChooseDraw(GameState state, PlayerState bot, BotLevel level, Random random)
    {
        if (state.DiscardTop is not { } top)
            return DrawSource.Stock;

        if (level == BotLevel.Easy && random.NextDouble() < 0.25)
            return DrawSource.Stock;

        var contract = state.Contract;
        var current = Evaluate(bot.Hand, contract, state.Options);

        var withCard = new List<Card>(bot.Hand) { top };
        var improved = Evaluate(withCard, contract, state.Options);

        var threshold = level == BotLevel.Hard ? 20 : 60;

        if (bot.HasLaidDown && CanPlaceSomewhere(state, bot, top))
            return DrawSource.Discard;

        return improved - current > threshold ? DrawSource.Discard : DrawSource.Stock;
    }

    public static bool WantsSteal(GameState state, PlayerState bot, BotLevel level, Random random)
    {
        if (state.Steal is not { } offer || bot.HasLaidDown)
            return false;

        if (level == BotLevel.Easy)
            return false;

        var contract = state.Contract;
        var current = Evaluate(bot.Hand, contract, state.Options);

        var after = new List<Card>(bot.Hand) { offer.Card };
        var improved = Evaluate(after, contract, state.Options) - state.Options.StealPenaltyCards * 15;

        var threshold = level == BotLevel.Hard ? 150 : 400;

        return improved - current > threshold && random.NextDouble() > 0.15;
    }

    public static List<MeldSpec>? TryLayDown(GameState state, PlayerState bot)
        => bot.HasLaidDown ? null : HandAnalyzer.FindContract(bot.Hand, state.Contract, state.Options);

    public static List<(string MeldId, int CardId)> FindPlacements(GameState state, PlayerState bot)
    {
        var placements = new List<(string, int)>();

        if (!bot.HasLaidDown)
            return placements;

        var table = state.Table.Select(m => m.Clone()).ToList();
        var hand = new List<Card>(bot.Hand);
        var progress = true;

        while (progress && hand.Count > 1)
        {
            progress = false;

            foreach (var card in hand.OrderByDescending(state.Options.ValueOf).ToList())
            {
                foreach (var meld in table)
                {
                    if (!state.Options.CanExtendOpponentMelds && meld.OwnerId != bot.Id)
                        continue;

                    if (!MeldValidator.CanExtend(meld, card, state.Options, out var position))
                        continue;

                    meld.Cards.Insert(position, card);
                    hand.Remove(card);
                    placements.Add((meld.Id, card.Id));
                    progress = true;
                    break;
                }

                if (progress)
                    break;
            }
        }

        return placements;
    }

    private static bool CanPlaceSomewhere(GameState state, PlayerState bot, Card card)
        => state.Table.Any(m => (state.Options.CanExtendOpponentMelds || m.OwnerId == bot.Id)
                                && MeldValidator.CanExtend(m, card, state.Options, out _));

    public static int ChooseDiscard(GameState state, PlayerState bot, BotLevel level, Random random)
    {
        if (bot.Hand.Count == 0)
            return -1;

        if (level == BotLevel.Easy && random.NextDouble() < 0.3)
            return bot.Hand[random.Next(bot.Hand.Count)].Id;

        var contract = state.Contract;
        var best = bot.Hand[0];
        var bestScore = int.MinValue;

        foreach (var card in bot.Hand)
        {

            if (card.IsJoker && bot.Hand.Count > 1)
                continue;

            var rest = bot.Hand.Where(c => c.Id != card.Id).ToList();
            var score = Evaluate(rest, contract, state.Options);

            score = score * 10 + state.Options.ValueOf(card);

            if (score > bestScore)
            {
                bestScore = score;
                best = card;
            }
        }

        return best.Id;
    }
}
