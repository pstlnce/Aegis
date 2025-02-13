using Aegis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Dapper;
using MapDataReader;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

#if true
BenchmarkRunner.Run<Bench>();
#else
BenchmarkRunner.Run<Benchy>();
#endif

/*
| Method                          | Mean       | Error     | StdDev    | Gen0      | Gen1      | Gen2     | Allocated |
|-------------------------------- |-----------:|----------:|----------:|----------:|----------:|---------:|----------:|
| CustomReflexMapper              | 256.267 ms | 3.3412 ms | 2.9619 ms | 9000.0000 | 2000.0000 |        - |  55.46 MB |
| MapDatareader_ViaDapper         |   8.214 ms | 0.1631 ms | 0.1602 ms |  750.0000 |  515.6250 | 156.2500 |   3.76 MB |
| MapDataReader_ViaMapaDataReader |   8.509 ms | 0.1432 ms | 0.1270 ms |  656.2500 |  453.1250 | 109.3750 |   3.46 MB |
| Aegis                           |   6.825 ms | 0.0810 ms | 0.0718 ms |  664.0625 |  453.1250 | 117.1875 |   3.46 MB |
*/

/*
| Method                          | Mean     | Error     | StdDev    | Gen0     | Gen1     | Gen2     | Allocated |
|-------------------------------- |---------:|----------:|----------:|---------:|---------:|---------:|----------:|
| MapDatareader_ViaDapper         | 8.682 ms | 0.1691 ms | 0.2425 ms | 750.0000 | 515.6250 | 156.2500 |   3.76 MB |
| MapDataReader_ViaMapaDataReader | 8.565 ms | 0.0469 ms | 0.0439 ms | 656.2500 | 453.1250 | 109.3750 |   3.46 MB |
| Aegis                           | 6.865 ms | 0.1267 ms | 0.1123 ms | 664.0625 | 453.1250 | 117.1875 |   3.46 MB |

| Method        | Mean     | Error   | StdDev  | Gen0      | Gen1      | Allocated |
|-------------- |---------:|--------:|--------:|----------:|----------:|----------:|
| Dapper        | 159.8 ms | 3.16 ms | 4.11 ms | 8750.0000 | 4250.0000 |  54.01 MB |
| Aegis         | 170.8 ms | 0.49 ms | 0.41 ms | 8666.6667 | 4333.3333 |  54.01 MB |
| MapDataReader | 424.5 ms | 2.94 ms | 2.75 ms | 9000.0000 | 4000.0000 |  54.02 MB |
*/

//object r = 0;
//object f = 1;
//object f22 = 22;
//object f33 = 33;
//object f44 = 44;

//ref object q = ref r;
//ref object q2 = ref f;

//ref object refer = ref q;
//refer = ref (true ? ref f33 : ref (false ? ref f22 : ref (false ? ref f22 : ref f22)));

//Console.WriteLine("refer: {0}, q: {1}, r: {2}, f: {3}, f22: {4}, f33: {5}, f44: {6}, q2: {7}",
//    refer, q, r, f, f22, f33, f44, q2);

//Console.ReadLine();

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

foreach (var item in MapperAegisAgent.ReadList(reader))
{
    Console.WriteLine(JsonSerializer.Serialize(item));
}

Console.ReadLine();

return;

