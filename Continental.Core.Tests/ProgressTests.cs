using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Progress;
using Continental.Core.Protocol;
using Continental.Core.Rules;
using Xunit;

namespace Continental.Core.Tests;

public class MissionCatalogTests
{
    [Fact]
    public void Every_mission_has_a_unique_id()
        => Assert.Equal(MissionCatalog.All.Count, MissionCatalog.All.Select(m => m.Id).Distinct().Count());

    [Fact]
    public void There_are_lots_and_lots_of_missions()
    {
        Assert.True(MissionCatalog.All.Count >= 800, $"Solo hay {MissionCatalog.All.Count} misiones");
        Assert.True(MissionCatalog.Achievements.Count >= 600, $"Logros: {MissionCatalog.Achievements.Count}");
        Assert.True(MissionCatalog.Secrets.Count >= 30, $"Secretas: {MissionCatalog.Secrets.Count}");
        Assert.True(MissionCatalog.DailyPool.Count >= 100, $"Diarias: {MissionCatalog.DailyPool.Count}");
        Assert.True(MissionCatalog.WeeklyPool.Count >= 70, $"Semanales: {MissionCatalog.WeeklyPool.Count}");
    }

    [Theory]
    [InlineData(MissionScope.Daily)]
    [InlineData(MissionScope.Weekly)]
    public void Each_period_pool_has_every_difficulty(MissionScope scope)
    {
        foreach (var difficulty in Enum.GetValues<Difficulty>())
            Assert.True(MissionCatalog.PoolFor(scope).Count(m => m.Difficulty == difficulty) >= 5);
    }

