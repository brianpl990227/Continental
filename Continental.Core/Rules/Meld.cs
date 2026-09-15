using Continental.Core.Model;

namespace Continental.Core.Rules;

public enum MeldKind
{
    Trio = 0,
    Escalera = 1
}

public sealed class Meld
{
    public required string Id { get; init; }

    public required string OwnerId { get; init; }

    public required MeldKind Kind { get; init; }

    public List<Card> Cards { get; init; } = [];

    public Rank? TrioRank { get; set; }

    public Suit? EscaleraSuit { get; set; }

    public int? LowValue { get; set; }

    public int Size => Cards.Count;

    public Meld Clone() => new()
    {
        Id = Id,
        OwnerId = OwnerId,
        Kind = Kind,
        Cards = [.. Cards],
        TrioRank = TrioRank,
        EscaleraSuit = EscaleraSuit,
        LowValue = LowValue
    };
}