[AegisAgent, Attr]
public sealed class Mapper
{
    public int Num1 { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public DateTime Time { get; set; }
}

[ShortRunJob, MemoryDiagnoser, Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class Benchy2
{
    static TestClass _o = new TestClass();
    static PropertyInfo _prop = _o.GetType().GetProperty("String1", BindingFlags.Public | BindingFlags.Instance);
    static PropertyInfo _nullableprop = _o.GetType().GetProperty("IntNullable", BindingFlags.Public | BindingFlags.Instance);

    [Benchmark]
    public void MapDatareader_ViaDapper()
    {
        var dr = _dt.CreateDataReader();
        var list = dr.Parse<TestClass2>().ToList();
    }

    [Benchmark]
    public void MapDataReader_ViaMapaDataReader()
    {
        var dr = _dt.CreateDataReader();
        var list = dr.ToTestClass2();
    }

    [Benchmark]
    public void Aegis()
    {
        var dr = _dt.CreateDataReader();
        var list = TestClass2AegisAgent.ReadList(dr).ToList();
    }

    static DataTable _dt;

    [GlobalSetup]
    public static void Setup()
    {
        //create datatable with test data
        _dt = new DataTable();
        _dt.Columns.AddRange(new[] {
                new DataColumn("String1", typeof(string)),
                new DataColumn("String2", typeof(string)),
                new DataColumn("String3", typeof(string)),
                new DataColumn("Int", typeof(int)),
                new DataColumn("Int2", typeof(int)),
                new DataColumn("IntNullable", typeof(int))
            });


        for (int i = 0; i < 1000; i++)
        {
            _dt.Rows.Add("xxx", "yyy", "zzz", 123, 321, 3211);
        }
    }
}

[GenerateDataReaderMapper, AegisAgent(Case = MatchCase.MatchOriginal)]
public class TestClass2
{
    public string String1 { get; set; }
    public string String2 { get; set; }
    public string String3 { get; set; }
    public string Int { get; set; }
    public string Int2 { get; set; }
    public int IntNullable { get; set; }
}

[MemoryDiagnoser, Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class Benchy
{
    static TestClass _o = new TestClass();
    static PropertyInfo _prop = _o.GetType().GetProperty("String1", BindingFlags.Public | BindingFlags.Instance);
    static PropertyInfo _nullableprop = _o.GetType().GetProperty("IntNullable", BindingFlags.Public | BindingFlags.Instance);

    [Params(100, 1000, 10_000, 50_000)]
    public static int count;

    [Benchmark]
    public void MapDatareader_ViaDapper()
    {
        var dr = _dt.CreateDataReader();
        var list = dr.Parse<TestClass>().ToList();
    }

    [Benchmark]
    public void MapDataReader_ViaMapaDataReader()
    {
        var dr = _dt.CreateDataReader();
        var list = dr.ToTestClass();
    }

    [Benchmark]
    public void Aegis()
    {
        var dr = _dt.CreateDataReader();
        var list = TestClassAegisAgent.ReadList(dr);
    }

    [Benchmark]
    public void Aegis_V3()
    {
        var dr = _dt.CreateDataReader();
        var list = TestClassAegisAgent.ReadList(dr);
    }

    static DataTable _dt;

    [GlobalSetup]
    public static void Setup()
    {
        //create datatable with test data
        _dt = new DataTable();
        _dt.Columns.AddRange(new[] {
                new DataColumn("String1", typeof(string)),
                new DataColumn("String2", typeof(string)),
                new DataColumn("String3", typeof(string)),
                new DataColumn("Int", typeof(int)),
                new DataColumn("Int2", typeof(int)),
                new DataColumn("IntNullable", typeof(int))

				//new DataColumn("String1_1", typeof(string)),
				//new DataColumn("String2_1", typeof(string)),
				//new DataColumn("String3_1", typeof(string)),
				//new DataColumn("Int_1", typeof(string)),
				//new DataColumn("Int2_1", typeof(string)),
				//new DataColumn("IntNullable_1", typeof(int)),

				//new DataColumn("String1_2", typeof(string)),
				//new DataColumn("String2_2", typeof(string)),
				//new DataColumn("String3_2", typeof(string)),
				//new DataColumn("Int_2", typeof(string)),
				//new DataColumn("Int2_2", typeof(string)),
				//new DataColumn("IntNullable_2", typeof(int)),

				//new DataColumn("String1_3", typeof(string)),
				//new DataColumn("String2_3", typeof(string)),
				//new DataColumn("String3_3", typeof(string)),
				//new DataColumn("Int_3", typeof(string)),
				//new DataColumn("Int2_3", typeof(string)),
				//new DataColumn("IntNullable_3", typeof(int))
            });


        for (int i = 0; i < count; i++)
        {
            _dt.Rows.Add(
				"xxx", "yyy", "zzz", 123, 321, 3211
				//"xxx", "yyy", "zzz", 123, 321, 3211,
				//"xxx", "yyy", "zzz", 123, 321, 3211,
				//"xxx", "yyy", "zzz", 123, 321, 3211
			);
        }
    }
}

[AegisAgent(Case = MatchCase.MatchOriginal)]
[GenerateDataReaderMapper]
public class TestClass
{
    public string String1 { get; set; }
    public string String2 { get; set; }
    public string String3 { get; set; }
    public string Int { get; set; }
    public string Int2 { get; set; }
    public int IntNullable { get; set; }

 //   public string String1_1 { get; set; }
 //   public string String2_1 { get; set; }
 //   public string String3_1 { get; set; }
 //   public string Int_1 { get; set; }
 //   public string Int2_1 { get; set; }
 //   public int IntNullable_1 { get; set; }

 //   public string String1_2 { get; set; }
 //   public string String2_2 { get; set; }
 //   public string String3_2 { get; set; }
 //   public string Int_2 { get; set; }
 //   public string Int2_2 { get; set; }
 //   public int IntNullable_2 { get; set; }

	//public string String1_3 { get; set; }
 //   public string String2_3 { get; set; }
 //   public string String3_3 { get; set; }
 //   public string Int_3 { get; set; }
 //   public string Int2_3 { get; set; }
 //   public int IntNullable_3 { get; set; }

}


[AttributeUsage(AttributeTargets.Class)]
public sealed class AttrAttribute : Attribute;

[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class Bench
{
    private static DataTable _source = new DataTable();

    //[Params(10, 100, 1000, 10_000, 100_000)]
    //public int _dataCount;

    // [Params(/* 1000,  */50_000)]
    // public static int count;

    static Bench()
    {
		const int count = 10_000;

        _source = new DataTable();

        _source.Columns.Add(nameof(Person.ColumnNum24_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum21_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum96), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber4_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum69_2), typeof(string));
        _source.Columns.Add(nameof(Person.Id), typeof(int));
        _source.Columns.Add(nameof(Person.ColumnNum102_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum77_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum83_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum76_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum3), typeof(string));
        _source.Columns.Add(nameof(Person.LastName), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum68_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum85_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum81), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum8_2), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber1_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum24), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum62_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum53), typeof(string));
        _source.Columns.Add(nameof(Person.Address), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum49), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum80), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum72_2), typeof(string));
        _source.Columns.Add(nameof(Person.LastName_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum46_2), typeof(string));
        _source.Columns.Add(nameof(Person.Gender_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum60), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum28_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum20_2), typeof(string));
        _source.Columns.Add(nameof(Person.IQ_2), typeof(int));
        _source.Columns.Add(nameof(Person.Movie_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum57), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum72), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum10_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum3_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum12), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum13), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum101), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum58_2), typeof(string));
        _source.Columns.Add(nameof(Person.Education_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum16_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum74), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum30), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum77), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum32_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum60_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum73), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum56_2), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber5_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum13_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum34_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum73_2), typeof(string));
        _source.Columns.Add(nameof(Person.SomePropertyWithLongLongLongLongLongLongLongLongName_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum38_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum65), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum35), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum28), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum26_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum69), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum88_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum34), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum9), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum37), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum98_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum29_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum45_2), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber2_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum97), typeof(string));
        _source.Columns.Add(nameof(Person.PostalCode_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum48), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum8), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum41_2), typeof(string));
        _source.Columns.Add(nameof(Person.FirstName_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum91_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum4_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum25), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum9_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum59_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum56), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber3_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum7_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum5), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum29), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum44_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum17), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum15_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum14_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum4), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum100_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum55), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum70), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum16), typeof(string));
        _source.Columns.Add(nameof(Person.IQ), typeof(int));
        _source.Columns.Add(nameof(Person.ColumnNum50), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum42), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum76), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum80_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum45), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum31), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum17_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum95_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum36), typeof(string));

        _source.Columns.Add(nameof(Person.ColumnNum44), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum35_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum65_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum22), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum36_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum54_2), typeof(string));
        _source.Columns.Add(nameof(Person.SecondName), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum37_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum52), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum82), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum54), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum11_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum87_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum23), typeof(string));
        _source.Columns.Add(nameof(Person.SecondName_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum40), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum25_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum99), typeof(string));
        _source.Columns.Add(nameof(Person.Movie), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum81_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum38), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum64), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum97_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum90_2), typeof(string));
        _source.Columns.Add(nameof(Person.BirthDate), typeof(DateTime));
        _source.Columns.Add(nameof(Person.OriginCountry), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum32), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum47_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum71), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum27_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum43_2), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber5), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum59), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum47), typeof(string));
        _source.Columns.Add(nameof(Person.BirthDate_2), typeof(DateTime));
        _source.Columns.Add(nameof(Person.ColumnNum2_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum55_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum43), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum95), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum6), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum49_2), typeof(string));
        _source.Columns.Add(nameof(Person.SomePropertyWithLongLongLongLongLongLongLongLongName), typeof(string));
        _source.Columns.Add(nameof(Person.Salary_2), typeof(double));
        _source.Columns.Add(nameof(Person.PostalCode), typeof(string));
        _source.Columns.Add(nameof(Person.AnotherPropertyWithSameLongLongLongLongLongLngLoName), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum92_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum33), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum46), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum31_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum79_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum19), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum40_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum51), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum11), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum89), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum93_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum64_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum66_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum10), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum102), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum6_2), typeof(string));
        _source.Columns.Add(nameof(Person.OriginCountry_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum63_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum66), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum19_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum18_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum22_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum85), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum42_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum75), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber3), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum51_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum82_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum1), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum93), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum100), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum61_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum48_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum96_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum86_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum74_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum30_2), typeof(string));
        _source.Columns.Add(nameof(Person.FirstName), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum83), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum84_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum101_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum70_2), typeof(string));
        _source.Columns.Add(nameof(Person.Education), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum99_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum52_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum68), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum33_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum78_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum98), typeof(string));
        _source.Columns.Add(nameof(Person.Salary), typeof(double));
        _source.Columns.Add(nameof(Person.ColumnNum50_2), typeof(string));
        _source.Columns.Add(nameof(Person.Address_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum86), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum92), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum94), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum7), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum39_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum79), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum12_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum23_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum61), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum41), typeof(string));
        _source.Columns.Add(nameof(Person.EQ), typeof(int));
        _source.Columns.Add(nameof(Person.ColumnNum71_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum53_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum1_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum58), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum67_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum5_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum89_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum27), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum94_2), typeof(string));
        _source.Columns.Add(nameof(Person.CardNumber4), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum88), typeof(string));
        _source.Columns.Add(nameof(Person.Id_2), typeof(int));
        _source.Columns.Add(nameof(Person.ColumnNum20), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum84), typeof(string));
        _source.Columns.Add(nameof(Person.Gender), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum75_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum14), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum67), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum87), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum18), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum57_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum63), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum62), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum21), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum26), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum15), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum91), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum78), typeof(string));
        _source.Columns.Add(nameof(Person.EQ_2), typeof(int));
        _source.Columns.Add(nameof(Person.CardNumber1), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum90), typeof(string));
        _source.Columns.Add(nameof(Person.AnotherPropertyWithSameLongLongLongLongLongLngLoName_2), typeof(string));
        _source.Columns.Add(nameof(Person.ColumnNum39), typeof(string));

        // _source.Columns.Add(nameof(Person.FirstName), typeof(string));
        // _source.Columns.Add(nameof(Person.SecondName), typeof(string));
        // _source.Columns.Add(nameof(Person.LastName), typeof(string));
        // _source.Columns.Add(nameof(Person.Salary), typeof(double));
        // _source.Columns.Add(nameof(Person.BirthDate), typeof(DateTime));
        // _source.Columns.Add(nameof(Person.Id), typeof(int));
        // _source.Columns.Add(nameof(Person.OriginCountry), typeof(string));
        // _source.Columns.Add(nameof(Person.Education), typeof(string));
        // _source.Columns.Add(nameof(Person.IQ), typeof(int));
        // _source.Columns.Add(nameof(Person.EQ), typeof(int));
        // _source.Columns.Add(nameof(Person.Gender), typeof(string));
        // _source.Columns.Add(nameof(Person.Movie), typeof(string));
        // _source.Columns.Add(nameof(Person.SomePropertyWithLongLongLongLongLongLongLongLongName), typeof(string));
        // _source.Columns.Add(nameof(Person.AnotherPropertyWithSameLongLongLongLongLongLngLoName), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber1), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber2), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber3), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber4), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber5), typeof(string));
        // _source.Columns.Add(nameof(Person.Address), typeof(string));
        // _source.Columns.Add(nameof(Person.PostalCode), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum1), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum3), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum4), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum5), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum6), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum7), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum8), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum9), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum10), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum11), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum12), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum13), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum14), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum15), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum16), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum17), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum18), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum19), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum20), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum21), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum22), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum23), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum24), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum25), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum26), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum27), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum28), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum29), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum30), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum31), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum32), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum33), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum34), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum35), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum36), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum37), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum38), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum39), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum40), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum41), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum42), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum43), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum44), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum45), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum46), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum47), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum48), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum49), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum50), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum51), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum52), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum53), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum54), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum55), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum56), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum57), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum58), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum59), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum60), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum61), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum62), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum63), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum64), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum65), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum66), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum67), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum68), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum69), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum70), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum71), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum72), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum73), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum74), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum75), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum76), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum77), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum78), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum79), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum80), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum81), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum82), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum83), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum84), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum85), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum86), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum87), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum88), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum89), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum90), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum91), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum92), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum93), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum94), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum95), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum96), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum97), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum98), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum99), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum100), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum101), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum102), typeof(string));
        //
        // _source.Columns.Add(nameof(Person.FirstName_2), typeof(string));
        // _source.Columns.Add(nameof(Person.SecondName_2), typeof(string));
        // _source.Columns.Add(nameof(Person.LastName_2), typeof(string));
        // _source.Columns.Add(nameof(Person.Salary_2), typeof(double));
        // _source.Columns.Add(nameof(Person.BirthDate_2), typeof(DateTime));
        // _source.Columns.Add(nameof(Person.Id_2), typeof(int));
        // _source.Columns.Add(nameof(Person.OriginCountry_2), typeof(string));
        // _source.Columns.Add(nameof(Person.Education_2), typeof(string));
        // _source.Columns.Add(nameof(Person.IQ_2), typeof(int));
        // _source.Columns.Add(nameof(Person.EQ_2), typeof(int));
        // _source.Columns.Add(nameof(Person.Gender_2), typeof(string));
        // _source.Columns.Add(nameof(Person.Movie_2), typeof(string));
        // _source.Columns.Add(nameof(Person.SomePropertyWithLongLongLongLongLongLongLongLongName_2), typeof(string));
        // _source.Columns.Add(nameof(Person.AnotherPropertyWithSameLongLongLongLongLongLngLoName_2), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber1_2), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber2_2), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber3_2), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber4_2), typeof(string));
        // _source.Columns.Add(nameof(Person.CardNumber5_2), typeof(string));
        // _source.Columns.Add(nameof(Person.Address_2), typeof(string));
        // _source.Columns.Add(nameof(Person.PostalCode_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum1_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum2_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum3_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum4_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum5_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum6_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum7_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum8_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum9_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum10_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum11_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum12_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum13_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum14_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum15_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum16_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum17_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum18_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum19_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum20_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum21_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum22_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum23_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum24_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum25_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum26_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum27_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum28_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum29_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum30_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum31_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum32_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum33_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum34_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum35_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum36_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum37_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum38_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum39_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum40_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum41_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum42_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum43_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum44_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum45_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum46_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum47_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum48_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum49_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum50_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum51_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum52_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum53_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum54_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum55_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum56_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum57_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum58_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum59_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum60_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum61_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum62_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum63_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum64_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum65_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum66_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum67_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum68_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum69_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum70_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum71_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum72_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum73_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum74_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum75_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum76_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum77_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum78_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum79_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum80_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum81_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum82_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum83_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum84_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum85_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum86_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum87_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum88_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum89_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum90_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum91_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum92_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum93_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum94_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum95_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum96_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum97_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum98_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum99_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum100_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum101_2), typeof(string));
        // _source.Columns.Add(nameof(Person.ColumnNum102_2), typeof(string));

        foreach (var person in GeneratePersons(count))
        {
            _source.Rows.Add(
                person.ColumnNum24_2,
                person.ColumnNum21_2,
                person.ColumnNum96,
                person.CardNumber4_2,
                person.ColumnNum69_2,
                person.Id,
                person.ColumnNum102_2,
                person.ColumnNum77_2,
                person.ColumnNum83_2,
                person.ColumnNum76_2,
                person.ColumnNum3,
                person.LastName,
                person.ColumnNum68_2,
                person.ColumnNum85_2,
                person.ColumnNum81,
                person.ColumnNum8_2,
                person.CardNumber1_2,
                person.ColumnNum24,
                person.ColumnNum62_2,
                person.ColumnNum53,
                person.Address,
                person.CardNumber2,
                person.ColumnNum49,
                person.ColumnNum80,
                person.ColumnNum72_2,
                person.LastName_2,
                person.ColumnNum46_2,
                person.Gender_2,
                person.ColumnNum60,
                person.ColumnNum28_2,
                person.ColumnNum20_2,
                person.IQ_2,
                person.Movie_2,
                person.ColumnNum57,
                person.ColumnNum72,
                person.ColumnNum10_2,
                person.ColumnNum3_2,
                person.ColumnNum12,
                person.ColumnNum13,
                person.ColumnNum101,
                person.ColumnNum58_2,
                person.Education_2,
                person.ColumnNum16_2,
                person.ColumnNum74,
                person.ColumnNum30,
                person.ColumnNum77,
                person.ColumnNum32_2,
                person.ColumnNum60_2,
                person.ColumnNum73,
                person.ColumnNum56_2,
                person.CardNumber5_2,
                person.ColumnNum13_2,
                person.ColumnNum34_2,
                person.ColumnNum73_2,
                person.SomePropertyWithLongLongLongLongLongLongLongLongName_2,
                person.ColumnNum38_2,
                person.ColumnNum65,
                person.ColumnNum35,
                person.ColumnNum28,
                person.ColumnNum26_2,
                person.ColumnNum69,
                person.ColumnNum88_2,
                person.ColumnNum34,
                person.ColumnNum9,
                person.ColumnNum37,
                person.ColumnNum98_2,
                person.ColumnNum29_2,
                person.ColumnNum45_2,
                person.CardNumber2_2,
                person.ColumnNum97,
                person.PostalCode_2,
                person.ColumnNum48,
                person.ColumnNum8,
                person.ColumnNum41_2,
                person.FirstName_2,
                person.ColumnNum91_2,
                person.ColumnNum4_2,
                person.ColumnNum25,
                person.ColumnNum9_2,
                person.ColumnNum59_2,
                person.ColumnNum56,
                person.CardNumber3_2,
                person.ColumnNum7_2,
                person.ColumnNum5,
                person.ColumnNum29,
                person.ColumnNum44_2,
                person.ColumnNum17,
                person.ColumnNum15_2,
                person.ColumnNum14_2,
                person.ColumnNum4,
                person.ColumnNum100_2,
                person.ColumnNum55,
                person.ColumnNum70,
                person.ColumnNum16,
                person.IQ,
                person.ColumnNum50,
                person.ColumnNum42,
                person.ColumnNum76,
                person.ColumnNum80_2,
                person.ColumnNum45,
                person.ColumnNum31,
                person.ColumnNum17_2,
                person.ColumnNum95_2,
                person.ColumnNum36,

                person.ColumnNum44,
                person.ColumnNum35_2,
                person.ColumnNum65_2,
                person.ColumnNum22,
                person.ColumnNum36_2,
                person.ColumnNum54_2,
                person.SecondName,
                person.ColumnNum37_2,
                person.ColumnNum52,
                person.ColumnNum82,
                person.ColumnNum54,
                person.ColumnNum11_2,
                person.ColumnNum87_2,
                person.ColumnNum23,
                person.SecondName_2,
                person.ColumnNum40,
                person.ColumnNum25_2,
                person.ColumnNum2,
                person.ColumnNum99,
                person.Movie,
                person.ColumnNum81_2,
                person.ColumnNum38,
                person.ColumnNum64,
                person.ColumnNum97_2,
                person.ColumnNum90_2,
                person.BirthDate,
                person.OriginCountry,
                person.ColumnNum32,
                person.ColumnNum47_2,
                person.ColumnNum71,
                person.ColumnNum27_2,
                person.ColumnNum43_2,
                person.CardNumber5,
                person.ColumnNum59,
                person.ColumnNum47,
                person.BirthDate_2,
                person.ColumnNum2_2,
                person.ColumnNum55_2,
                person.ColumnNum43,
                person.ColumnNum95,
                person.ColumnNum6,
                person.ColumnNum49_2,
                person.SomePropertyWithLongLongLongLongLongLongLongLongName,
                person.Salary_2,
                person.PostalCode,
                person.AnotherPropertyWithSameLongLongLongLongLongLngLoName,
                person.ColumnNum92_2,
                person.ColumnNum33,
                person.ColumnNum46,
                person.ColumnNum31_2,
                person.ColumnNum79_2,
                person.ColumnNum19,
                person.ColumnNum40_2,
                person.ColumnNum51,
                person.ColumnNum11,
                person.ColumnNum89,
                person.ColumnNum93_2,
                person.ColumnNum64_2,
                person.ColumnNum66_2,
                person.ColumnNum10,
                person.ColumnNum102,
                person.ColumnNum6_2,
                person.OriginCountry_2,
                person.ColumnNum63_2,
                person.ColumnNum66,
                person.ColumnNum19_2,
                person.ColumnNum18_2,
                person.ColumnNum22_2,
                person.ColumnNum85,
                person.ColumnNum42_2,
                person.ColumnNum75,
                person.CardNumber3,
                person.ColumnNum51_2,
                person.ColumnNum82_2,
                person.ColumnNum1,
                person.ColumnNum93,
                person.ColumnNum100,
                person.ColumnNum61_2,
                person.ColumnNum48_2,
                person.ColumnNum96_2,
                person.ColumnNum86_2,
                person.ColumnNum74_2,
                person.ColumnNum30_2,
                person.FirstName,
                person.ColumnNum83,
                person.ColumnNum84_2,
                person.ColumnNum101_2,
                person.ColumnNum70_2,
                person.Education,
                person.ColumnNum99_2,
                person.ColumnNum52_2,
                person.ColumnNum68,
                person.ColumnNum33_2,
                person.ColumnNum78_2,
                person.ColumnNum98,
                person.Salary,
                person.ColumnNum50_2,
                person.Address_2,
                person.ColumnNum86,
                person.ColumnNum92,
                person.ColumnNum94,
                person.ColumnNum7,
                person.ColumnNum39_2,
                person.ColumnNum79,
                person.ColumnNum12_2,
                person.ColumnNum23_2,
                person.ColumnNum61,
                person.ColumnNum41,
                person.EQ,
                person.ColumnNum71_2,
                person.ColumnNum53_2,
                person.ColumnNum1_2,
                person.ColumnNum58,
                person.ColumnNum67_2,
                person.ColumnNum5_2,
                person.ColumnNum89_2,
                person.ColumnNum27,
                person.ColumnNum94_2,
                person.CardNumber4,
                person.ColumnNum88,
                person.Id_2,
                person.ColumnNum20,
                person.ColumnNum84,
                person.Gender,
                person.ColumnNum75_2,
                person.ColumnNum14,
                person.ColumnNum67,
                person.ColumnNum87,
                person.ColumnNum18,
                person.ColumnNum57_2,
                person.ColumnNum63,
                person.ColumnNum62,
                person.ColumnNum21,
                person.ColumnNum26,
                person.ColumnNum15,
                person.ColumnNum91,
                person.ColumnNum78,
                person.EQ_2,
                person.CardNumber1,
                person.ColumnNum90,
                person.AnotherPropertyWithSameLongLongLongLongLongLngLoName_2,
                person.ColumnNum39


                // person.FirstName,
                // person.SecondName,
                // person.LastName,
                // person.Salary,
                // person.BirthDate,
                // person.Id,
                // person.OriginCountry,
                // person.Education,
                // person.IQ,
                // person.EQ,
                // person.Gender,
                // person.Movie,
                // person.SomePropertyWithLongLongLongLongLongLongLongLongName,
                // person.AnotherPropertyWithSameLongLongLongLongLongLngLoName,
                // person.CardNumber1,
                // person.CardNumber2,
                // person.CardNumber3,
                // person.CardNumber4,
                // person.CardNumber5,
                // person.Address,
                // person.PostalCode,
                // person.ColumnNum1,
                // person.ColumnNum2,
                // person.ColumnNum3,
                // person.ColumnNum4,
                // person.ColumnNum5,
                // person.ColumnNum6,
                // person.ColumnNum7,
                // person.ColumnNum8,
                // person.ColumnNum9,
                // person.ColumnNum10,
                // person.ColumnNum11,
                // person.ColumnNum12,
                // person.ColumnNum13,
                // person.ColumnNum14,
                // person.ColumnNum15,
                // person.ColumnNum16,
                // person.ColumnNum17,
                // person.ColumnNum18,
                // person.ColumnNum19,
                // person.ColumnNum20,
                // person.ColumnNum21,
                // person.ColumnNum22,
                // person.ColumnNum23,
                // person.ColumnNum24,
                // person.ColumnNum25,
                // person.ColumnNum26,
                // person.ColumnNum27,
                // person.ColumnNum28,
                // person.ColumnNum29,
                // person.ColumnNum30,
                // person.ColumnNum31,
                // person.ColumnNum32,
                // person.ColumnNum33,
                // person.ColumnNum34,
                // person.ColumnNum35,
                // person.ColumnNum36,
                // person.ColumnNum37,
                // person.ColumnNum38,
                // person.ColumnNum39,
                // person.ColumnNum40,
                // person.ColumnNum41,
                // person.ColumnNum42,
                // person.ColumnNum43,
                // person.ColumnNum44,
                // person.ColumnNum45,
                // person.ColumnNum46,
                // person.ColumnNum47,
                // person.ColumnNum48,
                // person.ColumnNum49,
                // person.ColumnNum50,
                // person.ColumnNum51,
                // person.ColumnNum52,
                // person.ColumnNum53,
                // person.ColumnNum54,
                // person.ColumnNum55,
                // person.ColumnNum56,
                // person.ColumnNum57,
                // person.ColumnNum58,
                // person.ColumnNum59,
                // person.ColumnNum60,
                // person.ColumnNum61,
                // person.ColumnNum62,
                // person.ColumnNum63,
                // person.ColumnNum64,
                // person.ColumnNum65,
                // person.ColumnNum66,
                // person.ColumnNum67,
                // person.ColumnNum68,
                // person.ColumnNum69,
                // person.ColumnNum70,
                // person.ColumnNum71,
                // person.ColumnNum72,
                // person.ColumnNum73,
                // person.ColumnNum74,
                // person.ColumnNum75,
                // person.ColumnNum76,
                // person.ColumnNum77,
                // person.ColumnNum78,
                // person.ColumnNum79,
                // person.ColumnNum80,
                // person.ColumnNum81,
                // person.ColumnNum82,
                // person.ColumnNum83,
                // person.ColumnNum84,
                // person.ColumnNum85,
                // person.ColumnNum86,
                // person.ColumnNum87,
                // person.ColumnNum88,
                // person.ColumnNum89,
                // person.ColumnNum90,
                // person.ColumnNum91,
                // person.ColumnNum92,
                // person.ColumnNum93,
                // person.ColumnNum94,
                // person.ColumnNum95,
                // person.ColumnNum96,
                // person.ColumnNum97,
                // person.ColumnNum98,
                // person.ColumnNum99,
                // person.ColumnNum100,
                // person.ColumnNum101,
                // person.ColumnNum102,
                //
                // person.FirstName_2,
                // person.SecondName_2,
                // person.LastName_2,
                // person.Salary_2,
                // person.BirthDate_2,
                // person.Id_2,
                // person.OriginCountry_2,
                // person.Education_2,
                // person.IQ_2,
                // person.EQ_2,
                // person.Gender_2,
                // person.Movie_2,
                // person.SomePropertyWithLongLongLongLongLongLongLongLongName_2,
                // person.AnotherPropertyWithSameLongLongLongLongLongLngLoName_2,
                // person.CardNumber1_2,
                // person.CardNumber2_2,
                // person.CardNumber3_2,
                // person.CardNumber4_2,
                // person.CardNumber5_2,
                // person.Address_2,
                // person.PostalCode_2,
                // person.ColumnNum1_2,
                // person.ColumnNum2_2,
                // person.ColumnNum3_2,
                // person.ColumnNum4_2,
                // person.ColumnNum5_2,
                // person.ColumnNum6_2,
                // person.ColumnNum7_2,
                // person.ColumnNum8_2,
                // person.ColumnNum9_2,
                // person.ColumnNum10_2,
                // person.ColumnNum11_2,
                // person.ColumnNum12_2,
                // person.ColumnNum13_2,
                // person.ColumnNum14_2,
                // person.ColumnNum15_2,
                // person.ColumnNum16_2,
                // person.ColumnNum17_2,
                // person.ColumnNum18_2,
                // person.ColumnNum19_2,
                // person.ColumnNum20_2,
                // person.ColumnNum21_2,
                // person.ColumnNum22_2,
                // person.ColumnNum23_2,
                // person.ColumnNum24_2,
                // person.ColumnNum25_2,
                // person.ColumnNum26_2,
                // person.ColumnNum27_2,
                // person.ColumnNum28_2,
                // person.ColumnNum29_2,
                // person.ColumnNum30_2,
                // person.ColumnNum31_2,
                // person.ColumnNum32_2,
                // person.ColumnNum33_2,
                // person.ColumnNum34_2,
                // person.ColumnNum35_2,
                // person.ColumnNum36_2,
                // person.ColumnNum37_2,
                // person.ColumnNum38_2,
                // person.ColumnNum39_2,
                // person.ColumnNum40_2,
                // person.ColumnNum41_2,
                // person.ColumnNum42_2,
                // person.ColumnNum43_2,
                // person.ColumnNum44_2,
                // person.ColumnNum45_2,
                // person.ColumnNum46_2,
                // person.ColumnNum47_2,
                // person.ColumnNum48_2,
                // person.ColumnNum49_2,
                // person.ColumnNum50_2,
                // person.ColumnNum51_2,
                // person.ColumnNum52_2,
                // person.ColumnNum53_2,
                // person.ColumnNum54_2,
                // person.ColumnNum55_2,
                // person.ColumnNum56_2,
                // person.ColumnNum57_2,
                // person.ColumnNum58_2,
                // person.ColumnNum59_2,
                // person.ColumnNum60_2,
                // person.ColumnNum61_2,
                // person.ColumnNum62_2,
                // person.ColumnNum63_2,
                // person.ColumnNum64_2,
                // person.ColumnNum65_2,
                // person.ColumnNum66_2,
                // person.ColumnNum67_2,
                // person.ColumnNum68_2,
                // person.ColumnNum69_2,
                // person.ColumnNum70_2,
                // person.ColumnNum71_2,
                // person.ColumnNum72_2,
                // person.ColumnNum73_2,
                // person.ColumnNum74_2,
                // person.ColumnNum75_2,
                // person.ColumnNum76_2,
                // person.ColumnNum77_2,
                // person.ColumnNum78_2,
                // person.ColumnNum79_2,
                // person.ColumnNum80_2,
                // person.ColumnNum81_2,
                // person.ColumnNum82_2,
                // person.ColumnNum83_2,
                // person.ColumnNum84_2,
                // person.ColumnNum85_2,
                // person.ColumnNum86_2,
                // person.ColumnNum87_2,
                // person.ColumnNum88_2,
                // person.ColumnNum89_2,
                // person.ColumnNum90_2,
                // person.ColumnNum91_2,
                // person.ColumnNum92_2,
                // person.ColumnNum93_2,
                // person.ColumnNum94_2,
                // person.ColumnNum95_2,
                // person.ColumnNum96_2,
                // person.ColumnNum97_2,
                // person.ColumnNum98_2,
                // person.ColumnNum99_2,
                // person.ColumnNum100_2,
                // person.ColumnNum101_2,
                // person.ColumnNum102_2

            );
        }
    }

	public static IEnumerable<Person> GeneratePersons(int count, int seed = 42)
	{
        var faker = new Bogus.Faker<Person>().UseSeed(seed)
            .RuleFor(x => x.FirstName, f => f.Name.FirstName())
            .RuleFor(x => x.SecondName, f => f.Name.FullName())
            .RuleFor(x => x.LastName, f => f.Name.LastName())
            .RuleFor(x => x.Salary, f => (double)f.Finance.Amount())
            .RuleFor(x => x.BirthDate, f => f.Person.DateOfBirth)
            .RuleFor(x => x.Id, f => f.IndexFaker)
            .RuleFor(x => x.OriginCountry, f => f.Address.Country())
			.RuleFor(x => x.Education, f => f.Music.Genre()) 
			.RuleFor(x => x.IQ, f => f.Random.Int(0, 70)) 
			.RuleFor(x => x.EQ, f => f.Random.Int(-999, -1)) 
			.RuleFor(x => x.Gender, f => f.Person.Gender.ToString()) 
			.RuleFor(x => x.Movie, f => f.Person.FullName) 
			.RuleFor(x => x.SomePropertyWithLongLongLongLongLongLongLongLongName, f => f.Random.String()) 
			.RuleFor(x => x.AnotherPropertyWithSameLongLongLongLongLongLngLoName, f => f.Random.String()) 
			.RuleFor(x => x.CardNumber1, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv()) 
			.RuleFor(x => x.CardNumber2, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv()) 
			.RuleFor(x => x.CardNumber3, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv()) 
			.RuleFor(x => x.CardNumber4, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv())
			.RuleFor(x => x.CardNumber5, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv())
			.RuleFor(x => x.Address, f => f.Address.FullAddress()) 
			.RuleFor(x => x.PostalCode, f => f.Address.ZipCode())
			.RuleFor(x => x.ColumnNum1, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum3, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum4, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum5, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum6, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum7, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum8, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum9, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum10, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum11, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum12, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum13, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum14, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum15, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum16, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum17, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum18, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum19, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum20, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum21, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum22, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum23, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum24, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum25, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum26, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum27, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum28, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum29, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum30, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum31, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum32, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum33, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum34, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum35, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum36, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum37, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum38, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum39, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum40, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum41, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum42, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum43, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum44, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum45, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum46, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum47, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum48, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum49, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum50, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum51, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum52, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum53, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum54, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum55, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum56, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum57, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum58, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum59, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum60, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum61, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum62, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum63, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum64, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum65, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum66, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum67, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum68, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum69, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum70, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum71, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum72, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum73, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum74, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum75, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum76, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum77, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum78, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum79, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum80, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum81, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum82, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum83, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum84, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum85, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum86, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum87, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum88, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum89, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum90, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum91, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum92, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum93, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum94, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum95, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum96, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum97, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum98, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum99, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum100, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum101, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum102, f => f.Random.Word())

            .RuleFor(x => x.FirstName_2, f => f.Name.FirstName())
            .RuleFor(x => x.SecondName_2, f => f.Name.FullName())
            .RuleFor(x => x.LastName_2, f => f.Name.LastName())
            .RuleFor(x => x.Salary_2, f => (double)f.Finance.Amount())
            .RuleFor(x => x.BirthDate_2, f => f.Person.DateOfBirth)
            .RuleFor(x => x.Id_2, f => f.IndexFaker)
            .RuleFor(x => x.OriginCountry_2, f => f.Address.Country())
			.RuleFor(x => x.Education_2, f => f.Music.Genre()) 
			.RuleFor(x => x.IQ_2, f => f.Random.Int(0, 70)) 
			.RuleFor(x => x.EQ_2, f => f.Random.Int(-999, -1)) 
			.RuleFor(x => x.Gender_2, f => f.Person.Gender.ToString()) 
			.RuleFor(x => x.Movie_2, f => f.Person.FullName) 
			.RuleFor(x => x.SomePropertyWithLongLongLongLongLongLongLongLongName_2, f => f.Random.String()) 
			.RuleFor(x => x.AnotherPropertyWithSameLongLongLongLongLongLngLoName_2, f => f.Random.String()) 
			.RuleFor(x => x.CardNumber1_2, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv()) 
			.RuleFor(x => x.CardNumber2_2, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv()) 
			.RuleFor(x => x.CardNumber3_2, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv()) 
			.RuleFor(x => x.CardNumber4_2, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv())
			.RuleFor(x => x.CardNumber5_2, f => f.Commerce.Ean13() + " " + f.Finance.CreditCardCvv())
			.RuleFor(x => x.Address_2, f => f.Address.FullAddress()) 
			.RuleFor(x => x.PostalCode_2, f => f.Address.ZipCode())
			.RuleFor(x => x.ColumnNum1_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum2_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum3_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum4_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum5_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum6_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum7_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum8_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum9_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum10_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum11_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum12_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum13_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum14_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum15_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum16_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum17_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum18_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum19_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum20_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum21_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum22_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum23_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum24_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum25_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum26_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum27_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum28_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum29_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum30_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum31_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum32_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum33_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum34_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum35_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum36_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum37_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum38_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum39_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum40_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum41_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum42_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum43_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum44_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum45_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum46_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum47_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum48_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum49_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum50_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum51_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum52_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum53_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum54_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum55_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum56_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum57_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum58_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum59_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum60_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum61_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum62_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum63_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum64_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum65_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum66_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum67_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum68_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum69_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum70_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum71_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum72_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum73_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum74_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum75_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum76_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum77_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum78_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum79_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum80_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum81_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum82_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum83_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum84_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum85_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum86_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum87_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum88_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum89_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum90_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum91_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum92_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum93_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum94_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum95_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum96_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum97_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum98_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum99_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum100_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum101_2, f => f.Random.Word())
			.RuleFor(x => x.ColumnNum102_2, f => f.Random.Word())

            ;

        for (int i = 0; i < count; i++)
        {
            var person = faker.Generate();
            yield return person;
        }
    }

