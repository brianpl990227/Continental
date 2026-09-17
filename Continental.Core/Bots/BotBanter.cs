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
    IJokerSwapped,
    MyJokerTaken,
    TheyJokerSwapped,
    TheyExtendedMine,
    OneCardLeft,
    IHaveOneCard,
    IClosed,
    TheyClosed,
    IWasWorst,
    TheyWereWorst,
    NobodyClosed,
    IWonGame,
    TheyWonGame,
    ILostGame,
    StockRecycled,
    JokerDiscarded,
    IdleHuman,
    ReplyGreeting,
    ReplyLaugh,
    ReplyQuestion,
    ReplyHuman,
    Retort,
    Musing
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
    private readonly List<BanterLine> _queue = [];
    private DateTimeOffset _lastDue = DateTimeOffset.MinValue;
    private string? _nudgedTurn;

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
                Say(state, Pick(bots), Cue.Hello, now, 1.2, 2.8, chance: 1.0, retort: 0.55);
                break;

            case GameEventKind.RoundStarted:
                if (state.RoundIndex > 0)
                    Say(state, Pick(bots), Cue.RoundStart, now, 1.5, 3.5, chance: 0.35, retort: 0.2);
                break;

            case GameEventKind.LaidDown when actor is not null:
                if (actor.IsBot)
                    Say(state, actor, Cue.ILaidDown, now, 0.8, 2.0, chance: 0.6, retort: 0.35);
                else
                    Say(state, Pick(bots), Cue.TheyLaidDown, now, 1.0, 2.6, chance: 0.7, retort: 0.3, subject: actor);
                break;

            case GameEventKind.Stole when actor is not null:
                if (actor.IsBot)
                    Say(state, actor, Cue.IStole, now, 0.7, 1.8, chance: 0.55, retort: 0.3, card: e.Card);
                else
                    Say(state, Pick(bots), Cue.TheyStole, now, 1.0, 2.4, chance: 0.6, retort: 0.25, subject: actor, card: e.Card);

                if (target is { IsBot: true } && target.Id != actor.Id)
                    Say(state, target, Cue.IWasBlocked, now, 2.2, 4.0, chance: 0.5, retort: 0, subject: actor, card: e.Card);
                break;

            case GameEventKind.TookDiscard when actor is { IsBot: true }:
                Say(state, actor, Cue.IPicked, now, 0.5, 1.4, chance: 0.1, retort: 0.1, card: e.Card);
                break;

            case GameEventKind.JokerSwapped when actor is not null:
            {
                var victim = target is not null && target.Id != actor.Id ? target : null;

                if (actor.IsBot)
                    Say(state, actor, Cue.IJokerSwapped, now, 0.6, 1.6, chance: victim is null ? 0.4 : 0.85, retort: 0.4, subject: victim, card: e.Card);
                else
                    Say(state, Pick(bots, except: victim), Cue.TheyJokerSwapped, now, 1.0, 2.4, chance: victim is null ? 0.4 : 0.75, retort: 0.3, subject: actor, card: e.Card);

                if (victim is { IsBot: true })
                    Say(state, victim, Cue.MyJokerTaken, now, 1.8, 3.6, chance: 0.9, retort: 0.25, subject: actor, card: e.Card);
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
                    if (actor.IsBot)
                        Say(state, actor, Cue.IHaveOneCard, now, 0.8, 2.0, chance: 0.5, retort: 0.4);
                    else
                        Say(state, Pick(bots), Cue.OneCardLeft, now, 1.0, 2.4, chance: 0.65, retort: 0.35, subject: actor);

                    break;
                }

                Say(state, Pick(bots), Cue.Musing, now, 1.5, 4.0, chance: 0.05, retort: 0.3);
                break;
            }

            case GameEventKind.RoundEnded:
            {
                var closer = actor;

                if (closer is null)
                    Say(state, Pick(bots), Cue.NobodyClosed, now, 1.0, 2.5, chance: 0.9, retort: 0.3);
                else if (closer.IsBot)
                    Say(state, closer, Cue.IClosed, now, 0.8, 2.0, chance: 0.9, retort: 0.45);
                else
                    Say(state, Pick(bots), Cue.TheyClosed, now, 1.0, 2.6, chance: 0.75, retort: 0.3, subject: closer);

                var scored = state.Players.Where(p => p.RoundScores.Count > 0).ToList();

                if (scored.Count > 1)
                {
                    var worstPoints = scored.Max(p => p.RoundScores[^1]);
                    var worst = scored.Where(p => p.RoundScores[^1] == worstPoints).ToList();

                    if (worst.Count == 1 && worstPoints >= 20)
                    {
                        var loser = worst[0];

                        if (loser.IsBot)
                            Say(state, loser, Cue.IWasWorst, now, 3.5, 6.0, chance: 0.6, retort: 0.3, points: worstPoints);
                        else
                            Say(state, Pick(bots), Cue.TheyWereWorst, now, 3.5, 6.0, chance: 0.7, retort: 0.3, subject: loser, points: worstPoints);
                    }
                }

                break;
            }

            case GameEventKind.GameOver:
            {
                var winner = actor;

                if (winner is { IsBot: true })
                    Say(state, winner, Cue.IWonGame, now, 1.0, 2.2, chance: 1.0, retort: 0.6);
                else if (winner is not null)
                    Say(state, Pick(bots), Cue.TheyWonGame, now, 1.0, 2.4, chance: 1.0, retort: 0.4, subject: winner);

                var loser = state.Players.OrderByDescending(p => p.TotalScore).FirstOrDefault();

                if (loser is { IsBot: true } && loser.Id != winner?.Id)
                    Say(state, loser, Cue.ILostGame, now, 4.0, 6.5, chance: 0.7, retort: 0.2);
                break;
            }

            case GameEventKind.StockRecycled:
                Say(state, Pick(bots), Cue.StockRecycled, now, 1.2, 2.8, chance: 0.4, retort: 0.2);
                break;

            case GameEventKind.ChatSaid when actor is { IsBot: false } && e.Text is { } text:
                Say(state, Pick(bots), CueForHuman(text), now, 1.8, 4.5, chance: 0.6, retort: 0.15, subject: actor);
                break;
        }
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
            Say(state, Pick(bots), Cue.IdleHuman, now, 0.3, 1.2, chance: 1.0, retort: 0.3, subject: human);
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
                     double chance, double retort, PlayerState? subject = null, Card? card = null, int points = 0)
    {
        if (bot is null || _queue.Count >= MaxQueued || random.NextDouble() > chance)
            return;

        if (_lastSpoke.TryGetValue(bot.Id, out var spoke) && now - spoke < BotGap && cue is not (Cue.MyJokerTaken or Cue.IdleHuman or Cue.IWonGame))
            return;

        var delay = TimeSpan.FromSeconds(minDelay + random.NextDouble() * (maxDelay - minDelay));
        var due = now + delay;

        if (due < _lastDue + GlobalGap)
            due = _lastDue + GlobalGap;

        var text = Render(bot, cue, state, subject, card, points);

        if (text is null)
            return;

        Enqueue(bot, text, due);

        if (retort > 0 && random.NextDouble() < retort)
        {
            var other = Pick(state.Players.Where(p => p.IsBot && p.IsConnected && p.Id != bot.Id).ToList());

            if (other is not null && !(_lastSpoke.TryGetValue(other.Id, out var otherSpoke) && now - otherSpoke < BotGap))
            {
                var reply = Render(other, Cue.Retort, state, bot, card, points);

                if (reply is not null)
                    Enqueue(other, reply, due + TimeSpan.FromSeconds(2.5 + random.NextDouble() * 3));
            }
        }
    }

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

    private static Cue CueForHuman(string text)
    {
        var t = text.ToLowerInvariant();

        if (t.Contains("jaja") || t.Contains("jeje") || t.Contains("xd") || t.Contains("😂") || t.Contains("🤣"))
            return Cue.ReplyLaugh;

        if (t.Contains("hola") || t.Contains("buenas") || t.Contains("hey") || t.Contains("qué tal") || t.Contains("que tal"))
            return Cue.ReplyGreeting;

        if (t.Contains('?') || t.Contains('¿'))
            return Cue.ReplyQuestion;

        return Cue.ReplyHuman;
    }

    private string? Render(PlayerState bot, Cue cue, GameState state, PlayerState? subject, Card? card, int points)
    {
        var voice = VoiceOf(bot.Name);
        var pool = Lines.TryGetValue((voice, cue), out var own) && own.Length > 0
            ? own
            : Lines.TryGetValue((null, cue), out var shared) ? shared : null;

        if (pool is null || pool.Length == 0)
            return null;

        var text = pool[random.Next(pool.Length)];
        var human = state.Players.FirstOrDefault(p => !p.IsBot && p.IsConnected);

        return text
            .Replace("{p}", subject?.Name ?? human?.Name ?? "tú")
            .Replace("{card}", card?.Label ?? "esa carta")
            .Replace("{n}", points.ToString())
            .Replace("{contract}", state.Contract.Describe())
            .Replace("{round}", (state.RoundIndex + 1).ToString())
            .Replace("{me}", bot.Name);
    }

    private static readonly Dictionary<(Voice? Voice, Cue Cue), string[]> Lines = new()
    {
        [(Voice.Cunado, Cue.Hello)] = ["Bueno bueno bueno, ¿quién se va a llevar la paliza hoy?", "Que empiece la fiesta. Y que pierda el de siempre.", "Avisad a los del seguro, que hoy vengo fino 😎"],
        [(Voice.Picara, Cue.Hello)] = ["Hola a todos. Sobre todo a los que van a perder.", "Ya estoy. Podéis ir rindiéndoos.", "Buenas. Voy a ser amable hasta la ronda dos, luego no prometo nada."],
        [(Voice.Zen, Cue.Hello)] = ["Buenas.", "Hola. Que gane quien menos hable.", "Ya estamos todos. Bien."],
        [(Voice.Dramatica, Cue.Hello)] = ["¡AY, qué nervios! Siete rondas, siete infartos.", "Hola, hola. Si pierdo no me habléis en una semana.", "Ya estoy aquí. Traigo suerte, la de siempre: ninguna."],
        [(Voice.Fanfarron, Cue.Hello)] = ["Llegó el campeón. Podéis aplaudir.", "Hoy no juego, hoy doy clase.", "Preparaos, que os voy a leer las cartas por la cara."],
        [(Voice.Abuela, Cue.Hello)] = ["Hola, cielos. A jugar limpio y sin mirar la mano del vecino.", "Buenas tardes, familia. Hoy gano yo, que me toca.", "Ay, qué ilusión. ¿Habéis merendado todos?"],

        [(null, Cue.RoundStart)] = ["Ronda {round}: {contract}. Esto ya se pone serio.", "Va, {contract}. Como no me toque un comodín me voy a tomar el aire.", "{contract}… ¿quién ha inventado esto?", "Ronda {round}. Respirad hondo."],
        [(Voice.Fanfarron, Cue.RoundStart)] = ["{contract}. Fácil. Para mí, claro.", "Ronda {round}. Os la regalo, la siguiente ya no."],
        [(Voice.Dramatica, Cue.RoundStart)] = ["{contract}… No sé si estoy preparada para esto.", "Ronda {round}. Mi corazón no aguanta siete de estas."],

        [(Voice.Cunado, Cue.ILaidDown)] = ["¡TOMA YA! ¿Habéis visto eso? ¿Lo habéis visto?", "Bajado. Que alguien me traiga un trofeo.", "Eso se llama jugar, lo demás son excusas."],
        [(Voice.Picara, Cue.ILaidDown)] = ["Ups, se me ha caído la mano a la mesa 😏", "Bajada. Ya podéis contar puntos.", "Yo ya estoy. Vosotros a lo vuestro, sin prisa."],
        [(Voice.Zen, Cue.ILaidDown)] = ["Bajado.", "Ahí queda.", "Sin prisa, pero sin pausa."],
        [(Voice.Dramatica, Cue.ILaidDown)] = ["¡NO ME LO CREO! ¡Me he bajado! ¡YO!", "Ay, ay, ay, que me bajo. ¡Que me he bajado!", "Milagro. Alguien apunte la fecha."],
        [(Voice.Fanfarron, Cue.ILaidDown)] = ["Bajado. Como estaba previsto.", "Lo dicho: clase magistral.", "¿Ya? Sí, ya. Es lo que hay."],
        [(Voice.Abuela, Cue.ILaidDown)] = ["Ay, mira qué bien, que me he bajado.", "Poquito a poco, que la abuela también sabe.", "Bajada, hijos. Y sin ayuda de nadie."],

        [(Voice.Cunado, Cue.TheyLaidDown)] = ["{p} se ha bajado. ¡Traición!", "Anda que {p}, calladito y bajándose.", "{p}, ¿te has bajado o te has caído?"],
        [(Voice.Picara, Cue.TheyLaidDown)] = ["Vaya, {p}. Qué inesperado. Qué sorpresa. Qué rabia.", "{p} se baja. Yo no pienso aplaudir.", "Mira {p}, con lo tranquilo que estaba todo."],
        [(Voice.Zen, Cue.TheyLaidDown)] = ["Bien jugado, {p}.", "Anotado, {p}.", "{p} se bajó. Toca espabilar."],
        [(Voice.Dramatica, Cue.TheyLaidDown)] = ["¿¡{p} YA!? Esto es el fin.", "{p} se ha bajado y yo tengo un abanico de puntos.", "Que alguien pare a {p}, POR FAVOR."],
        [(Voice.Fanfarron, Cue.TheyLaidDown)] = ["Suerte, {p}. Pura suerte.", "Bajarse es fácil, {p}. Lo difícil es ganarme.", "{p} se baja. Bueno. Alguien tenía que hacerlo de segundo."],
        [(Voice.Abuela, Cue.TheyLaidDown)] = ["Muy bien, {p}, así se hace.", "Ay, {p}, qué manitas.", "Bravo, {p}. Pero no te confíes, cielo."],

        [(null, Cue.IStole)] = ["Ese {card} era mío. Gracias por dejarlo ahí.", "Robo de contra y me da igual el castigo.", "Perdón, perdón. Bueno, en realidad no.", "Eso lo necesitaba yo más que nadie."],
        [(Voice.Fanfarron, Cue.IStole)] = ["El {card} viene con papá.", "Robo de contra. Con estilo, como todo lo mío."],
        [(Voice.Abuela, Cue.IStole)] = ["Ay, perdonad, que el {card} me hacía falta.", "Que no se enfade nadie, que la carta de castigo ya me la trago yo."],

        [(null, Cue.TheyStole)] = ["¡{p}! ¡Qué morro!", "{p} robando de contra. Se acabó la amistad.", "Con dos narices, {p}. Con dos.", "Ese {card} lo quería yo, {p}."],
        [(Voice.Zen, Cue.TheyStole)] = ["Buen robo, {p}.", "{p} lo vio antes que nadie."],

        [(null, Cue.IWasBlocked)] = ["{p}, ese {card} era MI turno.", "Gracias por nada, {p}.", "Ya me habían quitado el {card} de las manos, cómo no."],

        [(null, Cue.IPicked)] = ["Esa me sirve.", "Hmm, interesante.", "Gracias, majo."],

        [(null, Cue.IJokerSwapped)] = ["Cambio el {card} por el comodín. Legal, ¿eh? Lo pone en las reglas.", "Un comodín menos para {p}, uno más para mí. Así es la vida.", "Perdona {p}, tomo prestado tu comodín. Para siempre.", "Ese comodín estaba mal cuidado. Ahora está mejor."],
        [(Voice.Picara, Cue.IJokerSwapped)] = ["{p}, te cambio un {card} por un comodín. Gran negocio… para mí.", "Comodín adquirido. Sin devoluciones."],
        [(Voice.Cunado, Cue.IJokerSwapped)] = ["¡MÍO! El comodín de {p} ahora es mío. Muajaja.", "Jugadón. Con mayúsculas. JUGADÓN."],
        [(Voice.Abuela, Cue.IJokerSwapped)] = ["Ay, {p}, no te enfades, que te dejo el {card} bien bonito.", "Cambio de cromos, cariño. Tú el {card}, yo el comodín."],

        [(Voice.Cunado, Cue.MyJokerTaken)] = ["¡EH! ¡Ese comodín era mío! ¡ÁRBITRO!", "No me lo puedo creer, {p}. Ladrón.", "Me lo apunto, {p}. Me lo apunto."],
        [(Voice.Picara, Cue.MyJokerTaken)] = ["Vale, {p}. Muy bien. Recuerda que yo también tengo memoria.", "Ah, que me quitas el comodín. Sin más. Perfecto.", "Tranquilo, {p}, ya nos veremos en la siguiente ronda."],
        [(Voice.Zen, Cue.MyJokerTaken)] = ["Esperable.", "Bien visto, {p}.", "Va y viene, como todo."],
        [(Voice.Dramatica, Cue.MyJokerTaken)] = ["¡¿MI COMODÍN?! Esto es lo peor que me ha pasado hoy.", "Me voy. Me voy de la mesa. No, espera, sigo, pero muy dolida.", "{p}, me acabas de robar la ilusión."],
        [(Voice.Fanfarron, Cue.MyJokerTaken)] = ["Te lo presto, {p}. Total, no lo necesito.", "Cógelo, cógelo. Ya te lo devuelvo yo con intereses.", "Sin ese comodín sigo ganando. Es lo que tiene ser bueno."],
        [(Voice.Abuela, Cue.MyJokerTaken)] = ["Ay, {p}, que a una anciana se lo quitas.", "Qué disgusto, hijo. Con lo que me costó.", "Bueno, bueno. Que Dios te lo pague, {p}."],

        [(null, Cue.TheyJokerSwapped)] = ["Ojo, que {p} va robando comodines por la mesa.", "{p} sabe jugar, cuidado con esa.", "Bonito canje, {p}. Nadie lo vio venir.", "{p} entregó el {card} y se llevó el comodín. Así, sin anestesia."],

        [(null, Cue.TheyExtendedMine)] = ["Oye {p}, que ese juego es mío. Bueno, era.", "Gracias por decorarme la escalera, {p}.", "{p} me ha puesto un {card}. Sin permiso. Como en casa."],

        [(null, Cue.OneCardLeft)] = ["{p} con una carta. Que alguien haga algo.", "Cuidado, cuidado, que {p} cierra.", "¿Una carta, {p}? ¿En serio? Odio esto.", "{p} está a una. Rezad."],
        [(null, Cue.IHaveOneCard)] = ["Una carta. Solo una. 😏", "Id contando la mano, que esto se acaba.", "Última carta. Que nadie parpadee."],
        [(Voice.Fanfarron, Cue.IHaveOneCard)] = ["Una. Como los grandes.", "Me queda una y ya sé cuál es. Vosotros no."],

        [(Voice.Cunado, Cue.IClosed)] = ["¡¡CERRADO!! ¡A contar puntos, pringados!", "Se acabó. Vayan pasando por caja.", "¡Y con esa cierro! Que alguien grabe esto."],
        [(Voice.Picara, Cue.IClosed)] = ["Cierro. Sí, así, sin avisar.", "Fin de la ronda. No hace falta que me deis las gracias.", "Ya está. Contad despacito, que os veo nerviosos."],
        [(Voice.Zen, Cue.IClosed)] = ["Cerrado.", "Ronda hecha.", "Bien. Siguiente."],
        [(Voice.Dramatica, Cue.IClosed)] = ["¡HE CERRADO! ¡Que alguien me pellizque!", "Esto no me pasa nunca. ¡NUNCA! ¡Y ha pasado!", "Cierro y me emociono. Dadme un segundo."],
        [(Voice.Fanfarron, Cue.IClosed)] = ["Cerrado. Como siempre. Como todo.", "Otra ronda para la colección.", "Esto es lo que pasa cuando juegas contra mí."],
        [(Voice.Abuela, Cue.IClosed)] = ["Ay, que he cerrado. Qué alegría, hijos.", "Cerrado. La experiencia es un grado.", "Ya está. Ahora sí que me tomo el café."],

        [(Voice.Cunado, Cue.TheyClosed)] = ["{p} cierra. Yo me quedo con todo esto en la mano. Genial.", "Anda {p}, ni un poquito de piedad.", "{p} ha cerrado y yo tenía la mano PERFECTA. Casi."],
        [(Voice.Picara, Cue.TheyClosed)] = ["{p} cierra. Vale. Vaaaale.", "Muy bien, {p}. Disfrútalo, que dura poco.", "{p} cerró. Sospechoso, pero lo dejo pasar."],
        [(Voice.Zen, Cue.TheyClosed)] = ["Enhorabuena, {p}.", "{p} cerró. Sigamos.", "Ronda para {p}. Justo."],
        [(Voice.Dramatica, Cue.TheyClosed)] = ["¿{p} ya? ¡Si acabábamos de empezar!", "{p} cierra y yo con las cartas contadas. Qué dolor.", "No. No. NO. {p}, ¿por qué?"],
        [(Voice.Fanfarron, Cue.TheyClosed)] = ["{p} cierra esta. Yo cierro la partida, ya verás.", "Bien, {p}. Un descuido mío.", "Te dejo esta, {p}. Para que no te desanimes."],
        [(Voice.Abuela, Cue.TheyClosed)] = ["Qué bien, {p}, enhorabuena, cariño.", "Ay, {p}, cómo se te da esto.", "Muy bien, {p}. Y yo con la mano llena, como siempre."],

        [(null, Cue.IWasWorst)] = ["{n} puntos. Me lo he ganado a pulso.", "{n}. No miréis, por favor.", "He hecho {n} puntos. Coleccionar cartas también es bonito.", "Menos mal que esto son puntos y no euros."],
        [(Voice.Dramatica, Cue.IWasWorst)] = ["{n} PUNTOS. Voy a llorar en el pozo.", "Que conste que la culpa es del mazo. {n} puntos, por si alguien pregunta."],
        [(Voice.Fanfarron, Cue.IWasWorst)] = ["{n} puntos. Estrategia. Ya lo entenderéis.", "He dejado que me pasen para que haya emoción."],

        [(null, Cue.TheyWereWorst)] = ["{p}, {n} puntos. ¿Estás bien? ¿Necesitas algo?", "Un aplauso para {p} y sus {n} puntos.", "{n} puntos, {p}. Impresionante. En el mal sentido.", "{p} se ha quedado la mano entera. Coleccionista.", "{p}, con {n} puntos vas a necesitar un milagro."],
        [(Voice.Abuela, Cue.TheyWereWorst)] = ["{p}, cielo, {n} puntos. La próxima va mejor, seguro.", "No pasa nada, {p}. Peor es no jugar."],

        [(null, Cue.NobodyClosed)] = ["¿Nadie se ha bajado? Qué mesa más triste.", "Ronda en tablas. Todos contamos. Todos lloramos.", "Se acabó el mazo y nadie cerró. Menudo nivel."],

        [(Voice.Cunado, Cue.IWonGame)] = ["¡¡GANÉ!! ¡Que suene la música!", "Campeón. Ya está. Ya lo he dicho.", "Siete rondas para demostrar lo evidente."],
        [(Voice.Picara, Cue.IWonGame)] = ["He ganado. Qué raro, con lo bien que jugabais todos.", "Victoria. Voy a ser insoportable un rato, avisados.", "Gané. Podéis empezar a fingir que os alegráis."],
        [(Voice.Zen, Cue.IWonGame)] = ["Gané. Gracias por la partida.", "Bien jugado todos. Sobre todo yo."],
        [(Voice.Dramatica, Cue.IWonGame)] = ["¡¡¡HE GANADO!!! ¡NO ME LO CREO! ¡NADIE SE LO CREE!", "Esto va para mis fans. Que son ninguno. ¡Pero he ganado!"],
        [(Voice.Fanfarron, Cue.IWonGame)] = ["Como estaba previsto. Siguiente.", "Ganar es un hábito. Preguntadme cómo.", "Victoria. Podéis pedirme autógrafos."],
        [(Voice.Abuela, Cue.IWonGame)] = ["Ay, que he ganado yo. Con lo mayor que soy.", "Ganó la abuela. A ver quién se ríe ahora.", "Qué bonito. Ahora todos a fregar, que he ganado."],

        [(null, Cue.TheyWonGame)] = ["Enhorabuena, {p}. Pero la revancha es mañana.", "{p} gana. Se lo ha currado, hay que reconocerlo.", "Ha ganado {p}. Lo dejamos en empate técnico, ¿no?", "Bien, {p}. Apúntatelo, que no se repetirá."],
        [(Voice.Fanfarron, Cue.TheyWonGame)] = ["{p} gana. Tenía el día tonto.", "Hoy {p}. Mañana yo, como siempre."],

        [(null, Cue.ILostGame)] = ["Último. Otra vez. Empiezo a ver un patrón.", "He perdido, pero con mucha dignidad. Mucha.", "Bueno, alguien tenía que ser el último. Muy generoso por mi parte."],
        [(Voice.Dramatica, Cue.ILostGame)] = ["ÚLTIMA. No me habléis. No me miréis.", "Voy a borrar esta partida de mi memoria. Y la anterior."],

        [(null, Cue.StockRecycled)] = ["Se rehace el mazo. Esto se alarga más que una sobremesa.", "Otra vez el pozo al mazo. Que alguien cierre, por favor.", "El mazo se acabó. Como mi paciencia."],

        [(null, Cue.JokerDiscarded)] = ["¡¿{p} ha tirado un COMODÍN?! ¿Estás bien?", "{p} acaba de tirar un comodín. Silencio en la sala.", "Un comodín al pozo. {p}, eso ha dolido a todos.", "Que alguien le explique a {p} lo que es un comodín."],

        [(Voice.Cunado, Cue.IdleHuman)] = ["{p}, ¿te has dormido o estás pensando en la cena?", "¡{p}! Que es tu turno, campeón.", "{p}, tic tac, tic tac."],
        [(Voice.Picara, Cue.IdleHuman)] = ["{p}, cuando quieras. No hay prisa. Bueno, un poco sí.", "{p}, tu turno. Lo digo por si te estabas haciendo el interesante.", "Te toca, {p}. Las cartas no se ordenan solas."],
        [(Voice.Zen, Cue.IdleHuman)] = ["{p}, tu turno.", "Te toca, {p}. Sin prisa.", "{p}. Las cartas esperan."],
        [(Voice.Dramatica, Cue.IdleHuman)] = ["{p}, ¡me voy a hacer vieja esperándote!", "¿{p}? ¿Sigues ahí? ¡Dime algo!", "{p}, que estoy sufriendo, mueve algo."],
        [(Voice.Fanfarron, Cue.IdleHuman)] = ["{p}, ¿te estás inventando una estrategia? No te va a servir.", "Te toca, {p}. Roba, tira, pierde. Es fácil.", "{p}, mientras piensas yo ya he ganado dos veces."],
        [(Voice.Abuela, Cue.IdleHuman)] = ["{p}, cariño, que te toca.", "{p}, hijo, ¿te traigo un café?", "Tranquilo, {p}, la abuela espera lo que haga falta."],

        [(null, Cue.ReplyGreeting)] = ["¡Hola, {p}!", "Buenas, {p}. Cuánto tiempo.", "Hola {p}, ¿preparado para perder?", "¡Ey, {p}! Ya era hora."],
        [(null, Cue.ReplyLaugh)] = ["Jajaja", "jajajaja qué malo", "😂😂", "Te ríes ahora, {p}. Ahora.", "jaja no, en serio, te toca."],
        [(null, Cue.ReplyQuestion)] = ["Buena pregunta, {p}. Mejor sigue jugando.", "¿Y yo qué sé, {p}? Yo solo tiro cartas.", "Pregúntale a las reglas, {p}, que están en el botón de la interrogación.", "Eso ni el que inventó el juego lo sabe."],
        [(null, Cue.ReplyHuman)] = ["Sí, sí, {p}. Lo que tú digas.", "Interesante, {p}. Tira una carta.", "Te leo, {p}. Te leo.", "Jaja, vale, {p}.", "Eso mismo pensaba yo.", "Concentración, {p}, que esto es serio."],
        [(Voice.Zen, Cue.ReplyHuman)] = ["Ajá.", "Puede ser.", "Te escucho, {p}."],
        [(Voice.Abuela, Cue.ReplyHuman)] = ["Ay, {p}, qué cosas tienes.", "Sí, cielo, sí.", "Qué majo eres, {p}."],

        [(Voice.Cunado, Cue.Retort)] = ["Jajaja {p}, madre mía.", "{p}, cállate un poquito, anda.", "Eso lo dices tú, {p}, que vas perdiendo.", "{p} otra vez con lo mismo."],
        [(Voice.Picara, Cue.Retort)] = ["{p}, qué bonito hablas cuando pierdes.", "Sí, {p}, sí. Ya te vale.", "Guárdate las palabras, {p}, y juega.", "Lo dice {p}, precisamente."],
        [(Voice.Zen, Cue.Retort)] = ["Ya, {p}.", "Menos hablar, {p}.", "Puede ser.", "Cada uno con lo suyo, {p}."],
        [(Voice.Dramatica, Cue.Retort)] = ["¡{p}! ¡Cómo te pasas!", "Ay, {p}, no me hagas esto.", "{p}, te estás viniendo muy arriba.", "¡Que alguien calle a {p}!"],
        [(Voice.Fanfarron, Cue.Retort)] = ["Habla el que va último, {p}.", "{p}, tú a lo tuyo, que es perder.", "Apunta eso, {p}, para cuando te gane.", "Mucho ruido, {p}, pocas cartas."],
        [(Voice.Abuela, Cue.Retort)] = ["Ay, {p}, no seas así.", "Jajaja, {p}, qué cosas dices.", "Menos chulería, {p}, y más jugar.", "{p}, cielo, que te oyen los vecinos."],

        [(Voice.Cunado, Cue.Musing)] = ["¿Alguien tiene un comodín de sobra? Pregunto por un amigo.", "Esta mano parece una tómbola.", "Ojo, que hoy me siento con suerte. Bueno, eso lo digo siempre."],
        [(Voice.Picara, Cue.Musing)] = ["Qué silencio. ¿Estáis todos contando cartas o qué?", "Esa cara, {p}. Esa cara dice que no tienes nada.", "Podría bajarme ya, pero me gusta veros sufrir."],
        [(Voice.Zen, Cue.Musing)] = ["Paciencia.", "Todo llega.", "El mazo sabe lo que hace."],
        [(Voice.Dramatica, Cue.Musing)] = ["Tengo una mano que da miedo. Miedo de verdad, eh.", "Si me sale otro diez me tiro por la ventana.", "No sé qué estoy haciendo, pero lo hago con pasión."],
        [(Voice.Fanfarron, Cue.Musing)] = ["Estoy dejando que os acerquéis, para que haya emoción.", "Podría cerrar cuando quisiera. Pero no quiero. Todavía.", "Contad vuestras cartas, que yo ya sé las mías."],
        [(Voice.Abuela, Cue.Musing)] = ["¿Nadie quiere un caramelo?", "Ay, qué bonito es jugar así, en familia.", "En mis tiempos esto se jugaba con más cabeza."]
    };
}
