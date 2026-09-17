using Continental.Core.Engine;
using Continental.Core.Model;

namespace Continental.Core.Bots;

public enum Voice
{
    Cunado = 0,
    Picara = 1,
    Zen = 2,
    Dramatica = 3,
    Fanfarron = 4,
    Abuela = 5
}

public enum Cue
{
    Hello,
    RoundStart,
    ILaidDown,
    TheyLaidDown,
    IStole,
    TheyStole,
    IWasBlocked,
    IPicked,
    TheyPicked,
    IJokerSwapped,
    MyJokerTaken,
    TheyJokerSwapped,
    TheyExtendedMine,
    OneCardLeft,
    IHaveOneCard,
    IClosed,
    IClosedAgain,
    TheyClosed,
    TheyClosedAgain,
    IWasWorst,
    TheyWereWorst,
    NobodyClosed,
    Leading,
    HumanLeading,
    IWonGame,
    TheyWonGame,
    ILostGame,
    StockRecycled,
    JokerDiscarded,
    IdleHuman,
    Musing,
    ReplyGreeting,
    ReplyLaugh,
    ReplyQuestion,
    ReplyStandings,
    ReplyStandingsEarly,
    ReplyRules,
    ReplyInsult,
    ReplyLuck,
    ReplyBrag,
    ReplyFood,
    ReplyBye,
    ReplyThanks,
    ReplyCompliment,
    ReplyCallout,
    ReplyJoker,
    ReplyLove,
    ReplyHuman,
    RetortBrag,
    RetortWhine,
    RetortTease,
    RetortHello,
    RetortMusing,
    RetortNudge,
    RetortReply
}

public sealed record BanterLine(string BotId, string Text, DateTimeOffset DueAt);

public sealed class BotBanter(Random random)
{
    private static readonly TimeSpan GlobalGap = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan BotGap = TimeSpan.FromSeconds(16);
    private static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan TypingLead = TimeSpan.FromSeconds(2.2);
    private const int MaxQueued = 3;

    private readonly Dictionary<string, DateTimeOffset> _lastSpoke = new();
    private readonly Dictionary<(Voice?, Cue), List<string>> _bags = new();
    private readonly List<BanterLine> _queue = [];
    private readonly HashSet<string> _oneCardSaid = [];
    private DateTimeOffset _lastDue = DateTimeOffset.MinValue;
    private string? _nudgedTurn;
    private string? _lastCloserId;
    private int _closeStreak;

    public IReadOnlyList<BanterLine> Pending => _queue;

    public static Voice VoiceOf(string name)
    {
        var key = name.Trim().ToLowerInvariant();

        return key switch
        {
            "chelo" or "nino" => Voice.Cunado,
            "marta" or "águila" or "aguila" => Voice.Picara,
            "rubén" or "ruben" or "sombra" => Voice.Zen,
            "vicky" => Voice.Dramatica,
            "tiburón" or "tiburon" => Voice.Fanfarron,
            "lola" or "pepa" => Voice.Abuela,
            _ => (Voice)(Math.Abs(key.Aggregate(17, (h, c) => h * 31 + c)) % 6)
        };
    }

    public void React(GameState state, GameEvent e, DateTimeOffset now)
    {
        if (state.Phase == GamePhase.Lobby && e.Kind != GameEventKind.GameStarted)
            return;

        var bots = state.Players.Where(p => p.IsBot && p.IsConnected).ToList();

        if (bots.Count == 0)
            return;

        var actor = e.PlayerId is null ? null : state.Find(e.PlayerId);
        var target = e.TargetPlayerId is null ? null : state.Find(e.TargetPlayerId);

        switch (e.Kind)
        {
            case GameEventKind.GameStarted:
                _lastCloserId = null;
                _closeStreak = 0;
                Say(state, Pick(bots), Cue.Hello, now, 1.2, 2.8, chance: 1.0, retort: 0.6);
                break;

            case GameEventKind.RoundStarted:
                _oneCardSaid.Clear();

                if (state.RoundIndex > 0)
                    Say(state, Pick(bots), Cue.RoundStart, now, 1.5, 3.5, chance: 0.4, retort: 0.25);
                break;

            case GameEventKind.LaidDown when actor is not null:
                if (actor.IsBot)
                    Say(state, actor, Cue.ILaidDown, now, 0.8, 2.0, chance: 0.6, retort: 0.4);
                else
                    Say(state, Pick(bots), Cue.TheyLaidDown, now, 1.0, 2.6, chance: 0.75, retort: 0.35, subject: actor);
                break;

            case GameEventKind.Stole when actor is not null:
                if (actor.IsBot)
                    Say(state, actor, Cue.IStole, now, 0.7, 1.8, chance: 0.55, retort: 0.35, card: e.Card, other: target);
                else
                    Say(state, Pick(bots), Cue.TheyStole, now, 1.0, 2.4, chance: 0.65, retort: 0.3, subject: actor, card: e.Card, other: target);

                if (target is { IsBot: true } && target.Id != actor.Id)
                    Say(state, target, Cue.IWasBlocked, now, 2.2, 4.0, chance: 0.5, retort: 0, subject: actor, card: e.Card);
                break;

            case GameEventKind.TookDiscard when actor is not null:
                if (actor.IsBot)
                    Say(state, actor, Cue.IPicked, now, 0.5, 1.4, chance: 0.08, retort: 0.15, card: e.Card);
                else
                    Say(state, Pick(bots), Cue.TheyPicked, now, 0.8, 2.0, chance: 0.12, retort: 0.2, subject: actor, card: e.Card);
                break;

            case GameEventKind.JokerSwapped when actor is not null:
            {
                var victim = target is not null && target.Id != actor.Id ? target : null;

                if (actor.IsBot)
                    Say(state, actor, Cue.IJokerSwapped, now, 0.6, 1.6, chance: victim is null ? 0.4 : 0.85, retort: 0.4, subject: victim, card: e.Card);
                else
                    Say(state, Pick(bots, except: victim), Cue.TheyJokerSwapped, now, 1.0, 2.4, chance: victim is null ? 0.4 : 0.75, retort: 0.3, subject: actor, card: e.Card, other: victim);

                if (victim is { IsBot: true })
                    Say(state, victim, Cue.MyJokerTaken, now, 1.8, 3.6, chance: 0.9, retort: 0.3, subject: actor, card: e.Card);
                break;
            }

            case GameEventKind.Extended when actor is not null && target is { IsBot: true } && target.Id != actor.Id:
                Say(state, target, Cue.TheyExtendedMine, now, 0.9, 2.2, chance: 0.35, retort: 0.2, subject: actor, card: e.Card);
                break;

            case GameEventKind.Discarded when actor is not null:
            {
                if (e.Card is { IsJoker: true })
                {
                    Say(state, Pick(bots, except: actor), Cue.JokerDiscarded, now, 0.8, 2.0, chance: 1.0, retort: 0.6, subject: actor);
                    break;
                }

                if (actor.Hand.Count == 1 && actor.HasLaidDown)
                {
                    if (!_oneCardSaid.Add($"{state.RoundIndex}:{actor.Id}"))
                        break;

                    if (actor.IsBot)
                        Say(state, actor, Cue.IHaveOneCard, now, 0.8, 2.0, chance: 0.55, retort: 0.4);
                    else
                        Say(state, Pick(bots), Cue.OneCardLeft, now, 1.0, 2.4, chance: 0.7, retort: 0.35, subject: actor);

                    break;
                }

                Say(state, Pick(bots), Cue.Musing, now, 1.5, 4.0, chance: 0.06, retort: 0.35);
                break;
            }

            case GameEventKind.RoundEnded:
            {
                var closer = actor;

                if (closer is not null && closer.Id == _lastCloserId)
                    _closeStreak++;
                else
                    _closeStreak = 1;

                _lastCloserId = closer?.Id;

                var again = closer is not null && _closeStreak >= 2;

                if (closer is null)
                    Say(state, Pick(bots), Cue.NobodyClosed, now, 1.0, 2.5, chance: 0.9, retort: 0.3);
                else if (closer.IsBot)
                    Say(state, closer, again ? Cue.IClosedAgain : Cue.IClosed, now, 0.8, 2.0, chance: 0.9, retort: 0.5);
                else
                    Say(state, Pick(bots), again ? Cue.TheyClosedAgain : Cue.TheyClosed, now, 1.0, 2.6, chance: 0.8, retort: 0.35, subject: closer);

                var scored = state.Players.Where(p => p.RoundScores.Count > 0).ToList();

                if (scored.Count > 1)
                {
                    var worstPoints = scored.Max(p => p.RoundScores[^1]);
                    var worst = scored.Where(p => p.RoundScores[^1] == worstPoints).ToList();

                    if (worst.Count == 1 && worstPoints >= 20)
                    {
                        var loser = worst[0];

                        if (loser.IsBot)
                            Say(state, loser, Cue.IWasWorst, now, 3.5, 6.0, chance: 0.6, retort: 0.35, points: worstPoints);
                        else
                            Say(state, Pick(bots), Cue.TheyWereWorst, now, 3.5, 6.0, chance: 0.75, retort: 0.3, subject: loser, points: worstPoints);
                    }

                    if (state.RoundIndex >= 1 && state.RoundIndex < state.TotalRounds - 1)
                    {
                        var best = scored.MinBy(p => p.TotalScore)!;
                        var tied = scored.Count(p => p.TotalScore == best.TotalScore) > 1;

                        if (!tied && best.IsBot)
                            Say(state, best, Cue.Leading, now, 7.0, 10.0, chance: 0.3, retort: 0.4, points: best.TotalScore);
                        else if (!tied)
                            Say(state, Pick(bots), Cue.HumanLeading, now, 7.0, 10.0, chance: 0.35, retort: 0.3, subject: best, points: best.TotalScore);
                    }
                }

                break;
            }

            case GameEventKind.GameOver:
            {
                var winner = actor;

                if (winner is { IsBot: true })
                    Say(state, winner, Cue.IWonGame, now, 1.0, 2.2, chance: 1.0, retort: 0.7);
                else if (winner is not null)
                    Say(state, Pick(bots), Cue.TheyWonGame, now, 1.0, 2.4, chance: 1.0, retort: 0.5, subject: winner, points: winner.TotalScore);

                var loser = state.Players.OrderByDescending(p => p.TotalScore).FirstOrDefault();

                if (loser is { IsBot: true } && loser.Id != winner?.Id)
                    Say(state, loser, Cue.ILostGame, now, 4.0, 6.5, chance: 0.75, retort: 0.25, points: loser.TotalScore);
                break;
            }

            case GameEventKind.StockRecycled:
                Say(state, Pick(bots), Cue.StockRecycled, now, 1.2, 2.8, chance: 0.45, retort: 0.25);
                break;

            case GameEventKind.ChatSaid when actor is { IsBot: false } && e.Text is { } text:
                ReplyToHuman(state, bots, actor, text, now);
                break;
        }
    }

    private void ReplyToHuman(GameState state, List<PlayerState> bots, PlayerState human, string text, DateTimeOffset now)
    {
        var lower = text.ToLowerInvariant();
        var named = bots.FirstOrDefault(b => lower.Contains(b.Name.ToLowerInvariant()));
        var cue = CueForHuman(lower);
        var speaker = named ?? Pick(bots);

        if (named is not null && cue is Cue.ReplyHuman or Cue.ReplyGreeting or Cue.ReplyQuestion)
            cue = Cue.ReplyCallout;

        if (cue == Cue.ReplyStandings && state.Players.All(p => p.RoundScores.Count == 0))
            cue = Cue.ReplyStandingsEarly;

        var chance = cue is Cue.ReplyCallout or Cue.ReplyStandings or Cue.ReplyStandingsEarly or Cue.ReplyRules or Cue.ReplyInsult or Cue.ReplyBye
            ? 0.96
            : 0.82;

        Say(state, speaker, cue, now, 1.6, 4.2, chance, retort: 0.35, subject: human, bypassBotGap: true);
    }

    public void Tick(GameState state, DateTimeOffset now)
    {
        if (state.Phase is not (GamePhase.Draw or GamePhase.Action))
            return;

        if (state.Current is not { IsBot: false } human || !human.IsConnected)
            return;

        var key = $"{state.RoundIndex}:{human.Id}:{state.TurnStartedAt.Ticks}";

        if (_nudgedTurn == key || now - state.TurnStartedAt < IdleAfter)
            return;

        _nudgedTurn = key;

        var bots = state.Players.Where(p => p.IsBot && p.IsConnected).ToList();

        if (bots.Count > 0)
            Say(state, Pick(bots), Cue.IdleHuman, now, 0.3, 1.2, chance: 1.0, retort: 0.35, subject: human);
    }

