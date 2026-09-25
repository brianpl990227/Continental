using Continental.Core.Engine;
using Continental.Core.Progress;
using Continental.Core.Protocol;
using Microsoft.JSInterop;

namespace Continental.Shared.Services;

public interface IProfileStorage
{
    bool IsSupported { get; }

    Task<string?> LoadAsync();

    Task SaveAsync(string json);
}

public sealed class NoProfileStorage : IProfileStorage
{
    public bool IsSupported => false;

    public Task<string?> LoadAsync() => Task.FromResult<string?>(null);

    public Task SaveAsync(string json) => Task.CompletedTask;
}

public enum CelebrationKind
{
    Mission,
    DailyBonus,
    LevelUp
}

public sealed record Celebration(CelebrationKind Kind, Mission? Mission = null, int Level = 0, IReadOnlyList<Cosmetic>? Unlocked = null);

public sealed class GameSummary
{
    public required string Key { get; init; }

    public int XpStart { get; init; }

    public int XpEnd { get; set; }

    public bool Finished { get; set; }

    public GameRecord? Record { get; set; }

    public List<XpLine> GameXpLines { get; set; } = [];

    public List<Mission> Missions { get; } = [];

    public bool DailyBonus { get; set; }

    public List<Cosmetic> Unlocked { get; } = [];

    public int Gained => XpEnd - XpStart;

    public int LevelStart => Leveling.LevelFor(XpStart);

    public int LevelEnd => Leveling.LevelFor(XpEnd);
}

public sealed class ProgressService(IProfileStorage storage, IJSRuntime js) : IAsyncDisposable
{
    private readonly SemaphoreSlim _loading = new(1, 1);
    private readonly List<Celebration> _celebrations = [];
    private IJSObjectReference? _look;
    private bool _loaded;
    private bool _dirty;
    private CancellationTokenSource? _saveDelay;

    public bool IsSupported => storage.IsSupported;

    public PlayerProfile Profile { get; private set; } = PlayerProfile.Create(DateTimeOffset.Now);

    public bool Loaded => _loaded;

    public GameSummary? Summary { get; private set; }

    public event Action? Changed;

    public event Action? CelebrationQueued;

    public int BadgeVersion { get; private set; }

    public LevelProgress Level => Leveling.Progress(Profile.Xp);

