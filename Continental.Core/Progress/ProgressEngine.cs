using System.Globalization;
using Continental.Core.Protocol;
using Continental.Core.Rules;

namespace Continental.Core.Progress;

public sealed class ProgressReport
{
    public bool Changed { get; set; }

    public bool GameFinished { get; set; }

    public string? GameKey { get; set; }

    public GameRecord? Record { get; set; }

    public int XpBefore { get; set; }

    public int XpAfter { get; set; }

    public int GameXp { get; set; }

    public List<XpLine> GameXpLines { get; set; } = [];

    public List<Mission> Missions { get; set; } = [];

    public bool DailyBonus { get; set; }

    public List<Cosmetic> Unlocked { get; set; } = [];

    public int LevelBefore => Leveling.LevelFor(XpBefore);

    public int LevelAfter => Leveling.LevelFor(XpAfter);

    public bool LeveledUp => LevelAfter > LevelBefore;

    public bool HasNews => Missions.Count > 0 || DailyBonus || LeveledUp || Unlocked.Count > 0;
}

public static class ProgressEngine
{
    public const int RecordedLimit = 60;

    public static string DayKey(DateTimeOffset now) => now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string WeekKey(DateTimeOffset now)
    {
        var date = now.ToLocalTime().DateTime;
        return $"{ISOWeek.GetYear(date)}-W{ISOWeek.GetWeekOfYear(date):00}";
    }

    private static int DaySeed(DateTimeOffset now) => DateOnly.FromDateTime(now.ToLocalTime().DateTime).DayNumber * 7919 + 17;

    private static int WeekSeed(DateTimeOffset now)
    {
        var date = now.ToLocalTime().DateTime;
        return (ISOWeek.GetYear(date) * 100 + ISOWeek.GetWeekOfYear(date)) * 104729 + 31;
    }

    public static bool Migrate(PlayerProfile profile)
    {
        if (profile.Version >= PlayerProfile.CurrentVersion)
            return false;

        profile.Xp = Math.Min(profile.Xp, EarnedXp(profile));
        profile.Version = PlayerProfile.CurrentVersion;

        return true;
    }

    public static int EarnedXp(PlayerProfile profile)
    {
        var stats = profile.Lifetime;
        var winsAtBigTables = stats.Get(Stat.WinsTable3) + stats.Get(Stat.WinsFullTable);
        var seconds = Math.Max(0, stats.Get(Stat.Podiums) - winsAtBigTables);

        var games = stats.Get(Stat.GamesPlayed) * 40
                    + stats.Get(Stat.RoundsClosed) * 15
                    + stats.Get(Stat.RoundsLaidDown) * 5
                    + stats.Get(Stat.GamesWon) * 80
                    + seconds * 30;

        var missions = profile.Achievements.Sum(id => MissionCatalog.Find(id)?.Xp ?? 0)
                       + stats.Get(Stat.DailiesCompleted) * AverageXp(MissionScope.Daily)
                       + stats.Get(Stat.WeekliesCompleted) * AverageXp(MissionScope.Weekly)
                       + stats.Get(Stat.DailySetsCompleted) * MissionCatalog.DailyBonusXp;

        return games + missions;
    }

    private static int AverageXp(MissionScope scope)
        => (int)MissionCatalog.PoolFor(scope).Average(m => m.Xp);

    public static bool Roll(PlayerProfile profile, DateTimeOffset now)
    {
        var changed = false;
        var day = DayKey(now);

        if (profile.Daily.Key != day)
        {
            profile.Daily = new PeriodState { Key = day, Missions = MissionCatalog.Pick(MissionScope.Daily, DaySeed(now)) };
            changed = true;
        }

        var week = WeekKey(now);

        if (profile.Weekly.Key != week)
        {
            profile.Weekly = new PeriodState { Key = week, Missions = MissionCatalog.Pick(MissionScope.Weekly, WeekSeed(now)) };
            changed = true;
        }

        return changed;
    }

    public static int ProgressOf(PlayerProfile profile, Mission mission) => mission.Scope switch
    {
        MissionScope.Daily => profile.Daily.Stats.Get(mission.Stat),
        MissionScope.Weekly => profile.Weekly.Stats.Get(mission.Stat),
        _ => profile.Lifetime.Get(mission.Stat)
    };

    public static bool IsDone(PlayerProfile profile, Mission mission) => mission.Scope switch
    {
        MissionScope.Daily => profile.Daily.Completed.Contains(mission.Id),
        MissionScope.Weekly => profile.Weekly.Completed.Contains(mission.Id),
        _ => profile.HasCompleted(mission.Id)
    };

