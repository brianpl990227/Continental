using Continental.Core.Bots;
using Continental.Core.Engine;
using Continental.Core.Model;
using Continental.Core.Rules;

namespace Continental.Core.Protocol;

public sealed record PlayerSummary(
    string Id,
    string Name,
    int Seat,
    int CardCount,
    bool IsBot,
    bool IsHost,
    bool IsConnected,
    bool HasLaidDown,
    bool IsCurrent,
    int TotalScore,
    IReadOnlyList<int> RoundScores);

public sealed record MeldView(
    string Id,
    string OwnerId,
    string OwnerName,
    MeldKind Kind,
    IReadOnlyList<Card> Cards);

public sealed record StealView(Card Card, string BlockedPlayerId, int SecondsLeft, bool YouCanClaim);

public sealed record PlayerView
{
    public required string RoomId { get; init; }

    public required string RoomName { get; init; }

    public required string YouId { get; init; }

    public required GameOptions Options { get; init; }

    public required GamePhase Phase { get; init; }

    public int RoundIndex { get; init; }

    public int TotalRounds { get; init; }

    public string ContractCode { get; init; } = "";

    public string ContractText { get; init; } = "";

    public IReadOnlyList<Card> Hand { get; init; } = [];

    public IReadOnlyList<PlayerSummary> Players { get; init; } = [];

    public IReadOnlyList<MeldView> Table { get; init; } = [];

    public Card? DiscardTop { get; init; }

    public string? DiscardOwnerId { get; init; }

    public string? RoundCloserId { get; init; }

    public int DiscardCount { get; init; }

    public int StockCount { get; init; }

    public StealView? Steal { get; init; }

    public IReadOnlyList<string> Log { get; init; } = [];

    public bool YourTurn { get; init; }

    public bool CanLayDown { get; init; }

    public bool YouAreHost { get; init; }

    public static PlayerView For(GameState state, string playerId)
    {
        var me = state.Find(playerId);
        var current = state.Current;

        var canLayDown = me is not null
                         && !me.HasLaidDown
                         && state.Phase == GamePhase.Action
                         && current?.Id == playerId
                         && HandAnalyzer.FindContract(me.Hand, state.Contract, state.Options) is not null;

        StealView? steal = null;

        if (state.Steal is { } offer)
        {
            var eligible = me is not null
                           && me.Id != offer.BlockedPlayerId
                           && me.Id != offer.DiscarderId;

            var seconds = (int)Math.Max(0, Math.Ceiling((offer.Deadline - DateTimeOffset.UtcNow).TotalSeconds));
            steal = new StealView(offer.Card, offer.BlockedPlayerId, seconds, eligible);
        }

        return new PlayerView
        {
            RoomId = state.RoomId,
            RoomName = state.RoomName,
            YouId = playerId,
            Options = state.Options,
            Phase = state.Phase,
            RoundIndex = state.RoundIndex,
            TotalRounds = state.TotalRounds,
            ContractCode = state.Contract.Code,
            ContractText = state.Contract.Describe(),
            Hand = me?.Hand.ToList() ?? [],
            Players = state.Players.Select(p => new PlayerSummary(
                p.Id, p.Name, p.Seat, p.Hand.Count, p.IsBot, p.IsHost, p.IsConnected,
                p.HasLaidDown, current?.Id == p.Id, p.TotalScore, p.RoundScores.ToList())).ToList(),
            Table = state.Table.Select(m => new MeldView(
                m.Id, m.OwnerId, state.Find(m.OwnerId)?.Name ?? "?", m.Kind, m.Cards.ToList())).ToList(),
            DiscardTop = state.DiscardTop,
            DiscardOwnerId = state.DiscardOwnerId,
            RoundCloserId = state.LastCloserId,
            DiscardCount = state.Discard.Count,
            StockCount = state.Stock.Count,
            Steal = steal,
            Log = state.Log.ToList(),
            YourTurn = current?.Id == playerId,
            CanLayDown = canLayDown,
            YouAreHost = me?.IsHost ?? false
        };
    }
}
