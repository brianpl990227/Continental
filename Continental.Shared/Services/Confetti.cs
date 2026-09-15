using Microsoft.JSInterop;

namespace Continental.Shared.Services;

public sealed class Confetti(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _failed;

    public async Task InitAsync()
    {
        if (_module is not null || _failed)
            return;

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Continental.Shared/continental-confetti.js");
        }
        catch (Exception)
        {
            _failed = true;
        }
    }

    public void Burst(int count = 110)
    {
        if (_module is null)
            return;

        _ = CallAsync("burst", count);
    }

    public void Clear()
    {
        if (_module is null)
            return;

        _ = CallAsync("clear");
    }

    private async Task CallAsync(string name, params object[] args)
    {
        try
        {
            await _module!.InvokeVoidAsync(name, args);
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