    public static ProgressReport Observe(PlayerProfile profile, PlayerView view, DateTimeOffset now)
    {
        var report = new ProgressReport { XpBefore = profile.Xp, XpAfter = profile.Xp };
        var owned = OwnedIds(profile);

        report.Changed = Roll(profile, now);

        if (view.Phase == Engine.GamePhase.Lobby || view.GameNumber <= 0 || view.Tally is null)
            return report;

        var key = GameRecord.KeyOf(view);
        report.GameKey = key;

        if (profile.Recorded.Contains(key))
            return report;

        if (profile.Active?.Key != key)
        {
            profile.Active = new ActiveGame { Key = key, StartedAt = now };
            report.Changed = true;
        }

        var record = GameRecord.From(view, profile.Active.StartedAt, now);

        if (record is null)
            return report;

        report.Record = record;
        report.Changed |= Commit(profile, record.Contributions());

        if (record.Finished)
        {
            Finish(profile, record, now, report);
            report.Changed = true;
        }

        Derive(profile);
        Evaluate(profile, report);
        Conclude(profile, report, owned);

        if (record.Finished && profile.History.Count > 0)
            profile.History[0].Xp = report.XpAfter - report.XpBefore;

        return report;
    }

    public static ProgressReport? Reroll(PlayerProfile profile, string missionId, DateTimeOffset now)
    {
        Roll(profile, now);

        var daily = profile.Daily;

        if (daily.Rerolls >= MissionCatalog.DailyRerolls
            || !daily.Missions.Contains(missionId)
            || daily.Completed.Contains(missionId))
            return null;

        var replacement = MissionCatalog.Replacement(MissionScope.Daily, daily.Missions, missionId,
                                                     DaySeed(now) + 1000 * (daily.Rerolls + 1));

        if (replacement is null)
            return null;

        daily.Missions[daily.Missions.IndexOf(missionId)] = replacement;
        daily.Rerolls++;

        var report = new ProgressReport { XpBefore = profile.Xp, XpAfter = profile.Xp, Changed = true };
        var owned = OwnedIds(profile);

        Evaluate(profile, report);
        Conclude(profile, report, owned);

        return report;
    }

    private static HashSet<string> OwnedIds(PlayerProfile profile)
        => Cosmetics.All.Where(profile.Owns).Select(c => c.Id).ToHashSet();

    private static void Conclude(PlayerProfile profile, ProgressReport report, HashSet<string> owned)
    {
        report.XpAfter = profile.Xp;
        report.Unlocked = Cosmetics.All.Where(c => profile.Owns(c) && !owned.Contains(c.Id)).ToList();
    }

    private static bool Commit(PlayerProfile profile, Dictionary<Stat, int> contributions)
    {
        var committed = profile.Active!.Committed;
        var changed = false;

        foreach (var (stat, value) in contributions)
        {
            if (Stats.IsPeak(stat))
            {
                if (value > committed.GetValueOrDefault(stat))
                {
                    committed[stat] = value;
                    changed = true;
                }

                profile.Lifetime.Raise(stat, value);
                profile.Daily.Stats.Raise(stat, value);
                profile.Weekly.Stats.Raise(stat, value);
                continue;
            }

            var delta = value - committed.GetValueOrDefault(stat);

            if (delta <= 0)
                continue;

            committed[stat] = value;
            profile.Lifetime.Add(stat, delta);
            profile.Daily.Stats.Add(stat, delta);
            profile.Weekly.Stats.Add(stat, delta);
            changed = true;
        }

        return changed;
    }

