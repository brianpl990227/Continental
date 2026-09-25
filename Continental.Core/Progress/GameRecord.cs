using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Protocol;
using Continental.Core.Rules;

namespace Continental.Core.Progress;

public sealed record Opponent(string Name, bool IsBot, int Score);

public sealed class GameRecord
{
    public required string Key { get; init; }

    public bool Finished { get; init; }

    public DateTimeOffset At { get; init; }

    public int Minutes { get; init; }

    public RulePreset Preset { get; init; }

    public int Players { get; init; }

    public int OtherHumans { get; init; }

    public int Score { get; init; }

    public int Position { get; init; }

    public bool Won { get; init; }

    public bool Tied { get; init; }

    public bool Last { get; init; }

    public int Margin { get; init; }

    public bool Comeback { get; init; }

    public bool Miracle { get; init; }

    public bool Flawless { get; init; }

    public bool WorstInFirstRound { get; init; }

    public IReadOnlyList<int> RoundScores { get; init; } = [];

    public MatchTally Tally { get; init; } = new();

    public IReadOnlyList<Opponent> Opponents { get; init; } = [];

    public static string KeyOf(PlayerView view) => $"{view.RoomId}:{view.GameNumber}";

    public static GameRecord? From(PlayerView view, DateTimeOffset startedAt, DateTimeOffset now)
    {
        if (view.Phase == GamePhase.Lobby || view.GameNumber <= 0 || view.Tally is null)
            return null;

        var me = view.Players.FirstOrDefault(p => p.Id == view.YouId);

        if (me is null)
            return null;

        var finished = view.Phase == GamePhase.GameOver;
        var others = view.Players.Where(p => p.Id != me.Id).ToList();
        var best = view.Players.Min(p => p.TotalScore);
        var won = me.TotalScore == best;
        var tied = won && others.Any(p => p.TotalScore == best);
        var runnerUp = others.Count > 0 ? others.Min(p => p.TotalScore) : me.TotalScore;

        return new GameRecord
        {
            Key = KeyOf(view),
            Finished = finished,
            At = now,
            Minutes = (int)Math.Clamp((now - startedAt).TotalMinutes, 0, 300),
            Preset = view.Options.Preset,
            Players = view.Players.Count,
            OtherHumans = others.Count(p => !p.IsBot),
            Score = me.TotalScore,
            Position = 1 + others.Count(p => p.TotalScore < me.TotalScore),
            Won = won,
            Tied = tied,
            Last = others.Count > 0 && others.All(p => p.TotalScore < me.TotalScore),
            Margin = won ? runnerUp - me.TotalScore : me.TotalScore - best,
            Comeback = won && !LeadingAfter(view.Players, me, 5),
            Miracle = won && view.Players.Count >= 3 && LastAfter(view.Players, me, 6),
            Flawless = view.Players.Count >= 3 && NeverWorst(view.Players, me),
            WorstInFirstRound = view.Players.Count >= 3 && me.RoundScores.Count > 0
                                && view.Players.Where(p => p.Id != me.Id && p.RoundScores.Count > 0).All(p => p.RoundScores[0] < me.RoundScores[0]),
            RoundScores = me.RoundScores.ToList(),
            Tally = view.Tally.Snapshot(),
            Opponents = others.Select(p => new Opponent(p.Name, p.IsBot, p.TotalScore)).ToList()
        };
    }

    private static int Through(PlayerSummary player, int rounds) => player.RoundScores.Take(rounds).Sum();

    private static bool LeadingAfter(IReadOnlyList<PlayerSummary> players, PlayerSummary me, int rounds)
    {
        if (me.RoundScores.Count < rounds)
            return true;

        return Through(me, rounds) <= players.Min(p => Through(p, rounds));
    }

    private static bool LastAfter(IReadOnlyList<PlayerSummary> players, PlayerSummary me, int rounds)
    {
        if (me.RoundScores.Count < rounds)
            return false;

        var mine = Through(me, rounds);

        return players.Where(p => p.Id != me.Id).All(p => Through(p, rounds) < mine);
    }