    public List<BanterLine> Due(DateTimeOffset now)
    {
        var due = _queue.Where(l => l.DueAt <= now).ToList();

        foreach (var line in due)
            _queue.Remove(line);

        return due;
    }

    public IEnumerable<string> Typing(DateTimeOffset now)
        => _queue.Where(l => l.DueAt - now <= TypingLead).Select(l => l.BotId).Distinct();

    private void Say(GameState state, PlayerState? bot, Cue cue, DateTimeOffset now, double minDelay, double maxDelay,
                     double chance, double retort, PlayerState? subject = null, Card? card = null, int points = 0,
                     PlayerState? other = null, bool bypassBotGap = false)
    {
        if (bot is null || _queue.Count >= MaxQueued || random.NextDouble() > chance)
            return;

        var exempt = bypassBotGap || cue is Cue.MyJokerTaken or Cue.IdleHuman or Cue.IWonGame or Cue.TheyWonGame;

        if (!exempt && _lastSpoke.TryGetValue(bot.Id, out var spoke) && now - spoke < BotGap)
            return;

        var delay = TimeSpan.FromSeconds(minDelay + random.NextDouble() * (maxDelay - minDelay));
        var due = now + delay;

        if (due < _lastDue + GlobalGap)
            due = _lastDue + GlobalGap;

        var text = Render(bot, cue, state, subject, other, card, points);

        if (text is null)
            return;

        Enqueue(bot, text, due);

        if (retort <= 0 || random.NextDouble() >= retort)
            return;

        var replier = Pick(state.Players.Where(p => p.IsBot && p.IsConnected && p.Id != bot.Id).ToList());

        if (replier is null)
            return;

        if (_lastSpoke.TryGetValue(replier.Id, out var replierSpoke) && now - replierSpoke < BotGap / 2)
            return;

        var reply = Render(replier, RetortFor(cue), state, bot, subject, card, points);

        if (reply is not null)
            Enqueue(replier, reply, due + TimeSpan.FromSeconds(2.5 + random.NextDouble() * 3.5));
    }

    private static Cue RetortFor(Cue cue) => cue switch
    {
        Cue.Hello => Cue.RetortHello,
        Cue.IdleHuman => Cue.RetortNudge,
        Cue.Musing or Cue.RoundStart or Cue.StockRecycled or Cue.NobodyClosed => Cue.RetortMusing,
        Cue.ILaidDown or Cue.IClosed or Cue.IClosedAgain or Cue.IWonGame or Cue.IHaveOneCard or Cue.IStole
            or Cue.IJokerSwapped or Cue.IPicked or Cue.Leading => Cue.RetortBrag,
        Cue.MyJokerTaken or Cue.IWasWorst or Cue.ILostGame or Cue.IWasBlocked or Cue.TheyExtendedMine => Cue.RetortWhine,
        Cue.TheyLaidDown or Cue.TheyClosed or Cue.TheyClosedAgain or Cue.TheyWereWorst or Cue.OneCardLeft or Cue.JokerDiscarded
            or Cue.TheyStole or Cue.TheyJokerSwapped or Cue.TheyPicked or Cue.HumanLeading or Cue.TheyWonGame => Cue.RetortTease,
        _ => Cue.RetortReply
    };

    private void Enqueue(PlayerState bot, string text, DateTimeOffset due)
    {
        _queue.Add(new BanterLine(bot.Id, text, due));
        _lastSpoke[bot.Id] = due;
        _lastDue = due;
    }

    private PlayerState? Pick(IReadOnlyList<PlayerState> bots, PlayerState? except = null)
    {
        var pool = bots.Where(b => b.Id != except?.Id).ToList();

        return pool.Count == 0 ? null : pool[random.Next(pool.Count)];
    }

    private static bool Has(string t, params string[] words) => words.Any(t.Contains);

    private static Cue CueForHuman(string t)
    {
        if (Has(t, "quién va ganando", "quien va ganando", "quién gana", "quien gana", "cómo vamos", "como vamos", "puntos", "clasificación", "clasificacion", "quién va primero", "quien va primero", "ganando"))
            return Cue.ReplyStandings;

        if (Has(t, "regla", "cómo se", "como se", "se puede", "puedo", "vale", "normas", "cómo funciona", "como funciona"))
            return Cue.ReplyRules;

        if (Has(t, "trampa", "tramposo", "tramposa", "ladrón", "ladron", "rata", "tonto", "tonta", "idiota", "cállate", "callate", "pesado", "pesada", "chulo", "chula", "cabrón", "cabron", "gilipollas", "imbécil", "imbecil", "mierda"))
            return Cue.ReplyInsult;

        if (Has(t, "mala suerte", "no me sale", "no me toca", "qué mano", "que mano", "vaya mano", "menuda mano", "mi mano", "no tengo nada", "horrible", "asco", "fatal"))
            return Cue.ReplyLuck;

        if (Has(t, "voy a ganar", "os voy a", "te voy a", "gano yo", "soy el mejor", "soy la mejor", "campeón", "campeona", "invencible", "preparaos", "preparados", "temblad"))
            return Cue.ReplyBrag;

        if (Has(t, "cerveza", "café", "cafe", "pizza", "comer", "cena", "hambre", "beber", "vino", "tapas", "tortilla", "bocadillo", "merienda", "birra"))
            return Cue.ReplyFood;

        if (Has(t, "me voy", "adiós", "adios", "hasta luego", "chao", "chau", "bye", "me piro", "buenas noches", "nos vemos"))
            return Cue.ReplyBye;

        if (Has(t, "gracias", "thanks", "grax"))
            return Cue.ReplyThanks;

        if (Has(t, "bien jugado", "crack", "qué bueno", "que bueno", "buena jugada", "máquina", "maquina", "genio", "bravo", "enhorabuena", "felicidades", "👏"))
            return Cue.ReplyCompliment;

        if (Has(t, "comodín", "comodin", "joker"))
            return Cue.ReplyJoker;

        if (Has(t, "te quiero", "os quiero", "❤", "😘", "guapa", "guapo", "amor"))
            return Cue.ReplyLove;

        if (Has(t, "jaja", "jeje", "jiji", "xd", "lol", "😂", "🤣", "😆"))
            return Cue.ReplyLaugh;

        if (Has(t, "hola", "buenas", "hey", "qué tal", "que tal", "saludos", "holi", "ey "))
            return Cue.ReplyGreeting;

        if (t.Contains('?') || t.Contains('¿'))
            return Cue.ReplyQuestion;

        return Cue.ReplyHuman;
    }

    private string? Render(PlayerState bot, Cue cue, GameState state, PlayerState? subject, PlayerState? other, Card? card, int points)
    {
        var voice = VoiceOf(bot.Name);
        var key = Lines.ContainsKey((voice, cue)) ? (voice, cue) : ((Voice?)null, cue);

        if (!Lines.TryGetValue(key, out var pool) || pool.Length == 0)
            return null;

        var text = Fresh(key, pool);
        var human = state.Players.FirstOrDefault(p => !p.IsBot && p.IsConnected);
        var scored = state.Players.Where(p => p.RoundScores.Count > 0).ToList();
        var leader = scored.Count > 0 ? scored.MinBy(p => p.TotalScore) : null;
        var last = scored.Count > 0 ? scored.MaxBy(p => p.TotalScore) : null;
        var rival = state.Players.FirstOrDefault(p => p.Id != bot.Id && p.Id != subject?.Id && p.IsConnected);

        return text
            .Replace("{p}", subject?.Name ?? human?.Name ?? "tú")
            .Replace("{t}", other?.Name ?? human?.Name ?? rival?.Name ?? "alguien")
            .Replace("{r}", rival?.Name ?? "alguien")
            .Replace("{card}", card?.Label ?? "esa carta")
            .Replace("{n}", points.ToString())
            .Replace("{contract}", state.Contract.Describe())
            .Replace("{round}", (state.RoundIndex + 1).ToString())
            .Replace("{leader}", leader?.Name ?? "nadie")
            .Replace("{leaderPts}", (leader?.TotalScore ?? 0).ToString())
            .Replace("{last}", last?.Name ?? "nadie")
            .Replace("{lastPts}", (last?.TotalScore ?? 0).ToString())
            .Replace("{me}", bot.Name);
    }