    private static void Finish(PlayerProfile profile, GameRecord record, DateTimeOffset now, ProgressReport report)
    {
        report.GameFinished = true;

        var today = DayKey(now);

        if (profile.LastPlayedDay != today)
        {
            var yesterday = DayKey(now.AddDays(-1));
            profile.DayStreak = profile.LastPlayedDay == yesterday ? profile.DayStreak + 1 : 1;
            profile.LastPlayedDay = today;

            profile.Lifetime.Add(Stat.DaysPlayed, 1);
            profile.Weekly.Stats.Add(Stat.DaysPlayed, 1);
            profile.Daily.Stats.Add(Stat.DaysPlayed, 1);
            profile.Lifetime.Raise(Stat.BestDayStreak, profile.DayStreak);
        }

        if (record.Won)
        {
            profile.WinStreak++;
            profile.LossStreak = 0;
        }
        else
        {
            profile.LossStreak++;
            profile.WinStreak = 0;
        }

        profile.Lifetime.Raise(Stat.BestWinStreak, profile.WinStreak);
        profile.Lifetime.Raise(Stat.BestLossStreak, profile.LossStreak);
        profile.Weekly.Stats.Raise(Stat.BestWinStreak, profile.WinStreak);

        report.GameXp = Leveling.GameXp(record, out var lines);
        report.GameXpLines = lines;
        profile.Xp += report.GameXp;

        profile.TotalPoints += record.Score;
        profile.BestScore = profile.BestScore is { } best ? Math.Min(best, record.Score) : record.Score;
        profile.WorstScore = profile.WorstScore is { } worst ? Math.Max(worst, record.Score) : record.Score;

        var contracts = RoundContract.Standard.Count;

        while (profile.ContractPoints.Count < contracts)
            profile.ContractPoints.Add(0);

        while (profile.ContractRounds.Count < contracts)
            profile.ContractRounds.Add(0);

        for (var r = 0; r < Math.Min(contracts, record.RoundScores.Count); r++)
        {
            profile.ContractPoints[r] += record.RoundScores[r];
            profile.ContractRounds[r]++;
        }

        profile.History.Insert(0, new HistoryEntry
        {
            At = now,
            Score = record.Score,
            Position = record.Position,
            Players = record.Players,
            Won = record.Won,
            Closed = record.Tally.ClosedRounds.Count,
            Minutes = record.Minutes,
            Preset = record.Preset,
            Rounds = [.. record.RoundScores]
        });

        if (profile.History.Count > PlayerProfile.HistoryLimit)
            profile.History.RemoveRange(PlayerProfile.HistoryLimit, profile.History.Count - PlayerProfile.HistoryLimit);

        foreach (var opponent in record.Opponents)
        {
            var key = $"{(opponent.IsBot ? "bot" : "human")}:{opponent.Name.Trim().ToLowerInvariant()}";

            if (!profile.Rivals.TryGetValue(key, out var rival))
                profile.Rivals[key] = rival = new RivalRecord { Name = opponent.Name.Trim(), IsBot = opponent.IsBot };

            rival.Games++;
            rival.LastSeen = now;

            if (opponent.Score > record.Score)
                rival.Ahead++;
            else if (opponent.Score < record.Score)
                rival.Behind++;
        }

        if (profile.Rivals.Count > PlayerProfile.RivalLimit)
        {
            foreach (var stale in profile.Rivals.OrderBy(r => r.Value.LastSeen).Take(profile.Rivals.Count - PlayerProfile.RivalLimit).ToList())
                profile.Rivals.Remove(stale.Key);
        }

        profile.Recorded.Add(record.Key);

        if (profile.Recorded.Count > RecordedLimit)
            profile.Recorded.RemoveRange(0, profile.Recorded.Count - RecordedLimit);

        profile.Active = null;
    }

    private static void Derive(PlayerProfile profile)
    {
        foreach (var book in new[] { profile.Lifetime, profile.Daily.Stats, profile.Weekly.Stats })
            book.Raise(Stat.DistinctVoicesBeaten, Stats.BeatVoices.Count(v => book.Get(v) > 0));

        profile.Lifetime.Raise(Stat.MaxGamesInDay, profile.Daily.Stats.Get(Stat.GamesPlayed));
    }

    private static void Evaluate(PlayerProfile profile, ProgressReport report)
    {
        var daily = profile.Daily;

        foreach (var id in daily.Missions)
        {
            if (daily.Completed.Contains(id) || MissionCatalog.Find(id) is not { } mission)
                continue;

            if (daily.Stats.Get(mission.Stat) < mission.Target)
                continue;

            daily.Completed.Add(id);
            Award(profile, report, mission);
            profile.Lifetime.Add(Stat.DailiesCompleted, 1);
            profile.Weekly.Stats.Add(Stat.DailiesCompleted, 1);
        }

        if (!daily.BonusClaimed && daily.Missions.Count > 0 && daily.Missions.All(daily.Completed.Contains))
        {
            daily.BonusClaimed = true;
            profile.Xp += MissionCatalog.DailyBonusXp;
            report.DailyBonus = true;
            report.Changed = true;
            profile.Lifetime.Add(Stat.DailySetsCompleted, 1);
            profile.Weekly.Stats.Add(Stat.DailySetsCompleted, 1);
        }

        var weekly = profile.Weekly;

        foreach (var id in weekly.Missions)
        {
            if (weekly.Completed.Contains(id) || MissionCatalog.Find(id) is not { } mission)
                continue;

            if (weekly.Stats.Get(mission.Stat) < mission.Target)
                continue;

            weekly.Completed.Add(id);
            Award(profile, report, mission);
            profile.Lifetime.Add(Stat.WeekliesCompleted, 1);
        }

        var progressed = true;

        while (progressed)
        {
            progressed = false;

            foreach (var mission in MissionCatalog.Lifetime)
            {
                if (profile.HasCompleted(mission.Id) || profile.Lifetime.Get(mission.Stat) < mission.Target)
                    continue;

                profile.Achievements.Add(mission.Id);
                profile.Unseen.Add(mission.Id);
                Award(profile, report, mission);
                profile.Lifetime.Add(Stat.AchievementsCompleted, 1);
                progressed = true;
            }
        }
    }

    private static void Award(PlayerProfile profile, ProgressReport report, Mission mission)
    {
        profile.Xp += mission.Xp;
        report.Missions.Add(mission);
        report.Changed = true;
    }
}
