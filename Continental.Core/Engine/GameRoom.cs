using Continental.Core.Bots;
using Continental.Core.Protocol;
using Continental.Core.Rules;

namespace Continental.Core.Engine;

public sealed class GameRoom : IDisposable
{
    private readonly GameEngine _engine;
    private readonly Random _random = new();
    private readonly Dictionary<string, BotLevel> _botLevels = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private DateTimeOffset _botReadyAt = DateTimeOffset.MinValue;
    private int _botCounter;

    public GameRoom(string roomId, string roomName, GameOptions options)
    {
        State = new GameState { RoomId = roomId, RoomName = roomName, Options = options };
        _engine = new GameEngine(State, _random);
        _engine.Changed += () => StateChanged?.Invoke();
    }

    public GameState State { get; }

    public event Action? StateChanged;

    public PlayerView ViewFor(string playerId) => PlayerView.For(State, playerId);

    public async Task<Dictionary<string, PlayerView>> SnapshotAsync(IEnumerable<string> playerIds)
    {
        await _gate.WaitAsync();

        try
        {
            var views = new Dictionary<string, PlayerView>();

            foreach (var id in playerIds)
                views[id] = PlayerView.For(State, id);

            return views;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ActionResult AddHumanPlayer(string id, string name, bool isHost)
        => _engine.AddPlayer(id, name, isBot: false, isHost);

    public async Task<string?> HandleAsync(string playerId, ClientMessage message)
    {
        await _gate.WaitAsync();

        try
        {
            return Apply(playerId, message).Error;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ActionResult Apply(string playerId, ClientMessage m)
    {
        var isHost = State.Find(playerId)?.IsHost ?? false;

        switch (m.Type)
        {
            case MessageType.SetOptions:
                if (!isHost)
                    return ActionResult.Fail("Solo el anfitrión cambia las reglas.");

                return m.Options is null
                    ? ActionResult.Fail("Faltan las reglas.")
                    : _engine.SetOptions(m.Options);

            case MessageType.AddBot:
            {
                if (!isHost)
                    return ActionResult.Fail("Solo el anfitrión añade bots.");

                var level = (BotLevel)(m.BotLevel ?? (int)BotLevel.Normal);
                var id = $"bot-{++_botCounter}";
                var name = m.Name is { Length: > 0 } ? m.Name : BotName(level);
                var result = _engine.AddPlayer(id, name, isBot: true);

                if (result.Ok)
                    _botLevels[id] = level;

                return result;
            }

            case MessageType.Kick:
                if (!isHost)
                    return ActionResult.Fail("Solo el anfitrión puede expulsar.");

                return m.PlayerId is null
                    ? ActionResult.Fail("Falta el jugador.")
                    : _engine.RemovePlayer(m.PlayerId);

            case MessageType.Start:
                if (!isHost)
                    return ActionResult.Fail("Solo el anfitrión empieza la partida.");

                return _engine.StartGame();

            case MessageType.Draw:
                return _engine.Draw(playerId, (DrawSource)(m.Source ?? 0));

            case MessageType.ClaimSteal:
                return _engine.ClaimSteal(playerId);

            case MessageType.LayDown:
                if (m.Melds is null)
                    return ActionResult.Fail("No has elegido ninguna combinación.");

                return _engine.LayDown(playerId,
                    m.Melds.Select(x => new MeldSpec((MeldKind)x.Kind, x.CardIds)).ToList());

            case MessageType.Extend:
                if (m.MeldId is null || m.CardId is null)
                    return ActionResult.Fail("Falta la carta o la combinación.");

                return _engine.Extend(playerId, m.MeldId, m.CardId.Value);

            case MessageType.Discard:
                return m.CardId is null
                    ? ActionResult.Fail("Falta la carta.")
                    : _engine.Discard(playerId, m.CardId.Value);

            case MessageType.NextRound:
                return isHost ? _engine.NextRound() : ActionResult.Fail("Espera al anfitrión.");

            case MessageType.PlayAgain:
                return isHost ? _engine.PlayAgain() : ActionResult.Fail("Espera al anfitrión.");

            case MessageType.Leave:
                return _engine.RemovePlayer(playerId);

            default:
                return ActionResult.Fail($"Comando desconocido: {m.Type}");
        }
    }

    public void MarkDisconnected(string playerId) => _engine.RemovePlayer(playerId);

    public void MarkReconnected(string playerId) => _engine.Reconnect(playerId);

    public async Task RunAsync()
    {
        var token = _cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(200, token);
                await _gate.WaitAsync(token);

                try
                {
                    if (_engine.Tick())
                        continue;

                    if (State.Phase == GamePhase.StealWindow)
                    {
                        TryBotSteal();
                        continue;
                    }

                    PlayBotTurn();
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {

        }
    }

    private void TryBotSteal()
    {
        if (State.Steal is not { } offer)
            return;

        foreach (var bot in State.Players.Where(p => p.IsBot && p.Id != offer.BlockedPlayerId))
        {
            if (bot.Id == offer.DiscarderId)
                continue;

            var level = _botLevels.GetValueOrDefault(bot.Id, BotLevel.Normal);

            if (BotBrain.WantsSteal(State, bot, level, _random))
            {
                _engine.ClaimSteal(bot.Id);
                return;
            }
        }
    }

    private void PlayBotTurn()
    {
        if (State.Current is not { IsBot: true } bot)
            return;

        if (DateTimeOffset.UtcNow < _botReadyAt)
            return;

        var level = _botLevels.GetValueOrDefault(bot.Id, BotLevel.Normal);

        switch (State.Phase)
        {
            case GamePhase.Draw:
                _engine.Draw(bot.Id, BotBrain.ChooseDraw(State, bot, level, _random));
                Pause(600, 1100);
                break;

            case GamePhase.Action:
            {
                if (BotBrain.TryLayDown(State, bot) is { } specs)
                {
                    _engine.LayDown(bot.Id, specs);
                    Pause(700, 1200);
                    return;
                }

                foreach (var (meldId, cardId) in BotBrain.FindPlacements(State, bot))
                {
                    if (_engine.Extend(bot.Id, meldId, cardId).Ok)
                    {
                        Pause(500, 900);
                        return;
                    }
                }

                var discard = BotBrain.ChooseDiscard(State, bot, level, _random);

                if (discard >= 0)
                    _engine.Discard(bot.Id, discard);

                Pause(600, 1100);
                break;
            }
        }
    }

    private void Pause(int minMs, int maxMs)
        => _botReadyAt = DateTimeOffset.UtcNow.AddMilliseconds(_random.Next(minMs, maxMs));

    private static string BotName(BotLevel level)
    {
        string[] names = level switch
        {
            BotLevel.Easy => ["Nino", "Pepa", "Tito", "Lola"],
            BotLevel.Hard => ["Sombra", "Comodín", "Tiburón", "Águila"],
            _ => ["Chelo", "Marta", "Rubén", "Vicky"]
        };

        return names[Random.Shared.Next(names.Length)];
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _gate.Dispose();
    }
}