    private static bool NeverWorst(IReadOnlyList<PlayerSummary> players, PlayerSummary me)
    {
        if (me.RoundScores.Count == 0)
            return false;

        for (var r = 0; r < me.RoundScores.Count; r++)
        {
            var round = r;
            var worst = players.Where(p => p.RoundScores.Count > round).Max(p => p.RoundScores[round]);

            if (me.RoundScores[round] >= worst)
                return false;
        }

        return true;
    }

    public Dictionary<Stat, int> Contributions()
    {
        var c = new Dictionary<Stat, int>();

        void Put(Stat stat, int value)
        {
            if (value != 0)
                c[stat] = value;
        }

        void Flag(Stat stat, bool condition) => Put(stat, condition ? 1 : 0);

        var t = Tally;
        var rounds = RoundScores.Count;

        Put(Stat.RoundsPlayed, rounds);
        Put(Stat.RoundsClosed, t.ClosedRounds.Count);
        Put(Stat.SameTurnCloses, t.SameTurnCloseRounds.Count);
        Put(Stat.FirstTurnCloses, t.FirstTurnCloseRounds.Count);
        Put(Stat.FirstRoundFirstTurnCloses, t.FirstTurnCloseRounds.Count(r => r == 0));
        Put(Stat.EeeSameTurnCloses, t.SameTurnCloseRounds.Count(r => r == RoundContract.Standard.Count - 1));
        Put(Stat.ZeroRounds, RoundScores.Count(s => s <= 0));
        Put(Stat.RoundsLaidDown, t.LaidDownRounds.Count);
        Put(Stat.FullHandRounds, Enumerable.Range(0, rounds).Count(r => !t.LaidDownRounds.Contains(r)));
        Put(Stat.BigRounds, RoundScores.Count(s => s >= 100));

        for (var i = 0; i < Stats.ClosesByContract.Count; i++)
        {
            var index = i;
            Put(Stats.ClosesByContract[i], t.ClosedRounds.Count(r => r == index));
            Put(Stats.DownByContract[i], t.LaidDownRounds.Count(r => r == index));
            Put(Stats.ZeroByContract[i], index < rounds && RoundScores[index] <= 0 ? 1 : 0);
        }

        Put(Stat.CleanSweepRounds, RoundScores.Count(s => s < 0));
        Put(Stat.MaxSameTurnInGame, t.SameTurnCloseRounds.Count);
        Put(Stat.MaxFirstTurnInGame, t.FirstTurnCloseRounds.Count);
        Put(Stat.MaxJokersInGame, t.JokersLaid);
        Put(Stat.MaxExtensionsInGame, t.Extensions);
        Put(Stat.FirstThreeCloses, new[] { 0, 1, 2 }.All(t.ClosedRounds.Contains) ? 1 : 0);

        Put(Stat.Steals, t.Steals);
        Put(Stat.JokerSwaps, t.JokerSwaps);
        Put(Stat.Extensions, t.Extensions);
        Put(Stat.ExtensionsOnOthers, t.ExtensionsOnOthers);
        Put(Stat.TookDiscard, t.TookDiscard);
        Put(Stat.JokersLaid, t.JokersLaid);
        Put(Stat.TriosLaid, t.TriosLaid);
        Put(Stat.EscalerasLaid, t.EscalerasLaid);
        Put(Stat.ChatMessages, t.ChatMessages);
        Put(Stat.Turns, t.Turns);

        Put(Stat.LongestEscalera, t.LongestEscalera);
        Put(Stat.MaxSwapsInGame, t.JokerSwaps);
        Put(Stat.MaxSwapsInRound, t.MaxSwapsInRound);
        Put(Stat.MaxClosesInGame, t.ClosedRounds.Count);
        Put(Stat.MaxStealsInGame, t.Steals);

        if (!Finished)
            return c;

        var local = At.ToLocalTime();
        var bots = Opponents.Count(o => o.IsBot);

        Put(Stat.GamesPlayed, 1);
        Put(Stat.MinutesPlayed, Minutes);
        Flag(Stat.GamesWon, Won);
        Flag(Stat.GamesLost, !Won);
        Flag(Stat.GamesLast, Last);
        Flag(Stat.Podiums, Players >= 3 && Position <= 2);
        Flag(Stat.GamesVsHumans, OtherHumans > 0);
        Flag(Stat.WinsVsHumans, Won && OtherHumans > 0);
        Flag(Stat.GamesFullTable, Players >= 4);
        Flag(Stat.WinsFullTable, Won && Players >= 4);
        Flag(Stat.WinsSixTable, Won && Players >= 6);
        Flag(Stat.GamesDuel, Players == 2);
        Flag(Stat.WinsDuel, Won && Players == 2);
        Flag(Stat.GamesSpain, Preset == RulePreset.Spain);
        Flag(Stat.WinsSpain, Won && Preset == RulePreset.Spain);
        Flag(Stat.GamesLatam, Preset == RulePreset.LatinAmerica);
        Flag(Stat.WinsLatam, Won && Preset == RulePreset.LatinAmerica);
        Flag(Stat.WinsUnder100, Won && Score <= 100);
        Flag(Stat.WinsUnder50, Won && Score <= 50);
        Flag(Stat.WinsNegative, Won && Score < 0);
        Flag(Stat.WinsBy100, Won && !Tied && Margin >= 100);
        Flag(Stat.WinsByHair, Won && !Tied && Margin is > 0 and <= 5);
        Flag(Stat.TiedWins, Tied);
        Flag(Stat.Comebacks, Comeback);
        Flag(Stat.MiracleWins, Miracle);
        Flag(Stat.WinsWithoutSteal, Won && t.Steals == 0);
        Flag(Stat.WinsWithoutJokers, Won && t.JokersLaid == 0 && t.JokerSwaps == 0);
        Flag(Stat.GamesAllLaidDown, rounds > 0 && t.LaidDownRounds.Count >= rounds);
        Flag(Stat.FlawlessGames, Flawless);
        Flag(Stat.TouristGames, rounds > 0 && t.LaidDownRounds.Count == 0);
        Flag(Stat.HugeLosses, Score >= 500);
        Flag(Stat.WinsVsFiveBots, Won && bots >= 5);
        Flag(Stat.FastWins, Won && Minutes <= 12);
        Flag(Stat.MarathonGames, Minutes >= 60);
        Flag(Stat.NightGames, local.Hour is >= 1 and < 5);
        Flag(Stat.MorningGames, local.Hour is >= 5 and < 8);
        Flag(Stat.WeekendGames, local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);

        Flag(Stat.GamesTable3, Players == 3);
        Flag(Stat.GamesTable4, Players == 4);
        Flag(Stat.GamesTable5, Players == 5);
        Flag(Stat.GamesTable6, Players == 6);
        Flag(Stat.WinsTable3, Won && Players == 3);
        Flag(Stat.WinsTable4, Won && Players == 4);
        Flag(Stat.WinsTable5, Won && Players == 5);
        Flag(Stat.PodiumsVsHumans, OtherHumans > 0 && Players >= 3 && Position <= 2);
        Flag(Stat.UntouchableGames, rounds >= 7 && RoundScores.All(s => s <= 0));
        Flag(Stat.BadStartWins, Won && WorstInFirstRound);
        Flag(Stat.WinsVsThreeHumans, Won && OtherHumans >= 3);
        Flag(Stat.LostByHair, !Won && Margin is > 0 and <= 5);
        Flag(Stat.ExactHundredGames, Score == 100);
        Flag(Stat.ZeroGames, Score == 0);
        Flag(Stat.AllDownWins, Won && rounds > 0 && t.LaidDownRounds.Count >= rounds);
        Flag(Stat.BuzzerWins, Won && t.ClosedRounds.Contains(RoundContract.Standard.Count - 1));
        Flag(Stat.NoDiscardWins, Won && t.TookDiscard == 0);

        foreach (var voice in Enum.GetValues<Voice>())
        {
            var beat = Opponents.Any(o => o.IsBot && BotBanter.VoiceOf(o.Name) == voice && o.Score > Score);
            Flag(Stats.BeatVoices[(int)voice], beat);
        }

        Flag(Stat.LostToAbuela, Opponents.Any(o => o.IsBot && BotBanter.VoiceOf(o.Name) == Voice.Abuela && o.Score < Score));

        return c;
    }
}
