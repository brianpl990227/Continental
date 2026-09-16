using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Rules;

namespace Continental.Core.Bots;

public sealed class TableKnowledge
{
    private readonly GameOptions _options;
    private readonly int[,] _seen = new int[5, 14];
    private readonly HashSet<int> _ids = [];
    private int _seenJokers;

    public TableKnowledge(GameState state, PlayerState me)
    {
        _options = state.Options;

        foreach (var card in me.Hand)
            Note(card);

        foreach (var meld in state.Table)
        {
            foreach (var card in meld.Cards)
                Note(card);
        }

        foreach (var card in state.Discard)
            Note(card);

        foreach (var move in state.History)
        {
            if (move.PlayerId != me.Id && move.Kind != MoveKind.Discarded)
                Note(move.Card);
        }
    }

    private void Note(Card card)
    {
        if (!_ids.Add(card.Id))
            return;

        if (card.IsJoker)
            _seenJokers++;
        else
            _seen[(int)card.Suit, (int)card.Rank]++;
    }

    public int LiveJokers => Math.Max(0, _options.DeckCount * _options.JokersPerDeck - _seenJokers);

    public int Live(Suit suit, Rank rank)
        => Math.Max(0, _options.DeckCount - _seen[(int)suit, (int)rank]);

    public int LiveRank(Rank rank, IEnumerable<Suit>? excludeSuits = null)
    {
        var excluded = excludeSuits?.ToHashSet() ?? [];
        var total = 0;

        for (var suit = 1; suit <= 4; suit++)
        {
            if (!excluded.Contains((Suit)suit))
                total += Live((Suit)suit, rank);
        }

        return total;
    }

    public int Seen(Suit suit, Rank rank) => _seen[(int)suit, (int)rank];
}