#if false
    [Benchmark]
    public int Dapper()
    {
        var dr = _source.CreateDataReader();

        int i = 0;

        foreach (var item in dr.Parse<Person>())
        {
            i++;
        }

        return i;
    }

    [Benchmark]
    public int Aegis()
    {
        var dr = _source.CreateDataReader();

        int i = 0;

        foreach (var item in PersonAegisAgent.ReadSingleItemStream(dr))
        {
            i++;
        }

        return i;
    }

#else
    [Benchmark]
    public object Dapper()
    {
        var dr = _source.CreateDataReader();
        var list = dr.Parse<Person>().ToList();

        return list;
    }

    [Benchmark]
    public object Aegis_V3()
    {
        var dr = _source.CreateDataReader();

        //var list = PersonAegisAgent.ReadSingleItemStream(dr).ToList();
        //var list = PersonAegisAgent.ReadList(dr);
        //var list = PersonAegisAgent.Read(dr).ToList();
        //var list = PersonAegisAgent.ReadList2(dr);
        var list = PersonAegisAgent.ReadList(dr);

        return list;
    }

    [Benchmark]
    public object MapDataReader()
    {
        var dr = _source.CreateDataReader();
        var list = dr.ToPerson();

        return list;
    }
#endif

}

[AegisAgent(Case = MatchCase.MatchOriginal)]
[GenerateDataReaderMapper]
public sealed class Person
{
    public string FirstName { get; set; }
    public string SecondName { get; set; }
    public string LastName { get; set; }
    public double Salary { get; set; }
    public DateTime BirthDate { get; set; }
    public int Id { get; set; }
    public string OriginCountry { get; set; }
    public string Education { get; set; }
    public int IQ { get; set; }
    public int EQ { get; set; }
    public string Gender { get; set; }
    public string Movie { get; set; }
    public string SomePropertyWithLongLongLongLongLongLongLongLongName { get; set; }
    public string AnotherPropertyWithSameLongLongLongLongLongLngLoName { get; set; }
    public string CardNumber1 { get; set; }
    public string CardNumber2 { get; set; }
    public string CardNumber3 { get; set; }
    public string CardNumber4 { get; set; }
    public string CardNumber5 { get; set; }
    public string Address { get; set; }
    public string PostalCode { get; set; }
    public string ColumnNum1 { get; set; }
    public string ColumnNum2 { get; set; }
    public string ColumnNum3 { get; set; }
    public string ColumnNum4 { get; set; }
    public string ColumnNum5 { get; set; }
    public string ColumnNum6 { get; set; }
    public string ColumnNum7 { get; set; }
    public string ColumnNum8 { get; set; }
    public string ColumnNum9 { get; set; }
    public string ColumnNum10 { get; set; }
    public string ColumnNum11 { get; set; }
    public string ColumnNum12 { get; set; }
    public string ColumnNum13 { get; set; }
    public string ColumnNum14 { get; set; }
    public string ColumnNum15 { get; set; }
    public string ColumnNum16 { get; set; }
    public string ColumnNum17 { get; set; }
    public string ColumnNum18 { get; set; }
    public string ColumnNum19 { get; set; }
    public string ColumnNum20 { get; set; }
    public string ColumnNum21 { get; set; }
    public string ColumnNum22 { get; set; }
    public string ColumnNum23 { get; set; }
    public string ColumnNum24 { get; set; }
    public string ColumnNum25 { get; set; }
    public string ColumnNum26 { get; set; }
    public string ColumnNum27 { get; set; }
    public string ColumnNum28 { get; set; }
    public string ColumnNum29 { get; set; }
    public string ColumnNum30 { get; set; }
    public string ColumnNum31 { get; set; }
    public string ColumnNum32 { get; set; }
    public string ColumnNum33 { get; set; }
    public string ColumnNum34 { get; set; }
    public string ColumnNum35 { get; set; }
    public string ColumnNum36 { get; set; }
    public string ColumnNum37 { get; set; }
    public string ColumnNum38 { get; set; }
    public string ColumnNum39 { get; set; }
    public string ColumnNum40 { get; set; }
    public string ColumnNum41 { get; set; }
    public string ColumnNum42 { get; set; }
    public string ColumnNum43 { get; set; }
    public string ColumnNum44 { get; set; }
    public string ColumnNum45 { get; set; }
    public string ColumnNum46 { get; set; }
    public string ColumnNum47 { get; set; }
    public string ColumnNum48 { get; set; }
    public string ColumnNum49 { get; set; }
    public string ColumnNum50 { get; set; }
    public string ColumnNum51 { get; set; }
    public string ColumnNum52 { get; set; }
    public string ColumnNum53 { get; set; }
    public string ColumnNum54 { get; set; }
    public string ColumnNum55 { get; set; }
    public string ColumnNum56 { get; set; }
    public string ColumnNum57 { get; set; }
    public string ColumnNum58 { get; set; }
    public string ColumnNum59 { get; set; }
    public string ColumnNum60 { get; set; }
    public string ColumnNum61 { get; set; }
    public string ColumnNum62 { get; set; }
    public string ColumnNum63 { get; set; }
    public string ColumnNum64 { get; set; }
    public string ColumnNum65 { get; set; }
    public string ColumnNum66 { get; set; }
    public string ColumnNum67 { get; set; }
    public string ColumnNum68 { get; set; }
    public string ColumnNum69 { get; set; }
    public string ColumnNum70 { get; set; }
    public string ColumnNum71 { get; set; }
    public string ColumnNum72 { get; set; }
    public string ColumnNum73 { get; set; }
    public string ColumnNum74 { get; set; }
    public string ColumnNum75 { get; set; }
    public string ColumnNum76 { get; set; }
    public string ColumnNum77 { get; set; }
    public string ColumnNum78 { get; set; }
    public string ColumnNum79 { get; set; }
    public string ColumnNum80 { get; set; }
    public string ColumnNum81 { get; set; }
    public string ColumnNum82 { get; set; }
    public string ColumnNum83 { get; set; }
    public string ColumnNum84 { get; set; }
    public string ColumnNum85 { get; set; }
    public string ColumnNum86 { get; set; }
    public string ColumnNum87 { get; set; }
    public string ColumnNum88 { get; set; }
    public string ColumnNum89 { get; set; }
    public string ColumnNum90 { get; set; }
    public string ColumnNum91 { get; set; }
    public string ColumnNum92 { get; set; }
    public string ColumnNum93 { get; set; }
    public string ColumnNum94 { get; set; }
    public string ColumnNum95 { get; set; }
    public string ColumnNum96 { get; set; }
    public string ColumnNum97 { get; set; }
    public string ColumnNum98 { get; set; }
    public string ColumnNum99 { get; set; }
    public string ColumnNum100 { get; set; }
    public string ColumnNum101 { get; set; }
    public string ColumnNum102 { get; set; }

