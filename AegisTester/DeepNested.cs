using Aegis;

namespace AegisTester;

[AegisAgent(Case = MatchCase.IgnoreCase)]
internal class DeepNested
{
    public required NestLvl1 Required_1 { get; set; }
    public required NestLvl1 Required_2 { get; set; }
    public required NestLvl1 Required_3 { get; set; }
    public required DeadEnd Empty { get; set; }

    public NestLvl1? Optional_1 { get; set; }
    public NestLvl1? Optional_2 { get; set; }
    public NestLvl1? Optional_3 { get; set; }
    public DeadEnd? OptionalEmpty { get; set; }
}

internal class DeadEnd;

internal class NestLvl1
{
    public required int Age { get; set; }
    public required double Best { get; set; }
    public required string Name { get; set; }
    public string? Optinal { get; set; }

    public required NestLvl2 RequiredNesting { get; set; }
    public required NestLvl2 Nesting { get; set; }
}

internal class NestLvl2
{
    public required string Country { get; set; }
    public required float Celsius { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }

    public NestLvl3? OptionalNesting { get; set; }
}

internal class NestLvl3
{
    public required NestLvl4 First { get; set; }
    public required NestLvl4 Second { get; set; }
    public required NestLvl4 Third { get; set; }

    public NestLvl4? OptionalFirst { get; set; }
    public NestLvl4? OptionalSecond { get; set; }
    public NestLvl4? OptionalThird { get; set; }

}

internal class NestLvl4
{
    public string? Garbage_1 { get; set; }
    public string? Garbage_2 { get; set; }
    public string? Garbage_3 { get; set; }
    public string? Garbage_4 { get; set; }
    public string? Garbage_5 { get; set; }
    public string? Garbage_6 { get; set; }
}
