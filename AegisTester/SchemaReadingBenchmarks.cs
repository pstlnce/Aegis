using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace AegisTester;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
internal class SchemaReadingBenchmarks
{

}
