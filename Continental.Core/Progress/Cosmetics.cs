namespace Continental.Core.Progress;

public enum CosmeticKind
{
    CardBack,
    Felt,
    Avatar,
    Title,
    Victory,
    Sound,
    Trail,
    Phrases
}

public sealed record Cosmetic(string Id, CosmeticKind Kind, string Name, string Value, int Level = 1, string? Mission = null)
{
    public bool IsDefault => Level <= 1 && Mission is null;
}

public static class Cosmetics
{
    public const int LadderTop = 100;

    public static readonly IReadOnlyList<string> BasePhrases =
        ["jajaja", "¡Qué morro!", "Venga, que es tu turno", "Buena jugada 👏", "Eso no vale", "😭"];

    public static readonly IReadOnlyList<(string Id, string Name)> BackPatterns =
    [
        ("rayas", "Rayas"), ("lunares", "Lunares"), ("rombos", "Rombos"),
        ("ondas", "Ondas"), ("cuadros", "Cuadros"), ("estrellas", "Estrellas")
    ];

    public static readonly IReadOnlyList<(string Id, string Name)> BackColors =
    [
        ("rojo", "rojo"), ("azul", "azul"), ("verde", "verde"), ("morado", "morado"),
        ("naranja", "naranja"), ("rosa", "rosa"), ("turquesa", "turquesa"), ("negro", "negro"),
        ("dorado", "dorado"), ("plata", "plata"), ("burdeos", "burdeos"), ("celeste", "celeste")
    ];

    private const int Ladder = 0;

    public static IReadOnlyList<Cosmetic> All { get; }

    private static readonly Dictionary<string, Cosmetic> ById;

    static Cosmetics()
    {
        All = Build();
        ById = All.ToDictionary(c => c.Id);
    }

    public static Cosmetic? Find(string? id) => id is null ? null : ById.GetValueOrDefault(id);

    public static IEnumerable<Cosmetic> OfKind(CosmeticKind kind) => All.Where(c => c.Kind == kind);

    public static Cosmetic DefaultOf(CosmeticKind kind) => All.First(c => c.Kind == kind && c.IsDefault);

    public static string KindName(CosmeticKind kind) => kind switch
    {
        CosmeticKind.CardBack => "Dorsos",
        CosmeticKind.Felt => "Tapetes",
        CosmeticKind.Avatar => "Avatares",
        CosmeticKind.Title => "Títulos",
        CosmeticKind.Victory => "Celebraciones",
        CosmeticKind.Sound => "Sonidos",
        CosmeticKind.Trail => "Estelas",
        CosmeticKind.Phrases => "Frases del chat",
        _ => kind.ToString()
    };

    public static string KindSingular(CosmeticKind kind) => kind switch
    {
        CosmeticKind.CardBack => "Dorso",
        CosmeticKind.Felt => "Tapete",
        CosmeticKind.Avatar => "Avatar",
        CosmeticKind.Title => "Título",
        CosmeticKind.Victory => "Celebración",
        CosmeticKind.Sound => "Sonido",
        CosmeticKind.Trail => "Estela",
        CosmeticKind.Phrases => "Frases",
        _ => kind.ToString()
    };

    public static bool IsEquippable(CosmeticKind kind) => kind != CosmeticKind.Phrases;

    public static IEnumerable<Cosmetic> UnlockedAt(int level)
        => All.Where(c => c.Mission is null && c.Level == level && level > 1);

    public static Cosmetic? NextUnlock(int level)
        => All.Where(c => c.Mission is null && c.Level > level).OrderBy(c => c.Level).FirstOrDefault();