    [Fact]
    public void Missions_have_positive_targets_and_rewards()
        => Assert.All(MissionCatalog.All, m =>
        {
            Assert.True(m.Target > 0, m.Id);
            Assert.True(m.Xp > 0, m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.Title), m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.Detail), m.Id);
        });

    [Fact]
    public void Tiers_grow_monotonically()
    {
        foreach (var group in MissionCatalog.Achievements.GroupBy(m => m.Id[..m.Id.LastIndexOf('.')]))
        {
            var targets = group.OrderBy(m => m.Tier).Select(m => m.Target).ToList();
            Assert.Equal(targets.OrderBy(t => t), targets);
            Assert.Equal(targets.Count, targets.Distinct().Count());
        }
    }

    [Fact]
    public void Picking_is_deterministic_and_covers_three_difficulties()
    {
        var a = MissionCatalog.Pick(MissionScope.Daily, 12345);
        var b = MissionCatalog.Pick(MissionScope.Daily, 12345);

        Assert.Equal(a, b);
        Assert.Equal(3, a.Distinct().Count());
        Assert.Equal([Difficulty.Easy, Difficulty.Medium, Difficulty.Hard],
                     a.Select(id => MissionCatalog.Find(id)!.Difficulty));
    }

    [Fact]
    public void Cosmetic_ids_are_unique_and_mission_unlocks_point_to_real_missions()
    {
        Assert.Equal(Cosmetics.All.Count, Cosmetics.All.Select(c => c.Id).Distinct().Count());

        foreach (var cosmetic in Cosmetics.All.Where(c => c.Mission is not null))
        {
            var mission = MissionCatalog.Find(cosmetic.Mission!);
            Assert.NotNull(mission);
            Assert.True(mission!.Scope is MissionScope.Achievement or MissionScope.Secret, cosmetic.Id);
        }
    }

    [Fact]
    public void A_fresh_profile_wears_a_default_of_every_kind()
    {
        var fresh = PlayerProfile.Create(DateTimeOffset.UnixEpoch);

        foreach (var kind in Enum.GetValues<CosmeticKind>().Where(Cosmetics.IsEquippable))
        {
            Assert.True(fresh.Owns(Cosmetics.DefaultOf(kind)));
            Assert.Equal(Cosmetics.DefaultOf(kind), fresh.Current(kind));
        }
    }

    [Fact]
    public void Every_level_up_to_the_top_of_the_ladder_unlocks_something()
    {
        for (var level = 2; level <= Cosmetics.LadderTop; level++)
            Assert.True(Cosmetics.UnlockedAt(level).Any(), $"El nivel {level} no desbloquea nada");
    }

    [Fact]
    public void There_are_hundreds_of_collectibles()
        => Assert.True(Cosmetics.All.Count >= 300, $"Solo hay {Cosmetics.All.Count} coleccionables");

    [Fact]
    public void Names_do_not_repeat_within_a_kind()
    {
        foreach (var kind in Cosmetics.All.GroupBy(c => c.Kind))
            Assert.Equal(kind.Count(), kind.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void Avatars_do_not_repeat()
    {
        var avatars = Cosmetics.OfKind(CosmeticKind.Avatar).Where(c => c.Value.Length > 0).Select(c => c.Value).ToList();
        Assert.Equal(avatars.Count, avatars.Distinct().Count());
    }
}

public class LevelingTests
{
    [Fact]
    public void A_fresh_profile_is_level_one()
    {
        var progress = Leveling.Progress(0);

        Assert.Equal(1, progress.Level);
        Assert.Equal(0, progress.Into);
        Assert.Equal(Leveling.CostOf(1), progress.Needed);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(50)]
    public void Reaching_the_total_for_a_level_lands_exactly_on_it(int level)
    {
        Assert.Equal(level, Leveling.LevelFor(Leveling.TotalFor(level)));
        Assert.Equal(level - 1, Leveling.LevelFor(Leveling.TotalFor(level) - 1));
    }

    [Fact]
    public void Levels_get_more_expensive()
    {
        for (var level = 1; level < 100; level++)
            Assert.True(Leveling.CostOf(level + 1) > Leveling.CostOf(level));
    }

    [Fact]
    public void Ranks_follow_levels()
    {
        Assert.Equal("Novato", Leveling.RankFor(1).Name);
        Assert.Equal("Experto", Leveling.RankFor(17).Name);
        Assert.Equal("Inmortal del Continental", Leveling.RankFor(140).Name);
    }
}

public class ProgressEngineTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class Sim
    {
        public required GameState State { get; init; }

        public required GameEngine Engine { get; init; }

        public required Random Random { get; init; }
    }

    private static Sim NewTable(int seed, string[] names, RulePreset preset = RulePreset.LatinAmerica, string roomId = "room")
    {
        var random = new Random(seed);
        var state = new GameState { RoomId = roomId, RoomName = "Sim", Options = GameOptions.ForPreset(preset) };
        var engine = new GameEngine(state, random);

        foreach (var name in names)
            engine.AddPlayer(name.ToLowerInvariant(), name, isBot: name != names[0], isHost: name == names[0]);

        return new Sim { State = state, Engine = engine, Random = random };
    }

    private static void Step(Sim sim)
    {
        var state = sim.State;
        var engine = sim.Engine;

        switch (state.Phase)
        {
            case GamePhase.StealWindow:
            {
                var offer = state.Steal!;
                var thief = state.Players.FirstOrDefault(p => p.Id != offer.BlockedPlayerId && p.Id != offer.DiscarderId
                                                             && BotBrain.WantsSteal(state, p));

                if (thief is not null && sim.Random.Next(3) == 0)
                    engine.ClaimSteal(thief.Id);
                else
                    engine.PassSteal();

                break;
            }

            case GamePhase.Draw:
            {
                var bot = state.Current!;
                engine.Draw(bot.Id, BotBrain.ChooseDraw(state, bot));
                break;
            }

            case GamePhase.Action:
            {
                var bot = state.Current!;

                if (BotBrain.TryLayDown(state, bot) is { } specs && engine.LayDown(bot.Id, specs).Ok)
                    break;

                if (BotBrain.FindJokerSwap(state, bot) is { } swap
                    && engine.SwapJoker(bot.Id, swap.MeldId, swap.CardId, swap.TargetMeldId, swap.Position).Ok)
                    break;

                if (BotBrain.FindPlacements(state, bot).Any(p => engine.Extend(bot.Id, p.MeldId, p.CardId).Ok))
                    break;

                engine.Discard(bot.Id, BotBrain.ChooseDiscard(state, bot));
                break;
            }

            case GamePhase.RoundEnd:
                engine.NextRound();
                break;
        }
    }

    private static List<ProgressReport> Play(Sim sim, PlayerProfile profile, string playerId, DateTimeOffset start, int minutes = 25)
    {
        Assert.True(sim.Engine.StartGame().Ok);

        var reports = new List<ProgressReport> { ProgressEngine.Observe(profile, PlayerView.For(sim.State, playerId), start) };
        var steps = 0;

        while (sim.State.Phase != GamePhase.GameOver && steps++ < 60_000)
        {
            Step(sim);
            var progress = Math.Min(1.0, steps / 900.0);
            var now = start.AddMinutes(sim.State.Phase == GamePhase.GameOver ? minutes : minutes * progress * 0.9);
            reports.Add(ProgressEngine.Observe(profile, PlayerView.For(sim.State, playerId), now));
        }

        Assert.Equal(GamePhase.GameOver, sim.State.Phase);
        return reports;
    }

    [Fact]
    public void A_whole_game_is_recorded_once_with_consistent_numbers()
    {
        var sim = NewTable(4242, ["Ana", "Chelo", "Marta", "Lola"]);
        var profile = PlayerProfile.Create(Noon);

        var reports = Play(sim, profile, "ana", Noon);

        var me = sim.State.Find("ana")!;
        var tally = me.Tally;
        var life = profile.Lifetime;

        Assert.Equal(1, life.Get(Stat.GamesPlayed));
        Assert.Equal(7, life.Get(Stat.RoundsPlayed));
        Assert.Equal(tally.ClosedRounds.Count, life.Get(Stat.RoundsClosed));
        Assert.Equal(tally.Steals, life.Get(Stat.Steals));
        Assert.Equal(tally.JokerSwaps, life.Get(Stat.JokerSwaps));
        Assert.Equal(tally.Extensions, life.Get(Stat.Extensions));
        Assert.Equal(tally.LaidDownRounds.Count, life.Get(Stat.RoundsLaidDown));
        Assert.Equal(tally.TookDiscard, life.Get(Stat.TookDiscard));
        Assert.Equal(me.RoundScores.Count(s => s <= 0), life.Get(Stat.ZeroRounds));

        var won = me.TotalScore == sim.State.Players.Min(p => p.TotalScore);
        Assert.Equal(won ? 1 : 0, life.Get(Stat.GamesWon));
        Assert.Equal(won ? 0 : 1, life.Get(Stat.GamesLost));

        Assert.Single(profile.History);
        Assert.Equal(me.TotalScore, profile.History[0].Score);
        Assert.Equal(3, profile.Rivals.Count);
        Assert.Null(profile.Active);
        Assert.Single(reports, r => r.GameFinished);

        var gained = reports.Sum(r => r.XpAfter - r.XpBefore);
        Assert.Equal(profile.Xp, gained);
        Assert.Equal(profile.History[0].Xp, reports.Single(r => r.GameFinished).XpAfter - reports.Single(r => r.GameFinished).XpBefore);

        var again = ProgressEngine.Observe(profile, PlayerView.For(sim.State, "ana"), Noon.AddMinutes(40));
        Assert.False(again.GameFinished);
        Assert.Empty(again.Missions);
        Assert.Equal(1, profile.Lifetime.Get(Stat.GamesPlayed));
        Assert.Equal(gained, profile.Xp);
    }

    [Fact]
    public void Stats_count_live_while_the_game_is_still_going()
    {
        var sim = NewTable(99, ["Ana", "Chelo", "Marta"]);
        var profile = PlayerProfile.Create(Noon);

        Assert.True(sim.Engine.StartGame().Ok);

        var steps = 0;

        while (sim.State.RoundIndex < 2 && sim.State.Phase != GamePhase.GameOver && steps++ < 20_000)
        {
            Step(sim);
            ProgressEngine.Observe(profile, PlayerView.For(sim.State, "ana"), Noon);
        }

        var tally = sim.State.Find("ana")!.Tally;

        Assert.Equal(0, profile.Lifetime.Get(Stat.GamesPlayed));
        Assert.Equal(2, profile.Lifetime.Get(Stat.RoundsPlayed));
        Assert.Equal(tally.Turns, profile.Lifetime.Get(Stat.Turns));
        Assert.Equal(tally.Extensions, profile.Lifetime.Get(Stat.Extensions));
        Assert.NotNull(profile.Active);
    }

    [Fact]
    public void Every_round_is_closed_by_somebody_or_nobody()
    {
        var names = new[] { "Ana", "Beto", "Caro", "Dani" };
        var sim = NewTable(1234, names);
        var profiles = names.ToDictionary(n => n.ToLowerInvariant(), _ => PlayerProfile.Create(Noon));

        Assert.True(sim.Engine.StartGame().Ok);

        var steps = 0;

        while (sim.State.Phase != GamePhase.GameOver && steps++ < 60_000)
        {
            Step(sim);

            foreach (var (id, profile) in profiles)
                ProgressEngine.Observe(profile, PlayerView.For(sim.State, id), Noon.AddMinutes(20));
        }

        foreach (var (id, profile) in profiles)
            ProgressEngine.Observe(profile, PlayerView.For(sim.State, id), Noon.AddMinutes(20));

        var closes = profiles.Values.Sum(p => p.Lifetime.Get(Stat.RoundsClosed));
        Assert.InRange(closes, 1, 7);
        Assert.True(profiles.Values.Sum(p => p.Lifetime.Get(Stat.GamesWon)) >= 1);
        Assert.All(profiles.Values, p => Assert.Equal(1, p.Lifetime.Get(Stat.GamesPlayed)));
    }

    [Fact]
    public void Many_games_accumulate_and_unlock_rewards()
    {
        var profile = PlayerProfile.Create(Noon);
        var now = Noon;
        var levelUps = 0;
        var unlocked = new List<Cosmetic>();

        for (var game = 0; game < 30; game++)
        {
            var sim = NewTable(500 + game, ["Ana", "Lola", "Tiburón", "Vicky"], game % 3 == 0 ? RulePreset.Spain : RulePreset.LatinAmerica, $"room{game}");
            var reports = Play(sim, profile, "ana", now);
            levelUps += reports.Count(r => r.LeveledUp);
            unlocked.AddRange(reports.SelectMany(r => r.Unlocked));
            now = now.AddHours(game % 4 == 3 ? 20 : 1);
        }

        var life = profile.Lifetime;

        Assert.Equal(30, life.Get(Stat.GamesPlayed));
        Assert.Equal(30, life.Get(Stat.GamesWon) + life.Get(Stat.GamesLost));
        Assert.Equal(210, life.Get(Stat.RoundsPlayed));
        Assert.Equal(30, profile.History.Count);
        Assert.True(profile.Level > 1);
        Assert.True(levelUps > 0);
        Assert.NotEmpty(unlocked);
        Assert.Equal(unlocked.Count, unlocked.Select(c => c.Id).Distinct().Count());
        Assert.Contains("ach.played.1", profile.Achievements);
        Assert.Contains("ach.played.2", profile.Achievements);
        Assert.True(life.Get(Stat.DaysPlayed) >= 2);
        Assert.True(life.Get(Stat.DailiesCompleted) > 0);
        Assert.Equal(life.Get(Stat.AchievementsCompleted), profile.Achievements.Count);
        Assert.Equal(7, profile.ContractRounds.Count);
        Assert.All(profile.ContractRounds, n => Assert.Equal(30, n));
        Assert.Equal(profile.TotalPoints, profile.ContractPoints.Sum());
    }

    [Fact]
    public void A_new_day_brings_new_dailies_and_resets_their_counters()
    {
        var profile = PlayerProfile.Create(Noon);

        ProgressEngine.Roll(profile, Noon);
        var first = profile.Daily.Missions.ToList();
        profile.Daily.Stats.Add(Stat.GamesPlayed, 3);

        ProgressEngine.Roll(profile, Noon.AddHours(1));
        Assert.Equal(first, profile.Daily.Missions);
        Assert.Equal(3, profile.Daily.Stats.Get(Stat.GamesPlayed));

        var tomorrow = Noon.AddDays(1);
        ProgressEngine.Roll(profile, tomorrow);

        Assert.Equal(ProgressEngine.DayKey(tomorrow), profile.Daily.Key);
        Assert.Equal(0, profile.Daily.Stats.Get(Stat.GamesPlayed));
        Assert.Equal(3, profile.Daily.Missions.Count);
        Assert.Equal(3, profile.Weekly.Missions.Count);
    }

    [Fact]
    public void A_daily_can_be_swapped_once()
    {
        var profile = PlayerProfile.Create(Noon);
        ProgressEngine.Roll(profile, Noon);

        var target = profile.Daily.Missions[1];
        var report = ProgressEngine.Reroll(profile, target, Noon);

        Assert.NotNull(report);
        Assert.DoesNotContain(target, profile.Daily.Missions);
        Assert.Equal(3, profile.Daily.Missions.Distinct().Count());
        Assert.Equal(MissionCatalog.Find(target)!.Difficulty, MissionCatalog.Find(profile.Daily.Missions[1])!.Difficulty);

        Assert.Null(ProgressEngine.Reroll(profile, profile.Daily.Missions[0], Noon));
    }

    [Fact]
    public void Completing_all_three_dailies_pays_the_bonus_once()
    {
        var profile = PlayerProfile.Create(Noon);
        ProgressEngine.Roll(profile, Noon);

        foreach (var id in profile.Daily.Missions)
        {
            var mission = MissionCatalog.Find(id)!;
            profile.Daily.Stats.Raise(mission.Stat, mission.Target);
        }

        var report = ProgressEngine.Reroll(profile, "nope", Noon);
        Assert.Null(report);

        var sim = NewTable(7, ["Ana", "Beto"]);
        Assert.True(sim.Engine.StartGame().Ok);

        var first = ProgressEngine.Observe(profile, PlayerView.For(sim.State, "ana"), Noon);

        Assert.Equal(3, first.Missions.Count(m => m.Scope == MissionScope.Daily));
        Assert.True(first.DailyBonus);
        Assert.Equal(1, profile.Lifetime.Get(Stat.DailySetsCompleted));

        var second = ProgressEngine.Observe(profile, PlayerView.For(sim.State, "ana"), Noon);
        Assert.False(second.DailyBonus);
        Assert.DoesNotContain(second.Missions, m => m.Scope == MissionScope.Daily);
    }

    [Fact]
    public void The_profile_survives_a_round_trip_to_json()
    {
        var profile = PlayerProfile.Create(Noon);
        var sim = NewTable(31, ["Ana", "Pepa", "Rubén"]);
        Play(sim, profile, "ana", Noon);
        profile.Equipped[CosmeticKind.Avatar] = "avatar.comodin";

        var copy = PlayerProfile.FromJson(profile.ToJson());

        Assert.NotNull(copy);
        Assert.Equal(profile.Xp, copy!.Xp);
        Assert.Equal(profile.Lifetime.Values, copy.Lifetime.Values);
        Assert.Equal(profile.Achievements, copy.Achievements);
        Assert.Equal(profile.Daily.Missions, copy.Daily.Missions);
        Assert.Equal(profile.History.Count, copy.History.Count);
        Assert.Equal(profile.Rivals.Keys.OrderBy(k => k), copy.Rivals.Keys.OrderBy(k => k));
        Assert.Equal("avatar.comodin", copy.Equipped[CosmeticKind.Avatar]);
        Assert.Null(PlayerProfile.FromJson("{ roto"));
    }

    [Fact]
    public void Locked_cosmetics_cannot_be_worn()
    {
        var profile = PlayerProfile.Create(Noon);
        profile.Equipped[CosmeticKind.CardBack] = "back.inmortal";

        Assert.Equal("back.clasico", profile.Current(CosmeticKind.CardBack).Id);

        profile.Xp = Leveling.TotalFor(100);
        Assert.Equal("back.inmortal", profile.Current(CosmeticKind.CardBack).Id);
    }

    [Fact]
    public void Voices_beaten_are_counted_from_the_bot_names()
    {
        var view = new PlayerView
        {
            RoomId = "r",
            RoomName = "r",
            YouId = "me",
            Options = GameOptions.ForPreset(RulePreset.LatinAmerica),
            Phase = GamePhase.GameOver,
            GameNumber = 1,
            Tally = new MatchTally(),
            Players =
            [
                new PlayerSummary("me", "Yo", 0, 0, false, true, true, true, false, 40, [10, 10, 10, 10, 0, 0, 0]),
                new PlayerSummary("b1", "Lola", 1, 0, true, false, true, true, false, 90, [20, 20, 10, 10, 10, 10, 10]),
                new PlayerSummary("b2", "Chelo", 2, 0, true, false, true, true, false, 20, [0, 0, 0, 0, 10, 10, 0])
            ]
        };

        var record = GameRecord.From(view, Noon, Noon.AddMinutes(10))!;
        var c = record.Contributions();

        Assert.False(record.Won);
        Assert.Equal(2, record.Position);
        Assert.Equal(1, c.GetValueOrDefault(Stat.BeatAbuela));
        Assert.Equal(0, c.GetValueOrDefault(Stat.BeatCunado));
        Assert.Equal(0, c.GetValueOrDefault(Stat.LostToAbuela));
        Assert.Equal(1, c.GetValueOrDefault(Stat.Podiums));
    }
}

