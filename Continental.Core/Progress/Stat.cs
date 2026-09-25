namespace Continental.Core.Progress;

public enum Stat
{
    GamesPlayed,
    GamesWon,
    GamesLost,
    GamesLast,
    Podiums,
    GamesVsHumans,
    WinsVsHumans,
    GamesFullTable,
    WinsFullTable,
    WinsSixTable,
    GamesDuel,
    WinsDuel,
    GamesSpain,
    WinsSpain,
    GamesLatam,
    WinsLatam,
    WinsUnder100,
    WinsUnder50,
    WinsNegative,
    WinsBy100,
    WinsByHair,
    TiedWins,
    Comebacks,
    MiracleWins,
    WinsWithoutSteal,
    WinsWithoutJokers,
    GamesAllLaidDown,
    FlawlessGames,
    TouristGames,
    HugeLosses,
    WinsVsFiveBots,
    FastWins,
    MarathonGames,
    NightGames,
    MorningGames,
    WeekendGames,

    RoundsPlayed,
    RoundsClosed,
    SameTurnCloses,
    FirstTurnCloses,
    FirstRoundFirstTurnCloses,
    EeeSameTurnCloses,
    ZeroRounds,
    RoundsLaidDown,
    FullHandRounds,
    BigRounds,
    ClosesTT,
    ClosesTE,
    ClosesEE,
    ClosesTTT,
    ClosesTTE,
    ClosesTEE,
    ClosesEEE,

    Steals,
    JokerSwaps,
    Extensions,
    ExtensionsOnOthers,
    TookDiscard,
    JokersLaid,
    TriosLaid,
    EscalerasLaid,
    ChatMessages,
    Turns,
    MinutesPlayed,

    LongestEscalera,
    MaxSwapsInGame,
    MaxSwapsInRound,
    MaxClosesInGame,
    MaxStealsInGame,
    BestWinStreak,
    BestLossStreak,
    BestDayStreak,
    MaxGamesInDay,
    DistinctVoicesBeaten,

    DaysPlayed,
    DailiesCompleted,
    DailySetsCompleted,
    WeekliesCompleted,
    AchievementsCompleted,

    BeatCunado,
    BeatPicara,
    BeatZen,
    BeatDramatica,
    BeatFanfarron,
    BeatAbuela,
    LostToAbuela,

    DownTT,
    DownTE,
    DownEE,
    DownTTT,
    DownTTE,
    DownTEE,
    DownEEE,
    ZeroTT,
    ZeroTE,
    ZeroEE,
    ZeroTTT,
    ZeroTTE,
    ZeroTEE,
    ZeroEEE,

    GamesTable3,
    GamesTable4,
    GamesTable5,
    GamesTable6,
    WinsTable3,
    WinsTable4,
    WinsTable5,
    PodiumsVsHumans,

    UntouchableGames,
    BadStartWins,
    WinsVsThreeHumans,
    LostByHair,
    ExactHundredGames,
    ZeroGames,
    AllDownWins,
    FirstThreeCloses,
    BuzzerWins,
    MaxSameTurnInGame,
    MaxFirstTurnInGame,
    MaxJokersInGame,
    MaxExtensionsInGame,
    CleanSweepRounds,
    NoDiscardWins
}

public static class Stats
{
    private static readonly HashSet<Stat> Peaks =
    [
        Stat.LongestEscalera,
        Stat.MaxSwapsInGame,
        Stat.MaxSwapsInRound,
        Stat.MaxClosesInGame,
        Stat.MaxStealsInGame,
        Stat.BestWinStreak,
        Stat.BestLossStreak,
        Stat.BestDayStreak,
        Stat.MaxGamesInDay,
        Stat.DistinctVoicesBeaten,
        Stat.MaxSameTurnInGame,
        Stat.MaxFirstTurnInGame,
        Stat.MaxJokersInGame,
        Stat.MaxExtensionsInGame
    ];

    public static readonly IReadOnlyList<Stat> ClosesByContract =
    [
        Stat.ClosesTT, Stat.ClosesTE, Stat.ClosesEE, Stat.ClosesTTT, Stat.ClosesTTE, Stat.ClosesTEE, Stat.ClosesEEE
    ];

    public static readonly IReadOnlyList<Stat> DownByContract =
    [
        Stat.DownTT, Stat.DownTE, Stat.DownEE, Stat.DownTTT, Stat.DownTTE, Stat.DownTEE, Stat.DownEEE
    ];

    public static readonly IReadOnlyList<Stat> ZeroByContract =
    [
        Stat.ZeroTT, Stat.ZeroTE, Stat.ZeroEE, Stat.ZeroTTT, Stat.ZeroTTE, Stat.ZeroTEE, Stat.ZeroEEE
    ];

    public static readonly IReadOnlyList<Stat> BeatVoices =
    [
        Stat.BeatCunado, Stat.BeatPicara, Stat.BeatZen, Stat.BeatDramatica, Stat.BeatFanfarron, Stat.BeatAbuela
    ];

    public static bool IsPeak(Stat stat) => Peaks.Contains(stat);
}

public sealed class StatBook
{
    public Dictionary<Stat, int> Values { get; set; } = [];

    public int Get(Stat stat) => Values.GetValueOrDefault(stat);

    public void Add(Stat stat, int amount)
    {
        if (amount == 0)
            return;

        Values[stat] = Get(stat) + amount;
    }

    public void Raise(Stat stat, int value)
    {
        if (value > Get(stat))
            Values[stat] = value;
    }

    public void Apply(Stat stat, int amount)
    {
        if (Stats.IsPeak(stat))
            Raise(stat, amount);
        else
            Add(stat, amount);
    }
}
