using Microsoft.JSInterop;

namespace Continental.Shared.Services;

public sealed record Flight(string Target, string? FromSelector, double[]? FromRect, bool Flip, int DelayMs = 0);

public sealed record SeatFlight(string Pile, string? Look, string Seat, bool FaceUp, int DelayMs = 0);

public sealed class CardFlight(IJSRuntime js) : IAsyncDisposable
{
    public const string Stock = "#pile-stock";
    public const string Discard = "#pile-discard";
    public const string DiscardCard = "#pile-discard .card";
    public const string Hand = ".hand__rail";
    public const string StockCard = "#pile-stock .card";
    public const string TakenCard = "#taken-card .card";

    private IJSObjectReference? _module;
    private bool _failed;

    public static string CardAt(int cardId) => $"[data-card-id=\"{cardId}\"]";

    public static string Seat(string playerId) => $"[data-seat=\"{playerId}\"]";

    public static string MeldAt(string meldId) => $"[data-meld-id=\"{meldId}\"]";

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

    public async Task<Dictionary<int, double[]>> CaptureCardsAsync(IReadOnlyList<int> cardIds)
    {
        var rects = new Dictionary<int, double[]>();

        if (_module is null || cardIds.Count == 0)
            return rects;

        try
        {
            var flat = await _module.InvokeAsync<double[]?>("captureMany", string.Join('|', cardIds.Select(CardAt)));

            if (flat is null || flat.Length != cardIds.Count * 4)
                return rects;

            for (var i = 0; i < cardIds.Count; i++)
            {
                var rect = flat[(i * 4)..(i * 4 + 4)];

                if (rect[2] > 0)
                    rects[cardIds[i]] = rect;
            }
        }
        catch (Exception)
        {

        }

        return rects;
    }

    public void Play(IEnumerable<Flight> flights)
    {
        if (_module is null)
            return;

        foreach (var flight in flights)
            _ = PlayCoreAsync(flight);
    }

    public void Play(IEnumerable<SeatFlight> flights)
    {
        if (_module is null)
            return;

        foreach (var flight in flights)
            _ = PlayToSeatAsync(flight);
    }

    private async Task PlayToSeatAsync(SeatFlight flight)
    {
        try
        {
            await _module!.InvokeVoidAsync("flyToSeat", flight.Pile, flight.Look, flight.Seat, flight.FaceUp, flight.DelayMs);
        }
        catch (Exception)
        {

        }
    }

    private async Task PlayCoreAsync(Flight flight)
    {
        try
        {
            if (flight.FromSelector is { } selector)
            {
                await _module!.InvokeVoidAsync("flyFrom", flight.Target, selector, flight.Flip, flight.DelayMs);
            }
            else if (flight.FromRect is { Length: 4 } r)
            {
                await _module!.InvokeVoidAsync("flyFromRect", flight.Target, r[0], r[1], r[2], r[3], flight.Flip, flight.DelayMs);
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