public class TallyTests
{
    private static int _id = 5000;

    private static Card C(Suit suit, Rank rank) => new(_id++, suit, rank);

    [Fact]
    public void Stealing_is_counted_for_the_thief()
    {
        var state = new GameState { RoomId = "t", RoomName = "T", Options = GameOptions.ForPreset(RulePreset.LatinAmerica) };
        var engine = new GameEngine(state, new Random(3));
        engine.AddPlayer("a", "Ana", false, true);
        engine.AddPlayer("b", "Beto", false);
        engine.AddPlayer("c", "Caro", false);
        Assert.True(engine.StartGame().Ok);

        var current = state.Current!;
        Assert.True(engine.Draw(current.Id, DrawSource.Stock).Ok);
        Assert.Equal(GamePhase.StealWindow, state.Phase);

        var thief = state.Players.First(p => p.Id != current.Id && p.Id != state.Steal!.DiscarderId);
        Assert.True(engine.ClaimSteal(thief.Id).Ok);

        Assert.Equal(1, thief.Tally.Steals);
        Assert.Equal(1, current.Tally.Turns);
        Assert.Equal(0, thief.Tally.Turns);
    }

    [Fact]
    public void Closing_on_the_first_turn_with_a_lay_down_is_tracked()
    {
        var state = new GameState { RoomId = "t", RoomName = "T", Options = GameOptions.ForPreset(RulePreset.LatinAmerica) };
        var engine = new GameEngine(state, new Random(3));
        engine.AddPlayer("a", "Ana", false, true);
        engine.AddPlayer("b", "Beto", false);
        Assert.True(engine.StartGame().Ok);

        var me = state.Current!;
        me.Hand.Clear();
        me.Hand.AddRange([
            C(Suit.Clubs, Rank.King), C(Suit.Hearts, Rank.King), C(Suit.Spades, Rank.King),
            C(Suit.Clubs, Rank.Five), C(Suit.Hearts, Rank.Five), C(Suit.Spades, Rank.Five)
        ]);

        Assert.True(engine.Draw(me.Id, DrawSource.Discard).Ok);

        var specs = new List<MeldSpec>
        {
            new(MeldKind.Trio, me.Hand.Where(c => c.Rank == Rank.King).Select(c => c.Id).ToList()),
            new(MeldKind.Trio, me.Hand.Where(c => c.Rank == Rank.Five).Select(c => c.Id).ToList())
        };

        Assert.True(engine.LayDown(me.Id, specs).Ok);
        Assert.Single(me.Hand);
        Assert.True(engine.Discard(me.Id, me.Hand[0].Id).Ok);

        Assert.Equal(GamePhase.RoundEnd, state.Phase);
        Assert.Equal([0], me.Tally.ClosedRounds);
        Assert.Equal([0], me.Tally.SameTurnCloseRounds);
        Assert.Equal([0], me.Tally.FirstTurnCloseRounds);
        Assert.Equal([0], me.Tally.LaidDownRounds);
        Assert.Equal(2, me.Tally.TriosLaid);
        Assert.Equal(1, me.Tally.TookDiscard);
    }