    public LevelRank Rank => Leveling.RankFor(Profile.Level);

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            await ApplyLookAsync();
            return;
        }

        await _loading.WaitAsync();

        try
        {
            if (_loaded)
                return;

            if (IsSupported)
            {
                try
                {
                    Profile = PlayerProfile.FromJson(await storage.LoadAsync()) ?? PlayerProfile.Create(DateTimeOffset.Now);
                }
                catch (Exception)
                {
                    Profile = PlayerProfile.Create(DateTimeOffset.Now);
                }

                if (ProgressEngine.Roll(Profile, DateTimeOffset.Now))
                    _dirty = true;
            }

            _loaded = true;
        }
        finally
        {
            _loading.Release();
        }

        await ApplyLookAsync();

        if (_dirty)
            await SaveNowAsync();

        Changed?.Invoke();
    }

    public void Refresh()
    {
        if (!IsSupported || !_loaded)
            return;

        if (ProgressEngine.Roll(Profile, DateTimeOffset.Now))
        {
            ScheduleSave(0);
            Changed?.Invoke();
        }
    }

    public ProgressReport? Observe(PlayerView view)
    {
        if (!IsSupported || !_loaded)
            return null;

        var xpBefore = Profile.Xp;
        var report = ProgressEngine.Observe(Profile, view, DateTimeOffset.Now);

        if (report.GameKey is { } key)
        {
            if (Summary?.Key != key && report.Record is not null)
                Summary = new GameSummary { Key = key, XpStart = xpBefore, XpEnd = xpBefore };

            if (Summary?.Key == key)
            {
                Summary.XpEnd = Profile.Xp;
                Summary.Missions.AddRange(report.Missions);
                Summary.Unlocked.AddRange(report.Unlocked);
                Summary.DailyBonus |= report.DailyBonus;

                if (report.GameFinished)
                {
                    Summary.Finished = true;
                    Summary.Record = report.Record;
                    Summary.GameXpLines = report.GameXpLines;
                }
            }
        }

        Announce(report);

        if (report.Changed)
            ScheduleSave(report.GameFinished || report.HasNews ? 0 : 2500);

        if (report.HasNews || report.GameFinished)
            Changed?.Invoke();

        return report;
    }

    public GameSummary? SummaryFor(PlayerView view)
        => Summary is { } s && s.Key == GameRecord.KeyOf(view) ? s : null;

    private void Announce(ProgressReport report)
    {
        var any = false;

        foreach (var mission in report.Missions
                     .OrderBy(m => m.Scope switch
                     {
                         MissionScope.Daily => 0,
                         MissionScope.Weekly => 1,
                         MissionScope.Secret => 3,
                         _ => 2
                     }))
        {
            _celebrations.Add(new Celebration(CelebrationKind.Mission, mission));
            any = true;
        }

        if (report.DailyBonus)
        {
            _celebrations.Add(new Celebration(CelebrationKind.DailyBonus));
            any = true;
        }

        if (report.LeveledUp || report.Unlocked.Count > 0)
        {
            var pending = _celebrations.FindLastIndex(c => c.Kind == CelebrationKind.LevelUp);
            var level = report.LeveledUp ? report.LevelAfter : 0;
            var unlocked = new List<Cosmetic>();

            if (pending >= 0)
            {
                level = Math.Max(level, _celebrations[pending].Level);
                unlocked.AddRange(_celebrations[pending].Unlocked ?? []);
                _celebrations.RemoveAt(pending);
            }

            unlocked.AddRange(report.Unlocked);
            _celebrations.Add(new Celebration(CelebrationKind.LevelUp, Level: level, Unlocked: unlocked));

            if (report.LeveledUp)
                BadgeVersion++;

            any = true;
        }

        if (any)
            CelebrationQueued?.Invoke();
    }

    public Celebration? NextCelebration()
    {
        if (_celebrations.Count == 0)
            return null;

        var next = _celebrations[0];
        _celebrations.RemoveAt(0);
        return next;
    }

    public Celebration? PeekCelebration() => _celebrations.Count > 0 ? _celebrations[0] : null;

    public int PendingCelebrations => _celebrations.Count;

    public PlayerBadge? Badge
    {
        get
        {
            if (!IsSupported || !_loaded)
                return null;

            var avatar = Profile.Current(CosmeticKind.Avatar).Value;
            var title = Profile.Current(CosmeticKind.Title);

            return new PlayerBadge(
                Profile.Level,
                string.IsNullOrEmpty(avatar) ? null : avatar,
                title.Id == "title.sin-titulo" ? null : title.Value);
        }
    }

    public IReadOnlyList<string> QuickPhrases
    {
        get
        {
            var phrases = new List<string>(Cosmetics.BasePhrases);

            if (IsSupported && _loaded)
            {
                foreach (var pack in Cosmetics.OfKind(CosmeticKind.Phrases).Where(Profile.Owns))
                    phrases.AddRange(pack.Value.Split('|'));
            }

            return phrases;
        }
    }

    public string VictoryStyle => IsSupported && _loaded ? Profile.Current(CosmeticKind.Victory).Value : "confeti";

    public async Task EquipAsync(Cosmetic cosmetic)
    {
        if (!IsSupported || !Profile.Owns(cosmetic) || !Cosmetics.IsEquippable(cosmetic.Kind))
            return;

        Profile.Equipped[cosmetic.Kind] = cosmetic.Id;

        if (cosmetic.Kind is CosmeticKind.Avatar or CosmeticKind.Title)
            BadgeVersion++;

        await ApplyLookAsync();
        await SaveNowAsync();
        Changed?.Invoke();
    }

    public bool IsNew(Cosmetic cosmetic)
        => IsSupported && !cosmetic.IsDefault && Profile.Owns(cosmetic) && !Profile.Seen.Contains(cosmetic.Id);

    public int NewCount => IsSupported ? Cosmetics.All.Count(IsNew) : 0;

    public async Task MarkSeenAsync(IEnumerable<Cosmetic> cosmetics)
    {
        var fresh = cosmetics.Where(IsNew).Select(c => c.Id).ToList();

        if (fresh.Count == 0)
            return;

        Profile.Seen.AddRange(fresh);
        await SaveNowAsync();
        Changed?.Invoke();
    }

    public async Task<bool> RerollAsync(string missionId)
    {
        if (!IsSupported)
            return false;

        var report = ProgressEngine.Reroll(Profile, missionId, DateTimeOffset.Now);

        if (report is null)
            return false;

        Announce(report);
        await SaveNowAsync();
        Changed?.Invoke();

        return true;
    }

    public async Task ApplyLookAsync()
    {
        try
        {
            _look ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Continental.Shared/continental-look.js");

            if (!IsSupported)
            {
                await _look.InvokeVoidAsync("apply", "clasico", "verde", "ninguna", "clasico", "confeti");
                return;
            }

            await _look.InvokeVoidAsync("apply",
                Profile.Current(CosmeticKind.CardBack).Value,
                Profile.Current(CosmeticKind.Felt).Value,
                Profile.Current(CosmeticKind.Trail).Value,
                Profile.Current(CosmeticKind.Sound).Value,
                Profile.Current(CosmeticKind.Victory).Value);
        }
        catch (Exception)
        {
        }
    }

    public async Task PreviewAsync(Cosmetic cosmetic)
    {
        try
        {
            _look ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Continental.Shared/continental-look.js");
            await _look.InvokeVoidAsync("preview", cosmetic.Kind.ToString(), cosmetic.Value);
        }
        catch (Exception)
        {
        }
    }

    private void ScheduleSave(int delayMs)
    {
        _dirty = true;
        _saveDelay?.Cancel();

        if (delayMs <= 0)
        {
            _ = SaveNowAsync();
            return;
        }

        var cts = _saveDelay = new CancellationTokenSource();
        _ = SaveLaterAsync(delayMs, cts.Token);
    }

    private async Task SaveLaterAsync(int delayMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token);
            await SaveNowAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task SaveNowAsync()
    {
        if (!IsSupported)
            return;

        _dirty = false;

        try
        {
            await storage.SaveAsync(Profile.ToJson());
        }
        catch (Exception)
        {
            _dirty = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_dirty)
            await SaveNowAsync();

        if (_look is not null)
        {
            try
            {
                await _look.DisposeAsync();
            }
            catch (Exception)
            {
            }
        }
    }
}
