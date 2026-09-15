namespace Continental.Core.Rules;

public sealed record RoundContract(int Trios, int Escaleras)
{

    public static readonly IReadOnlyList<RoundContract> Standard =
    [
        new(2, 0),
        new(1, 1),
        new(0, 2),
        new(3, 0),
        new(2, 1),
        new(1, 2),
        new(0, 3)
    ];

    public int MeldCount => Trios + Escaleras;

    public string Describe()
    {
        var parts = new List<string>();

        if (Trios > 0)
            parts.Add(Trios == 1 ? "1 trío" : $"{Trios} tríos");

        if (Escaleras > 0)
            parts.Add(Escaleras == 1 ? "1 escalera" : $"{Escaleras} escaleras");

        return string.Join(" + ", parts);
    }

    public string Code => new string('T', Trios) + new string('E', Escaleras);
}