    [Fact]
    public void A_new_game_starts_a_fresh_tally_and_a_new_game_number()
    {
        var state = new GameState { RoomId = "t", RoomName = "T" };
        var engine = new GameEngine(state, new Random(3));
        engine.AddPlayer("a", "Ana", false, true);
        engine.AddPlayer("b", "Beto", false);

        Assert.True(engine.StartGame().Ok);
        Assert.Equal(1, state.GameNumber);
        state.Players[0].Tally.Steals = 4;

        state.Phase = GamePhase.GameOver;
        Assert.True(engine.PlayAgain().Ok);
        Assert.True(engine.StartGame().Ok);

        Assert.Equal(2, state.GameNumber);
        Assert.Equal(0, state.Players[0].Tally.Steals);
    }

    [Fact]
    public void Badges_are_sanitised_and_shown_to_everyone()
    {
        var state = new GameState { RoomId = "t", RoomName = "T" };
        var engine = new GameEngine(state, new Random(3));
        engine.AddPlayer("a", "Ana", false, true);
        engine.AddPlayer("b", "Beto", false);

        Assert.True(engine.SetBadge("a", new PlayerBadge(5000, "🦊", new string('x', 80))).Ok);

        var seen = PlayerView.For(state, "b").Players.First(p => p.Id == "a").Badge!;

        Assert.Equal(999, seen.Level);
        Assert.Equal("🦊", seen.Avatar);
        Assert.Equal(PlayerBadge.MaxTitleLength, seen.Title!.Length);
        Assert.Null(PlayerView.For(state, "a").Players.First(p => p.Id == "b").Badge);
    }

