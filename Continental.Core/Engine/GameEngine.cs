using Continental.Core.Model;
using Continental.Core.Rules;

namespace Continental.Core.Engine;

public enum DrawSource
{
    Stock = 0,
    Discard = 1
}

public readonly record struct ActionResult(bool Ok, string? Error)
{
    public static readonly ActionResult Success = new(true, null);

    public static ActionResult Fail(string error) => new(false, error);
}

public sealed record MeldSpec(MeldKind Kind, IReadOnlyList<int> CardIds);

public sealed class GameEngine(GameState state, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private int _meldSequence;

    public GameState State { get; } = state;

    public event Action? Changed;

    private void Notify() => Changed?.Invoke();

    public ActionResult AddPlayer(string id, string name, bool isBot, bool isHost = false)
    {
        if (State.Phase != GamePhase.Lobby)
            return ActionResult.Fail("La partida ya ha empezado.");

        if (State.Players.Count >= 6)
            return ActionResult.Fail("La sala está llena (máximo 6 jugadores).");

        if (State.Players.Any(p => p.Id == id))
            return ActionResult.Success;

        var taken = State.Players.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unique = name.Trim();

        if (string.IsNullOrWhiteSpace(unique))
            unique = isBot ? "Bot" : "Jugador";

        var suffix = 2;
        var candidate = unique;

        while (taken.Contains(candidate))
            candidate = $"{unique} {suffix++}";

        State.Players.Add(new PlayerState
        {
            Id = id,
            Name = candidate,
            IsBot = isBot,
            IsHost = isHost,
            Seat = State.Players.Count
        });

        State.Say($"{candidate} se unió a la sala.");
        Notify();

        return ActionResult.Success;
    }

    public ActionResult RemovePlayer(string id)
    {
        var player = State.Find(id);

        if (player is null)
            return ActionResult.Success;

        if (State.Phase == GamePhase.Lobby)
        {
            State.Players.Remove(player);

            for (var i = 0; i < State.Players.Count; i++)
                State.Players[i].Seat = i;

            State.Say($"{player.Name} salió de la sala.");
        }
        else
        {

            player.IsConnected = false;
            State.Say($"{player.Name} se desconectó.");

            if (State.Current?.Id == id)
                AdvanceTurn();
        }

        Notify();
        return ActionResult.Success;
    }

    public ActionResult Reconnect(string id)
    {
        var player = State.Find(id);

        if (player is null)
            return ActionResult.Fail("Ese jugador no está en la partida.");

        player.IsConnected = true;
        State.Say($"{player.Name} volvió a conectarse.");
        Notify();

        return ActionResult.Success;
    }

    public ActionResult SetOptions(GameOptions options)
    {
        if (State.Phase != GamePhase.Lobby)
            return ActionResult.Fail("Las reglas solo pueden cambiarse antes de empezar.");

        State.Options = options;
        Notify();

        return ActionResult.Success;
    }

    public ActionResult StartGame()
    {
        if (State.Phase != GamePhase.Lobby)
            return ActionResult.Fail("La partida ya ha empezado.");

        if (State.Players.Count < 2)
            return ActionResult.Fail("Hacen falta al menos 2 jugadores.");

        State.RoundIndex = 0;
        State.DealerIndex = _random.Next(State.Players.Count);

        foreach (var player in State.Players)
        {
            player.TotalScore = 0;
            player.RoundScores.Clear();
        }

        StartRound();
        return ActionResult.Success;
    }

    private void StartRound()
    {
        State.Stock.Clear();
        State.Discard.Clear();
        State.Table.Clear();
        State.Steal = null;
        State.DiscardOwnerId = null;
        State.StockRecycles = 0;
        State.LastCloserId = null;

        var shoe = Deck.Build(State.Options);
        Deck.Shuffle(shoe, _random);
        State.Stock.AddRange(shoe);

        var count = State.Options.CardsForRound(State.RoundIndex);

        foreach (var player in State.Players)
        {
            player.Hand.Clear();
            player.HasLaidDown = false;
            player.LaidDownThisTurn = false;

            for (var i = 0; i < count; i++)
                player.Hand.Add(TakeFromStock());

            player.Hand.Sort((a, b) => a.Suit != b.Suit ? a.Suit.CompareTo(b.Suit) : a.Rank.CompareTo(b.Rank));
        }

        State.Discard.Add(TakeFromStock());

        State.CurrentPlayerIndex = (State.DealerIndex + 1) % State.Players.Count;
        State.Phase = GamePhase.Draw;

        State.Say($"Ronda {State.RoundIndex + 1} de {State.TotalRounds}: {State.Contract.Describe()} · {count} cartas.");
        Notify();
    }

    private Card TakeFromStock()
    {
        if (State.Stock.Count == 0)
            ReplenishStock();

        var card = State.Stock[^1];
        State.Stock.RemoveAt(State.Stock.Count - 1);

        return card;
    }

    private void ReplenishStock()
    {
        if (State.Discard.Count <= 1)
        {

            var extra = Deck.Build(State.Options);
            Deck.Shuffle(extra, _random);
            State.Stock.AddRange(extra);
            State.Say("El mazo se agotó; se añade una baraja nueva.");
            return;
        }

        var top = State.Discard[^1];
        var recycled = State.Discard.Take(State.Discard.Count - 1).ToList();

        State.Discard.Clear();
        State.Discard.Add(top);

        Deck.Shuffle(recycled, _random);
        State.Stock.AddRange(recycled);
        State.StockRecycles++;

        State.Say("Se barajó el pozo para rehacer el mazo.");
    }

    public ActionResult Draw(string playerId, DrawSource source)
    {
        if (State.Phase != GamePhase.Draw)
            return ActionResult.Fail("No toca robar ahora.");

        if (State.Current?.Id != playerId)
            return ActionResult.Fail("No es tu turno.");

        var player = State.Current;

        if (source == DrawSource.Discard)
        {
            if (State.DiscardTop is not { } top)
                return ActionResult.Fail("El pozo está vacío.");

            State.Discard.RemoveAt(State.Discard.Count - 1);
            player.Hand.Add(top);
            State.DiscardOwnerId = null;
            State.Phase = GamePhase.Action;
            State.Say($"{player.Name} tomó {top.Label} del pozo.");
            Notify();

            return ActionResult.Success;
        }

        if (ShouldOpenStealWindow(player))
        {
            State.Steal = new StealOffer
            {
                Card = State.DiscardTop!.Value,
                BlockedPlayerId = player.Id,
                DiscarderId = State.DiscardOwnerId,
                Deadline = DateTimeOffset.UtcNow.AddSeconds(State.Options.StealWindowSeconds)
            };

            State.Phase = GamePhase.StealWindow;
            State.Say($"{State.Steal.Card.Label} queda libre: ¿alguien roba de contra?");
            Notify();

            return ActionResult.Success;
        }

        CompleteStockDraw(player);
        return ActionResult.Success;
    }

    private bool ShouldOpenStealWindow(PlayerState drawer)
    {
        if (!State.Options.AllowSteal || State.DiscardTop is null)
            return false;

        return State.Players.Any(p => IsEligibleToSteal(p, drawer.Id));
    }

    private bool IsEligibleToSteal(PlayerState player, string blockedId)
        => player.Id != blockedId
           && player.Id != State.DiscardOwnerId
           && player.IsConnected;

    private void CompleteStockDraw(PlayerState player)
    {
        var card = TakeFromStock();
        player.Hand.Add(card);

        State.Steal = null;
        State.Phase = GamePhase.Action;
        State.Say($"{player.Name} robó del mazo.");
        Notify();
    }

    public ActionResult ClaimSteal(string playerId)
    {
        if (State.Phase != GamePhase.StealWindow || State.Steal is not { } offer)
            return ActionResult.Fail("No hay ninguna carta para robar de contra.");

        var player = State.Find(playerId);

        if (player is null)
            return ActionResult.Fail("Jugador desconocido.");

        if (!IsEligibleToSteal(player, offer.BlockedPlayerId))
            return ActionResult.Fail("No puedes robar esta carta.");

        State.Discard.RemoveAt(State.Discard.Count - 1);
        player.Hand.Add(offer.Card);

        for (var i = 0; i < State.Options.StealPenaltyCards; i++)
            player.Hand.Add(TakeFromStock());

        State.DiscardOwnerId = null;
        State.Say($"{player.Name} robó de contra {offer.Card.Label} (+{State.Options.StealPenaltyCards} de castigo).");

        var blocked = State.Find(offer.BlockedPlayerId);
        State.Steal = null;

        if (blocked is not null)
            CompleteStockDraw(blocked);
        else
            AdvanceTurn();

        return ActionResult.Success;
    }

    public ActionResult PassSteal()
    {
        if (State.Phase != GamePhase.StealWindow || State.Steal is not { } offer)
            return ActionResult.Success;

        var blocked = State.Find(offer.BlockedPlayerId);
        State.Steal = null;

        if (blocked is not null)
            CompleteStockDraw(blocked);
        else
            AdvanceTurn();

        return ActionResult.Success;
    }

    public ActionResult LayDown(string playerId, IReadOnlyList<MeldSpec> specs)
    {
        if (State.Phase != GamePhase.Action)
            return ActionResult.Fail("Solo puedes bajarte después de robar.");

        if (State.Current?.Id != playerId)
            return ActionResult.Fail("No es tu turno.");

        var player = State.Current;

        if (player.HasLaidDown)
            return ActionResult.Fail("Ya te bajaste en esta ronda.");

        var contract = State.Contract;
        var trios = specs.Count(s => s.Kind == MeldKind.Trio);
        var escaleras = specs.Count(s => s.Kind == MeldKind.Escalera);

        if (trios != contract.Trios || escaleras != contract.Escaleras)
            return ActionResult.Fail($"Esta ronda pide exactamente {contract.Describe()}.");

        var used = new HashSet<int>();

        foreach (var id in specs.SelectMany(s => s.CardIds))
        {
            if (!used.Add(id))
                return ActionResult.Fail("Has repetido una carta.");

            if (player.Hand.All(c => c.Id != id))
                return ActionResult.Fail("Esa carta no está en tu mano.");
        }

        var built = new List<Meld>();

        foreach (var spec in specs)
        {
            var cards = spec.CardIds.Select(id => player.Hand.First(c => c.Id == id)).ToList();
            var (meld, error) = MeldValidator.Build($"m{++_meldSequence}", playerId, spec.Kind, cards, State.Options);

            if (meld is null)
                return ActionResult.Fail(error ?? "Combinación no válida.");

            built.Add(meld);
        }

        player.Hand.RemoveAll(c => used.Contains(c.Id));
        State.Table.AddRange(built);

        player.HasLaidDown = true;
        player.LaidDownThisTurn = true;

        State.Say($"{player.Name} se bajó con {contract.Describe()}.");
        Notify();

        return ActionResult.Success;
    }

    public ActionResult Extend(string playerId, string meldId, int cardId, int? position = null)
    {
        if (State.Phase != GamePhase.Action)
            return ActionResult.Fail("Solo puedes colocar cartas después de robar.");

        if (State.Current?.Id != playerId)
            return ActionResult.Fail("No es tu turno.");

        var player = State.Current;

        if (!player.HasLaidDown)
            return ActionResult.Fail("Primero tienes que bajarte.");

        if (player.Hand.Count <= 1)
            return ActionResult.Fail("Guarda una carta para descartar y cerrar la ronda.");

        var meld = State.Table.FirstOrDefault(m => m.Id == meldId);

        if (meld is null)
            return ActionResult.Fail("Esa combinación no existe.");

        if (!State.Options.CanExtendOpponentMelds && meld.OwnerId != playerId)
            return ActionResult.Fail("En esta variante solo puedes ampliar tus propios juegos.");

        var index = player.Hand.FindIndex(c => c.Id == cardId);

        if (index < 0)
            return ActionResult.Fail("Esa carta no está en tu mano.");

        var card = player.Hand[index];

        var fits = MeldValidator.ExtendPositions(meld, card, State.Options);

        if (fits.Count == 0)
            return ActionResult.Fail($"{card.Label} no encaja ahí.");

        var slot = position ?? fits[0];

        if (!fits.Contains(slot))
            return ActionResult.Fail($"{card.Label} no encaja por ese lado.");

        meld.Cards.Insert(slot, card);
        player.Hand.RemoveAt(index);

        if (meld.Kind == MeldKind.Trio)
            meld.TrioRank ??= card.IsJoker ? null : card.Rank;
        else if (!card.IsJoker)
            meld.EscaleraSuit ??= card.Suit;

        State.Say($"{player.Name} colocó {card.Label}.");
        Notify();

        return ActionResult.Success;
    }

    // Canje del comodín: entregas la carta natural que estaba tapando, te llevas el
    // comodín y lo colocas en el acto. No se puede guardar en la mano.
    public ActionResult SwapJoker(string playerId, string meldId, int cardId, string? targetMeldId, int? position)
    {
        if (State.Options.JokerSwapMode == JokerSwap.Off)
            return ActionResult.Fail("En esta variante el comodín se queda donde está.");

        if (State.Phase != GamePhase.Action)
            return ActionResult.Fail("Solo puedes canjear el comodín después de robar.");

        if (State.Current?.Id != playerId)
            return ActionResult.Fail("No es tu turno.");

        var player = State.Current;

        if (!player.HasLaidDown)
            return ActionResult.Fail("Primero tienes que bajarte.");

        var source = State.Table.FirstOrDefault(m => m.Id == meldId);

        if (source is null)
            return ActionResult.Fail("Esa combinación no existe.");

        if (source.Kind != MeldKind.Escalera)
            return ActionResult.Fail("Solo se canjea el comodín de una escalera.");

        if (State.Options.JokerSwapMode == JokerSwap.OwnerOnly && source.OwnerId != playerId)
            return ActionResult.Fail("En esta variante solo el dueño de la escalera puede canjear su comodín.");

        var handIndex = player.Hand.FindIndex(c => c.Id == cardId);

        if (handIndex < 0)
            return ActionResult.Fail("Esa carta no está en tu mano.");

        var replacement = player.Hand[handIndex];

        if (replacement.IsJoker)
            return ActionResult.Fail("No puedes canjear un comodín por otro comodín.");

        var jokerIndex = -1;

        for (var i = 0; i < source.Size; i++)
        {
            if (MeldValidator.JokerStandsFor(source, i, State.Options) is { } stands
                && stands.Rank == replacement.Rank && stands.Suit == replacement.Suit)
            {
                jokerIndex = i;
                break;
            }
        }

        if (jokerIndex < 0)
            return ActionResult.Fail($"Ningún comodín de esa escalera está haciendo de {replacement.Label}.");

        var joker = source.Cards[jokerIndex];

        // El comodín liberado tiene que caber en el sitio elegido antes de mover nada.
        var target = targetMeldId is null
            ? source
            : State.Table.FirstOrDefault(m => m.Id == targetMeldId);

        if (target is null)
            return ActionResult.Fail("Esa combinación no existe.");

        if (target.OwnerId != playerId && !State.Options.CanExtendOpponentMelds)
            return ActionResult.Fail("El comodín solo puede ir a uno de tus juegos.");

        var probe = target.Clone();

        if (ReferenceEquals(target, source))
            probe.Cards[jokerIndex] = replacement;

        var fits = MeldValidator.ExtendPositions(probe, joker, State.Options);

        if (fits.Count == 0)
            return ActionResult.Fail("El comodín no cabe ahí sin romper la escalera.");

        var slot = position ?? fits[0];

        if (!fits.Contains(slot))
            return ActionResult.Fail("El comodín no cabe por ese lado.");

        source.Cards[jokerIndex] = replacement;
        source.EscaleraSuit ??= replacement.Suit;

        target.Cards.Insert(slot, joker);

        player.Hand.RemoveAt(handIndex);

        State.Say(ReferenceEquals(target, source)
            ? $"{player.Name} canjeó el comodín por {replacement.Label}."
            : $"{player.Name} canjeó el comodín por {replacement.Label} y lo movió de sitio.");

        Notify();

        return ActionResult.Success;
    }

    public ActionResult Discard(string playerId, int cardId)
    {
        if (State.Phase != GamePhase.Action)
            return ActionResult.Fail("No toca descartar ahora.");

        if (State.Current?.Id != playerId)
            return ActionResult.Fail("No es tu turno.");

        var player = State.Current;
        var index = player.Hand.FindIndex(c => c.Id == cardId);

        if (index < 0)
            return ActionResult.Fail("Esa carta no está en tu mano.");

        var card = player.Hand[index];
        player.Hand.RemoveAt(index);
        State.Discard.Add(card);
        State.DiscardOwnerId = playerId;

        State.Say($"{player.Name} descartó {card.Label}.");

        if (player.Hand.Count == 0 && player.HasLaidDown)
        {
            EndRound(player);
            return ActionResult.Success;
        }

        player.LaidDownThisTurn = false;
        AdvanceTurn();

        return ActionResult.Success;
    }

    private void AdvanceTurn()
    {
        if (State.Phase is GamePhase.RoundEnd or GamePhase.GameOver)
            return;

        if (State.StockRecycles > State.Options.MaxStockRecycles)
        {
            EndRound(closer: null);
            return;
        }

        var guard = 0;

        do
        {
            State.CurrentPlayerIndex = (State.CurrentPlayerIndex + 1) % State.Players.Count;
            guard++;
        }
        while (!State.Players[State.CurrentPlayerIndex].IsConnected && guard <= State.Players.Count);

        State.Current!.LaidDownThisTurn = false;
        State.Phase = GamePhase.Draw;
        Notify();
    }

    private void EndRound(PlayerState? closer)
    {
        State.LastCloserId = closer?.Id;

        var bonus = closer?.LaidDownThisTurn == true ? State.Options.CloseSameTurnBonus : 0;

        foreach (var player in State.Players)
        {
            var points = player.Id == closer?.Id
                ? bonus
                : player.Hand.Sum(State.Options.ValueOf);

            player.RoundScores.Add(points);
            player.TotalScore += points;
        }

        State.Say(closer is null
            ? "Ronda en tablas: nadie logró bajarse. Todos cuentan su mano."
            : bonus < 0
                ? $"¡{closer.Name} se bajó y cerró en la misma jugada! ({bonus} pts)"
                : $"{closer.Name} cerró la ronda.");

        State.Phase = GamePhase.RoundEnd;
        Notify();
    }

    public ActionResult NextRound()
    {
        if (State.Phase != GamePhase.RoundEnd)
            return ActionResult.Fail("La ronda no ha terminado.");

        State.RoundIndex++;

        if (State.RoundIndex >= State.TotalRounds)
        {
            State.Phase = GamePhase.GameOver;

            var winner = State.Players.MinBy(p => p.TotalScore);
            State.Say($"Fin de la partida. Gana {winner?.Name} con {winner?.TotalScore} puntos.");
            Notify();

            return ActionResult.Success;
        }

        State.DealerIndex = (State.DealerIndex + 1) % State.Players.Count;
        StartRound();

        return ActionResult.Success;
    }

    public ActionResult PlayAgain()
    {
        if (State.Phase != GamePhase.GameOver)
            return ActionResult.Fail("La partida sigue en curso.");

        State.Phase = GamePhase.Lobby;
        State.RoundIndex = 0;
        State.Table.Clear();
        State.Stock.Clear();
        State.Discard.Clear();

        foreach (var player in State.Players)
        {
            player.Hand.Clear();
            player.TotalScore = 0;
            player.RoundScores.Clear();
            player.HasLaidDown = false;
        }

        State.Say("Nueva partida lista.");
        Notify();

        return ActionResult.Success;
    }

    public bool Tick()
    {
        if (State.Phase == GamePhase.StealWindow && State.Steal is { } offer
            && DateTimeOffset.UtcNow >= offer.Deadline)
        {
            PassSteal();
            return true;
        }

        return false;
    }
}
