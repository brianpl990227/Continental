using Microsoft.JSInterop;

namespace Continental.Shared.Services;

public sealed record Flight(string Target, string? FromSelector, double[]? FromRect, bool Flip);

public sealed class CardFlight(IJSRuntime js) : IAsyncDisposable
{
    public const string Stock = "#pile-stock";
    public const string Discard = "#pile-discard";
    public const string DiscardCard = "#pile-discard .card";

    private IJSObjectReference? _module;
    private bool _failed;

    public static string CardAt(int cardId) => $"[data-card-id=\"{cardId}\"]";

    public static string Seat(string playerId) => $"[data-seat=\"{playerId}\"]";

    public async Task InitAsync()
    {
        if (_module is not null || _failed)
            return;

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Continental.Shared/continental-fly.js");
        }
        catch (Exception)
        {
            _failed = true;
        }
    }

    public async Task<double[]?> CaptureAsync(string selector)
    {
        if (_module is null)
            return null;

        try
        {
            var rect = await _module.InvokeAsync<double[]?>("capture", selector);

            return rect is { Length: 4 } ? rect : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Play(IEnumerable<Flight> flights)
    {
        if (_module is null)
            return;

        foreach (var flight in flights)
            _ = PlayCoreAsync(flight);
    }

    private async Task PlayCoreAsync(Flight flight)
    {
        try
        {
            if (flight.FromSelector is { } selector)
            {
                await _module!.InvokeVoidAsync("flyFrom", flight.Target, selector, flight.Flip);
            }
            else if (flight.FromRect is { Length: 4 } r)
            {
                await _module!.InvokeVoidAsync("flyFromRect", flight.Target, r[0], r[1], r[2], r[3], flight.Flip);
            }
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