    public string FirstName_2 { get; set; }
    public string SecondName_2 { get; set; }
    public string LastName_2 { get; set; }
    public double Salary_2 { get; set; }
    public DateTime BirthDate_2 { get; set; }
    public int Id_2 { get; set; }
    public string OriginCountry_2 { get; set; }
    public string Education_2 { get; set; }
    public int IQ_2 { get; set; }
    public int EQ_2 { get; set; }
    public string Gender_2 { get; set; }
    public string Movie_2 { get; set; }
    public string SomePropertyWithLongLongLongLongLongLongLongLongName_2 { get; set; }
    public string AnotherPropertyWithSameLongLongLongLongLongLngLoName_2 { get; set; }
    public string CardNumber1_2 { get; set; }
    public string CardNumber2_2 { get; set; }
    public string CardNumber3_2 { get; set; }
    public string CardNumber4_2 { get; set; }
    public string CardNumber5_2 { get; set; }
    public string Address_2 { get; set; }
    public string PostalCode_2 { get; set; }
    public string ColumnNum1_2 { get; set; }
    public string ColumnNum2_2 { get; set; }
    public string ColumnNum3_2 { get; set; }
    public string ColumnNum4_2 { get; set; }
    public string ColumnNum5_2 { get; set; }
    public string ColumnNum6_2 { get; set; }
    public string ColumnNum7_2 { get; set; }
    public string ColumnNum8_2 { get; set; }
    public string ColumnNum9_2 { get; set; }
    public string ColumnNum10_2 { get; set; }
    public string ColumnNum11_2 { get; set; }
    public string ColumnNum12_2 { get; set; }
    public string ColumnNum13_2 { get; set; }
    public string ColumnNum14_2 { get; set; }
    public string ColumnNum15_2 { get; set; }
    public string ColumnNum16_2 { get; set; }
    public string ColumnNum17_2 { get; set; }
    public string ColumnNum18_2 { get; set; }
    public string ColumnNum19_2 { get; set; }
    public string ColumnNum20_2 { get; set; }
    public string ColumnNum21_2 { get; set; }
    public string ColumnNum22_2 { get; set; }
    public string ColumnNum23_2 { get; set; }
    public string ColumnNum24_2 { get; set; }
    public string ColumnNum25_2 { get; set; }
    public string ColumnNum26_2 { get; set; }
    public string ColumnNum27_2 { get; set; }
    public string ColumnNum28_2 { get; set; }
    public string ColumnNum29_2 { get; set; }
    public string ColumnNum30_2 { get; set; }
    public string ColumnNum31_2 { get; set; }
    public string ColumnNum32_2 { get; set; }
    public string ColumnNum33_2 { get; set; }
    public string ColumnNum34_2 { get; set; }
    public string ColumnNum35_2 { get; set; }
    public string ColumnNum36_2 { get; set; }
    public string ColumnNum37_2 { get; set; }
    public string ColumnNum38_2 { get; set; }
    public string ColumnNum39_2 { get; set; }
    public string ColumnNum40_2 { get; set; }
    public string ColumnNum41_2 { get; set; }
    public string ColumnNum42_2 { get; set; }
    public string ColumnNum43_2 { get; set; }
    public string ColumnNum44_2 { get; set; }
    public string ColumnNum45_2 { get; set; }
    public string ColumnNum46_2 { get; set; }
    public string ColumnNum47_2 { get; set; }
    public string ColumnNum48_2 { get; set; }
    public string ColumnNum49_2 { get; set; }
    public string ColumnNum50_2 { get; set; }
    public string ColumnNum51_2 { get; set; }
    public string ColumnNum52_2 { get; set; }
    public string ColumnNum53_2 { get; set; }
    public string ColumnNum54_2 { get; set; }
    public string ColumnNum55_2 { get; set; }
    public string ColumnNum56_2 { get; set; }
    public string ColumnNum57_2 { get; set; }
    public string ColumnNum58_2 { get; set; }
    public string ColumnNum59_2 { get; set; }
    public string ColumnNum60_2 { get; set; }
    public string ColumnNum61_2 { get; set; }
    public string ColumnNum62_2 { get; set; }
    public string ColumnNum63_2 { get; set; }
    public string ColumnNum64_2 { get; set; }
    public string ColumnNum65_2 { get; set; }
    public string ColumnNum66_2 { get; set; }
    public string ColumnNum67_2 { get; set; }
    public string ColumnNum68_2 { get; set; }
    public string ColumnNum69_2 { get; set; }
    public string ColumnNum70_2 { get; set; }
    public string ColumnNum71_2 { get; set; }
    public string ColumnNum72_2 { get; set; }
    public string ColumnNum73_2 { get; set; }
    public string ColumnNum74_2 { get; set; }
    public string ColumnNum75_2 { get; set; }
    public string ColumnNum76_2 { get; set; }
    public string ColumnNum77_2 { get; set; }
    public string ColumnNum78_2 { get; set; }
    public string ColumnNum79_2 { get; set; }
    public string ColumnNum80_2 { get; set; }
    public string ColumnNum81_2 { get; set; }
    public string ColumnNum82_2 { get; set; }
    public string ColumnNum83_2 { get; set; }
    public string ColumnNum84_2 { get; set; }
    public string ColumnNum85_2 { get; set; }
    public string ColumnNum86_2 { get; set; }
    public string ColumnNum87_2 { get; set; }
    public string ColumnNum88_2 { get; set; }
    public string ColumnNum89_2 { get; set; }
    public string ColumnNum90_2 { get; set; }
    public string ColumnNum91_2 { get; set; }
    public string ColumnNum92_2 { get; set; }
    public string ColumnNum93_2 { get; set; }
    public string ColumnNum94_2 { get; set; }
    public string ColumnNum95_2 { get; set; }
    public string ColumnNum96_2 { get; set; }
    public string ColumnNum97_2 { get; set; }
    public string ColumnNum98_2 { get; set; }
    public string ColumnNum99_2 { get; set; }
    public string ColumnNum100_2 { get; set; }
    public string ColumnNum101_2 { get; set; }
    public string ColumnNum102_2 { get; set; }
}

public struct PonterToObject(int ptr)
{
    public int Ptr = ptr;
}
