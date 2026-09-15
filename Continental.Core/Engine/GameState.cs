using Continental.Core.Model;
using Continental.Core.Rules;

namespace Continental.Core.Engine;

public enum GamePhase
{
    Lobby = 0,

    Draw = 1,

    StealWindow = 2,

    Action = 3,

    RoundEnd = 4,
    GameOver = 5
}

public sealed class PlayerState
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public bool IsBot { get; init; }

    public bool IsHost { get; set; }

    public bool IsConnected { get; set; } = true;

    public int Seat { get; set; }

    public List<Card> Hand { get; init; } = [];

    public bool HasLaidDown { get; set; }

    public bool LaidDownThisTurn { get; set; }

    public int TotalScore { get; set; }

    public List<int> RoundScores { get; init; } = [];
}

public sealed class StealOffer
{
    public required Card Card { get; init; }

    public required string BlockedPlayerId { get; init; }

    public string? DiscarderId { get; init; }

    public required DateTimeOffset Deadline { get; init; }
}

public sealed class GameState
{
    public required string RoomId { get; init; }

    public required string RoomName { get; set; }

    public GameOptions Options { get; set; } = GameOptions.ForPreset(RulePreset.LatinAmerica);

    public GamePhase Phase { get; set; } = GamePhase.Lobby;

    public int RoundIndex { get; set; }

    public List<PlayerState> Players { get; init; } = [];

    public int CurrentPlayerIndex { get; set; }

    public int DealerIndex { get; set; }

    public List<Card> Stock { get; init; } = [];

    public List<Card> Discard { get; init; } = [];

    public List<Meld> Table { get; init; } = [];

    public StealOffer? Steal { get; set; }

    public string? DiscardOwnerId { get; set; }

    public int StockRecycles { get; set; }

    public string? LastCloserId { get; set; }

    public List<string> Log { get; init; } = [];

    public RoundContract Contract => RoundContract.Standard[Math.Clamp(RoundIndex, 0, RoundContract.Standard.Count - 1)];

    public int TotalRounds => RoundContract.Standard.Count;

    public PlayerState? Current =>
        Players.Count > 0 && CurrentPlayerIndex >= 0 && CurrentPlayerIndex < Players.Count
            ? Players[CurrentPlayerIndex]
            : null;

    public Card? DiscardTop => Discard.Count > 0 ? Discard[^1] : null;

    public PlayerState? Find(string playerId) => Players.FirstOrDefault(p => p.Id == playerId);

    public void Say(string message)
    {
        Log.Add(message);

        if (Log.Count > 60)
            Log.RemoveRange(0, Log.Count - 60);
    }
}
