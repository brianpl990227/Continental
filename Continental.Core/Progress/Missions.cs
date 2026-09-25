namespace Continental.Core.Progress;

public enum MissionScope
{
    Daily,
    Weekly,
    Achievement,
    Secret
}

public enum Difficulty
{
    Easy,
    Medium,
    Hard
}

public sealed record Mission(
    string Id,
    MissionScope Scope,
    Stat Stat,
    int Target,
    string Title,
    string Detail,
    int Xp,
    string Icon,
    string Group,
    Difficulty Difficulty = Difficulty.Easy,
    int Tier = 0,
    int Tiers = 1);

public static class MissionCatalog
{
    public const int DailyBonusXp = 75;
    public const int DailyRerolls = 1;

    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];
    private static readonly int[] TierXp = [5, 15, 30, 60, 100, 150, 210, 280, 360, 450];

    public static IReadOnlyList<Mission> Achievements { get; }

    public static IReadOnlyList<Mission> Secrets { get; }

    public static IReadOnlyList<Mission> DailyPool { get; }

    public static IReadOnlyList<Mission> WeeklyPool { get; }

    public static IReadOnlyList<Mission> All { get; }

    private static readonly Dictionary<string, Mission> ById;

    static MissionCatalog()
    {
        Achievements = BuildAchievements();
        Secrets = BuildSecrets();
        DailyPool = BuildDaily();
        WeeklyPool = BuildWeekly();
        All = [.. Achievements, .. Secrets, .. DailyPool, .. WeeklyPool];
        ById = All.ToDictionary(m => m.Id);
    }

    public static Mission? Find(string id) => ById.GetValueOrDefault(id);

    public static IEnumerable<Mission> Lifetime => Achievements.Concat(Secrets);

    private static List<Mission> BuildAchievements()
    {
        var list = new List<Mission>();

        void Tiered(string id, string icon, string group, string name, Stat stat, int[] targets, Func<int, string> detail)
        {
            for (var i = 0; i < targets.Length; i++)
            {
                list.Add(new Mission(
                    $"ach.{id}.{i + 1}",
                    MissionScope.Achievement,
                    stat,
                    targets[i],
                    targets.Length == 1 ? name : $"{name} {Numerals[i]}",
                    detail(targets[i]),
                    TierXp[Math.Min(i + Math.Max(0, 3 - targets.Length), TierXp.Length - 1)],
                    icon,
                    group,
                    Tier: i,
                    Tiers: targets.Length));
            }
        }

        static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

        const string partidas = "Partidas";
        const string rondas = "Rondas";
        const string jugadas = "Jugadas";
        const string rivales = "Rivales";
        const string constancia = "Constancia";

        Tiered("played", "🃏", partidas, "Habitual", Stat.GamesPlayed, [1, 5, 10, 25, 50, 100, 250, 500, 1000, 2000],
            n => $"Termina {Plural(n, "partida", "partidas")}.");
        Tiered("won", "🏆", partidas, "Ganador", Stat.GamesWon, [1, 3, 5, 10, 25, 50, 100, 250, 500, 1000],
            n => $"Gana {Plural(n, "partida", "partidas")}.");
        Tiered("podium", "🥈", partidas, "En el podio", Stat.Podiums, [1, 5, 25, 50, 100, 250, 500],
            n => $"Acaba entre los dos primeros {Plural(n, "vez", "veces")} en mesas de 3 o más.");
        Tiered("humans", "🧑‍🤝‍🧑", partidas, "Buena compañía", Stat.GamesVsHumans, [1, 5, 10, 25, 50, 200, 500],
            n => $"Juega {Plural(n, "partida", "partidas")} con otra persona en la mesa.");
        Tiered("winhumans", "🎯", partidas, "Ganador de verdad", Stat.WinsVsHumans, [1, 5, 10, 25, 50, 150, 300],
            n => $"Gana {Plural(n, "partida", "partidas")} con otra persona en la mesa.");
        Tiered("fulltable", "🪑", partidas, "Mesa llena", Stat.WinsFullTable, [1, 5, 10, 25, 50, 150, 300],
            n => $"Gana {Plural(n, "partida", "partidas")} en una mesa de 4 o más.");
        Tiered("six", "🎲", partidas, "Rey de la sala", Stat.WinsSixTable, [1, 3, 5, 10, 25, 50],
            n => $"Gana {Plural(n, "partida", "partidas")} en una mesa de 6.");
        Tiered("duel", "⚔️", partidas, "Duelista", Stat.WinsDuel, [1, 5, 10, 25, 50, 100],
            n => $"Gana {Plural(n, "duelo", "duelos")} mano a mano.");
        Tiered("spain", "🇪🇸", partidas, "Con reglas de España", Stat.WinsSpain, [1, 5, 10, 25, 50, 100],
            n => $"Gana {Plural(n, "partida", "partidas")} con la variante de España.");
        Tiered("latam", "🌎", partidas, "Con reglas latinas", Stat.WinsLatam, [1, 5, 10, 25, 50, 100],
            n => $"Gana {Plural(n, "partida", "partidas")} con la variante de Latinoamérica.");
        Tiered("under100", "🧊", partidas, "Sangre fría", Stat.WinsUnder100, [1, 3, 10, 25, 50, 100],
            n => $"Gana {Plural(n, "partida", "partidas")} con 100 puntos o menos.");
        Tiered("under50", "❄️", partidas, "Bajo cero", Stat.WinsUnder50, [1, 3, 5, 10, 25],
            n => $"Gana {Plural(n, "partida", "partidas")} con 50 puntos o menos.");
        Tiered("by100", "🚀", partidas, "Paliza", Stat.WinsBy100, [1, 3, 10, 25, 50],
            n => $"Gana {Plural(n, "partida", "partidas")} sacando 100 puntos o más al segundo.");
        Tiered("comeback", "🔄", partidas, "Remontada", Stat.Comebacks, [1, 3, 5, 10, 25],
            n => $"Gana {Plural(n, "partida", "partidas")} sin ir primero tras la quinta ronda.");
        Tiered("nosteal", "😇", partidas, "Juego limpio", Stat.WinsWithoutSteal, [1, 5, 10, 25, 50, 100],
            n => $"Gana {Plural(n, "partida", "partidas")} sin robar de contra.");
        Tiered("nojokers", "🐎", partidas, "Pura sangre", Stat.WinsWithoutJokers, [1, 3, 10, 25, 50],
            n => $"Gana {Plural(n, "partida", "partidas")} sin bajar ni canjear comodines.");
        Tiered("alldown", "📐", partidas, "Siempre a tiempo", Stat.GamesAllLaidDown, [1, 5, 10, 25, 50, 100],
            n => $"Bájate en las siete rondas de {Plural(n, "partida", "partidas")}.");
        Tiered("flawless", "🛡️", partidas, "Sin sustos", Stat.FlawlessGames, [1, 5, 10, 25, 50],
            n => $"Termina {Plural(n, "partida", "partidas")} sin ser nunca el peor de una ronda (mesas de 3+).");
        Tiered("playedspain", "🇪🇸", partidas, "Aficionado español", Stat.GamesSpain, [1, 5, 10, 25, 50, 100],
            n => $"Termina {n} partidas con la variante de España.");
        Tiered("playedlatam", "🌎", partidas, "Aficionado latino", Stat.GamesLatam, [1, 5, 10, 25, 50, 100],
            n => $"Termina {n} partidas con la variante de Latinoamérica.");
        Tiered("playedfull", "🪑", partidas, "Sala concurrida", Stat.GamesFullTable, [1, 5, 10, 25, 50, 100, 250],
            n => $"Termina {n} partidas con 4 jugadores o más.");
        Tiered("playedduel", "⚔️", partidas, "Cara a cara", Stat.GamesDuel, [1, 5, 10, 25, 50, 100],
            n => $"Termina {n} partidas de dos jugadores.");
        Tiered("table3", "🔺", partidas, "Trío de ases", Stat.WinsTable3, [1, 5, 10, 25, 50, 100],
            n => $"Gana {Plural(n, "partida", "partidas")} en una mesa de exactamente 3.");
        Tiered("table4", "🟥", partidas, "Cuarteto", Stat.WinsTable4, [1, 5, 10, 25, 50, 100],
            n => $"Gana {Plural(n, "partida", "partidas")} en una mesa de exactamente 4.");
        Tiered("table5", "⭐", partidas, "Quinteto", Stat.WinsTable5, [1, 5, 10, 25, 50],
            n => $"Gana {Plural(n, "partida", "partidas")} en una mesa de exactamente 5.");
        Tiered("played3", "🔺", partidas, "Mesa de tres", Stat.GamesTable3, [1, 10, 25, 50, 100],
            n => $"Termina {Plural(n, "partida", "partidas")} en una mesa de 3.");
        Tiered("played4", "🟥", partidas, "Mesa de cuatro", Stat.GamesTable4, [1, 10, 25, 50, 100],
            n => $"Termina {Plural(n, "partida", "partidas")} en una mesa de 4.");
        Tiered("played5", "⭐", partidas, "Mesa de cinco", Stat.GamesTable5, [1, 10, 25, 50, 100],
            n => $"Termina {Plural(n, "partida", "partidas")} en una mesa de 5.");
        Tiered("played6", "🎲", partidas, "Mesa de seis", Stat.GamesTable6, [1, 10, 25, 50, 100],
            n => $"Termina {Plural(n, "partida", "partidas")} en una mesa de 6.");
        Tiered("podiumhumans", "🎖️", partidas, "Podio con amigos", Stat.PodiumsVsHumans, [1, 5, 10, 25, 50, 100],
            n => $"Acaba entre los dos primeros {Plural(n, "vez", "veces")} con otras personas en la mesa.");
        Tiered("alldownwins", "🏅", partidas, "Puntualidad premiada", Stat.AllDownWins, [1, 3, 10, 25],
            n => $"Gana {Plural(n, "partida", "partidas")} bajándote en las siete rondas.");
        Tiered("buzzer", "⏰", partidas, "Sobre la bocina", Stat.BuzzerWins, [1, 3, 10, 25, 50],
            n => $"Gana {Plural(n, "partida", "partidas")} cerrando la última ronda.");
        Tiered("nodiscard", "🚫", partidas, "Sin mirar al pozo", Stat.NoDiscardWins, [1, 3, 10, 25],
            n => $"Gana {Plural(n, "partida", "partidas")} sin tomar ninguna carta del pozo.");
        Tiered("badstart", "🐢", partidas, "Mal comienzo, buen final", Stat.BadStartWins, [1, 3, 10],
            n => $"Gana {Plural(n, "partida", "partidas")} habiendo sido el peor de la primera ronda.");
        Tiered("last", "🏮", partidas, "Farolillo rojo", Stat.GamesLast, [1, 5, 10, 25, 50],
            n => $"Acaba último {Plural(n, "vez", "veces")}. También cuenta.");

        Tiered("rounds", "🔁", rondas, "Incansable", Stat.RoundsPlayed, [7, 50, 100, 250, 500, 1000, 2500, 5000, 10000],
            n => $"Juega {n} rondas.");
        Tiered("closed", "🚪", rondas, "Cerrojo", Stat.RoundsClosed, [1, 5, 10, 25, 50, 100, 200, 500, 1000, 2000],
            n => $"Cierra {Plural(n, "ronda", "rondas")}.");
        Tiered("sameturn", "💥", rondas, "De golpe", Stat.SameTurnCloses, [1, 5, 10, 25, 50, 150, 300],
            n => $"Bájate y cierra en la misma jugada {Plural(n, "vez", "veces")}.");
        Tiered("firstturn", "⚡", rondas, "Relámpago", Stat.FirstTurnCloses, [1, 3, 5, 10, 20, 50],
            n => $"Cierra una ronda en tu primer turno {Plural(n, "vez", "veces")}.");
        Tiered("zero", "0️⃣", rondas, "Mano limpia", Stat.ZeroRounds, [1, 10, 25, 50, 100, 200, 500, 1000],
            n => $"Acaba {n} rondas con 0 puntos o menos.");
        Tiered("laiddown", "🪜", rondas, "Siempre abajo", Stat.RoundsLaidDown, [1, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000],
            n => $"Bájate en {n} rondas.");
        Tiered("big", "🎒", rondas, "Coleccionista", Stat.BigRounds, [1, 10, 25, 50, 100],
            n => $"Suma 100 puntos o más en una ronda {Plural(n, "vez", "veces")}.");
        Tiered("fullhand", "🙈", rondas, "Mano entera", Stat.FullHandRounds, [1, 10, 25, 50, 100],
            n => $"Termina {Plural(n, "ronda", "rondas")} sin haberte bajado.");

        string[] codes = ["TT", "TE", "EE", "TTT", "TTE", "TEE", "EEE"];
        string[] names = ["Dos tríos", "Trío y escalera", "Dos escaleras", "Tres tríos", "Dos tríos y escalera", "Trío y dos escaleras", "Tres escaleras"];

        for (var i = 0; i < codes.Length; i++)
        {
            var code = codes[i];
            Tiered($"close{code.ToLowerInvariant()}", "🧩", rondas, $"Maestro {code}", Stats.ClosesByContract[i], [1, 5, 10, 25, 50, 150, 300],
                n => $"Cierra {Plural(n, "ronda", "rondas")} de {code} ({names[Array.IndexOf(codes, code)].ToLowerInvariant()}).");
        }

        for (var i = 0; i < codes.Length; i++)
        {
            var code = codes[i];
            Tiered($"down{code.ToLowerInvariant()}", "⬇️", rondas, $"Abajo en {code}", Stats.DownByContract[i], [1, 10, 25, 50, 100, 250],
                n => $"Bájate en {Plural(n, "ronda", "rondas")} de {code}.");
            Tiered($"zero{code.ToLowerInvariant()}", "🧼", rondas, $"Limpio en {code}", Stats.ZeroByContract[i], [1, 5, 10, 25, 50, 100],
                n => $"Acaba {Plural(n, "ronda", "rondas")} de {code} con 0 puntos o menos.");
        }

        Tiered("negative", "➖", rondas, "Saldo a favor", Stat.CleanSweepRounds, [1, 5, 10, 25, 50, 100, 250],
            n => $"Acaba {Plural(n, "ronda", "rondas")} con puntos negativos (bajar y cerrar de golpe).");

        Tiered("steals", "🦝", jugadas, "Ladrón", Stat.Steals, [1, 5, 10, 25, 50, 100, 200, 500, 1000],
            n => $"Roba de contra {Plural(n, "vez", "veces")}.");
        Tiered("swaps", "🌟", jugadas, "Rey del comodín", Stat.JokerSwaps, [1, 5, 10, 25, 50, 100, 200, 500, 1000],
            n => $"Canjea {Plural(n, "comodín", "comodines")}.");
        Tiered("extend", "➕", jugadas, "Colocador", Stat.Extensions, [1, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000],
            n => $"Coloca {n} cartas en juegos de la mesa.");
        Tiered("extendothers", "🎁", jugadas, "Okupa", Stat.ExtensionsOnOthers, [1, 5, 25, 50, 100, 250, 500, 1000],
            n => $"Coloca {n} cartas en juegos de otros jugadores.");
        Tiered("discard", "♻️", jugadas, "Del pozo", Stat.TookDiscard, [1, 10, 50, 100, 250, 500, 1000, 2500],
            n => $"Toma {n} cartas del pozo.");
        Tiered("jokers", "🃏", jugadas, "Comodinero", Stat.JokersLaid, [1, 5, 25, 50, 100, 250, 500, 1000, 2500],
            n => $"Baja {n} comodines en tus juegos.");
        Tiered("trios", "3️⃣", jugadas, "Triero", Stat.TriosLaid, [1, 10, 25, 50, 100, 250, 500, 1000, 2500],
            n => $"Baja {n} tríos.");
        Tiered("escaleras", "🪜", jugadas, "Escalador", Stat.EscalerasLaid, [1, 10, 25, 50, 100, 250, 500, 1000, 2500],
            n => $"Baja {n} escaleras.");
        Tiered("longest", "🏗️", jugadas, "Arquitecto", Stat.LongestEscalera, [5, 6, 7, 8, 9, 10, 11, 12, 13],
            n => $"Ten una escalera tuya de {n} cartas.");
        Tiered("swapsgame", "🎩", jugadas, "Prestidigitador", Stat.MaxSwapsInGame, [2, 3, 4, 5, 6],
            n => $"Canjea {n} comodines en una misma partida.");
        Tiered("closesgame", "🔥", jugadas, "En racha", Stat.MaxClosesInGame, [2, 3, 4, 5, 6],
            n => $"Cierra {n} rondas en una misma partida.");
        Tiered("stealsgame", "🧤", jugadas, "Manos largas", Stat.MaxStealsInGame, [2, 3, 4, 5, 6, 8],
            n => $"Roba de contra {n} veces en una misma partida.");
        Tiered("samegame", "💣", jugadas, "Golpe tras golpe", Stat.MaxSameTurnInGame, [2, 3, 4],
            n => $"Bájate y cierra de golpe {n} veces en una misma partida.");
        Tiered("firstgame", "🌩️", jugadas, "Tormenta", Stat.MaxFirstTurnInGame, [2, 3],
            n => $"Cierra {n} rondas en tu primer turno dentro de una misma partida.");
        Tiered("jokersgame", "🎪", jugadas, "Circo de comodines", Stat.MaxJokersInGame, [3, 4, 5, 6, 8],
            n => $"Baja {n} comodines en una misma partida.");
        Tiered("extendgame", "🧱", jugadas, "Constructor", Stat.MaxExtensionsInGame, [5, 8, 10, 15, 20],
            n => $"Coloca {n} cartas en juegos de la mesa en una misma partida.");
        Tiered("turns", "🔂", jugadas, "Turno a turno", Stat.Turns, [50, 100, 250, 500, 1000, 2000, 5000, 10000],
            n => $"Juega {n} turnos.");
        Tiered("chat", "💬", jugadas, "Charlatán", Stat.ChatMessages, [1, 10, 25, 50, 100, 250, 500, 1000],
            n => $"Escribe {Plural(n, "mensaje", "mensajes")} en el chat de la mesa.");

        Tiered("streak", "📈", constancia, "Imbatible", Stat.BestWinStreak, [2, 3, 4, 5, 6, 7, 8, 10, 15],
            n => $"Gana {n} partidas seguidas.");
        Tiered("gamesday", "☕", constancia, "Sesión larga", Stat.MaxGamesInDay, [2, 3, 4, 5, 7],
            n => $"Termina {n} partidas en un mismo día.");
        Tiered("days", "📅", constancia, "Asiduo", Stat.DaysPlayed, [1, 2, 3, 5, 7, 14, 30, 60, 100, 365],
            n => $"Juega {Plural(n, "día", "días distintos")}.");
        Tiered("daystreak", "🗓️", constancia, "Constante", Stat.BestDayStreak, [2, 3, 4, 5, 7, 10, 14, 21, 30, 60],
            n => $"Juega {n} días seguidos.");
        Tiered("dailies", "☀️", constancia, "Cumplidor", Stat.DailiesCompleted, [1, 3, 10, 25, 50, 100, 150, 250, 500, 1000],
            n => $"Completa {Plural(n, "misión diaria", "misiones diarias")}.");
        Tiered("dailysets", "🌞", constancia, "Día redondo", Stat.DailySetsCompleted, [1, 3, 7, 14, 30, 60, 100, 200],
            n => $"Completa las tres diarias de un día {Plural(n, "vez", "veces")}.");
        Tiered("weeklies", "🗞️", constancia, "Semana a semana", Stat.WeekliesCompleted, [1, 3, 5, 10, 20, 52, 100],
            n => $"Completa {Plural(n, "misión semanal", "misiones semanales")}.");
        Tiered("minutes", "⏳", constancia, "Tiempo de mesa", Stat.MinutesPlayed, [30, 60, 120, 300, 600, 1500, 3000, 6000, 12000],
            n => n >= 120 ? $"Juega {n / 60} horas." : $"Juega {n} minutos.");
        Tiered("achievements", "🎖️", constancia, "Coleccionista de logros", Stat.AchievementsCompleted, [5, 10, 25, 50, 100, 200, 300, 400, 500],
            n => $"Consigue {n} logros.");

        (string Id, string Name, Stat Stat)[] voices =
        [
            ("cunado", "el cuñado (Chelo, Nino)", Stat.BeatCunado),
            ("picara", "la pícara (Marta, Águila)", Stat.BeatPicara),
            ("zen", "el zen (Rubén, Sombra)", Stat.BeatZen),
            ("dramatica", "la dramática (Vicky)", Stat.BeatDramatica),
            ("fanfarron", "el fanfarrón (Tiburón)", Stat.BeatFanfarron),
            ("abuela", "la abuela (Lola, Pepa)", Stat.BeatAbuela)
        ];

        string[] voiceTitles = ["Calla, cuñado", "Más pícaro que nadie", "Rompe la calma", "Sin drama", "Baja humos", "Terror de la abuela"];

        for (var i = 0; i < voices.Length; i++)
        {
            var (id, name, stat) = voices[i];
            Tiered($"beat{id}", "🤖", rivales, voiceTitles[i], stat, [1, 3, 5, 10, 25, 50, 100, 250],
                n => $"Acaba por delante de {name} en {Plural(n, "partida", "partidas")}.");
        }

        Tiered("voices", "🗺️", rivales, "Conoces a todos", Stat.DistinctVoicesBeaten, [2, 3, 4, 5, 6],
            n => $"Gánale a {n} personalidades de bot distintas.");

        Tiered("lostabuela", "👵", rivales, "Nieto obediente", Stat.LostToAbuela, [1, 3, 5],
            n => $"Acaba por detrás de la abuela {Plural(n, "vez", "veces")}.");
        Tiered("fivebots", "🦾", rivales, "Cazador de bots", Stat.WinsVsFiveBots, [2, 5, 10, 25, 50],
            n => $"Gana {Plural(n, "partida", "partidas")} contra cinco bots.");

        Tiered("lost", "🙃", partidas, "Se aprende perdiendo", Stat.GamesLost, [5, 25, 100, 250, 500, 1000],
            n => $"Pierde {Plural(n, "partida", "partidas")} sin tirar la toalla.");
        Tiered("hair", "🪒", partidas, "Al filo", Stat.WinsByHair, [2, 5, 10, 25],
            n => $"Gana {Plural(n, "partida", "partidas")} por 5 puntos o menos.");
        Tiered("fastwins", "⏱️", partidas, "Ganador exprés", Stat.FastWins, [2, 5, 10, 25, 50],
            n => $"Gana {Plural(n, "partida", "partidas")} en 12 minutos o menos.");
        Tiered("negwins", "🌀", partidas, "Números negros", Stat.WinsNegative, [2, 5, 10],
            n => $"Gana {Plural(n, "partida", "partidas")} con puntuación negativa.");
        Tiered("vsthree", "🥳", partidas, "Fiesta grande", Stat.WinsVsThreeHumans, [2, 5, 10, 25],
            n => $"Gana {Plural(n, "partida", "partidas")} con tres personas más en la mesa.");
        Tiered("threestart", "🏁", rondas, "Buen arranque", Stat.FirstThreeCloses, [2, 5, 10, 25],
            n => $"Cierra las tres primeras rondas en {Plural(n, "partida", "partidas")}.");
        Tiered("weekend", "🛋️", constancia, "Fin de semana", Stat.WeekendGames, [25, 50, 100, 250, 500],
            n => $"Juega {n} partidas en fin de semana.");
        Tiered("night", "🦉", constancia, "Trasnochador", Stat.NightGames, [3, 10, 50, 100],
            n => $"Termina {Plural(n, "partida", "partidas")} entre la 1 y las 5 de la madrugada.");
        Tiered("morning", "🐓", constancia, "Tempranero", Stat.MorningGames, [3, 10, 25, 50, 100],
            n => $"Termina {Plural(n, "partida", "partidas")} entre las 5 y las 8 de la mañana.");
        Tiered("marathons", "🏃", constancia, "Fondista", Stat.MarathonGames, [2, 5, 10, 25],
            n => $"Juega {Plural(n, "partida", "partidas")} de una hora o más.");

        return list;
    }

    private static List<Mission> BuildSecrets()
    {
        Mission Secret(string id, string icon, string title, string detail, Stat stat, int target = 1, int xp = 75)
            => new($"sec.{id}", MissionScope.Secret, stat, target, title, detail, xp, icon, "Secretas", Difficulty.Hard);

        return
        [
            Secret("negative", "🌀", "Magia negra", "Gana una partida con puntuación negativa.", Stat.WinsNegative, xp: 150),
            Secret("hair", "🪒", "Por los pelos", "Gana por 5 puntos o menos.", Stat.WinsByHair),
            Secret("tie", "🤝", "Empate técnico", "Comparte el primer puesto con otro jugador.", Stat.TiedWins),
            Secret("miracle", "🙏", "Milagro", "Gana yendo último tras la sexta ronda.", Stat.MiracleWins, xp: 120),
            Secret("party", "🎉", "Que empiece la fiesta", "Cierra la primera ronda en tu primer turno.", Stat.FirstRoundFirstTurnCloses, xp: 100),
            Secret("royal", "👑", "Escalera real", "Bájate y cierra de golpe la ronda de tres escaleras.", Stat.EeeSameTurnCloses, xp: 120),
            Secret("magician", "🪄", "Mago", "Canjea dos comodines en la misma ronda.", Stat.MaxSwapsInRound, 2),
            Secret("tourist", "🧳", "Turista", "Termina una partida sin haberte bajado ni una vez.", Stat.TouristGames, xp: 50),
            Secret("huge", "🎒", "Mochilero", "Termina una partida con 500 puntos o más.", Stat.HugeLosses, xp: 50),
            Secret("terminator", "🦾", "Exterminador", "Gana una partida contra cinco bots.", Stat.WinsVsFiveBots),
            Secret("fast", "⏱️", "Exprés", "Gana una partida en 12 minutos o menos.", Stat.FastWins),
            Secret("marathon", "🏃", "Maratón", "Juega una partida de una hora o más.", Stat.MarathonGames),
            Secret("owl", "🦉", "Búho", "Termina una partida entre la 1 y las 5 de la madrugada.", Stat.NightGames),
            Secret("early", "🐓", "Madrugador", "Termina una partida entre las 5 y las 8 de la mañana.", Stat.MorningGames),
            Secret("sunday", "🛋️", "Dominguero", "Juega 10 partidas en fin de semana.", Stat.WeekendGames, 10),
            Secret("elders", "👵", "Respeta a tus mayores", "Acaba por detrás de la abuela 10 veces.", Stat.LostToAbuela, 10),
            Secret("addict", "🌙", "Una más y lo dejo", "Juega 10 partidas en un mismo día.", Stat.MaxGamesInDay, 10),
            Secret("resilient", "🧱", "Resiliente", "Encadena 5 derrotas seguidas. Y sigue jugando.", Stat.BestLossStreak, 5, 50),
            Secret("seven", "7️⃣", "Pleno", "Cierra las siete rondas de una partida.", Stat.MaxClosesInGame, 7, 200),
            Secret("hoarder", "🧲", "Aspiradora", "Roba de contra 10 veces en una partida.", Stat.MaxStealsInGame, 10),
            Secret("tower", "🗼", "Torre", "Ten una escalera tuya de 14 cartas.", Stat.LongestEscalera, 14, 150),
            Secret("untouchable", "👻", "Intocable", "Acaba las siete rondas de una partida con 0 puntos o menos.", Stat.UntouchableGames, xp: 300),
            Secret("party3", "🥳", "Fiesta", "Gana una partida con tres personas más en la mesa.", Stat.WinsVsThreeHumans, xp: 100),
            Secret("almost", "😬", "Casi", "Pierde por 5 puntos o menos.", Stat.LostByHair, xp: 50),
            Secret("hundred", "💯", "Cien redondos", "Termina una partida con exactamente 100 puntos.", Stat.ExactHundredGames, xp: 60),
            Secret("zero", "⭕", "Cero absoluto", "Termina una partida con exactamente 0 puntos.", Stat.ZeroGames, xp: 150),
            Secret("threestart", "🏁", "Salida lanzada", "Cierra las tres primeras rondas de una partida.", Stat.FirstThreeCloses, xp: 120),
            Secret("twoflash", "🌩️", "Doble relámpago", "Cierra dos rondas en tu primer turno en una misma partida.", Stat.MaxFirstTurnInGame, 2, 150),
            Secret("hattrick", "🎩", "Tres de golpe", "Bájate y cierra de golpe tres veces en una partida.", Stat.MaxSameTurnInGame, 3, 150),
            Secret("nopozo", "🙅", "Ni lo mires", "Gana sin tomar ni una carta del pozo.", Stat.NoDiscardWins, xp: 60),
            Secret("marathoner", "🏃", "Ultramaratón", "Juega 60 horas en total.", Stat.MinutesPlayed, 3600, 150),
            Secret("longstreak", "🔥", "Imparable", "Gana 12 partidas seguidas.", Stat.BestWinStreak, 12, 300),
            Secret("collector", "🧺", "Completista", "Consigue 250 logros.", Stat.AchievementsCompleted, 250, 250),
            Secret("chatter", "📣", "Megáfono", "Escribe 2000 mensajes en el chat.", Stat.ChatMessages, 2000, 100),
            Secret("hundreds", "💯", "Centenario", "Termina 100 partidas con otras personas en la mesa.", Stat.GamesVsHumans, 100, 100),
            Secret("archmage", "🧙", "Archimago", "Canjea tres comodines en la misma ronda.", Stat.MaxSwapsInRound, 3, 150),
            Secret("rock", "🪨", "Roca", "Encadena 10 derrotas seguidas. Y sigue jugando.", Stat.BestLossStreak, 10, 75),
            Secret("siblings", "👯", "Hermanos de mesa", "Comparte el primer puesto 3 veces.", Stat.TiedWins, 3, 120),
            Secret("storm", "🌪️", "Huracán", "Cierra cinco rondas en tu primer turno en total.", Stat.FirstTurnCloses, 5, 120),
            Secret("wall", "🧱", "Muralla", "Coloca 20 cartas en juegos de la mesa en una misma partida.", Stat.MaxExtensionsInGame, 20, 100),
            Secret("insomnia", "🌌", "Insomne", "Termina 25 partidas entre la 1 y las 5 de la madrugada.", Stat.NightGames, 25, 100)
        ];
    }

    private static List<Mission> BuildDaily()
    {
        var list = new List<Mission>();
        int[] xp = [30, 50, 75];

        void Add(string id, string icon, Difficulty difficulty, Stat stat, int target, string title, string detail)
            => list.Add(new Mission($"day.{id}", MissionScope.Daily, stat, target, title, detail, xp[(int)difficulty], icon, "Diarias", difficulty));

        var e = Difficulty.Easy;
        var m = Difficulty.Medium;
        var h = Difficulty.Hard;

        Add("play1", "🃏", e, Stat.GamesPlayed, 1, "Una partidita", "Termina una partida.");
        Add("play2", "🃏", m, Stat.GamesPlayed, 2, "Dos de seguido", "Termina 2 partidas.");
        Add("play3", "🃏", h, Stat.GamesPlayed, 3, "Tarde de cartas", "Termina 3 partidas.");
        Add("win1", "🏆", m, Stat.GamesWon, 1, "A ganar", "Gana una partida.");
        Add("win2", "🏆", h, Stat.GamesWon, 2, "Doblete", "Gana 2 partidas.");
        Add("close1", "🚪", e, Stat.RoundsClosed, 1, "Cierra la puerta", "Cierra una ronda.");
        Add("close3", "🚪", m, Stat.RoundsClosed, 3, "Portazo", "Cierra 3 rondas.");
        Add("close6", "🚪", h, Stat.RoundsClosed, 6, "Cerrajero", "Cierra 6 rondas.");
        Add("down3", "🪜", e, Stat.RoundsLaidDown, 3, "A la mesa", "Bájate en 3 rondas.");
        Add("down7", "🪜", m, Stat.RoundsLaidDown, 7, "Una semana entera", "Bájate en 7 rondas.");
        Add("down12", "🪜", h, Stat.RoundsLaidDown, 12, "Siempre abajo", "Bájate en 12 rondas.");
        Add("steal1", "🦝", e, Stat.Steals, 1, "Mano rápida", "Roba de contra una vez.");
        Add("steal2", "🦝", m, Stat.Steals, 2, "Mano larga", "Roba de contra 2 veces.");
        Add("steal4", "🦝", h, Stat.Steals, 4, "Carterista", "Roba de contra 4 veces.");
        Add("swap1", "🌟", m, Stat.JokerSwaps, 1, "Te lo cambio", "Canjea un comodín.");
        Add("swap2", "🌟", h, Stat.JokerSwaps, 2, "Coleccionista de comodines", "Canjea 2 comodines.");
        Add("extend3", "➕", e, Stat.Extensions, 3, "Rellenando", "Coloca 3 cartas en juegos de la mesa.");
        Add("extend6", "➕", m, Stat.Extensions, 6, "Colocador", "Coloca 6 cartas en juegos de la mesa.");
        Add("extend12", "➕", h, Stat.Extensions, 12, "Albañil", "Coloca 12 cartas en juegos de la mesa.");
        Add("others2", "🎁", e, Stat.ExtensionsOnOthers, 2, "Regalito", "Coloca 2 cartas en juegos ajenos.");
        Add("others5", "🎁", m, Stat.ExtensionsOnOthers, 5, "Okupa", "Coloca 5 cartas en juegos ajenos.");
        Add("pozo3", "♻️", e, Stat.TookDiscard, 3, "Rebuscando", "Toma 3 cartas del pozo.");
        Add("pozo8", "♻️", m, Stat.TookDiscard, 8, "Buceador", "Toma 8 cartas del pozo.");
        Add("jokers2", "🃏", e, Stat.JokersLaid, 2, "Comodín va", "Baja 2 comodines en tus juegos.");
        Add("jokers5", "🃏", m, Stat.JokersLaid, 5, "Lluvia de comodines", "Baja 5 comodines en tus juegos.");
        Add("trios3", "3️⃣", e, Stat.TriosLaid, 3, "Tres de lo mismo", "Baja 3 tríos.");
        Add("trios6", "3️⃣", m, Stat.TriosLaid, 6, "Triero", "Baja 6 tríos.");
        Add("esc3", "🪜", e, Stat.EscalerasLaid, 3, "Peldaño a peldaño", "Baja 3 escaleras.");
        Add("esc6", "🪜", m, Stat.EscalerasLaid, 6, "Escalador", "Baja 6 escaleras.");
        Add("zero1", "0️⃣", e, Stat.ZeroRounds, 1, "Limpio", "Acaba una ronda con 0 puntos o menos.");
        Add("zero3", "0️⃣", m, Stat.ZeroRounds, 3, "Impoluto", "Acaba 3 rondas con 0 puntos o menos.");
        Add("same1", "💥", m, Stat.SameTurnCloses, 1, "De golpe", "Bájate y cierra en la misma jugada.");
        Add("same2", "💥", h, Stat.SameTurnCloses, 2, "Doble golpe", "Bájate y cierra en la misma jugada 2 veces.");
        Add("long6", "🏗️", m, Stat.LongestEscalera, 6, "Escalera larga", "Ten una escalera tuya de 6 cartas.");
        Add("long8", "🏗️", h, Stat.LongestEscalera, 8, "Rascacielos", "Ten una escalera tuya de 8 cartas.");
        Add("closes2", "🔥", h, Stat.MaxClosesInGame, 2, "En racha", "Cierra 2 rondas en una misma partida.");
        Add("podium", "🥈", m, Stat.Podiums, 1, "Podio", "Acaba entre los dos primeros en una mesa de 3 o más.");
        Add("under100", "🧊", h, Stat.WinsUnder100, 1, "Sangre fría", "Gana con 100 puntos o menos.");
        Add("nosteal", "😇", h, Stat.WinsWithoutSteal, 1, "Juego limpio", "Gana sin robar de contra.");
        Add("flawless", "🛡️", h, Stat.FlawlessGames, 1, "Sin sustos", "No seas el peor de ninguna ronda (mesa de 3+).");
        Add("alldown", "📐", h, Stat.GamesAllLaidDown, 1, "Siempre a tiempo", "Bájate en las siete rondas de una partida.");
        Add("comeback", "🔄", h, Stat.Comebacks, 1, "Remontada", "Gana sin ir primero tras la quinta ronda.");
        Add("fulltable", "🪑", m, Stat.GamesFullTable, 1, "Mesa llena", "Termina una partida con 4 jugadores o más.");
        Add("winfull", "🪑", h, Stat.WinsFullTable, 1, "Rey de la mesa llena", "Gana en una mesa de 4 o más.");
        Add("duel", "⚔️", m, Stat.GamesDuel, 1, "Mano a mano", "Termina una partida de dos jugadores.");
        Add("chat1", "💬", e, Stat.ChatMessages, 1, "Saluda", "Escribe algo en el chat de la mesa.");
        Add("chat5", "💬", e, Stat.ChatMessages, 5, "Charlatán", "Escribe 5 mensajes en el chat de la mesa.");
        Add("rounds7", "🔁", e, Stat.RoundsPlayed, 7, "Siete rondas", "Juega 7 rondas.");
        Add("rounds14", "🔁", m, Stat.RoundsPlayed, 14, "Catorce rondas", "Juega 14 rondas.");
        Add("minutes20", "⏳", e, Stat.MinutesPlayed, 20, "Un rato", "Juega 20 minutos.");
        Add("minutes45", "⏳", m, Stat.MinutesPlayed, 45, "Una buena sesión", "Juega 45 minutos.");
        Add("turns30", "🔂", e, Stat.Turns, 30, "Treinta turnos", "Juega 30 turnos.");
        Add("win3", "🏆", h, Stat.GamesWon, 3, "Triplete", "Gana 3 partidas.");
        Add("others8", "🎁", h, Stat.ExtensionsOnOthers, 8, "Okupa profesional", "Coloca 8 cartas en juegos ajenos.");
        Add("turns60", "🔂", m, Stat.Turns, 60, "Sesenta turnos", "Juega 60 turnos.");
        Add("trios9", "3️⃣", h, Stat.TriosLaid, 9, "Fábrica de tríos", "Baja 9 tríos.");
        Add("esc9", "🪜", h, Stat.EscalerasLaid, 9, "Fábrica de escaleras", "Baja 9 escaleras.");
        Add("jokers8", "🃏", h, Stat.JokersLaid, 8, "Comodinero", "Baja 8 comodines en tus juegos.");
        Add("zero5", "0️⃣", h, Stat.ZeroRounds, 5, "Inmaculado", "Acaba 5 rondas con 0 puntos o menos.");
        Add("pozo12", "♻️", h, Stat.TookDiscard, 12, "Arqueólogo", "Toma 12 cartas del pozo.");
        Add("long7", "🏗️", m, Stat.LongestEscalera, 7, "Escalera de siete", "Ten una escalera tuya de 7 cartas.");
        Add("steal3", "🦝", m, Stat.Steals, 3, "Tres de contra", "Roba de contra 3 veces.");
        Add("spain", "🇪🇸", m, Stat.GamesSpain, 1, "A la española", "Termina una partida con reglas de España.");
        Add("latam", "🌎", m, Stat.GamesLatam, 1, "A la latina", "Termina una partida con reglas de Latinoamérica.");

        string[] codes = ["TT", "TE", "EE", "TTT", "TTE", "TEE", "EEE"];

        for (var i = 0; i < codes.Length; i++)
        {
            Add($"close{codes[i].ToLowerInvariant()}", "🧩", i < 3 ? m : h, Stats.ClosesByContract[i], 1,
                $"Cierra {codes[i]}", $"Cierra la ronda de {codes[i]}.");
            Add($"down{codes[i].ToLowerInvariant()}", "⬇️", i < 3 ? e : m, Stats.DownByContract[i], 1,
                $"Bájate en {codes[i]}", $"Bájate en la ronda de {codes[i]}.");
            Add($"zero{codes[i].ToLowerInvariant()}", "🧼", i < 3 ? m : h, Stats.ZeroByContract[i], 1,
                $"Limpio en {codes[i]}", $"Acaba la ronda de {codes[i]} con 0 puntos o menos.");
        }

        Add("table3", "🔺", m, Stat.GamesTable3, 1, "Mesa de tres", "Termina una partida con 3 jugadores.");
        Add("table4", "🟥", m, Stat.GamesTable4, 1, "Mesa de cuatro", "Termina una partida con 4 jugadores.");
        Add("table5", "⭐", m, Stat.GamesTable5, 1, "Mesa de cinco", "Termina una partida con 5 jugadores.");
        Add("table6", "🎲", h, Stat.GamesTable6, 1, "Mesa de seis", "Termina una partida con 6 jugadores.");
        Add("wintable3", "🔺", h, Stat.WinsTable3, 1, "Gana a tres", "Gana una partida de 3 jugadores.");
        Add("wintable4", "🟥", h, Stat.WinsTable4, 1, "Gana a cuatro", "Gana una partida de 4 jugadores.");
        Add("winduel", "⚔️", h, Stat.WinsDuel, 1, "Duelo ganado", "Gana una partida de dos jugadores.");
        Add("winspain", "🇪🇸", h, Stat.WinsSpain, 1, "Victoria española", "Gana con reglas de España.");
        Add("winlatam", "🌎", h, Stat.WinsLatam, 1, "Victoria latina", "Gana con reglas de Latinoamérica.");
        Add("buzzer", "⏰", h, Stat.BuzzerWins, 1, "Sobre la bocina", "Gana cerrando la última ronda.");
        Add("negative1", "➖", m, Stat.CleanSweepRounds, 1, "Saldo a favor", "Acaba una ronda con puntos negativos.");
        Add("jokersgame3", "🎪", h, Stat.MaxJokersInGame, 3, "Circo", "Baja 3 comodines en una misma partida.");
        Add("extendgame5", "🧱", m, Stat.MaxExtensionsInGame, 5, "Constructor", "Coloca 5 cartas en una misma partida.");
        Add("steals2game", "🧤", h, Stat.MaxStealsInGame, 2, "Doble contra", "Roba de contra 2 veces en una misma partida.");
        Add("chat3", "💬", e, Stat.ChatMessages, 3, "Conversador", "Escribe 3 mensajes en el chat.");
        Add("rounds21", "🔁", h, Stat.RoundsPlayed, 21, "Tres partidas de rondas", "Juega 21 rondas.");
        Add("big1", "🎒", e, Stat.BigRounds, 1, "Mochila llena", "Suma 100 puntos o más en una ronda. Pasa.");
        Add("fullhand1", "🙈", e, Stat.FullHandRounds, 1, "Mano entera", "Termina una ronda sin haberte bajado.");
        Add("hair1", "🪒", h, Stat.WinsByHair, 1, "Al límite", "Gana por 5 puntos o menos.");
        Add("under50", "❄️", h, Stat.WinsUnder50, 1, "Bajo cero", "Gana con 50 puntos o menos.");
        Add("by100", "🚀", h, Stat.WinsBy100, 1, "Paliza", "Gana sacando 100 puntos o más al segundo.");
        Add("firstturn1", "⚡", h, Stat.FirstTurnCloses, 1, "Relámpago", "Cierra una ronda en tu primer turno.");
        Add("swapgame2", "🎩", h, Stat.MaxSwapsInGame, 2, "Prestidigitador", "Canjea 2 comodines en una misma partida.");
        Add("nojokers", "🐎", h, Stat.WinsWithoutJokers, 1, "Pura sangre", "Gana sin bajar ni canjear comodines.");
        Add("nodiscard", "🚫", h, Stat.NoDiscardWins, 1, "Ni lo mires", "Gana sin tomar ninguna carta del pozo.");
        Add("extendgame8", "🧱", h, Stat.MaxExtensionsInGame, 8, "Albañil de una tarde", "Coloca 8 cartas en juegos de la mesa en una misma partida.");
        Add("chat10", "💬", m, Stat.ChatMessages, 10, "Tertuliano", "Escribe 10 mensajes en el chat.");
        Add("turns90", "🔂", h, Stat.Turns, 90, "Noventa turnos", "Juega 90 turnos.");
        Add("minutes90", "⏳", h, Stat.MinutesPlayed, 90, "Tarde entera", "Juega 90 minutos.");

        (string Id, string To, string Of, Stat Stat)[] voices =
        [
            ("cunado", "al cuñado", "del cuñado", Stat.BeatCunado),
            ("picara", "a la pícara", "de la pícara", Stat.BeatPicara),
            ("zen", "al zen", "del zen", Stat.BeatZen),
            ("dramatica", "a la dramática", "de la dramática", Stat.BeatDramatica),
            ("fanfarron", "al fanfarrón", "del fanfarrón", Stat.BeatFanfarron),
            ("abuela", "a la abuela", "de la abuela", Stat.BeatAbuela)
        ];

        foreach (var (id, to, of, stat) in voices)
            Add($"beat{id}", "🤖", m, stat, 1, $"Gánale {to}", $"Acaba por delante {of} (bot) en una partida.");

        return list;
    }

    private static List<Mission> BuildWeekly()
    {
        var list = new List<Mission>();
        int[] xp = [150, 225, 325];

        void Add(string id, string icon, Difficulty difficulty, Stat stat, int target, string title, string detail)
            => list.Add(new Mission($"week.{id}", MissionScope.Weekly, stat, target, title, detail, xp[(int)difficulty], icon, "Semanales", difficulty));

        var e = Difficulty.Easy;
        var m = Difficulty.Medium;
        var h = Difficulty.Hard;

        Add("play8", "🃏", e, Stat.GamesPlayed, 8, "Semana de cartas", "Termina 8 partidas.");
        Add("play15", "🃏", m, Stat.GamesPlayed, 15, "Semana intensa", "Termina 15 partidas.");
        Add("play25", "🃏", h, Stat.GamesPlayed, 25, "Semana de vicio", "Termina 25 partidas.");
        Add("win4", "🏆", e, Stat.GamesWon, 4, "Cuatro victorias", "Gana 4 partidas.");
        Add("win8", "🏆", m, Stat.GamesWon, 8, "Ocho victorias", "Gana 8 partidas.");
        Add("win15", "🏆", h, Stat.GamesWon, 15, "Dominador", "Gana 15 partidas.");
        Add("close15", "🚪", e, Stat.RoundsClosed, 15, "Portero", "Cierra 15 rondas.");
        Add("close30", "🚪", m, Stat.RoundsClosed, 30, "Cerrajero mayor", "Cierra 30 rondas.");
        Add("close50", "🚪", h, Stat.RoundsClosed, 50, "Llave maestra", "Cierra 50 rondas.");
        Add("steal8", "🦝", e, Stat.Steals, 8, "Ladronzuelo", "Roba de contra 8 veces.");
        Add("steal15", "🦝", m, Stat.Steals, 15, "Ladrón", "Roba de contra 15 veces.");
        Add("swap5", "🌟", m, Stat.JokerSwaps, 5, "Cambiacromos", "Canjea 5 comodines.");
        Add("swap10", "🌟", h, Stat.JokerSwaps, 10, "Rey del comodín", "Canjea 10 comodines.");
        Add("extend30", "➕", e, Stat.Extensions, 30, "Colocador", "Coloca 30 cartas en juegos de la mesa.");
        Add("extend60", "➕", m, Stat.Extensions, 60, "Constructor", "Coloca 60 cartas en juegos de la mesa.");
        Add("down30", "🪜", e, Stat.RoundsLaidDown, 30, "Bajadas", "Bájate en 30 rondas.");
        Add("down60", "🪜", m, Stat.RoundsLaidDown, 60, "Siempre abajo", "Bájate en 60 rondas.");
        Add("same5", "💥", h, Stat.SameTurnCloses, 5, "Cinco golpes", "Bájate y cierra en la misma jugada 5 veces.");
        Add("zero10", "0️⃣", m, Stat.ZeroRounds, 10, "Diez limpias", "Acaba 10 rondas con 0 puntos o menos.");
        Add("dailies6", "☀️", e, Stat.DailiesCompleted, 6, "Cumplidor", "Completa 6 misiones diarias.");
        Add("dailies12", "☀️", m, Stat.DailiesCompleted, 12, "Muy cumplidor", "Completa 12 misiones diarias.");
        Add("sets3", "🌞", h, Stat.DailySetsCompleted, 3, "Tres días redondos", "Completa las tres diarias en 3 días.");
        Add("days3", "📅", e, Stat.DaysPlayed, 3, "Tres días", "Juega 3 días distintos.");
        Add("days5", "📅", m, Stat.DaysPlayed, 5, "Cinco días", "Juega 5 días distintos.");
        Add("humans3", "🧑‍🤝‍🧑", m, Stat.GamesVsHumans, 3, "Con amigos", "Termina 3 partidas con otras personas.");
        Add("winhumans2", "🎯", h, Stat.WinsVsHumans, 2, "Gana a tus amigos", "Gana 2 partidas con otras personas.");
        Add("full5", "🪑", m, Stat.GamesFullTable, 5, "Mesas llenas", "Termina 5 partidas con 4 jugadores o más.");
        Add("under100x2", "🧊", h, Stat.WinsUnder100, 2, "Doble sangre fría", "Gana 2 partidas con 100 puntos o menos.");
        Add("comeback", "🔄", h, Stat.Comebacks, 1, "Remontada", "Gana sin ir primero tras la quinta ronda.");
        Add("alldown2", "📐", h, Stat.GamesAllLaidDown, 2, "Puntual", "Bájate en las siete rondas de 2 partidas.");
        Add("long9", "🏗️", h, Stat.LongestEscalera, 9, "Rascacielos", "Ten una escalera tuya de 9 cartas.");
        Add("closes3", "🔥", h, Stat.MaxClosesInGame, 3, "Hat-trick", "Cierra 3 rondas en una misma partida.");
        Add("minutes120", "⏳", m, Stat.MinutesPlayed, 120, "Dos horas de mesa", "Juega 2 horas.");
        Add("chat20", "💬", e, Stat.ChatMessages, 20, "Tertulia", "Escribe 20 mensajes en el chat.");
        Add("pozo25", "♻️", e, Stat.TookDiscard, 25, "Del pozo", "Toma 25 cartas del pozo.");
        Add("jokers15", "🃏", m, Stat.JokersLaid, 15, "Comodinero", "Baja 15 comodines en tus juegos.");
        Add("voices", "🗺️", h, Stat.DistinctVoicesBeaten, 4, "Gira de bots", "Gánale a 4 personalidades de bot distintas.");
        Add("eee", "🧩", h, Stat.ClosesEEE, 2, "Tres escaleras", "Cierra 2 rondas de EEE.");
        Add("ttt", "🧩", m, Stat.ClosesTTT, 2, "Tres tríos", "Cierra 2 rondas de TTT.");
        Add("tt", "🧩", e, Stat.ClosesTT, 2, "Dos tríos", "Cierra 2 rondas de TT.");
        Add("te", "🧩", e, Stat.ClosesTE, 2, "Trío y escalera", "Cierra 2 rondas de TE.");
        Add("ee", "🧩", m, Stat.ClosesEE, 2, "Dos escaleras", "Cierra 2 rondas de EE.");
        Add("tte", "🧩", m, Stat.ClosesTTE, 2, "TTE", "Cierra 2 rondas de TTE.");
        Add("tee", "🧩", h, Stat.ClosesTEE, 2, "TEE", "Cierra 2 rondas de TEE.");
        Add("downeee", "⬇️", m, Stat.DownEEE, 3, "Escaleras abajo", "Bájate en 3 rondas de EEE.");
        Add("downttt", "⬇️", e, Stat.DownTTT, 3, "Tríos abajo", "Bájate en 3 rondas de TTT.");
        Add("zero20", "0️⃣", h, Stat.ZeroRounds, 20, "Veinte limpias", "Acaba 20 rondas con 0 puntos o menos.");
        Add("win25", "🏆", h, Stat.GamesWon, 25, "Arrasador", "Gana 25 partidas.");
        Add("play40", "🃏", h, Stat.GamesPlayed, 40, "Semana de maratón", "Termina 40 partidas.");
        Add("closes80", "🚪", h, Stat.RoundsClosed, 80, "Cerrajero legendario", "Cierra 80 rondas.");
        Add("steal25", "🦝", h, Stat.Steals, 25, "Gran ladrón", "Roba de contra 25 veces.");
        Add("swap3", "🌟", e, Stat.JokerSwaps, 3, "Tres canjes", "Canjea 3 comodines.");
        Add("extend100", "➕", h, Stat.Extensions, 100, "Obra mayor", "Coloca 100 cartas en juegos de la mesa.");
        Add("others20", "🎁", m, Stat.ExtensionsOnOthers, 20, "Okupa semanal", "Coloca 20 cartas en juegos ajenos.");
        Add("trios20", "3️⃣", e, Stat.TriosLaid, 20, "Veinte tríos", "Baja 20 tríos.");
        Add("esc20", "🪜", e, Stat.EscalerasLaid, 20, "Veinte escaleras", "Baja 20 escaleras.");
        Add("trios40", "3️⃣", m, Stat.TriosLaid, 40, "Cuarenta tríos", "Baja 40 tríos.");
        Add("esc40", "🪜", m, Stat.EscalerasLaid, 40, "Cuarenta escaleras", "Baja 40 escaleras.");
        Add("same10", "💥", h, Stat.SameTurnCloses, 10, "Diez golpes", "Bájate y cierra de golpe 10 veces.");
        Add("same2", "💥", m, Stat.SameTurnCloses, 2, "Dos golpes", "Bájate y cierra de golpe 2 veces.");
        Add("first2", "⚡", h, Stat.FirstTurnCloses, 2, "Doble relámpago", "Cierra 2 rondas en tu primer turno.");
        Add("podium5", "🥈", m, Stat.Podiums, 5, "Cinco podios", "Acaba entre los dos primeros 5 veces (mesas de 3+).");
        Add("flawless2", "🛡️", h, Stat.FlawlessGames, 2, "Sin sustos", "Termina 2 partidas sin ser nunca el peor de una ronda.");
        Add("spain5", "🇪🇸", m, Stat.GamesSpain, 5, "Semana española", "Termina 5 partidas con reglas de España.");
        Add("latam5", "🌎", m, Stat.GamesLatam, 5, "Semana latina", "Termina 5 partidas con reglas de Latinoamérica.");
        Add("duel5", "⚔️", m, Stat.GamesDuel, 5, "Cinco duelos", "Termina 5 partidas de dos jugadores.");
        Add("six3", "🎲", h, Stat.GamesTable6, 3, "Mesas de seis", "Termina 3 partidas con 6 jugadores.");
        Add("minutes300", "⏳", h, Stat.MinutesPlayed, 300, "Cinco horas", "Juega 5 horas.");
        Add("minutes60", "⏳", e, Stat.MinutesPlayed, 60, "Una hora", "Juega 1 hora.");
        Add("turns200", "🔂", e, Stat.Turns, 200, "Doscientos turnos", "Juega 200 turnos.");
        Add("turns500", "🔂", m, Stat.Turns, 500, "Quinientos turnos", "Juega 500 turnos.");
        Add("big5", "🎒", e, Stat.BigRounds, 5, "Mochilero", "Suma 100 puntos o más en 5 rondas.");
        Add("buzzer2", "⏰", h, Stat.BuzzerWins, 2, "Bocina doble", "Gana 2 partidas cerrando la última ronda.");
        Add("negative5", "➖", h, Stat.CleanSweepRounds, 5, "Números rojos", "Acaba 5 rondas con puntos negativos.");
        Add("sets1", "🌞", e, Stat.DailySetsCompleted, 1, "Un día redondo", "Completa las tres diarias en un día.");
        Add("voices2", "🗺️", m, Stat.DistinctVoicesBeaten, 2, "Gira corta", "Gánale a 2 personalidades de bot distintas.");
        Add("hair3", "🪒", h, Stat.WinsByHair, 3, "Tres sustos", "Gana 3 partidas por 5 puntos o menos.");
        Add("under50x2", "❄️", h, Stat.WinsUnder50, 2, "Doble bajo cero", "Gana 2 partidas con 50 puntos o menos.");
        Add("by100x3", "🚀", h, Stat.WinsBy100, 3, "Tres palizas", "Gana 3 partidas sacando 100 puntos o más al segundo.");
        Add("nosteal5", "😇", m, Stat.WinsWithoutSteal, 5, "Semana limpia", "Gana 5 partidas sin robar de contra.");
        Add("jokers30", "🃏", h, Stat.JokersLaid, 30, "Comodinero mayor", "Baja 30 comodines en tus juegos.");
        Add("days7", "📅", h, Stat.DaysPlayed, 7, "Todos los días", "Juega los siete días de la semana.");
        Add("rounds70", "🔁", m, Stat.RoundsPlayed, 70, "Setenta rondas", "Juega 70 rondas.");
        Add("first5", "⚡", h, Stat.FirstTurnCloses, 5, "Tormenta eléctrica", "Cierra 5 rondas en tu primer turno.");
        Add("alldownwin2", "🏅", h, Stat.AllDownWins, 2, "Puntualidad premiada", "Gana 2 partidas bajándote en las siete rondas.");
        Add("podiumhumans3", "🎖️", m, Stat.PodiumsVsHumans, 3, "Podio con amigos", "Acaba 3 veces entre los dos primeros con otras personas en la mesa.");

        return list;
    }

    public static IReadOnlyList<Mission> PoolFor(MissionScope scope)
        => scope == MissionScope.Daily ? DailyPool : WeeklyPool;

    public static List<string> Pick(MissionScope scope, int seed)
    {
        var pool = PoolFor(scope);
        var random = new Random(seed);
        var picked = new List<string>();

        foreach (var difficulty in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
        {
            var options = pool.Where(m => m.Difficulty == difficulty).ToList();
            picked.Add(options[random.Next(options.Count)].Id);
        }

        return picked;
    }

    public static string? Replacement(MissionScope scope, IReadOnlyCollection<string> current, string replacing, int seed)
    {
        if (Find(replacing) is not { } old)
            return null;

        var options = PoolFor(scope)
            .Where(m => m.Difficulty == old.Difficulty && !current.Contains(m.Id))
            .ToList();

        return options.Count == 0 ? null : options[new Random(seed).Next(options.Count)].Id;
    }
}