    [Fact]
    public void The_tally_and_badge_travel_over_the_wire()
    {
        var state = new GameState { RoomId = "t", RoomName = "T" };
        var engine = new GameEngine(state, new Random(3));
        engine.AddPlayer("a", "Ana", false, true);
        engine.AddPlayer("b", "Beto", false);
        engine.SetBadge("a", new PlayerBadge(7, "🐙", "Experto"));
        Assert.True(engine.StartGame().Ok);
        state.Players[0].Tally.Steals = 2;
        state.Players[0].Tally.ClosedRounds.Add(0);

        var json = Wire.Serialize(new ServerMessage { Type = MessageType.State, View = PlayerView.For(state, "a") });
        var back = Wire.ReadServer(json)!.View!;

        Assert.Equal(2, back.Tally!.Steals);
        Assert.Equal([0], back.Tally.ClosedRounds);
        Assert.Equal(1, back.GameNumber);
        Assert.Equal(new PlayerBadge(7, "🐙", "Experto"), back.Players.First(p => p.Id == "a").Badge);

        var badge = Wire.ReadClient(Wire.Serialize(new ClientMessage { Type = MessageType.Badge, Badge = new PlayerBadge(3, null, "X") }))!;
        Assert.Equal(new PlayerBadge(3, null, "X"), badge.Badge);
    }
}
