using Continental.Core.Rules;

namespace Continental.Core.Model;

public static class Deck
{

    public static List<Card> Build(GameOptions options)
    {
        var cards = new List<Card>(options.TotalCards);
        var id = 0;

        for (var deck = 0; deck < options.DeckCount; deck++)
        {
            foreach (var suit in new[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades })
            {
                for (var rank = Rank.Ace; rank <= Rank.King; rank++)
                    cards.Add(new Card(id++, suit, rank));
            }

            for (var j = 0; j < options.JokersPerDeck; j++)
                cards.Add(Card.Joker(id++));
        }

        return cards;
    }

    public static void Shuffle(IList<Card> cards, Random random)
    {
        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }
}

public enum HandSort
{

    BySuit = 0,

    ByRank = 1,

    Manual = 2
}

public static class HandSorter
{
    public static List<Card> Sort(IEnumerable<Card> cards, HandSort mode) => mode switch
    {
        HandSort.BySuit =>
        [
            .. cards.OrderBy(c => c.IsJoker ? 1 : 0)
                    .ThenBy(c => c.Suit)
                    .ThenBy(c => c.Rank)
                    .ThenBy(c => c.Id)
        ],
        HandSort.ByRank =>
        [
            .. cards.OrderBy(c => c.IsJoker ? 1 : 0)
                    .ThenBy(c => c.Rank)
                    .ThenBy(c => c.Suit)
                    .ThenBy(c => c.Id)
        ],
        _ => [.. cards]
    };
}