    private static List<Cosmetic> Build()
    {
        var list = new List<Cosmetic>();

        void Add(CosmeticKind kind, string id, string name, string value, int level, string? mission)
            => list.Add(new Cosmetic(id, kind, name, value, level, mission));

        void Back(string id, string name, int level = Ladder, string? mission = null)
            => Add(CosmeticKind.CardBack, $"back.{id}", name, id, level, mission);

        void Felt(string id, string name, int level = Ladder, string? mission = null)
            => Add(CosmeticKind.Felt, $"felt.{id}", name, id, level, mission);

        void Avatar(string emoji, string name, int level = Ladder, string? mission = null)
            => Add(CosmeticKind.Avatar, $"avatar.{Slug(name)}", name, emoji, level, mission);

        void Title(string text, int level = Ladder, string? mission = null)
            => Add(CosmeticKind.Title, $"title.{Slug(text)}", text, text, level, mission);

        void Victory(string id, string name, int level = Ladder, string? mission = null)
            => Add(CosmeticKind.Victory, $"victory.{id}", name, id, level, mission);

        void Sound(string id, string name, int level = Ladder)
            => Add(CosmeticKind.Sound, $"sound.{id}", name, id, level, null);

        void Trail(string id, string name, int level = Ladder, string? mission = null)
            => Add(CosmeticKind.Trail, $"trail.{id}", name, id, level, mission);

        void Phrases(string id, string name, string[] lines, int level = Ladder, string? mission = null)
            => Add(CosmeticKind.Phrases, $"phrases.{id}", name, string.Join("|", lines), level, mission);

        Back("clasico", "Clásico", 1);
        Back("granate", "Granate");
        Back("esmeralda", "Esmeralda");
        Back("noche", "Noche estrellada");
        Back("oro", "Oro viejo");
        Back("damero", "Damero");
        Back("deco", "Art déco");
        Back("diamantes", "Diamantes");
        Back("arcoiris", "Arcoíris");
        Back("carbono", "Carbono");
        Back("leyenda", "Leyenda", 65);
        Back("inmortal", "Inmortal", 100);
        Back("semidios", "Semidiós", 120);
        Back("eterno", "Eterno", 150);
        Back("tormenta", "Tormenta", mission: "sec.storm");
        Back("cazador", "Cazador", mission: "ach.fivebots.4");
        Back("comodin", "Comodín", mission: "ach.swaps.6");
        Back("ladron", "Antifaz", mission: "ach.steals.6");
        Back("galaxia", "Galaxia", mission: "sec.longstreak");
        Back("fuego", "Brasas", mission: "ach.closed.7");
        Back("hielo", "Escarcha", mission: "ach.under100.4");
        Back("real", "Sello real", mission: "ach.won.7");

        foreach (var (color, colorName) in BackColors)
            foreach (var (pattern, patternName) in BackPatterns)
                Back($"{pattern}-{color}", $"{patternName} en {colorName}");

        Felt("verde", "Verde clásico", 1);
        Felt("azul", "Azul casino");
        Felt("granate", "Granate");
        Felt("purpura", "Púrpura");
        Felt("grafito", "Grafito");
        Felt("oceano", "Océano");
        Felt("atardecer", "Atardecer");
        Felt("medianoche", "Medianoche");
        Felt("oronegro", "Oro negro");
        Felt("rubi", "Rubí");
        Felt("zafiro", "Zafiro");
        Felt("jade", "Jade");
        Felt("ambar", "Ámbar");
        Felt("lavanda", "Lavanda");
        Felt("chocolate", "Chocolate");
        Felt("pizarra", "Pizarra");
        Felt("bosque", "Bosque");
        Felt("vino", "Vino");
        Felt("cobalto", "Cobalto");
        Felt("oliva", "Oliva");
        Felt("coral", "Coral");
        Felt("glaciar", "Glaciar");
        Felt("menta", "Menta");
        Felt("mostaza", "Mostaza");
        Felt("ciruela", "Ciruela");
        Felt("acero", "Acero");
        Felt("terracota", "Terracota");
        Felt("musgo", "Musgo");
        Felt("indigo", "Índigo");
        Felt("carmesi", "Carmesí");
        Felt("aurora", "Aurora", 70);
        Felt("olimpo", "Olimpo", 120);
        Felt("eterno", "Eterno", 150);
        Felt("insomne", "Insomne", mission: "sec.insomnia");
        Felt("fondista", "Pista de fondo", mission: "ach.marathons.3");
        Felt("real", "Casino real", mission: "ach.won.6");
        Felt("cerezo", "Cerezo", mission: "ach.daystreak.5");
        Felt("volcan", "Volcán", mission: "ach.sameturn.5");
        Felt("abismo", "Abismo", mission: "sec.untouchable");

        Avatar("", "Inicial", 1);

        (string Emoji, string Name)[] ladderAvatars =
        [
            ("🃏", "Comodín"), ("🦊", "Zorro"), ("🐙", "Pulpo"), ("🦉", "Búho"), ("🐯", "Tigre"), ("🐸", "Rana"),
            ("🐼", "Panda"), ("🦁", "León"), ("🐺", "Lobo"), ("🦄", "Unicornio"), ("🐲", "Dragón"), ("🦈", "Tiburón"),
            ("🎩", "Chistera"), ("🤠", "Vaquero"), ("🥷", "Ninja"), ("🧙", "Mago"), ("🦖", "Dinosaurio"), ("🤖", "Robot"),
            ("👽", "Alien"), ("🌵", "Cactus"), ("😎", "Chulo"), ("🦩", "Flamenco"), ("🐢", "Tortuga"), ("🍀", "Trébol"),
            ("🌶️", "Guindilla"), ("🚀", "Cohete"), ("⚡", "Rayo"), ("🔥", "Fuego"), ("🌙", "Luna"), ("☀️", "Sol"),
            ("🐧", "Pingüino"), ("🐬", "Delfín"), ("🎃", "Calabaza"), ("💀", "Calavera"), ("🐶", "Perro"), ("🐱", "Gato"),
            ("🐭", "Ratón"), ("🐹", "Hámster"), ("🐰", "Conejo"), ("🐻", "Oso"), ("🐨", "Koala"), ("🐮", "Vaca"),
            ("🐷", "Cerdito"), ("🐵", "Mono"), ("🐔", "Gallina"), ("🐤", "Pollito"), ("🦆", "Pato"), ("🦅", "Águila"),
            ("🦇", "Murciélago"), ("🐗", "Jabalí"), ("🐴", "Caballo"), ("🐝", "Abeja"), ("🦋", "Mariposa"), ("🐌", "Caracol"),
            ("🐞", "Mariquita"), ("🦂", "Escorpión"), ("🐍", "Serpiente"), ("🦎", "Lagartija"), ("🐊", "Cocodrilo"), ("🐳", "Ballena"),
            ("🐠", "Pez tropical"), ("🐡", "Pez globo"), ("🦑", "Calamar"), ("🦀", "Cangrejo"), ("🦞", "Langosta"), ("🦒", "Jirafa"),
            ("🦓", "Cebra"), ("🦍", "Gorila"), ("🐘", "Elefante"), ("🦛", "Hipopótamo"), ("🦏", "Rinoceronte"), ("🦘", "Canguro"),
            ("🦙", "Llama"), ("🦌", "Ciervo"), ("🦚", "Pavo real"), ("🦜", "Loro"), ("🦢", "Cisne"), ("🦔", "Erizo"),
            ("🦦", "Nutria"), ("🦥", "Perezoso"), ("🍎", "Manzana"), ("🍉", "Sandía"), ("🍓", "Fresa"), ("🥑", "Aguacate"),
            ("🌮", "Taco"), ("🍔", "Hamburguesa"), ("🍩", "Rosquilla"), ("🍿", "Palomitas"), ("🧀", "Queso"), ("🥐", "Cruasán"),
            ("☕", "Cafetito"), ("🎸", "Guitarra"), ("🎺", "Trompeta"), ("🥁", "Tambor"), ("🎯", "Diana"), ("🎳", "Bolos"),
            ("⚽", "Balón"), ("🏀", "Canasta"), ("🥊", "Guante de boxeo"), ("🗿", "Moái"), ("🏰", "Castillo"), ("🌋", "Volcán"),
            ("🌊", "Ola"), ("❄️", "Copo"), ("☃️", "Muñeco de nieve"), ("🌻", "Girasol"), ("🌹", "Rosa"), ("🍄", "Seta"),
            ("🌲", "Pino"), ("🧛", "Vampiro"), ("🧜", "Sirena"), ("🧞", "Genio"), ("🦸", "Superhéroe"), ("🤡", "Payaso"),
            ("👻", "Fantasma"), ("🎅", "Papá Noel"), ("🐉", "Dragón chino"), ("👑", "Corona"), ("💎", "Diamante"),
            ("🦫", "Castor"), ("🦡", "Tejón"), ("🦭", "Foca"), ("🐿️", "Ardilla"), ("🦤", "Dodo"), ("🐛", "Oruga"),
            ("🐟", "Pez"), ("🦗", "Grillo"), ("🐫", "Camello"), ("🐆", "Leopardo"), ("🦬", "Bisonte"), ("🐇", "Liebre"),
            ("🍕", "Pizza"), ("🥨", "Pretzel"), ("🍤", "Gamba"), ("🥘", "Paella"), ("🍫", "Chocolatina"), ("🧉", "Mate"),
            ("🎻", "Violín"), ("🪗", "Acordeón"), ("🎲", "Dado"), ("♟️", "Peón"), ("🪁", "Cometa"), ("🛼", "Patín"),
            ("🏝️", "Isla"), ("🗻", "Monte Fuji"), ("🌪️", "Remolino"), ("🌺", "Hibisco"), ("🪐", "Saturno"), ("☄️", "Meteorito"),
            ("🧑‍🚀", "Astronauta"), ("🕵️", "Detective"), ("🧑‍🍳", "Cocinero"), ("🧑‍🎤", "Rockero"), ("🥸", "Disfrazado"), ("🤓", "Empollón")
        ];

        foreach (var (emoji, name) in ladderAvatars)
            Avatar(emoji, name);

        Avatar("🐐", "Cabra", 75);
        Avatar("🌌", "Galaxia", 90);
        Avatar("🌠", "Estrella fugaz", 120);
        Avatar("🏛️", "Panteón", 150);
        Avatar("🔮", "Bola de cristal", mission: "sec.archmage");
        Avatar("🪨", "Roca", mission: "sec.rock");
        Avatar("👯", "Gemelos", mission: "sec.siblings");
        Avatar("🧱", "Ladrillo", mission: "sec.wall");
        Avatar("🎰", "Tragaperras", mission: "ach.fastwins.3");
        Avatar("🛋️", "Sofá", mission: "ach.weekend.3");
        Avatar("🌅", "Amanecer", mission: "ach.morning.3");
        Avatar("🏹", "Arquero", mission: "ach.fivebots.3");
        Avatar("🦝", "Mapache", mission: "ach.steals.5");
        Avatar("🎭", "Teatro", mission: "ach.beatdramatica.4");
        Avatar("👵", "Abuela", mission: "sec.elders");
        Avatar("🌃", "Noctámbulo", mission: "sec.owl");
        Avatar("🪄", "Varita", mission: "sec.magician");
        Avatar("🌈", "Suertudo", mission: "sec.miracle");
        Avatar("🐓", "Gallo", mission: "sec.early");
        Avatar("🦾", "Brazo biónico", mission: "sec.terminator");
        Avatar("🏃", "Corredor", mission: "sec.marathon");
        Avatar("🧳", "Maleta", mission: "sec.tourist");
        Avatar("🎒", "Mochila", mission: "sec.huge");
        Avatar("🧲", "Imán", mission: "sec.hoarder");
        Avatar("🗼", "Torre", mission: "sec.tower");
        Avatar("🌫️", "Espectro", mission: "sec.untouchable");
        Avatar("🥳", "Fiestero", mission: "sec.party3");
        Avatar("⭕", "Cero", mission: "sec.zero");
        Avatar("🏁", "Bandera a cuadros", mission: "sec.threestart");
        Avatar("🪶", "Pluma", mission: "ach.winhumans.4");
        Avatar("🦣", "Mamut", mission: "ach.played.7");
        Avatar("🧠", "Cerebro", mission: "ach.flawless.3");
        Avatar("📚", "Sabio", mission: "ach.alldown.3");
        Avatar("🏆", "Trofeo", mission: "ach.won.5");
        Avatar("🥇", "Medalla de oro", mission: "ach.streak.4");
        Avatar("🔑", "Llave", mission: "ach.closed.6");
        Avatar("🧩", "Pieza", mission: "ach.closeeee.3");
        Avatar("🪜", "Escalera", mission: "ach.longest.6");

        foreach (var rank in Leveling.Ranks)
            Title(rank.Name, rank.From);

        Title("Sin título", 1);

        string[] ladderTitles =
        [
            "El de las cartas", "Barajador", "Rey del barrio", "Cuñado profesional", "Mano de santo", "Sin piedad",
            "Tiburón de mesa", "Rey del pozo", "La calma", "Estratega", "El profesor", "Mente fría", "Pícaro",
            "Zorro viejo", "Pura suerte", "Dedos rápidos", "Cara de póker", "El que nunca tira", "Contador de cartas",
            "Maestro del farol", "Sabio de la mesa", "Terror de los bots", "El elegido", "Carta blanca",
            "Siete vidas", "Fénix", "El croupier", "Rey de corazones", "Reina de picas", "Jota de diamantes",
            "Barón del descarte", "Duque del comodín", "Rey sin corona", "La leyenda del bar", "Mano de hierro",
            "Pulso firme", "El tapado", "Silencioso", "Ojo de halcón", "El paciente", "Mago de la baraja",
            "Carta ganadora", "Doble o nada", "El calculador", "Pies de plomo", "Guante de seda", "Viejo lobo",
            "Sin miedo", "El jefe de la mesa", "As en la manga"
        ];

        foreach (var text in ladderTitles)
            Title(text);

        (string Text, string Mission)[] missionTitles =
        [
            ("Ladrón de guante blanco", "ach.steals.5"), ("Rey del comodín", "ach.swaps.5"), ("Cerrojo", "ach.closed.6"),
            ("Relámpago", "ach.firstturn.3"), ("Terror de la abuela", "ach.beatabuela.4"), ("Remontador", "ach.comeback.3"),
            ("Imbatible", "ach.streak.4"), ("Farolillo rojo", "ach.last.3"), ("Constante", "ach.daystreak.5"),
            ("Arquitecto", "ach.longest.5"), ("Pura sangre", "ach.nojokers.3"), ("Okupa", "ach.extendothers.4"),
            ("Buzo del pozo", "ach.discard.4"), ("Triero mayor", "ach.trios.5"), ("Escalador", "ach.escaleras.5"),
            ("Charlatán", "ach.chat.4"), ("Duelista", "ach.duel.3"), ("Castizo", "ach.spain.3"),
            ("Latino de corazón", "ach.latam.3"), ("Sangre fría", "ach.under100.3"), ("Apisonadora", "ach.by100.3"),
            ("Juego limpio", "ach.nosteal.3"), ("Siempre a tiempo", "ach.alldown.3"), ("Sin sustos", "ach.flawless.3"),
            ("Incansable", "ach.rounds.6"), ("De golpe", "ach.sameturn.4"), ("Mano limpia", "ach.zero.5"),
            ("Rey de la sala", "ach.six.3"), ("Alma de la fiesta", "ach.humans.5"), ("Ganador de verdad", "ach.winhumans.4"),
            ("Cumplidor", "ach.dailies.5"), ("Día redondo", "ach.dailysets.4"), ("Coleccionista", "ach.achievements.5"),
            ("Veterano de guerra", "ach.played.7"), ("Campeón", "ach.won.6"), ("Trío de ases", "ach.table3.3"),
            ("Sobre la bocina", "ach.buzzer.3"), ("Conoces a todos", "ach.voices.5"), ("Cuñado domado", "ach.beatcunado.4"),
            ("Más pícaro que nadie", "ach.beatpicara.4"), ("Rompe la calma", "ach.beatzen.4"), ("Baja humos", "ach.beatfanfarron.4"),
            ("Búho nocturno", "sec.owl"), ("Madrugador", "sec.early"), ("Mochilero", "sec.huge"),
            ("Mago de los comodines", "sec.magician"), ("El del milagro", "sec.miracle"), ("Magia negra", "sec.negative"),
            ("Pleno al siete", "sec.seven"), ("Intocable", "sec.untouchable"), ("Cero absoluto", "sec.zero"),
            ("Imparable", "sec.longstreak"), ("Completista", "sec.collector"), ("Exterminador", "sec.terminator"),
            ("Dominguero", "sec.sunday"), ("Nieto obediente", "sec.elders"), ("Por los pelos", "sec.hair"),
            ("Exprés", "sec.fast"), ("Turista", "sec.tourist"), ("Aspiradora", "sec.hoarder"),
            ("Archimago", "sec.archmage"), ("Roca", "sec.rock"), ("Hermanos de mesa", "sec.siblings"),
            ("Huracán", "sec.storm"), ("Muralla", "sec.wall"), ("Insomne", "sec.insomnia"),
            ("Cazador de bots", "ach.fivebots.3"), ("Se aprende perdiendo", "ach.lost.4"), ("Al filo", "ach.hair.3"),
            ("Ganador exprés", "ach.fastwins.3"), ("Números negros", "ach.negwins.2"), ("Fiesta grande", "ach.vsthree.3"),
            ("Buen arranque", "ach.threestart.3"), ("Fin de semana", "ach.weekend.3"), ("Trasnochador", "ach.night.3"),
            ("Tempranero", "ach.morning.3"), ("Fondista", "ach.marathons.3")
        ];

        foreach (var (text, mission) in missionTitles)
            Title(text, mission: mission);

        Victory("confeti", "Confeti", 1);
        Victory("palos", "Lluvia de palos");
        Victory("estrellas", "Estrellas");
        Victory("monedas", "Monedas");
        Victory("comodines", "Comodines");
        Victory("fuegos", "Fuegos artificiales");
        Victory("oro", "Lluvia de oro");
        Victory("hielo", "Confeti de hielo");
        Victory("brasas", "Confeti de fuego");
        Victory("neon", "Confeti neón");
        Victory("pastel", "Confeti pastel");
        Victory("bosque", "Confeti del bosque");
        Victory("noche", "Confeti de medianoche");
        Victory("caramelo", "Confeti de caramelo");
        Victory("arcoiris", "Arcoíris", mission: "ach.dailysets.3");
        Victory("tricolor", "Tricolor", mission: "ach.winhumans.3");

        Sound("clasico", "Clásico", 1);
        Sound("suave", "Suave");
        Sound("retro", "Retro 8 bits");
        Sound("casino", "Casino");
        Sound("cristal", "Cristal");
        Sound("grave", "Grave");
        Sound("campanas", "Campanas");
        Sound("organo", "Órgano");

        Trail("ninguna", "Sin estela", 1);
        Trail("dorada", "Dorada");
        Trail("hielo", "Hielo");
        Trail("fuego", "Fuego");
        Trail("arcoiris", "Arcoíris");
        Trail("esmeralda", "Esmeralda");
        Trail("rosa", "Rosa");
        Trail("violeta", "Violeta");
        Trail("plata", "Plata");
        Trail("sombra", "Sombra");
        Trail("neon", "Neón");
        Trail("sol", "Sol");
        Trail("menta", "Menta");
        Trail("cobre", "Cobre");
        Trail("sangre", "Sangre");
        Trail("aurora", "Aurora");
        Trail("tornado", "Tornado", mission: "sec.storm");
        Trail("rayo", "Rayo", mission: "ach.firstturn.4");
        Trail("cometa", "Cometa", mission: "sec.threestart");

        Phrases("emojis", "Emojis", ["🔥", "🤡", "🙈", "😎", "🥲", "🫡"]);
        Phrases("pique", "Pique", ["¿Eso es todo?", "Te veo nervioso", "Tira ya, que nos dormimos", "Esa carta era mía", "Qué suerte tienes"]);
        Phrases("abuela", "La abuela", ["Ay, criatura", "En mis tiempos se jugaba mejor", "Come algo, que estás flaco", "Esto lo gano con los ojos cerrados"]);
        Phrases("fanfarron", "Fanfarrón", ["Soy una máquina", "Tomad nota", "Otra lección gratis", "Demasiado fácil 😏"]);
        Phrases("drama", "Drama", ["¡NOOOO!", "Me quiero ir a casa", "Esto es una injusticia", "Mi corazón no aguanta 💔"]);
        Phrases("leyenda", "Leyenda", ["👑", "🐐", "💎", "GG", "Aprended del maestro"]);
        Phrases("educado", "Educado", ["Buenas tardes a todos", "Por favor, su turno", "Muy bien jugado, de verdad", "Gracias por la partida"]);
        Phrases("futbolero", "Futbolero", ["¡Golazo!", "Esto es un penalti", "Tarjeta roja", "Se nos va al descanso"]);
        Phrases("pirata", "Pirata", ["¡Al abordaje!", "Arrr, esa carta es mía", "A la plancha contigo", "Botín asegurado 🏴‍☠️"]);
        Phrases("poeta", "Poeta", ["Verde que te quiero verde", "Caminante, no hay carta", "Mis cartas, mi poesía", "Oh, fortuna"]);
        Phrases("zen", "Zen", ["Respira", "Todo pasa", "La carta llegará", "Paciencia 🧘"]);
        Phrases("cunado", "Cuñado", ["Yo esto lo veía venir", "Te lo dije", "Eso lo hago yo mejor", "Hazme caso a mí"]);
        Phrases("casino", "Casino", ["Las cartas no mienten", "Otra mano, caballeros", "Aquí se viene a ganar", "Voy con todo"]);
        Phrases("fiesta", "Fiesta", ["🎉🎉🎉", "¡Esto hay que celebrarlo!", "¡Que siga la fiesta!", "🍾"]);
        Phrases("lloron", "Llorón", ["Siempre me tocan las malas", "No es justo 😢", "Hoy no es mi día", "Otra vez yo..."]);
        Phrases("tecnico", "Técnico", ["Probabilidad baja, pero ahí va", "Jugada de manual", "Estadísticamente, me toca", "Calculado 📐"]);
        Phrases("misterioso", "Misterioso", ["...", "Ya lo verás", "Nadie sospecha nada", "🤫"]);
        Phrases("motivador", "Motivador", ["¡Tú puedes!", "Esa es la actitud", "¡Vamos, equipo!", "Nunca te rindas 💪"]);
        Phrases("sarcastico", "Sarcástico", ["Qué sorpresa...", "Nadie lo vio venir", "Brillante, de verdad", "Ah, genial 🙃"]);
        Phrases("roca", "Roca", ["Otra más, qué le vamos a hacer", "Yo no me rindo", "La próxima es mía", "Firme como una roca 🪨"], mission: "sec.rock");
        Phrases("ladron", "Ladrón", ["Esa me la llevo", "Gracias por el regalo", "Robado con cariño 😇"], mission: "ach.steals.4");

        return AssignLadder(list);
    }

    private static List<Cosmetic> AssignLadder(List<Cosmetic> list)
    {
        var ladder = list
            .Select((c, index) => (c, index))
            .Where(x => x.c.Level == Ladder && x.c.Mission is null)
            .GroupBy(x => x.c.Kind)
            .SelectMany(g =>
            {
                var items = g.ToList();
                return items.Select((x, i) => (x.index, Order: (i + 0.5) / items.Count, Kind: (int)g.Key));
            })
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Kind)
            .ToList();

        var span = LadderTop - 1;

        for (var i = 0; i < ladder.Count; i++)
        {
            var level = 2 + (int)((long)i * span / ladder.Count);
            list[ladder[i].index] = list[ladder[i].index] with { Level = level };
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Level == Ladder)
                list[i] = list[i] with { Level = 1 };
        }

        return list;
    }

    private static string Slug(string text)
    {
        var chars = text.ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        return new string(chars).Trim('-');
    }
}
