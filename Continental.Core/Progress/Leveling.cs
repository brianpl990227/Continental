namespace Continental.Core.Progress;

public sealed record LevelRank(int From, string Name, string Tier);

public readonly record struct LevelProgress(int Level, int Into, int Needed)
{
    public double Fraction => Needed <= 0 ? 1 : Math.Clamp((double)Into / Needed, 0, 1);
}

public static class Leveling
{
    public static readonly IReadOnlyList<LevelRank> Ranks =
    [
        new(1, "Novato", "wood"),
        new(3, "Principiante", "wood"),
        new(5, "Aprendiz", "bronze"),
        new(8, "Aficionado", "bronze"),
        new(10, "Jugador de peña", "bronze"),
        new(15, "Experto", "silver"),
        new(20, "Crupier", "silver"),
        new(25, "Tahúr", "silver"),
        new(30, "Veterano", "gold"),
        new(35, "Profesional", "gold"),
        new(40, "Maestro", "gold"),
        new(45, "Estrella de la mesa", "gold"),
        new(50, "Gran maestro", "platinum"),
        new(57, "Élite", "platinum"),
        new(65, "Leyenda", "diamond"),
        new(72, "Virtuoso", "diamond"),
        new(80, "Mito", "diamond"),
        new(90, "Titán", "diamond"),
        new(100, "Inmortal del Continental", "mythic"),
        new(120, "Semidiós", "mythic"),
        new(150, "El Continental", "mythic")
    ];

    public static int CostOf(int level)
    {
        var step = Math.Max(1, level) - 1;

        return 300 + 40 * step + step * step;
    }

    public static int TotalFor(int level)
    {
        var total = 0;

        for (var l = 1; l < level; l++)
            total += CostOf(l);

        return total;
    }

    public static LevelProgress Progress(int xp)
    {
        var level = 1;
        var left = Math.Max(0, xp);

        while (left >= CostOf(level))
        {
            left -= CostOf(level);
            level++;
        }

        return new LevelProgress(level, left, CostOf(level));
    }

    public static int LevelFor(int xp) => Progress(xp).Level;

    public static LevelRank RankFor(int level) => Ranks.Last(r => r.From <= level);

    public static int GameXp(GameRecord record, out List<XpLine> lines)
    {
        lines = [new XpLine("Partida terminada", 40)];

        var closes = record.Tally.ClosedRounds.Count;
        var downs = record.Tally.LaidDownRounds.Count;

        if (closes > 0)
            lines.Add(new XpLine(closes == 1 ? "1 ronda cerrada" : $"{closes} rondas cerradas", closes * 15));

        if (downs > 0)
            lines.Add(new XpLine(downs == 1 ? "Te bajaste en 1 ronda" : $"Te bajaste en {downs} rondas", downs * 5));

        if (record.Won)
            lines.Add(new XpLine("Victoria", 80));
        else if (record.Players >= 3 && record.Position == 2)
            lines.Add(new XpLine("Segundo puesto", 30));

        return lines.Sum(l => l.Xp);
    }
}

public sealed record XpLine(string Label, int Xp);