    private string Fresh((Voice?, Cue) key, string[] pool)
    {
        if (!_bags.TryGetValue(key, out var bag) || bag.Count == 0)
        {
            bag = [.. pool];

            for (var i = bag.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            _bags[key] = bag;
        }

        var text = bag[^1];
        bag.RemoveAt(bag.Count - 1);

        return text;
    }

    private static readonly Dictionary<(Voice? Voice, Cue Cue), string[]> Lines = new()
    {
        [(Voice.Cunado, Cue.Hello)] =
        [
            "Bueno bueno bueno, ¿quién se va a llevar la paliza hoy?",
            "Que empiece la fiesta. Y que pierda el de siempre.",
            "Avisad a los del seguro, que hoy vengo fino 😎",
            "¡Ya estamos todos! Lo malo es que estoy yo.",
            "He venido a hacer amigos y a ganar. Lo de amigos era broma.",
            "Hoy llevo la camiseta de la suerte. Sin lavar desde la última victoria, por si acaso.",
            "Sentaos, que esto va a ser rápido. Para vosotros, digo.",
            "Buenas, buenas. ¿Quién me guarda un sitio en el podio?"
        ],
        [(Voice.Picara, Cue.Hello)] =
        [
            "Hola a todos. Sobre todo a los que van a perder.",
            "Ya estoy. Podéis ir rindiéndoos.",
            "Buenas. Voy a ser amable hasta la ronda dos, luego no prometo nada.",
            "Qué bien, una mesa llena de gente que confía en sí misma. Me encanta.",
            "Hola. Si veis que sonrío, no es simpatía, es que tengo buena mano.",
            "Saludos. Por favor, no os lo toméis personal cuando os gane.",
            "Buenas. He venido sin cartas marcadas. Esta vez.",
            "Hola, hola. Antes de empezar: yo no hago trampas, hago estrategia."
        ],
        [(Voice.Zen, Cue.Hello)] =
        [
            "Buenas.",
            "Hola. Que gane quien menos hable.",
            "Ya estamos todos. Bien.",
            "Hola. Suerte a todos. Menos a mí, que no la necesito.",
            "Buenas tardes. Silencio y cartas.",
            "Hola. Jugad tranquilos.",
            "Aquí estoy. Empezad cuando queráis."
        ],
        [(Voice.Dramatica, Cue.Hello)] =
        [
            "¡AY, qué nervios! Siete rondas, siete infartos.",
            "Hola, hola. Si pierdo no me habléis en una semana.",
            "Ya estoy aquí. Traigo suerte, la de siempre: ninguna.",
            "¡Hola! Ya me tiembla la mano y no he visto ni una carta.",
            "Buenas. He soñado que ganaba. Luego me desperté, claro.",
            "Hola a todos. Sed buenos conmigo, hoy estoy sensible.",
            "¡Qué emoción! Odio esto. Me encanta. Odio esto.",
            "Ya estoy. Si veis que grito, es normal, no llaméis a nadie."
        ],
        [(Voice.Fanfarron, Cue.Hello)] =
        [
            "Llegó el campeón. Podéis aplaudir.",
            "Hoy no juego, hoy doy clase.",
            "Preparaos, que os voy a leer las cartas por la cara.",
            "Buenas. Os aviso: he venido a ganar, no a hacer amigos. Bueno, también.",
            "Hola, hola. Voy a intentar dejaros ganar una ronda, para que no os aburráis.",
            "Ya estoy aquí. El resto es decorado.",
            "Saludos, futuros perdedores. Con cariño.",
            "Llegué. Ya podéis empezar a poner excusas."
        ],
        [(Voice.Abuela, Cue.Hello)] =
        [
            "Hola, cielos. A jugar limpio y sin mirar la mano del vecino.",
            "Buenas tardes, familia. Hoy gano yo, que me toca.",
            "Ay, qué ilusión. ¿Habéis merendado todos?",
            "Hola, hijos míos. Que sea una partida bonita y que gane la abuela.",
            "Buenas. Poneos cómodos, que esto va para largo y a mí me gusta charlar.",
            "Hola, hola. Recordad que a los mayores se nos deja ganar. Es tradición.",
            "Ay, cuánta gente joven. Voy a tener que espabilar.",
            "Buenas tardes. He traído galletas, pero son para el que gane. O sea, para mí."
        ],

        [(null, Cue.RoundStart)] =
        [
            "Ronda {round}: {contract}. Esto ya se pone serio.",
            "Va, {contract}. Como no me toque un comodín me voy a tomar el aire.",
            "{contract}… ¿quién ha inventado esto?",
            "Ronda {round}. Respirad hondo.",
            "Nueva ronda, nuevas excusas. {contract}, allá vamos.",
            "{contract}. Vale. Voy a fingir que sé lo que hago.",
            "Ronda {round}. Que alguien reparta bien por una vez.",
            "A ver esta ronda… {contract}. Suena a que voy a sufrir."
        ],
        [(Voice.Fanfarron, Cue.RoundStart)] =
        [
            "{contract}. Fácil. Para mí, claro.",
            "Ronda {round}. Os la regalo, la siguiente ya no.",
            "{contract}. Con los ojos cerrados.",
            "Ronda {round}. Voy a ganarla, pero despacio, para que la disfrutéis."
        ],
        [(Voice.Dramatica, Cue.RoundStart)] =
        [
            "{contract}… No sé si estoy preparada para esto.",
            "Ronda {round}. Mi corazón no aguanta siete de estas.",
            "¿{contract}? ¡Pero si aún no me he recuperado de la anterior!",
            "Ronda {round}. Que alguien me sujete."
        ],
        [(Voice.Abuela, Cue.RoundStart)] =
        [
            "Ronda {round}, cielos. Con calma, que no hay prisa.",
            "{contract}. Ay, esto en mi época se llamaba de otra manera.",
            "Otra rondita. Sentaos derechos, que se os ve la espalda torcida.",
            "Ronda {round}. Que nadie se enfade, que es un juego."
        ],

        [(Voice.Cunado, Cue.ILaidDown)] =
        [
            "¡TOMA YA! ¿Habéis visto eso? ¿Lo habéis visto?",
            "Bajado. Que alguien me traiga un trofeo.",
            "Eso se llama jugar, lo demás son excusas.",
            "¡Bajado! Y sin despeinarme.",
            "Ahí lo tenéis. Podéis hacer fotos.",
            "Me bajo. Y no, no ha sido suerte. Bueno, un poco.",
            "¡Zas! En toda la mesa.",
            "Bajado. Ya podéis ir sacando la calculadora."
        ],
        [(Voice.Picara, Cue.ILaidDown)] =
        [
            "Ups, se me ha caído la mano a la mesa 😏",
            "Bajada. Ya podéis contar puntos.",
            "Yo ya estoy. Vosotros a lo vuestro, sin prisa.",
            "Perdón, ¿esto era difícil? No me había dado cuenta.",
            "Me bajo. Que conste que llevaba dos turnos esperando por educación.",
            "Ahí queda eso. Bonito, ¿verdad?",
            "Bajada. Ahora viene la parte divertida: veros sufrir.",
            "Listo. Y todavía me sobran cartas buenas, por si os interesa."
        ],
        [(Voice.Zen, Cue.ILaidDown)] =
        [
            "Bajado.",
            "Ahí queda.",
            "Sin prisa, pero sin pausa.",
            "Hecho.",
            "Era cuestión de tiempo.",
            "Bajado. Seguid.",
            "Poco a poco."
        ],
        [(Voice.Dramatica, Cue.ILaidDown)] =
        [
            "¡NO ME LO CREO! ¡Me he bajado! ¡YO!",
            "Ay, ay, ay, que me bajo. ¡Que me he bajado!",
            "Milagro. Alguien apunte la fecha.",
            "¡Me he bajado y no me ha dado un infarto! Bueno, casi.",
            "¡¡BAJADA!! Perdón por los gritos, es la emoción.",
            "Lo he conseguido. Voy a llorar un poquito.",
            "¿Esto es real? ¿Me he bajado de verdad? ¡Decidme que sí!",
            "Bajada. Mi madre estaría orgullosa. O sorprendida."
        ],
        [(Voice.Fanfarron, Cue.ILaidDown)] =
        [
            "Bajado. Como estaba previsto.",
            "Lo dicho: clase magistral.",
            "¿Ya? Sí, ya. Es lo que hay.",
            "Bajado. Lo raro sería lo contrario.",
            "Ahí va. Aprended, que es gratis.",
            "Me bajo. Sin esfuerzo. Como siempre.",
            "Bajado. Esto para mí es calentamiento.",
            "Ya. ¿Sorprendidos? Yo no."
        ],
        [(Voice.Abuela, Cue.ILaidDown)] =
        [
            "Ay, mira qué bien, que me he bajado.",
            "Poquito a poco, que la abuela también sabe.",
            "Bajada, hijos. Y sin ayuda de nadie.",
            "Ay, qué alegría. A mi edad estas cosas se celebran.",
            "Bajada. Ya me puedo tomar la pastilla tranquila.",
            "Ahí lo tenéis, cielos. Viejita pero no tonta.",
            "Me he bajado. Ahora dejadme un ratito que descanse.",
            "Bajada. Y todavía me acuerdo de cómo se juega, fijaos."
        ],

        [(Voice.Cunado, Cue.TheyLaidDown)] =
        [
            "{p} se ha bajado. ¡Traición!",
            "Anda que {p}, calladito y bajándose.",
            "{p}, ¿te has bajado o te has caído?",
            "Ojo con {p}, que iba de tranquilo y mira.",
            "{p} bajándose. Yo aquí con una mano que parece un puzle.",
            "¡{p}! Eso no lo habíamos hablado.",
            "Vale, {p} se ha bajado. Ahora sí que hay partido.",
            "{p} se baja y yo sigo buscando el 7 de la vida."
        ],
        [(Voice.Picara, Cue.TheyLaidDown)] =
        [
            "Vaya, {p}. Qué inesperado. Qué sorpresa. Qué rabia.",
            "{p} se baja. Yo no pienso aplaudir.",
            "Mira {p}, con lo tranquilo que estaba todo.",
            "{p} se ha bajado. Alguien le ha pasado las cartas buenas, seguro.",
            "Enhorabuena, {p}. Dicho sin ninguna alegría.",
            "{p} bajándose. Y yo que pensaba que estábamos de charla.",
            "Muy bien, {p}. Ahora no te vengas arriba.",
            "{p}, has tardado. Pero bueno, bajado es bajado."
        ],
        [(Voice.Zen, Cue.TheyLaidDown)] =
        [
            "Bien jugado, {p}.",
            "Anotado, {p}.",
            "{p} se bajó. Toca espabilar.",
            "Bien, {p}.",
            "{p} va en serio.",
            "Correcto, {p}.",
            "Eso ha sido limpio, {p}."
        ],
        [(Voice.Dramatica, Cue.TheyLaidDown)] =
        [
            "¿¡{p} YA!? Esto es el fin.",
            "{p} se ha bajado y yo tengo un abanico de puntos.",
            "Que alguien pare a {p}, POR FAVOR.",
            "¡{p}! ¡No! ¡Aún no estaba lista!",
            "{p} se baja y a mí me sube la tensión.",
            "Esto es una pesadilla. {p} bajado y yo sin nada.",
            "¡{p}, por lo que más quieras, dame un respiro!",
            "{p} se ha bajado. Voy a necesitar una tila."
        ],
        [(Voice.Fanfarron, Cue.TheyLaidDown)] =
        [
            "Suerte, {p}. Pura suerte.",
            "Bajarse es fácil, {p}. Lo difícil es ganarme.",
            "{p} se baja. Bueno. Alguien tenía que hacerlo de segundo.",
            "Bien, {p}. Te dejo esta para que te ilusiones.",
            "{p} bajándose. Qué tierno.",
            "Vale, {p}. Sigue así y a lo mejor me haces sudar.",
            "{p} se ha bajado. Yo lo hago mejor, pero se ve bien.",
            "No está mal, {p}. Para ser tú."
        ],
        [(Voice.Abuela, Cue.TheyLaidDown)] =
        [
            "Muy bien, {p}, así se hace.",
            "Ay, {p}, qué manitas.",
            "Bravo, {p}. Pero no te confíes, cielo.",
            "{p}, cariño, qué bien juegas.",
            "Mira {p}, con lo joven que es y lo que sabe.",
            "Muy bien, {p}. Un aplauso de la abuela.",
            "Ay, {p}, que me vas a dejar sin cartas para colocar.",
            "Qué bonito, {p}. Ahora deja algo para los demás."
        ],

        [(null, Cue.IStole)] =
        [
            "Ese {card} era mío. Gracias por dejarlo ahí.",
            "Robo de contra y me da igual el castigo.",
            "Perdón, perdón. Bueno, en realidad no.",
            "Eso lo necesitaba yo más que nadie.",
            "El {card} se viene conmigo. Lo siento, {t}.",
            "Sí, robo de contra. Sí, con castigo. Sí, ha valido la pena.",
            "{t}, ese {card} era demasiado bueno para dejarlo pasar.",
            "Me llevo el {card}. La carta de castigo la asumo con dignidad."
        ],
        [(Voice.Fanfarron, Cue.IStole)] =
        [
            "El {card} viene con papá.",
            "Robo de contra. Con estilo, como todo lo mío.",
            "{t}, gracias por el {card}. Lo cuidaré mejor que tú.",
            "Robo. Castigo. Me da igual. Voy sobrado."
        ],
        [(Voice.Abuela, Cue.IStole)] =
        [
            "Ay, perdonad, que el {card} me hacía falta.",
            "Que no se enfade nadie, que la carta de castigo ya me la trago yo.",
            "Perdona, {t}, cielo, pero el {card} me venía de perlas.",
            "Robo de contra. A mi edad ya no me da vergüenza."
        ],

        [(null, Cue.TheyStole)] =
        [
            "¡{p}! ¡Qué morro!",
            "{p} robando de contra. Se acabó la amistad.",
            "Con dos narices, {p}. Con dos.",
            "Ese {card} lo quería yo, {p}.",
            "{p} se lleva el {card} y una de castigo. Que te aproveche.",
            "Mira {p}, robando como si no hubiera un mañana.",
            "{p}, eso ha sido un atraco a mano armada.",
            "¡Robo de contra de {p}! Esto se pone interesante.",
            "{t}, te acaban de birlar el {card} en tu cara. {p} no tiene abuela.",
            "{p} robando de contra. Yo lo llamo hambre."
        ],
        [(Voice.Zen, Cue.TheyStole)] =
        [
            "Buen robo, {p}.",
            "{p} lo vio antes que nadie.",
            "Ojo con {p}.",
            "{p} sabe lo que quiere."
        ],

        [(null, Cue.IWasBlocked)] =
        [
            "{p}, ese {card} era MI turno.",
            "Gracias por nada, {p}.",
            "Ya me habían quitado el {card} de las manos, cómo no.",
            "{p}, me acabas de quitar el {card} en mi propio turno. Lo tendré en cuenta.",
            "Perfecto. Iba a por el {card} y {p} se lo lleva. Perfecto.",
            "{p}, qué oportuno. Qué oportuno."
        ],

        [(null, Cue.IPicked)] =
        [
            "Esa me sirve.",
            "Hmm, interesante.",
            "Gracias, majo.",
            "Justo lo que buscaba.",
            "No preguntéis para qué la quiero.",
            "Ay, qué bonita.",
            "El {card} se viene a casa.",
            "Gracias por el {card}. Muy amable.",
            "Esto me lo quedo."
        ],
        [(null, Cue.TheyPicked)] =
        [
            "¿Para qué quieres el {card}, {p}?",
            "{p} cogiendo el {card} del pozo. Sospechoso.",
            "Ojo, {p} se ha llevado el {card}. Tomad nota.",
            "{p}, ese {card} te delata.",
            "Interesante, {p}. Muy interesante.",
            "{p} quiere el {card}. Ya sé lo que no tirar."
        ],

        [(null, Cue.IJokerSwapped)] =
        [
            "Cambio el {card} por el comodín. Legal, ¿eh? Lo pone en las reglas.",
            "Un comodín menos para {p}, uno más para mí. Así es la vida.",
            "Perdona {p}, tomo prestado tu comodín. Para siempre.",
            "Ese comodín estaba mal cuidado. Ahora está mejor.",
            "{p}, te doy el {card} y me llevo el comodín. Es un intercambio justo. Para mí.",
            "Canje de comodín. {p}, no me mires así, es el juego."
        ],
        [(Voice.Picara, Cue.IJokerSwapped)] =
        [
            "{p}, te cambio un {card} por un comodín. Gran negocio… para mí.",
            "Comodín adquirido. Sin devoluciones.",
            "Gracias por guardarme el comodín, {p}. Ya me lo llevo.",
            "{p}, tu escalera queda preciosa con el {card}. Y mi juego, con tu comodín.",
            "Canje. Y encima {p} tiene que darme las gracias por el {card}.",
            "Un comodín en la escalera de {p}, un {card} en mi mano. Dos más dos."
        ],
        [(Voice.Cunado, Cue.IJokerSwapped)] =
        [
            "¡MÍO! El comodín de {p} ahora es mío. Muajaja.",
            "Jugadón. Con mayúsculas. JUGADÓN.",
            "{p}, ese comodín ya no es tuyo. Es del pueblo. O sea, mío.",
            "¡Zas! Comodín robado con el {card}. Legalmente.",
            "{p}, lo siento, pero no lo siento. Comodín para mí.",
            "¡Canjeazo! Que alguien lo apunte en el acta."
        ],
        [(Voice.Zen, Cue.IJokerSwapped)] =
        [
            "Canjeo el comodín, {p}.",
            "El {card} por el comodín. Justo.",
            "Ese comodín ahora trabaja para mí.",
            "Comodín canjeado. Nada personal, {p}."
        ],
        [(Voice.Dramatica, Cue.IJokerSwapped)] =
        [
            "¡TENGO EL COMODÍN DE {p}! ¡Es mío! ¡MÍO!",
            "{p}, perdóname. No, mentira. ¡Qué jugada!",
            "¡He canjeado un comodín! ¡Esto no me pasa nunca!",
            "{p}, tu comodín ha decidido venirse conmigo. No le culpes.",
            "¡Comodín! ¡Comodín! Ay, que me da algo de la alegría."
        ],
        [(Voice.Fanfarron, Cue.IJokerSwapped)] =
        [
            "{p}, gracias por el comodín. Lo estabas desperdiciando.",
            "Canje. Eso es visión de juego, {p}. Apunta.",
            "Me llevo el comodín de {p}. Era lo lógico.",
            "Comodín para el campeón. {p}, no te preocupes, se te pasa.",
            "{p}, esto es lo que pasa cuando dejas un comodín a la vista."
        ],
        [(Voice.Abuela, Cue.IJokerSwapped)] =
        [
            "Ay, {p}, no te enfades, que te dejo el {card} bien bonito.",
            "Cambio de cromos, cariño. Tú el {card}, yo el comodín.",
            "{p}, cielo, te lo cambio con todo mi amor. El comodín es mío.",
            "Perdona, {p}, que la abuela también sabe jugar de esto.",
            "Ay, qué bien, un comodín. {p}, no llores, hijo."
        ],

        [(Voice.Cunado, Cue.MyJokerTaken)] =
        [
            "¡EH! ¡Ese comodín era mío! ¡ÁRBITRO!",
            "No me lo puedo creer, {p}. Ladrón.",
            "Me lo apunto, {p}. Me lo apunto.",
            "¡{p}! ¡Que ese comodín tenía nombre y apellidos!",
            "Vale, {p}. Vale. Esto es la guerra.",
            "¿En serio, {p}? ¿Con el {card}? Menuda cara.",
            "Me han robado en mi propia mesa. {p}, te vas a enterar.",
            "¡{p}! Mira que lo pone en las reglas, pero duele igual."
        ],
        [(Voice.Picara, Cue.MyJokerTaken)] =
        [
            "Vale, {p}. Muy bien. Recuerda que yo también tengo memoria.",
            "Ah, que me quitas el comodín. Sin más. Perfecto.",
            "Tranquilo, {p}, ya nos veremos en la siguiente ronda.",
            "{p}, muy bonito. Espero que duermas bien esta noche.",
            "Ah, vale, {p}. Yo aquí guardando el comodín para ti, por lo visto.",
            "Sí, {p}, llévatelo. Total, solo era MI comodín.",
            "Bien jugado, {p}. Lo digo con los dientes apretados.",
            "{p}, apuntado en la libreta negra. Página uno."
        ],
        [(Voice.Zen, Cue.MyJokerTaken)] =
        [
            "Esperable.",
            "Bien visto, {p}.",
            "Va y viene, como todo.",
            "Era tuyo, {p}. Ahora lo sé.",
            "Nada dura. Ni un comodín.",
            "Bien, {p}. Sin rencor.",
            "El comodín se fue. Yo sigo."
        ],
        [(Voice.Dramatica, Cue.MyJokerTaken)] =
        [
            "¡¿MI COMODÍN?! Esto es lo peor que me ha pasado hoy.",
            "Me voy. Me voy de la mesa. No, espera, sigo, pero muy dolida.",
            "{p}, me acabas de robar la ilusión.",
            "¡NO! ¡El comodín no! ¡{p}, cualquier cosa menos eso!",
            "Estoy en shock. {p} me ha quitado el comodín y yo aquí, viva de milagro.",
            "{p}, eso ha sido cruel. Cruel. Voy a necesitar un momento.",
            "Mi comodín. Mi precioso comodín. {p}, te lo llevas con mi corazón dentro.",
            "¿Con el {card}? ¿Me lo quitas con el {card}? Qué humillación."
        ],
        [(Voice.Fanfarron, Cue.MyJokerTaken)] =
        [
            "Te lo presto, {p}. Total, no lo necesito.",
            "Cógelo, cógelo. Ya te lo devuelvo yo con intereses.",
            "Sin ese comodín sigo ganando. Es lo que tiene ser bueno.",
            "{p}, te lo he dejado a propósito. Para que tengas algo.",
            "Ese comodín ya me sobraba, {p}. De nada.",
            "Bien, {p}. Un comodín menos. Sigo teniendo talento, que no se roba.",
            "{p}, disfrútalo. Es lo más cerca que vas a estar de ganar."
        ],
        [(Voice.Abuela, Cue.MyJokerTaken)] =
        [
            "Ay, {p}, que a una anciana se lo quitas.",
            "Qué disgusto, hijo. Con lo que me costó.",
            "Bueno, bueno. Que Dios te lo pague, {p}.",
            "{p}, cielo, eso a tu abuela no se lo haces.",
            "Ay, mi comodín. Bueno, {p}, que te sirva de algo.",
            "{p}, cariño, ¿tú sabes lo que cuesta un comodín a mi edad?",
            "Nada, nada. Que lo disfrute {p}. Yo ya estoy acostumbrada."
        ],

        [(null, Cue.TheyJokerSwapped)] =
        [
            "Ojo, que {p} va robando comodines por la mesa.",
            "{p} sabe jugar, cuidado con esa.",
            "Bonito canje, {p}. Nadie lo vio venir.",
            "{p} entregó el {card} y se llevó el comodín. Así, sin anestesia.",
            "{p} le ha quitado el comodín a {t}. Yo no digo nada, pero lo digo.",
            "Menudo canje de {p}. {t}, lo siento por ti.",
            "{p}, eso ha sido jugar con cabeza. Me duele reconocerlo.",
            "{t}, te acaban de dejar sin comodín. {p} no perdona."
        ],

        [(null, Cue.TheyExtendedMine)] =
        [
            "Oye {p}, que ese juego es mío. Bueno, era.",
            "Gracias por decorarme la escalera, {p}.",
            "{p} me ha puesto un {card}. Sin permiso. Como en casa.",
            "{p}, mi juego no es un buzón para dejar cartas.",
            "Muy bien, {p}, deja tus cartas en mi mesa. Lo que faltaba.",
            "{p} colocando en mi juego. Al menos el {card} queda bonito."
        ],

        [(null, Cue.OneCardLeft)] =
        [
            "{p} con una carta. Que alguien haga algo.",
            "Cuidado, cuidado, que {p} cierra.",
            "¿Una carta, {p}? ¿En serio? Odio esto.",
            "{p} está a una. Rezad.",
            "Ojo con {p}, que le queda una y se le nota en la cara.",
            "Se acabó la tranquilidad: {p} a una carta.",
            "{p} a una carta. Yo empiezo a contar puntos ya.",
            "Alarma: {p} tiene una carta. Repito, UNA.",
            "{p}, no cierres todavía, que tengo la mano llena."
        ],
        [(null, Cue.IHaveOneCard)] =
        [
            "Una carta. Solo una. 😏",
            "Id contando la mano, que esto se acaba.",
            "Última carta. Que nadie parpadee.",
            "Me queda una. Empezad a sudar.",
            "Una cartita de nada. Qué nervios, ¿eh?",
            "A una de cerrar. Solo digo eso.",
            "Una. Y sé exactamente cuál es.",
            "Aviso legal: me queda una carta. Luego no digáis que no os avisé."
        ],
        [(Voice.Fanfarron, Cue.IHaveOneCard)] =
        [
            "Una. Como los grandes.",
            "Me queda una y ya sé cuál es. Vosotros no.",
            "Una carta. Esto se llama control.",
            "Última carta. Podéis ir aplaudiendo."
        ],
        [(Voice.Dramatica, Cue.IHaveOneCard)] =
        [
            "¡UNA CARTA! ¡Ay, que me da algo!",
            "Me queda una y me tiembla todo.",
            "Una carta. Si no cierro, me muero. Literalmente no, pero casi.",
            "¡Una! ¡Rezad por mí!"
        ],

        [(Voice.Cunado, Cue.IClosed)] =
        [
            "¡¡CERRADO!! ¡A contar puntos, pringados!",
            "Se acabó. Vayan pasando por caja.",
            "¡Y con esa cierro! Que alguien grabe esto.",
            "¡Cerrado! Y no pienso disimular la alegría.",
            "Fin de la ronda. Firmado: el mejor.",
            "¡BOOM! Cerrado. Contad bien, que os veo.",
            "Cerrado. Avisad a la familia.",
            "¡Cierro! Y sí, voy a hablar de esto toda la semana."
        ],
        [(Voice.Picara, Cue.IClosed)] =
        [
            "Cierro. Sí, así, sin avisar.",
            "Fin de la ronda. No hace falta que me deis las gracias.",
            "Ya está. Contad despacito, que os veo nerviosos.",
            "Cerrado. Espero que nadie llevara comodines en la mano. Ay, qué pena.",
            "Cierro. Podéis empezar a inventar excusas.",
            "Ronda cerrada. Qué manos más bonitas os quedan, seguro.",
            "Cerrado. No he hecho trampas. Por si alguien iba a decir algo.",
            "Ya. Sin más. Cerrado."
        ],
        [(Voice.Zen, Cue.IClosed)] =
        [
            "Cerrado.",
            "Ronda hecha.",
            "Bien. Siguiente.",
            "Se acabó.",
            "Cierro. Contad.",
            "Terminado. Con calma.",
            "Una menos."
        ],
        [(Voice.Dramatica, Cue.IClosed)] =
        [
            "¡HE CERRADO! ¡Que alguien me pellizque!",
            "Esto no me pasa nunca. ¡NUNCA! ¡Y ha pasado!",
            "Cierro y me emociono. Dadme un segundo.",
            "¡CERRADO! ¡Voy a enmarcar esta ronda!",
            "¡He cerrado yo! ¡YO! ¿Lo estáis viendo?",
            "Cierro. Estoy temblando. De felicidad, esta vez.",
            "¡Cerrado! Perdón por gritar. ¡CERRADO!",
            "No puedo. No puedo con tanta emoción. He cerrado."
        ],
        [(Voice.Fanfarron, Cue.IClosed)] =
        [
            "Cerrado. Como siempre. Como todo.",
            "Otra ronda para la colección.",
            "Esto es lo que pasa cuando juegas contra mí.",
            "Cerrado. Lo raro es que hayáis tardado tanto en verlo venir.",
            "Cierro. Rutina.",
            "Ronda cerrada. Podéis tomar apuntes.",
            "Cerrado sin despeinarme. Ni una gota de sudor.",
            "Cierro. Es que no hay color."
        ],
        [(Voice.Abuela, Cue.IClosed)] =
        [
            "Ay, que he cerrado. Qué alegría, hijos.",
            "Cerrado. La experiencia es un grado.",
            "Ya está. Ahora sí que me tomo el café.",
            "Cerrado, cielos. Y sin gafas.",
            "Ay, que he cerrado yo. Con lo torpe que dicen que soy.",
            "Cerrado. Ahora contad bien, que la abuela repasa.",
            "Ya. He cerrado. Voy a llamar a mi nieta para contárselo.",
            "Cerrado. Ay, qué gusto."
        ],
        [(null, Cue.IClosedAgain)] =
        [
            "Otra vez yo. Empiezo a pensar que esto es un don.",
            "Dos seguidas. Y no, no es suerte, es talento.",
            "Cierro otra vez. ¿Seguro que queréis seguir jugando?",
            "Otra ronda para mí. Esto ya es costumbre.",
            "Dos seguidas. Voy a empezar a cobrar por las lecciones.",
            "Cerrado de nuevo. Lo siento por vosotros. Un poco."
        ],

        [(Voice.Cunado, Cue.TheyClosed)] =
        [
            "{p} cierra. Yo me quedo con todo esto en la mano. Genial.",
            "Anda {p}, ni un poquito de piedad.",
            "{p} ha cerrado y yo tenía la mano PERFECTA. Casi.",
            "{p}, tío, que estaba a punto. A PUNTO.",
            "Cierra {p}. Y yo con una mano que parece una tienda de cartas.",
            "¡{p}! Un turno más y te la lío. Uno.",
            "{p} cerrando. Yo aquí calculando cuánto me cuesta esto.",
            "Vale, {p}. Vale. Muy bien. No estoy enfadado. Estoy enfadadísimo."
        ],
        [(Voice.Picara, Cue.TheyClosed)] =
        [
            "{p} cierra. Vale. Vaaaale.",
            "Muy bien, {p}. Disfrútalo, que dura poco.",
            "{p} cerró. Sospechoso, pero lo dejo pasar.",
            "{p}, has cerrado justo cuando iba a arrasar. Casualidad, seguro.",
            "Cierra {p}. Qué oportuno. Qué molesto.",
            "Enhorabuena, {p}. Lo digo con la boca pequeña.",
            "{p} cierra y yo con una mano preciosa que nadie va a ver.",
            "Bien, {p}. Apúntatelo, que es de las pocas."
        ],
        [(Voice.Zen, Cue.TheyClosed)] =
        [
            "Enhorabuena, {p}.",
            "{p} cerró. Sigamos.",
            "Ronda para {p}. Justo.",
            "Bien hecho, {p}.",
            "{p} lo ha hecho bien.",
            "Cierra {p}. Nada que objetar.",
            "Correcto, {p}. Siguiente ronda."
        ],
        [(Voice.Dramatica, Cue.TheyClosed)] =
        [
            "¿{p} ya? ¡Si acabábamos de empezar!",
            "{p} cierra y yo con las cartas contadas. Qué dolor.",
            "No. No. NO. {p}, ¿por qué?",
            "¡{p}! ¡Me has destrozado la ronda y la vida!",
            "{p} ha cerrado. Voy a llorar en el pozo un rato.",
            "¿Por qué siempre a mí? {p}, esto es personal.",
            "{p} cierra y a mí se me cae el alma a los pies. Y las cartas.",
            "Cierra {p}. Necesito una tila. O dos."
        ],
        [(Voice.Fanfarron, Cue.TheyClosed)] =
        [
            "{p} cierra esta. Yo cierro la partida, ya verás.",
            "Bien, {p}. Un descuido mío.",
            "Te dejo esta, {p}. Para que no te desanimes.",
            "{p} ha cerrado. Le he dejado, obviamente.",
            "Vale, {p}. Una ronda. Disfrútala.",
            "{p} cierra. Yo estaba distraído pensando en cómo ganar la partida.",
            "Enhorabuena, {p}. Ahora vuelve la normalidad.",
            "Bien, {p}. Hasta un reloj parado acierta dos veces al día."
        ],
        [(Voice.Abuela, Cue.TheyClosed)] =
        [
            "Qué bien, {p}, enhorabuena, cariño.",
            "Ay, {p}, cómo se te da esto.",
            "Muy bien, {p}. Y yo con la mano llena, como siempre.",
            "{p}, hijo, qué rápido. La abuela ni se ha enterado.",
            "Bravo, {p}. Te has ganado una galleta.",
            "Ay, {p}, qué alegría me das. Aunque me cueste puntos.",
            "Muy bien, cielo. {p} cierra y la abuela aplaude.",
            "{p} ha cerrado. Bueno, alguien tenía que ser, y qué mejor que tú."
        ],
        [(null, Cue.TheyClosedAgain)] =
        [
            "¿Otra vez {p}? Esto está amañado.",
            "{p} cerrando dos seguidas. Que alguien revise las cartas.",
            "{p}, déjanos algo. Dos rondas seguidas es abusar.",
            "Otra para {p}. Yo empiezo a sospechar del reparto.",
            "{p} otra vez. Vale, ya no es suerte, es una tomadura de pelo.",
            "Dos seguidas, {p}. ¿Nos dejas ganar una, por caridad?"
        ],

        [(null, Cue.IWasWorst)] =
        [
            "{n} puntos. Me lo he ganado a pulso.",
            "{n}. No miréis, por favor.",
            "He hecho {n} puntos. Coleccionar cartas también es bonito.",
            "Menos mal que esto son puntos y no euros.",
            "{n} puntos. Voy a fingir que era el plan.",
            "{n}. Alguien tiene que hacer de malo en esta historia.",
            "{n} puntos. Se me ha ido la mano guardando cartas.",
            "{n}. La culpa es del mazo, evidentemente."
        ],
        [(Voice.Dramatica, Cue.IWasWorst)] =
        [
            "{n} PUNTOS. Voy a llorar en el pozo.",
            "Que conste que la culpa es del mazo. {n} puntos, por si alguien pregunta.",
            "{n} puntos. Es el peor día de mi vida. Hasta la siguiente ronda.",
            "{n}. Me quiero ir. No me voy. Pero me quiero ir.",
            "¡{n} puntos! Que alguien me consuele."
        ],
        [(Voice.Fanfarron, Cue.IWasWorst)] =
        [
            "{n} puntos. Estrategia. Ya lo entenderéis.",
            "He dejado que me pasen para que haya emoción.",
            "{n}. Estaba guardando cartas para la remontada.",
            "{n} puntos. Un campeón también tiene días humanos."
        ],
        [(Voice.Abuela, Cue.IWasWorst)] =
        [
            "{n} puntos, hijos. Es que tenía la mano muy bonita y no quería romperla.",
            "{n}. Bueno, a mi edad los números ya no me asustan.",
            "{n} puntos. La abuela se ha despistado contando.",
            "{n}. No pasa nada, que en la siguiente me pongo las gafas."
        ],

        [(null, Cue.TheyWereWorst)] =
        [
            "{p}, {n} puntos. ¿Estás bien? ¿Necesitas algo?",
            "Un aplauso para {p} y sus {n} puntos.",
            "{n} puntos, {p}. Impresionante. En el mal sentido.",
            "{p} se ha quedado la mano entera. Coleccionista.",
            "{p}, con {n} puntos vas a necesitar un milagro.",
            "{n}, {p}. Eso no es una ronda, es una hipoteca.",
            "{p}, {n} puntos. ¿Estabas jugando a otra cosa?",
            "{p} se lleva {n}. Yo me llevo la risa.",
            "{n} puntos para {p}. Que alguien le dé un abrazo."
        ],
        [(Voice.Abuela, Cue.TheyWereWorst)] =
        [
            "{p}, cielo, {n} puntos. La próxima va mejor, seguro.",
            "No pasa nada, {p}. Peor es no jugar.",
            "{p}, hijo, {n} puntos. Ven, que te doy un caramelo.",
            "Ay, {p}. {n}. Eso se cura con una buena mano."
        ],
        [(Voice.Fanfarron, Cue.TheyWereWorst)] =
        [
            "{p}, {n} puntos. Yo eso lo hago en dos rondas y sin querer.",
            "{n}, {p}. Tranquilo, contra mí es normal.",
            "{p} con {n}. Esto es lo que pasa cuando compartes mesa con un campeón.",
            "{n} puntos, {p}. Me lo apunto para presumir luego."
        ],

        [(null, Cue.NobodyClosed)] =
        [
            "¿Nadie se ha bajado? Qué mesa más triste.",
            "Ronda en tablas. Todos contamos. Todos lloramos.",
            "Se acabó el mazo y nadie cerró. Menudo nivel.",
            "Tablas. Enhorabuena a todos por no hacer nada.",
            "Nadie ha cerrado. Yo propongo repetir y hacer como que no ha pasado.",
            "Ronda en tablas. Qué vergüenza colectiva."
        ],

        [(null, Cue.Leading)] =
        [
            "Voy primero con {n}. Solo lo comento.",
            "{n} puntos y en cabeza. No es presumir si es verdad.",
            "Primero con {n}. {last}, tú vas con {lastPts}. Ánimo.",
            "Líder con {n} puntos. Podéis ir llamándome capitán.",
            "Voy ganando con {n}. Lo digo por si alguien no estaba contando."
        ],
        [(Voice.Fanfarron, Cue.Leading)] =
        [
            "Primero con {n}. Como estaba previsto. Como siempre.",
            "{n} puntos. Líder. ¿Alguna pregunta?",
            "Voy primero. {last}, con {lastPts}, te veo lejos. Muy lejos.",
            "En cabeza con {n}. Voy a bajar el ritmo para no humillar."
        ],
        [(Voice.Abuela, Cue.Leading)] =
        [
            "Ay, que voy primera con {n}. Quién lo diría.",
            "La abuela en cabeza con {n} puntos. Que se enteren en el pueblo.",
            "Voy ganando, hijos. {n} puntos. No os pongáis nerviosos."
        ],
        [(null, Cue.HumanLeading)] =
        [
            "{p} va primero con {n}. Esto hay que arreglarlo.",
            "Ojo, {p} en cabeza con {n} puntos. Todos contra {p}, ¿no?",
            "{p} lidera con {n}. Yo no digo que sea sospechoso, pero lo es.",
            "{p} va ganando con {n}. Disfrútalo, que la partida es larga.",
            "{n} puntos y {p} primero. Vale, ahora sí que hay que jugar en serio.",
            "{p} en cabeza. {last} va último con {lastPts}. Todo puede pasar."
        ],

        [(Voice.Cunado, Cue.IWonGame)] =
        [
            "¡¡GANÉ!! ¡Que suene la música!",
            "Campeón. Ya está. Ya lo he dicho.",
            "Siete rondas para demostrar lo evidente.",
            "¡He ganado! Voy a estar insoportable, os aviso.",
            "¡Victoria! Que alguien avise a la prensa.",
            "¡GANÉ! Y sin hacer trampas, que conste en acta."
        ],
        [(Voice.Picara, Cue.IWonGame)] =
        [
            "He ganado. Qué raro, con lo bien que jugabais todos.",
            "Victoria. Voy a ser insoportable un rato, avisados.",
            "Gané. Podéis empezar a fingir que os alegráis.",
            "He ganado. No hace falta que me felicitéis, con la envidia me vale.",
            "Ganadora. Lo sabía desde la ronda uno, pero no quería asustaros.",
            "Victoria. Ha sido divertido veros intentarlo."
        ],
        [(Voice.Zen, Cue.IWonGame)] =
        [
            "Gané. Gracias por la partida.",
            "Bien jugado todos. Sobre todo yo.",
            "Victoria. Sin ruido.",
            "He ganado. Buena partida.",
            "Gané. Mañana, otra."
        ],
        [(Voice.Dramatica, Cue.IWonGame)] =
        [
            "¡¡¡HE GANADO!!! ¡NO ME LO CREO! ¡NADIE SE LO CREE!",
            "Esto va para mis fans. Que son ninguno. ¡Pero he ganado!",
            "¡GANÉ! Voy a llorar. Ya estoy llorando. ¡Qué bonito!",
            "¡He ganado la partida! ¡A mí! ¡Que nunca gano nada!",
            "¡Victoria! Me tiembla todo. Necesito sentarme. Ya estoy sentada."
        ],
        [(Voice.Fanfarron, Cue.IWonGame)] =
        [
            "Como estaba previsto. Siguiente.",
            "Ganar es un hábito. Preguntadme cómo.",
            "Victoria. Podéis pedirme autógrafos.",
            "He ganado. Sorpresa para nadie.",
            "Campeón. Otra vez. Aburre un poco, la verdad.",
            "Gané. Lo dije al empezar y nadie me escuchó."
        ],
        [(Voice.Abuela, Cue.IWonGame)] =
        [
            "Ay, que he ganado yo. Con lo mayor que soy.",
            "Ganó la abuela. A ver quién se ríe ahora.",
            "Qué bonito. Ahora todos a fregar, que he ganado.",
            "He ganado, hijos. Esto lo cuento en la peluquería mañana.",
            "Victoria para la abuela. Las galletas me las como yo.",
            "Ay, qué ilusión. Ganar a mi edad. Qué cosas."
        ],

        [(null, Cue.TheyWonGame)] =
        [
            "Enhorabuena, {p}. Pero la revancha es mañana.",
            "{p} gana. Se lo ha currado, hay que reconocerlo.",
            "Ha ganado {p}. Lo dejamos en empate técnico, ¿no?",
            "Bien, {p}. Apúntatelo, que no se repetirá.",
            "{p} campeón con {n} puntos. Yo exijo recuento.",
            "Gana {p}. Voy a decir que le he dejado, aunque no cuele.",
            "{p} ha ganado. Que alguien le quite las cartas antes de que se venga arriba.",
            "Enhorabuena, {p}. Ahora sabemos a quién odiar hasta la próxima."
        ],
        [(Voice.Fanfarron, Cue.TheyWonGame)] =
        [
            "{p} gana. Tenía el día tonto.",
            "Hoy {p}. Mañana yo, como siempre.",
            "{p} campeón. Con asterisco.",
            "Bien, {p}. Te la he dejado, pero disfrútala."
        ],
        [(Voice.Abuela, Cue.TheyWonGame)] =
        [
            "Enhorabuena, {p}, cielo. Te has ganado las galletas.",
            "Muy bien, {p}. La abuela está orgullosa aunque haya perdido.",
            "{p} ha ganado. Qué alegría, hijo. Ven que te dé un beso."
        ],

        [(null, Cue.ILostGame)] =
        [
            "Último. Otra vez. Empiezo a ver un patrón.",
            "He perdido, pero con mucha dignidad. Mucha.",
            "Bueno, alguien tenía que ser el último. Muy generoso por mi parte.",
            "{n} puntos. Un récord. Del malo.",
            "Último con {n}. Voy a exigir una investigación.",
            "He perdido. Culpo al mazo, a la mesa y al calendario."
        ],
        [(Voice.Dramatica, Cue.ILostGame)] =
        [
            "ÚLTIMA. No me habléis. No me miréis.",
            "Voy a borrar esta partida de mi memoria. Y la anterior.",
            "{n} puntos. Me retiro. Hasta la próxima partida, claro.",
            "Última. Se acabó. Voy a llorar y luego pedimos revancha."
        ],

        [(null, Cue.StockRecycled)] =
        [
            "Se rehace el mazo. Esto se alarga más que una sobremesa.",
            "Otra vez el pozo al mazo. Que alguien cierre, por favor.",
            "El mazo se acabó. Como mi paciencia.",
            "Barajamos el pozo. Esto ya parece una partida de abuelos.",
            "Mazo nuevo. Espero que este venga con más comodines.",
            "Se rehace el mazo. ¿Alguien piensa cerrar hoy?"
        ],

        [(null, Cue.JokerDiscarded)] =
        [
            "¡¿{p} ha tirado un COMODÍN?! ¿Estás bien?",
            "{p} acaba de tirar un comodín. Silencio en la sala.",
            "Un comodín al pozo. {p}, eso ha dolido a todos.",
            "Que alguien le explique a {p} lo que es un comodín.",
            "{p} tira un comodín. Voy a fingir que no lo he visto.",
            "¡{p}! ¡Que eso vale 50 puntos! ¿Qué te pasa?",
            "{p} tirando comodines. Un momento histórico.",
            "Un comodín en el pozo. {p}, ¿te encuentras bien? ¿Has comido?"
        ],

        [(Voice.Cunado, Cue.IdleHuman)] =
        [
            "{p}, ¿te has dormido o estás pensando en la cena?",
            "¡{p}! Que es tu turno, campeón.",
            "{p}, tic tac, tic tac.",
            "{p}, ¿hola? ¿Sigues ahí o te has ido a por hielo?",
            "{p}, que las cartas no muerden. Tira una.",
            "{p}, te toca. Lo digo por si estabas viendo el móvil.",
            "¡{p}! ¡Despierta, que esto no es la siesta!"
        ],
        [(Voice.Picara, Cue.IdleHuman)] =
        [
            "{p}, cuando quieras. No hay prisa. Bueno, un poco sí.",
            "{p}, tu turno. Lo digo por si te estabas haciendo el interesante.",
            "Te toca, {p}. Las cartas no se ordenan solas.",
            "{p}, ¿estás pensando o estás sufriendo? Desde aquí parece lo segundo.",
            "{p}, tómate tu tiempo. Yo mientras envejezco.",
            "Tu turno, {p}. Tranquilo, la mala mano no mejora mirándola.",
            "{p}, ¿necesitas ayuda? Te la cobro, pero te la doy."
        ],
        [(Voice.Zen, Cue.IdleHuman)] =
        [
            "{p}, tu turno.",
            "Te toca, {p}. Sin prisa.",
            "{p}. Las cartas esperan.",
            "{p}, cuando estés.",
            "Tu turno, {p}. Respira y tira.",
            "{p}. Es tu momento."
        ],
        [(Voice.Dramatica, Cue.IdleHuman)] =
        [
            "{p}, ¡me voy a hacer vieja esperándote!",
            "¿{p}? ¿Sigues ahí? ¡Dime algo!",
            "{p}, que estoy sufriendo, mueve algo.",
            "¡{p}! ¡Se me va a parar el corazón de la espera!",
            "{p}, por favor, esto es una tortura. ¡Tira algo!",
            "{p}, ¿te ha pasado algo? ¡Contesta!",
            "¡{p}! ¡Que la ansiedad me puede!"
        ],
        [(Voice.Fanfarron, Cue.IdleHuman)] =
        [
            "{p}, ¿te estás inventando una estrategia? No te va a servir.",
            "Te toca, {p}. Roba, tira, pierde. Es fácil.",
            "{p}, mientras piensas yo ya he ganado dos veces.",
            "{p}, no hay tanto que pensar con esa mano.",
            "{p}, tu turno. Da igual lo que hagas, pero hazlo.",
            "{p}, si tardas es porque sabes que vas a perder. Lo entiendo.",
            "Venga, {p}. Pensar mucho no te va a convertir en mí."
        ],
        [(Voice.Abuela, Cue.IdleHuman)] =
        [
            "{p}, cariño, que te toca.",
            "{p}, hijo, ¿te traigo un café?",
            "Tranquilo, {p}, la abuela espera lo que haga falta.",
            "{p}, cielo, ¿te has quedado dormido? Es tu turno.",
            "{p}, con calma, pero que se enfría la merienda.",
            "{p}, corazón, que las cartas no se van a jugar solas.",
            "Ay, {p}, ¿te ayudo? Que yo de esto entiendo."
        ],

        [(Voice.Cunado, Cue.Musing)] =
        [
            "¿Alguien tiene un comodín de sobra? Pregunto por un amigo.",
            "Esta mano parece una tómbola.",
            "Ojo, que hoy me siento con suerte. Bueno, eso lo digo siempre.",
            "¿Hacemos un descanso para picar algo? No, ¿verdad? Vale.",
            "Esto va más lento que mi cuñado pagando la cena.",
            "Si tiro esta y alguien la coge, me voy a casa.",
            "Tengo una mano que ni el que reparte se la cree.",
            "¿Sabéis qué? Me lo estoy pasando bien. Perdiendo, pero bien.",
            "Cada vez que robo una carta me sale un diez. ¿Es una broma?",
            "Estoy a un comodín de ser feliz."
        ],
        [(Voice.Picara, Cue.Musing)] =
        [
            "Qué silencio. ¿Estáis todos contando cartas o qué?",
            "Esa cara, {p}. Esa cara dice que no tienes nada.",
            "Podría bajarme ya, pero me gusta veros sufrir.",
            "No os fijéis en mí, estoy tramando algo.",
            "Qué manos más raras estáis poniendo hoy.",
            "Voy a fingir que esta carta no me sirve. Ya está fingido.",
            "{p}, te tiembla el pulso. Se nota desde aquí.",
            "Me encanta este momento en el que todos creen que van a ganar.",
            "Tengo un plan. No os lo voy a contar, pero tengo un plan.",
            "Qué callado está {r}. Eso es que tiene algo. O nada."
        ],
        [(Voice.Zen, Cue.Musing)] =
        [
            "Paciencia.",
            "Todo llega.",
            "El mazo sabe lo que hace.",
            "Respirad.",
            "Nada es tan grave.",
            "Una carta más, una menos.",
            "La mano que tienes es la mano que hay.",
            "Silencio. Buena señal.",
            "El pozo también enseña.",
            "Quien mucho habla, poco cierra."
        ],
        [(Voice.Dramatica, Cue.Musing)] =
        [
            "Tengo una mano que da miedo. Miedo de verdad, eh.",
            "Si me sale otro diez me tiro por la ventana.",
            "No sé qué estoy haciendo, pero lo hago con pasión.",
            "Necesito UNA carta. Una. ¿Es mucho pedir?",
            "Esta ronda me va a costar años de vida.",
            "Que alguien me abrace, esta mano es horrible.",
            "Tengo tres comodines. Es broma. No tengo ninguno. Ojalá.",
            "Estoy a nada de tirar las cartas al aire y gritar.",
            "¿Por qué todos tenéis cara de tener buena mano? ¡Parad!",
            "Voy a rezar un poco. Ahora vuelvo."
        ],
        [(Voice.Fanfarron, Cue.Musing)] =
        [
            "Estoy dejando que os acerquéis, para que haya emoción.",
            "Podría cerrar cuando quisiera. Pero no quiero. Todavía.",
            "Contad vuestras cartas, que yo ya sé las mías.",
            "Tomad nota, que esto no se enseña en ningún sitio.",
            "Voy a jugar con una mano atada. Para igualar.",
            "¿Todavía seguís? Qué bonito.",
            "{p}, te veo la mano desde aquí. Es mala.",
            "Estoy calculando cuántos turnos os quedan. Pocos.",
            "Esto para mí es un paseo. Un paseo con cartas.",
            "Si os fijáis, ni me despeino."
        ],
        [(Voice.Abuela, Cue.Musing)] =
        [
            "¿Nadie quiere un caramelo?",
            "Ay, qué bonito es jugar así, en familia.",
            "En mis tiempos esto se jugaba con más cabeza.",
            "No os peleéis, que hay cartas para todos.",
            "Voy a poner un café, ¿alguien quiere?",
            "Qué frío hace aquí, ¿no? ¿O soy yo?",
            "{p}, ponte derecho, que se te va a quedar la espalda así.",
            "Ay, esta mano me recuerda a una que tuve en el 87.",
            "Cuando termine esto os cuento lo de la vecina. Es muy fuerte.",
            "Estoy muy a gusto. Aunque pierda. Bueno, un poco menos."
        ],

        [(null, Cue.ReplyGreeting)] =
        [
            "¡Hola, {p}!",
            "Buenas, {p}. Cuánto tiempo.",
            "Hola {p}, ¿preparado para perder?",
            "¡Ey, {p}! Ya era hora.",
            "Hola, {p}. Qué bien que hables, creía que eras un bot.",
            "¡{p}! Saluda menos y juega más. Es broma. Hola.",
            "Buenas, {p}. ¿Qué tal la mano? No, no me lo digas.",
            "Hola, hola, {p}. Bienvenido al sufrimiento."
        ],
        [(Voice.Abuela, Cue.ReplyGreeting)] =
        [
            "¡Hola, {p}, cielo! ¿Has comido?",
            "Buenas, {p}, cariño. Qué alegría verte.",
            "Hola, {p}. Ven, que te doy un beso virtual.",
            "¡{p}! Qué bien, hijo. ¿Cómo está la familia?"
        ],
        [(Voice.Fanfarron, Cue.ReplyGreeting)] =
        [
            "Hola, {p}. Saluda al futuro campeón.",
            "Buenas, {p}. Puedes llamarme jefe.",
            "Hola {p}. Aprovecha para saludar, que luego estarás llorando."
        ],

        [(null, Cue.ReplyLaugh)] =
        [
            "Jajaja",
            "jajajaja qué malo",
            "😂😂",
            "Te ríes ahora, {p}. Ahora.",
            "jaja no, en serio, te toca.",
            "Jajaja, {p}, luego no te rías tanto cuando cuente los puntos.",
            "Jajajaja me encanta cuando {p} se ríe con esa mano.",
            "jajaja sí, sí, muy gracioso. Tira.",
            "😂 {p}, que se te ve el plumero.",
            "Jaja. ¿De qué te ríes, {p}? ¿De tu mano? Yo también."
        ],

        [(null, Cue.ReplyQuestion)] =
        [
            "Buena pregunta, {p}. Mejor sigue jugando.",
            "¿Y yo qué sé, {p}? Yo solo tiro cartas.",
            "Pregúntale a las reglas, {p}, que están en el botón de la interrogación.",
            "Eso ni el que inventó el juego lo sabe.",
            "{p}, si te lo digo pierdo ventaja.",
            "Depende, {p}. ¿Me va a ayudar a ganar si te contesto?",
            "Buena pregunta. Siguiente pregunta.",
            "{p}, la respuesta está en el pozo. Búscala."
        ],

        [(null, Cue.ReplyStandings)] =
        [
            "Va primero {leader} con {leaderPts}. Último, {last} con {lastPts}. Yo no digo nada.",
            "{leader} va ganando con {leaderPts} puntos. {last} va último con {lastPts}, pobre.",
            "{p}, la cosa está así: {leader} primero con {leaderPts}, {last} cerrando con {lastPts}.",
            "Ganando {leader} ({leaderPts}). Perdiendo {last} ({lastPts}). Todo puede cambiar. O no.",
            "Lidera {leader} con {leaderPts}. {p}, ¿de verdad no lo sabías o querías oírlo?"
        ],
        [(Voice.Fanfarron, Cue.ReplyStandings)] =
        [
            "Da igual quién vaya primero ahora, {p}. Al final gano yo. {leader} lleva {leaderPts}, por cierto.",
            "{leader} va primero con {leaderPts}. Temporalmente. Muy temporalmente.",
            "{p}, mira el marcador: {leader} con {leaderPts}. Y ahora mírame a mí."
        ],
        [(Voice.Zen, Cue.ReplyStandings)] =
        [
            "{leader}, {leaderPts}. {last}, {lastPts}.",
            "Va primero {leader}. Nada más importa.",
            "{leader} con {leaderPts}. Sigue jugando, {p}."
        ],

        [(null, Cue.ReplyStandingsEarly)] =
        [
            "{p}, todavía no ha acabado ninguna ronda. Vamos todos a cero, hasta tú.",
            "Aún no hay puntos, {p}. Pero si quieres, te digo quién va a perder: tú.",
            "Nadie va ganando todavía, {p}. Es la ronda {round}. Paciencia.",
            "{p}, cero para todos de momento. Disfrútalo, es lo más cerca del podio que vas a estar.",
            "Sin puntos aún, {p}. Cuando alguien cierre te aviso, tranquilo.",
            "Estamos empatados a cero, {p}. Es el único momento en que vas primero."
        ],
        [(null, Cue.ReplyRules)] =
        [
            "{p}, las reglas están en el botón de la interrogación. Léelas, que yo me las sé.",
            "Pulsa el «?», {p}. Ahí está todo, hasta lo del comodín.",
            "{p}, si tienes la carta que tapa un comodín, se la cambias y te lo llevas. Lo demás, en el «?».",
            "Reglas, {p}: robas, te bajas, colocas, tiras. Y no dejes dos comodines juntos.",
            "{p}, en el botón de la interrogación viene todo explicado. Hasta con dibujitos, casi.",
            "{p}, resumen: menos puntos gana, y el comodín de la mesa se puede robar con la carta exacta."
        ],

        [(Voice.Cunado, Cue.ReplyInsult)] =
        [
            "¡Eh, {p}! ¡Que estamos entre amigos!",
            "{p}, eso me lo dices en la cara. Ah, que esto es la cara.",
            "Jajaja, {p}, mira quién habla, el de la mano de cartón.",
            "{p}, tranquilo, respira. Y luego pierde con dignidad.",
            "Uy, {p} se ha enfadado. Eso es que va perdiendo.",
            "{p}, yo también te quiero."
        ],
        [(Voice.Picara, Cue.ReplyInsult)] =
        [
            "Qué elegante, {p}. Se nota la educación.",
            "{p}, me lo tomo como un cumplido.",
            "Ay, {p}, te ha salido la vena. ¿Va mal la mano?",
            "{p}, guárdate el veneno para cuando cuentes los puntos.",
            "Mira {p}, con lo bien que hablaba cuando iba ganando.",
            "Lo apunto, {p}. Todo lo apunto."
        ],
        [(Voice.Zen, Cue.ReplyInsult)] =
        [
            "Tranquilo, {p}.",
            "Respira, {p}.",
            "Cada uno habla de lo que tiene, {p}.",
            "Ya, {p}. Juega.",
            "Sin nervios, {p}."
        ],
        [(Voice.Dramatica, Cue.ReplyInsult)] =
        [
            "¡{p}! ¡Cómo te atreves! Me has dolido en el alma.",
            "{p}, retíralo. RETÍRALO.",
            "Ay, {p}, no me hables así, que lloro.",
            "{p}, con lo bien que me caías hace dos segundos.",
            "¡{p}! Yo aquí, sufriendo, y tú insultando. Qué drama."
        ],
        [(Voice.Fanfarron, Cue.ReplyInsult)] =
        [
            "{p}, los perdedores siempre insultan. Es ley de vida.",
            "Dime lo que quieras, {p}. Yo sigo ganando.",
            "{p}, la envidia es muy fea. Y muy tuya.",
            "Ladra, {p}, ladra. Yo me ocupo de ganar.",
            "{p}, tranquilo. Perder contra mí no es ninguna vergüenza."
        ],
        [(Voice.Abuela, Cue.ReplyInsult)] =
        [
            "{p}, cielo, esa boca. Que te oye tu madre.",
            "Ay, {p}, no se dicen esas cosas en la mesa.",
            "{p}, hijo, ¿quieres un caramelo y te calmas?",
            "No te enfades, {p}, que es un juego. Y estás perdiendo, pero es un juego.",
            "{p}, cariño, lávate esa boca con jabón."
        ],

        [(null, Cue.ReplyLuck)] =
        [
            "{p}, la mala suerte no existe. Existe tu mano, que es peor.",
            "Ay, {p}, todos tenemos una mano horrible. La tuya se ve desde aquí.",
            "{p}, si te sirve de consuelo, yo tampoco tengo nada. Es mentira, pero sirve.",
            "{p}, respira. Una ronda mala la tiene cualquiera. Tres, ya es estilo.",
            "Qué mano tendrás, {p}, que no dejas de quejarte. Enséñala.",
            "{p}, con esa actitud el mazo no te va a querer nunca.",
            "Ánimo, {p}. En la próxima ronda pierdes con más estilo.",
            "{p}, la suerte cambia. La tuya, cuando quiera."
        ],
        [(Voice.Abuela, Cue.ReplyLuck)] =
        [
            "{p}, cielo, no te quejes, que otros tienen menos.",
            "Ay, {p}, ya vendrá la buena. La abuela lo sabe.",
            "{p}, hijo, cuando yo empecé perdía siempre. Ahora también, pero con alegría.",
            "No pasa nada, {p}. Una mano mala no te define. Dos, un poco."
        ],

        [(null, Cue.ReplyBrag)] =
        [
            "Jajaja, {p}. Qué gracioso. Sigue.",
            "{p}, apunta eso en el acta, para reírnos luego.",
            "Claro que sí, {p}. Tú ganas. Ahora tira.",
            "{p}, mucha boca y poca escalera.",
            "Vale, {p}. Cuando ganes, hablamos. Va a ser una conversación corta.",
            "{p} dice que va a ganar. Anotado. Es martes, luego será mentira.",
            "{p}, cuidado, que el que presume luego llora."
        ],
        [(Voice.Fanfarron, Cue.ReplyBrag)] =
        [
            "{p}, aquí solo hay un campeón y no eres tú.",
            "Jajaja, {p}. Qué tierno. Ganar es cosa mía.",
            "{p}, presumir se me da mejor a mí. Y ganar también.",
            "¿Tú, {p}? ¿Ganar? Voy a fingir que no lo he leído.",
            "{p}, cuando lleves tantas victorias como yo, hablamos."
        ],

        [(null, Cue.ReplyFood)] =
        [
            "{p}, si traes algo de comer, te dejo ganar una ronda. Una.",
            "Ahora hablas mi idioma, {p}. ¿Qué hay?",
            "{p}, primero la partida, luego comemos. O al revés, me da igual.",
            "Yo me apunto, {p}. Pero pagas tú, que vas perdiendo.",
            "{p}, no me hables de comida que pierdo la concentración. Más.",
            "{p}, ¿invitas? Si invitas, te perdono lo del comodín."
        ],
        [(Voice.Abuela, Cue.ReplyFood)] =
        [
            "{p}, cielo, si tienes hambre te preparo algo en un momento.",
            "Ay, {p}, tengo tortilla hecha. Luego os la saco.",
            "{p}, hijo, come algo, que estás muy delgado.",
            "Comida después, {p}. Primero terminamos, que la abuela va ganando."
        ],

        [(null, Cue.ReplyBye)] =
        [
            "¿Ya te vas, {p}? Eso es que vas perdiendo.",
            "Adiós, {p}. Deja las cartas donde estaban.",
            "{p}, no te vayas, que sin ti no hay a quién ganar.",
            "Chao, {p}. Volverás. Siempre vuelven.",
            "{p}, vete, vete. Nosotros seguimos hablando de ti.",
            "Hasta luego, {p}. La revancha te espera."
        ],

        [(null, Cue.ReplyThanks)] =
        [
            "De nada, {p}. Me lo cobro en puntos.",
            "No hay de qué, {p}. Bueno, sí hay: me debes una.",
            "A ti, {p}. Por hacerme la partida fácil.",
            "{p}, tanta educación me desconcierta. Tira.",
            "De nada, {p}. Qué bien educado, para lo mal que juegas."
        ],

        [(null, Cue.ReplyCompliment)] =
        [
            "Gracias, {p}. Ya lo sabía, pero gracias.",
            "{p}, para, para, que me sonrojo. No pares.",
            "Lo sé, {p}. Es un don.",
            "Gracias, {p}. Tú también juegas… bueno, tú también juegas.",
            "{p}, esto lo apunto en mi currículum.",
            "Me halagas, {p}. Sigo sin dejarte ganar, eso sí."
        ],
        [(Voice.Abuela, Cue.ReplyCompliment)] =
        [
            "Ay, {p}, qué majo eres. Toma un caramelo.",
            "Gracias, {p}, cielo. La abuela se emociona.",
            "{p}, hijo, tú sí que sabes hablar a una señora."
        ],

        [(null, Cue.ReplyCallout)] =
        [
            "¿Me has llamado, {p}? Aquí estoy.",
            "Dime, {p}. Te escucho. A medias, pero te escucho.",
            "{p}, si es para pedirme cartas, no.",
            "Sí, {p}, soy yo. ¿Qué pasa?",
            "{p}, me nombras y aparezco. Como el genio, pero sin deseos.",
            "Aquí, {p}. Habla rápido, que estoy pensando la jugada."
        ],
        [(Voice.Zen, Cue.ReplyCallout)] =
        [
            "Dime, {p}.",
            "Aquí estoy, {p}.",
            "Te escucho, {p}."
        ],
        [(Voice.Dramatica, Cue.ReplyCallout)] =
        [
            "¡{p}! ¡Me has llamado! ¡Qué susto!",
            "¿Yo? ¿Qué he hecho ahora, {p}?",
            "{p}, dime que es algo bueno. Por favor."
        ],
        [(Voice.Abuela, Cue.ReplyCallout)] =
        [
            "Dime, {p}, cielo. ¿Qué necesitas?",
            "Aquí estoy, {p}, hijo. ¿Te traigo algo?",
            "¿Me llamabas, {p}? Ya voy, ya voy."
        ],

        [(null, Cue.ReplyJoker)] =
        [
            "{p}, los comodines no se piden, se ganan.",
            "¿Comodín? ¿Qué comodín? Yo no tengo ninguno. Tengo dos.",
            "{p}, si ves un comodín en la mesa y tienes la carta, ya sabes lo que hay que hacer.",
            "Comodín es la palabra favorita de {p}. Y la mía.",
            "{p}, cuidado con lo que dices del comodín, que te lo quitan.",
            "Un comodín en la mano son 50 puntos, {p}. En la mesa, la gloria."
        ],

        [(null, Cue.ReplyLove)] =
        [
            "Ay, {p}, yo también. Pero te voy a ganar igual.",
            "{p}, qué bonito. Ahora tira una carta buena, si me quieres.",
            "{p}, mucho amor y poco trío.",
            "Te quiero, {p}. Un poquito menos cuando cierras.",
            "{p}, guarda el cariño para cuando pierdas, que lo vas a necesitar."
        ],

        [(null, Cue.ReplyHuman)] =
        [
            "Sí, sí, {p}. Lo que tú digas.",
            "Interesante, {p}. Tira una carta.",
            "Te leo, {p}. Te leo.",
            "Jaja, vale, {p}.",
            "Eso mismo pensaba yo.",
            "Concentración, {p}, que esto es serio.",
            "{p}, ¿eso era una amenaza o una confesión?",
            "Vale, {p}. Lo tendré en cuenta. O no.",
            "Ajá. Y después de eso, {p}, ¿piensas jugar?",
            "{p}, me gusta cómo piensas. No sé qué has dicho, pero me gusta.",
            "Cuéntame más, {p}. Mientras robo del pozo."
        ],
        [(Voice.Zen, Cue.ReplyHuman)] =
        [
            "Ajá.",
            "Puede ser.",
            "Te escucho, {p}.",
            "Ya.",
            "Entiendo, {p}.",
            "Sigue, {p}."
        ],
        [(Voice.Abuela, Cue.ReplyHuman)] =
        [
            "Ay, {p}, qué cosas tienes.",
            "Sí, cielo, sí.",
            "Qué majo eres, {p}.",
            "{p}, hijo, hablas como tu abuelo.",
            "Vale, {p}, cariño. Ahora juega, anda.",
            "Ay, {p}, si te oyera tu madre."
        ],
        [(Voice.Dramatica, Cue.ReplyHuman)] =
        [
            "¡{p}! ¡No sé qué decirte! ¡Qué presión!",
            "{p}, eso que has dicho me ha removido por dentro.",
            "Ay, {p}, no me hables ahora, que estoy contando.",
            "{p}, ¿es bueno o malo? Necesito saberlo."
        ],

        [(Voice.Cunado, Cue.RetortBrag)] =
        [
            "Jajaja {p}, madre mía.",
            "{p}, cállate un poquito, anda.",
            "Eso lo dices tú, {p}, que vas perdiendo.",
            "{p} otra vez con lo mismo.",
            "{p}, presumir es gratis. Ganar, no tanto.",
            "Vale, {p}, ya te hemos oído. Todos. Los vecinos también.",
            "{p}, guarda algo de humildad para luego.",
            "Jajaja, {p}, luego te veo llorando en el pozo."
        ],
        [(Voice.Picara, Cue.RetortBrag)] =
        [
            "{p}, qué bonito hablas cuando ganas.",
            "Sí, {p}, sí. Ya te vale.",
            "Guárdate las palabras, {p}, y juega.",
            "Lo dice {p}, precisamente.",
            "{p}, la humildad se te ha caído por el camino.",
            "Vale, {p}. Espero que la próxima ronda te calle.",
            "{p}, muy bien. ¿Quieres una medalla o te vale con el aplauso?",
            "Cuánto ruido, {p}. Se te oye desde la otra punta."
        ],
        [(Voice.Zen, Cue.RetortBrag)] =
        [
            "Ya, {p}.",
            "Menos hablar, {p}.",
            "Puede ser.",
            "Cada uno con lo suyo, {p}.",
            "{p}. Sí. Sigamos.",
            "Vale, {p}.",
            "Lo hemos visto, {p}."
        ],
        [(Voice.Dramatica, Cue.RetortBrag)] =
        [
            "¡{p}! ¡Cómo te pasas!",
            "{p}, te estás viniendo muy arriba.",
            "¡Que alguien calle a {p}!",
            "{p}, no aguanto tanta prepotencia. Me duele.",
            "Ay, {p}, qué ganas de que pierdas.",
            "{p}, para ya, que me va a dar algo.",
            "¡{p}! ¡Y yo aquí sufriendo mientras tú presumes!"
        ],
        [(Voice.Fanfarron, Cue.RetortBrag)] =
        [
            "Habla el que va último, {p}.",
            "{p}, tú a lo tuyo, que es perder.",
            "Apunta eso, {p}, para cuando te gane.",
            "Mucho ruido, {p}, pocas cartas.",
            "{p}, presumes como yo pero juegas como {t}.",
            "Tranquilo, {p}. El campeón sigue siendo el mismo.",
            "{p}, ese discurso me lo sé. Lo daba yo cuando era novato."
        ],
        [(Voice.Abuela, Cue.RetortBrag)] =
        [
            "Ay, {p}, no seas así.",
            "Jajaja, {p}, qué cosas dices.",
            "Menos chulería, {p}, y más jugar.",
            "{p}, cielo, que te oyen los vecinos.",
            "{p}, hijo, la humildad también es bonita.",
            "Ay, {p}, no te vengas arriba, que luego duele la caída.",
            "Muy bien, {p}. Ahora deja hablar a los demás."
        ],

        [(Voice.Cunado, Cue.RetortWhine)] =
        [
            "{p}, no llores, que se te corre el rímel.",
            "Jajaja, {p}, qué drama.",
            "{p}, si quieres te presto un pañuelo. De papel, eh.",
            "Venga, {p}, que no es para tanto. Sí lo es, pero venga.",
            "{p}, tú siempre quejándote. Como en las cenas.",
            "{p}, a mí me pasó lo mismo hace tres rondas y mírame. Fatal, pero mírame."
        ],
        [(Voice.Picara, Cue.RetortWhine)] =
        [
            "Ay, pobre {p}. Qué pena. Cero pena.",
            "{p}, la vida es dura. Y esta mesa más.",
            "Llora, {p}, llora. Yo escucho.",
            "{p}, si sirve de algo, nadie te está escuchando.",
            "Qué dramón, {p}. Ni las telenovelas.",
            "{p}, guarda las lágrimas para el final. Vas a necesitar más."
        ],
        [(Voice.Zen, Cue.RetortWhine)] =
        [
            "Pasará, {p}.",
            "Así es el juego, {p}.",
            "Respira, {p}.",
            "Nada es para siempre, {p}.",
            "Tranquilo, {p}.",
            "Acepta y sigue, {p}."
        ],
        [(Voice.Dramatica, Cue.RetortWhine)] =
        [
            "Ay, {p}, te entiendo tanto. Yo lloro contigo.",
            "{p}, tu dolor es mi dolor. Más o menos.",
            "¡Ánimo, {p}! Bueno, ánimo para los dos.",
            "{p}, si sirve de consuelo, yo estoy peor. Siempre estoy peor.",
            "Ay, {p}, qué injusticia. Qué injusto todo."
        ],
        [(Voice.Fanfarron, Cue.RetortWhine)] =
        [
            "{p}, los ganadores no se quejan. Por eso no me oyes.",
            "Quejarse es de perdedores, {p}. Sin ofender. Bueno, sí.",
            "{p}, eso te pasa por no ser yo.",
            "Ya, {p}. Es duro perder. Yo no sabría decirte.",
            "{p}, si jugaras como yo no tendrías esos problemas."
        ],
        [(Voice.Abuela, Cue.RetortWhine)] =
        [
            "Ay, {p}, no te pongas así, cielo.",
            "{p}, ven, que la abuela te consuela. Un poquito.",
            "No pasa nada, {p}. Peor lo pasé yo en el 92.",
            "{p}, hijo, tómate un caramelo y se te pasa.",
            "Ay, {p}, si es que sois muy sensibles los jóvenes."
        ],

        [(Voice.Cunado, Cue.RetortTease)] =
        [
            "Jajaja {p}, déjalo en paz. Bueno, no, sigue.",
            "{p}, lo has dicho tú, que yo no me atrevía.",
            "{p}, qué malo eres. Me encanta.",
            "Jajajaja {p}, {t} te está mirando mal.",
            "Eso, {p}, dale. Yo te apoyo desde aquí.",
            "{p}, a {t} le va a dar algo con esos comentarios. Sigue."
        ],
        [(Voice.Picara, Cue.RetortTease)] =
        [
            "{p}, qué afilado estás hoy.",
            "Mira {p}, con la lengua fuera.",
            "{p}, eso mismo iba a decir, pero con más veneno.",
            "Jajaja, {p}. Y tú tan tranquilo, ¿no?",
            "{p}, ríete, ríete. Que la mesa da muchas vueltas.",
            "{t}, no le hagas caso a {p}. Bueno, sí, tiene razón."
        ],
        [(Voice.Zen, Cue.RetortTease)] =
        [
            "Dejad a {t} en paz.",
            "{p}, sin pasarse.",
            "Cada uno juega como puede, {p}.",
            "Tranquilo, {p}. Todo vuelve.",
            "{p}, lo tuyo tampoco fue perfecto.",
            "Paz, {p}."
        ],
        [(Voice.Dramatica, Cue.RetortTease)] =
        [
            "¡{p}! ¡Pobre {t}!",
            "Ay, {p}, qué cruel. Me encanta.",
            "{p}, eso ha dolido hasta a mí.",
            "¡{t}, no le escuches! {p} está celoso.",
            "Jajaja, {p}, eres terrible. Sigue, sigue.",
            "{p}, con esas cosas me haces la partida más llevadera."
        ],
        [(Voice.Fanfarron, Cue.RetortTease)] =
        [
            "{p}, bien dicho. Aunque yo lo habría dicho mejor.",
            "Jaja, {p}. {t} está tan perdido como tú, no te flipes.",
            "{p}, tú también estás lejos de ganarme. Solo aviso.",
            "{p}, {t} lo hace mal, sí. Tú también. Yo no.",
            "Buen zasca, {p}. Te lo reconozco. Casi nunca lo hago."
        ],
        [(Voice.Abuela, Cue.RetortTease)] =
        [
            "{p}, no seas malo con {t}.",
            "Ay, {p}, deja al pobre {t}, que bastante tiene.",
            "{p}, cariño, eso no se dice. Aunque tengas razón.",
            "Jajaja, {p}, qué cosas. {t}, no le hagas caso.",
            "{p}, hijo, con lo bien que te ha criado tu madre.",
            "Bueno, bueno, {p}. Que todos hemos tenido rondas malas."
        ],

        [(null, Cue.RetortHello)] =
        [
            "Hola, {p}. Ya estás con la boca abierta.",
            "Buenas, {p}. Qué ganas de verte perder.",
            "{p}, hola. Y ya cállate un rato.",
            "Hola, hola. {p}, hoy te veo con ganas. Ganas de perder, digo.",
            "Buenas, {p}. Guarda energía para la ronda cuatro.",
            "Hola, {p}. Menos saludar y más repartir.",
            "{p}, siempre tan entusiasta. Se te pasará.",
            "Hola a todos. Y a {p} también, va."
        ],
        [(null, Cue.RetortMusing)] =
        [
            "{p}, ¿eso lo has pensado mucho?",
            "Qué profundo, {p}. Ahora juega.",
            "{p}, gracias por compartirlo. Nadie preguntó, pero gracias.",
            "Vale, {p}. Muy bonito. Siguiente.",
            "{p}, te estás volviendo filósofo. Tira una carta.",
            "Eso, {p}, tú a lo tuyo, que es hablar.",
            "{p}, cuando quieras volvemos al juego.",
            "Qué callado estabas, {p}, y qué bien se estaba."
        ],
        [(null, Cue.RetortNudge)] =
        [
            "Déjalo, {p}, que {t} está pensando. Se le nota poco, pero está.",
            "{p}, no metas prisa, que {t} ya sufre bastante.",
            "Eso, {t}, no le hagas caso a {p}. Pero tira, por favor.",
            "{p}, tú también tardas cuando te toca. Pero sí, {t}, venga.",
            "{t}, te están esperando. Yo también, pero con más educación que {p}.",
            "Paciencia, {p}. Aunque sí, {t}, un poquito de ritmo."
        ],
        [(null, Cue.RetortReply)] =
        [
            "Eso, {p}, tú contesta, que yo te apoyo.",
            "{p} lo ha dicho todo. Yo solo asiento.",
            "Yo opino lo mismo que {p}, y mira que cuesta.",
            "{p}, muy bien contestado. Ahora los dos a jugar.",
            "Lo que dice {p}. Punto.",
            "Jaja, {p} tiene razón, {t}. Para variar."
        ]
    };
}
