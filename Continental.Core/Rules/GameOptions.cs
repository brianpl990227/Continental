using Continental.Core.Model;

namespace Continental.Core.Rules;

public enum RulePreset
{
    Spain = 0,
    LatinAmerica = 1,
    Custom = 2
}

public sealed record GameOptions
{
    public RulePreset Preset { get; init; } = RulePreset.LatinAmerica;

    public int StartingCards { get; init; } = 7;

    public int DeckCount { get; init; } = 2;

    public int JokersPerDeck { get; init; } = 3;

    public int AceValue { get; init; } = 30;

    public int JokerValue { get; init; } = 50;

    public int FaceValue { get; init; } = 10;

    public bool AceHighAndLow { get; init; }

    public int MinTrioSize { get; init; } = 3;

    public int MinEscaleraSize { get; init; } = 4;

    public bool TrioRequiresDistinctSuits { get; init; }

    public bool NoTwoAdjacentJokersInEscalera { get; init; } = true;

    public bool AllowSteal { get; init; } = true;

    public int StealPenaltyCards { get; init; } = 1;

    public int StealWindowSeconds { get; init; } = 5;

    public int CloseSameTurnBonus { get; init; } = -10;

    public bool CanExtendOpponentMelds { get; init; } = true;

    public int TurnSeconds { get; init; }

    public int MaxStockRecycles { get; init; } = 2;

    public static GameOptions ForPreset(RulePreset preset) => preset switch
    {
        RulePreset.Spain => new GameOptions
        {
            Preset = RulePreset.Spain,
            StartingCards = 6,
            AceValue = 20,
            AceHighAndLow = true,
            TrioRequiresDistinctSuits = true
        },
        _ => new GameOptions
        {
            Preset = RulePreset.LatinAmerica,
            StartingCards = 7,
            AceValue = 30,
            AceHighAndLow = false,
            TrioRequiresDistinctSuits = false
        }
    };

    public string DisplayName => Preset switch
    {
        RulePreset.Spain => "España",
        RulePreset.LatinAmerica => "Latinoamérica",
        _ => "Personalizada"
    };

    public int TotalCards => DeckCount * (52 + JokersPerDeck);

    public int CardsForRound(int roundIndex) => StartingCards + roundIndex;

    public int ValueOf(Card card) => card.Rank switch
    {
        Rank.Joker => JokerValue,
        Rank.Ace => AceValue,
        Rank.Jack or Rank.Queen or Rank.King => FaceValue,
        _ => (int)card.Rank
    };

    public IReadOnlyList<(string Label, string Value)> Summary()
    {
        var rows = new List<(string, string)>
        {
            ("Variante", DisplayName),
            ("Cartas en la 1ª ronda", StartingCards.ToString()),
            ("Barajas", $"{DeckCount} ({DeckCount * JokersPerDeck} comodines)"),
            ("Valor del As", $"{AceValue} pts"),
            ("As en escalera", AceHighAndLow ? "Alto y bajo (K-A-2)" : "Solo alto"),
            ("Trío mínimo", $"{MinTrioSize} cartas"),
            ("Escalera mínima", $"{MinEscaleraSize} cartas"),
            ("Trío con palos distintos", TrioRequiresDistinctSuits ? "Sí" : "No"),
            ("Comodines seguidos en escalera", NoTwoAdjacentJokersInEscalera ? "Prohibido" : "Permitido"),
            ("Robar de contra", AllowSteal ? $"Sí (+{StealPenaltyCards} de castigo)" : "No"),
            ("Bajarse y cerrar a la vez", $"{CloseSameTurnBonus} pts"),
            ("Ampliar juegos ajenos", CanExtendOpponentMelds ? "Sí" : "No")
        };
        return rows;
    }
}
