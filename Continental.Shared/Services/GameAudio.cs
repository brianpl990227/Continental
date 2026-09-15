using Microsoft.JSInterop;

namespace Continental.Shared.Services;

public static class Sfx
{
    public const string Deal = "deal";
    public const string Draw = "draw";
    public const string Place = "place";
    public const string Select = "select";
    public const string Shuffle = "shuffle";
    public const string Turn = "turn";
    public const string LayDown = "laydown";
    public const string OpponentLayDown = "opponentLaydown";
    public const string Steal = "steal";
    public const string Tick = "tick";
    public const string RoundEnd = "roundEnd";
    public const string Win = "win";
    public const string Lose = "lose";
    public const string Celebrate = "celebrate";
    public const string Flop = "flop";
    public const string Error = "error";
    public const string Tap = "tap";
}

public sealed class GameAudio(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _failed;

    public bool Muted { get; private set; }

    public async Task InitAsync()
    {
        if (_module is not null || _failed)
            return;

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Continental.Shared/continental-audio.js");

            Muted = await _module.InvokeAsync<bool>("isMuted");
        }
        catch (Exception)
        {

            _failed = true;
        }
    }

    public void Play(string name)
    {
        if (_module is null || Muted)
            return;

        _ = PlayCoreAsync(name);
    }

    private async Task PlayCoreAsync(string name)
    {
        try
        {
            await _module!.InvokeVoidAsync("play", name);
        }
        catch (Exception)
        {

        }
    }

    public async Task<bool> ToggleMuteAsync()
    {
        if (_module is null)
            return Muted;

        try
        {
            Muted = await _module.InvokeAsync<bool>("setMuted", !Muted);
        }
        catch (Exception)
        {

        }

        return Muted;
    }

    public void Unlock()
    {
        if (_module is null)
            return;

        _ = UnlockCoreAsync();
    }

    private async Task UnlockCoreAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync("unlock");
        }
        catch (Exception)
        {

        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
            return;

        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception)
        {

        }
    }
}
