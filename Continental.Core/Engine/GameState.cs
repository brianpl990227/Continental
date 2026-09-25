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

    public PlayerBadge? Badge { get; set; }

    public MatchTally Tally { get; set; } = new();

    public int TurnsThisRound { get; set; }

    public int SwapsThisRound { get; set; }
}

public sealed record PlayerBadge(int Level, string? Avatar, string? Title)
{
    public const int MaxAvatarLength = 16;
    public const int MaxTitleLength = 40;

    public static PlayerBadge? Sanitize(PlayerBadge? badge)
    {
        if (badge is null)
            return null;

        var avatar = badge.Avatar?.Trim();
        var title = badge.Title?.Trim();

        return new PlayerBadge(
            Math.Clamp(badge.Level, 1, 999),
            string.IsNullOrEmpty(avatar) || avatar.Length > MaxAvatarLength ? null : avatar,
            string.IsNullOrEmpty(title) ? null : title.Length > MaxTitleLength ? title[..MaxTitleLength] : title);
    }
}

public sealed class MatchTally
{
    public int Turns { get; set; }

    public int Steals { get; set; }

    public int JokerSwaps { get; set; }

    public int MaxSwapsInRound { get; set; }

    public int Extensions { get; set; }

    public int ExtensionsOnOthers { get; set; }

    public int TookDiscard { get; set; }

    public int JokersLaid { get; set; }

    public int TriosLaid { get; set; }

    public int EscalerasLaid { get; set; }

    public int LongestEscalera { get; set; }

    public int ChatMessages { get; set; }

    public List<int> ClosedRounds { get; set; } = [];

    public List<int> LaidDownRounds { get; set; } = [];

    public List<int> SameTurnCloseRounds { get; set; } = [];

    public List<int> FirstTurnCloseRounds { get; set; } = [];

    public MatchTally Snapshot() => new()
    {
        Turns = Turns,
        Steals = Steals,
        JokerSwaps = JokerSwaps,
        MaxSwapsInRound = MaxSwapsInRound,
        Extensions = Extensions,
        ExtensionsOnOthers = ExtensionsOnOthers,
        TookDiscard = TookDiscard,
        JokersLaid = JokersLaid,
        TriosLaid = TriosLaid,
        EscalerasLaid = EscalerasLaid,
        LongestEscalera = LongestEscalera,
        ChatMessages = ChatMessages,
        ClosedRounds = [.. ClosedRounds],
        LaidDownRounds = [.. LaidDownRounds],
        SameTurnCloseRounds = [.. SameTurnCloseRounds],
        FirstTurnCloseRounds = [.. FirstTurnCloseRounds]
    };
}

public enum MoveKind
{
    TookDiscard = 0,
    Stole = 1,
    Discarded = 2
}

public sealed record PublicMove(string PlayerId, Card Card, MoveKind Kind);

public sealed record ChatLine(int Seq, string PlayerId, string Name, string Text, bool IsBot);

public enum GameEventKind
{
    GameStarted = 0,
    RoundStarted = 1,
    TookDiscard = 2,
    Stole = 3,
    LaidDown = 4,
    Extended = 5,
    JokerSwapped = 6,
    Discarded = 7,
    RoundEnded = 8,
    GameOver = 9,
    StockRecycled = 10,
    PlayerJoined = 11,
    PlayerLeft = 12,
    ChatSaid = 13
}

public sealed record GameEvent(GameEventKind Kind, string? PlayerId = null, string? TargetPlayerId = null, Card? Card = null, string? Text = null);

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

    public int GameNumber { get; set; }

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

    public List<PublicMove> History { get; init; } = [];

    public List<string> Log { get; init; } = [];

    public List<ChatLine> Chat { get; init; } = [];

    public List<string> TypingIds { get; init; } = [];

    public DateTimeOffset TurnStartedAt { get; set; } = DateTimeOffset.UtcNow;

    public int ChatSeq { get; set; }

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

    public ChatLine Talk(PlayerState player, string text)
    {
        var line = new ChatLine(++ChatSeq, player.Id, player.Name, text, player.IsBot);
        Chat.Add(line);

        if (Chat.Count > 80)
            Chat.RemoveRange(0, Chat.Count - 80);

        return line;
    }
}
