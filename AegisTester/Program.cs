using Aegis;
using System.Data;
using System.Data.Common;
using System.Text.Json;

#if false
BenchmarkRunner.Run<BigModelBenchmark>();
#elif false
BenchmarkRunner.Run<Benchy>();
#endif

return;
    
var source = new DataTable();
source.Columns.Add(nameof(Mapper.Name), typeof(string));
source.Columns.Add(nameof(Mapper.Num1), typeof(int));
source.Columns.Add(nameof(Mapper.Description), typeof(string));
source.Columns.Add(nameof(Mapper.Time), typeof(DateTime));

source.Rows.Add("Map1", 1, "Description for map 1", DateTime.Now);
source.Rows.Add("Map2", 2, "Description for map 2", DateTime.Now);
source.Rows.Add("Map3", 3, "Description for map 3", DateTime.Now);
source.Rows.Add("Map4", 4, "Description for map 4", DateTime.Now);
source.Rows.Add("Map5", 5, "Description for map 5", DateTime.Now);
source.Rows.Add("Map6", 6, "Description for map 6", DateTime.Now);
source.Rows.Add("Map7", 7, "Description for map 7", DateTime.Now);

DbDataReader reader = source.CreateDataReader();

foreach (var item in MapperParser.ReadList(reader))
{
    Console.WriteLine(JsonSerializer.Serialize(item));
}

Console.ReadLine();

return;

[
    AegisAgent,
    Some(Map = ["", 1, 3])
]
public sealed class Mapper
{
    public static string[] _p;

    public int Num1 { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public DateTime Time { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
internal sealed class SomeAttribute : Attribute
{
    public required object[] Map { get; init; }

    public SomeAttribute() { }

    public SomeAttribute(object[] map)
    {
        Map = map;
    }
}


