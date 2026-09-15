namespace Continental.Core.Model;

public enum Suit
{
    None = 0,
    Clubs = 1,
    Diamonds = 2,
    Hearts = 3,
    Spades = 4
}

public enum Rank
{
    Joker = 0,
    Ace = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13
}

public readonly record struct Card(int Id, Suit Suit, Rank Rank)
{
    public bool IsJoker => Rank == Rank.Joker;

    public bool IsRed => Suit is Suit.Diamonds or Suit.Hearts;

    public static Card Joker(int id) => new(id, Suit.None, Rank.Joker);

    public string Label => IsJoker ? "★" : $"{RankLabel}{SuitSymbol}";

    public string RankLabel => Rank switch
    {
        Rank.Joker => "★",
        Rank.Ace => "A",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        _ => ((int)Rank).ToString()
    };

    public string SuitSymbol => Suit switch
    {
        Suit.Clubs => "♣",
        Suit.Diamonds => "♦",
        Suit.Hearts => "♥",
        Suit.Spades => "♠",
        _ => ""
    };

    public override string ToString() => Label;
}
