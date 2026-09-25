using System.Text.Json;
using System.Text.Json.Serialization;
using Continental.Core.Rules;

namespace Continental.Core.Progress;

public sealed class PeriodState
{
    public string Key { get; set; } = "";

    public StatBook Stats { get; set; } = new();

    public List<string> Missions { get; set; } = [];

    public List<string> Completed { get; set; } = [];

    public bool BonusClaimed { get; set; }

    public int Rerolls { get; set; }
}

public sealed class ActiveGame
{
    public string Key { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public Dictionary<Stat, int> Committed { get; set; } = [];
}

public sealed class HistoryEntry
{
    public DateTimeOffset At { get; set; }

    public int Score { get; set; }

    public int Position { get; set; }

    public int Players { get; set; }

    public bool Won { get; set; }

    public int Closed { get; set; }

    public int Minutes { get; set; }

    public int Xp { get; set; }

    public RulePreset Preset { get; set; }

    public List<int> Rounds { get; set; } = [];
}

public sealed class RivalRecord
{
    public string Name { get; set; } = "";

    public bool IsBot { get; set; }

    public int Games { get; set; }

    public int Ahead { get; set; }

    public int Behind { get; set; }

    public DateTimeOffset LastSeen { get; set; }
}

public sealed class PlayerProfile
{
    public const int CurrentVersion = 2;
    public const int HistoryLimit = 40;
    public const int RivalLimit = 40;

    public int Version { get; set; } = CurrentVersion;

    public DateTimeOffset CreatedAt { get; set; }

    public int Xp { get; set; }

    public StatBook Lifetime { get; set; } = new();

    public PeriodState Daily { get; set; } = new();

    public PeriodState Weekly { get; set; } = new();

    public List<string> Achievements { get; set; } = [];

    public Dictionary<CosmeticKind, string> Equipped { get; set; } = [];

    public List<string> Seen { get; set; } = [];

    public List<string> Unseen { get; set; } = [];

    public int WinStreak { get; set; }

    public int LossStreak { get; set; }

    public int DayStreak { get; set; }

    public string? LastPlayedDay { get; set; }

    public int? BestScore { get; set; }

    public int? WorstScore { get; set; }

    public int TotalPoints { get; set; }

    public List<int> ContractPoints { get; set; } = [];

    public List<int> ContractRounds { get; set; } = [];

    public List<HistoryEntry> History { get; set; } = [];

    public Dictionary<string, RivalRecord> Rivals { get; set; } = [];

    public List<string> Recorded { get; set; } = [];

    public ActiveGame? Active { get; set; }

    public int Level => Leveling.LevelFor(Xp);

    public bool HasCompleted(string missionId) => Achievements.Contains(missionId);

    public bool Owns(Cosmetic cosmetic)
        => cosmetic.Mission is { } mission ? HasCompleted(mission) : cosmetic.Level <= Level;

    public Cosmetic Current(CosmeticKind kind)
        => Equipped.TryGetValue(kind, out var id) && Cosmetics.Find(id) is { } c && c.Kind == kind && Owns(c)
            ? c
            : Cosmetics.DefaultOf(kind);

    public static PlayerProfile Create(DateTimeOffset now) => new() { CreatedAt = now };

    public string ToJson() => JsonSerializer.Serialize(this, ProfileJsonContext.Default.PlayerProfile);

    public static PlayerProfile? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, ProfileJsonContext.Default.PlayerProfile);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlayerProfile))]
public partial class ProfileJsonContext : JsonSerializerContext;
