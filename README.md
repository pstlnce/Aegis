# Aegis

Code gen for fast parsing answers from databases.

## Goals

Create an API that allows optimizing parsing for a specific model, customizing behaviour for specific cases (missed required property, for example), allowing interception of processing in most stages (for logs, metrics, for example)

## Usage

```csharp
// Case - allows you to specify in what transformed form
// of the original property name to match columns from a IDataReader.
// It is planned to add the ability to specify specific names for matching...
[AegisAgent(Case = MatchCase.MatchOriginal)]
internal sealed class SomeClass
{
  public string? Property1 { get; set; }
  public int Property2 { get; set; }
}

// There is no asynchronous version so far...
IDataReader reader = dataTable.CreateDataReader();
var results = SomeClassAegisAgent.ReadList(reader);
```

## What prompted me to create a separate project when there is [MapDataReader](https://github.com/jitbit/MapDataReader)?

From the description of the MapDataReader, it seems that it is aimed at light-weight and simplicity, while my project is planned to be filled with as great functionality as possible, and probably this is the main reason.

Another reason is that MapDataReader provides only synchronous reading and has problems with performance that is not mentioned in the description, but vice versa in the description there is information that compared to [Dapper](https://github.com/DapperLib/Dapper), the performance is higher (which is not always the case)

## Perfomance

This topic is existential for the project, therefore, at the initial stages of development, time is devoted more to different methods of optimization and benchmarks (this is from a part of an excuse for the poor code structure).

The first idea of ​​reading data from [IDataReader](https://learn.microsoft.com/en-us/dotnet/api/system.data.idatareader?view=net-9.0) was to analyze the scheme of the columns by finding from different variations of the same name the property of the one that is found in the scheme, to write down this in a variable that would later be used for reading. But this option immediately showed itself worse at the [benchmark](https://github.com/jitbit/MapDataReader/blob/main/MapDataReader.Benchmarks/Program.cs) from the MapDataReader repository.

| Method                          | Mean     | Error    | StdDev  | Gen0    | Gen1    | Allocated |
|-------------------------------- |---------:|---------:|--------:|--------:|--------:|----------:|
| MapDataReader_ViaMapaDataReader | 151.4 us | 49.32 us | 2.70 us | 36.6211 |  7.3242 | 150.11 KB |
| MapDatareader_ViaDapper         | 177.6 us | 47.52 us | 2.60 us | 44.1895 | 10.0098 | 181.13 KB |
| Aegis                           | 229.1 us | 60.01 us | 3.29 us | 45.4102 |  0.4883 | 186.55 KB |

The next idea was the storage of the column indices after successful comparison, which actually also opens the opportunity to compare with nameless columns (as a data type for example)
And the results were not long in coming!

| Method                          | Mean     | Error    | StdDev  | Gen0    | Gen1    | Allocated |
|-------------------------------- |---------:|---------:|--------:|--------:|--------:|----------:|
| Aegis                           | 135.8 us |  2.14 us | 0.12 us | 38.5742 |  0.4883 | 157.68 KB |
| MapDataReader_ViaMapaDataReader | 146.5 us | 11.51 us | 0.63 us | 36.6211 |  7.3242 | 150.11 KB |
| MapDatareader_ViaDapper         | 170.7 us | 16.34 us | 0.90 us | 44.1895 | 10.0098 | 181.13 KB |

An example with the first implementation demonstrates that it is more effective to pars through Dapper than manually both in terms of performance and in terms of effort spent.
And in reality, these benchmars do not demonstrate the weak performance of Dapper and, on the contrary, Dapper holds the bar well compared to static parsers. In addition, there is a pattern of performance degradation, which is suffered by both implementations of Compile-Time Parsers.
The more properties the target model has, the more the perfomance difference with Dapper is erased. In previous tests, the model had only 6 properties, now a model with 24 properties was used.

| Method                          | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2     | Allocated |
|-------------------------------- |---------:|---------:|---------:|----------:|----------:|---------:|----------:|
| Aegis                           | 40.43 ms | 0.770 ms | 0.856 ms | 3153.8462 | 1769.2308 | 461.5385 |  17.02 MB |
| MapDataReader_ViaMapaDataReader | 46.30 ms | 0.922 ms | 0.947 ms | 3090.9091 | 1727.2727 | 454.5455 |  17.02 MB |
| MapDatareader_ViaDapper         | 50.16 ms | 0.739 ms | 0.617 ms | 3400.0000 | 1900.0000 | 500.0000 |  18.55 MB |

But in a script with a large model (for example, more than 120 properties, like in this benchmark), Dapper exceeds both static parsers ...

| Method        | count | Mean         | Error       | StdDev      | Gen0      | Gen1      | Gen2     | Allocated   |
|-------------- |------ |-------------:|------------:|------------:|----------:|----------:|---------:|------------:|
| Dapper        | 100   |     135.6 us |     1.17 us |     1.10 us |   27.0996 |    0.7324 |        - |   111.52 KB |
| Aegis         | 100   |     143.0 us |     0.48 us |     0.43 us |   27.0996 |    0.2441 |        - |   111.45 KB |
| MapDataReader | 100   |     609.7 us |     1.03 us |     0.91 us |   28.3203 |    5.8594 |        - |   118.18 KB |
| Dapper        | 1000  |   1,594.5 us |     6.55 us |     6.12 us |  197.2656 |  156.2500 |        - |  1102.75 KB |
| Aegis         | 1000  |   1,760.4 us |    19.01 us |    17.78 us |  199.2188 |  150.3906 |        - |  1102.68 KB |
| MapDataReader | 1000  |   6,420.1 us |    16.34 us |    14.49 us |  195.3125 |  140.6250 |        - |  1109.42 KB |
| Dapper        | 10000 |  36,547.5 us |   579.73 us |   542.28 us | 1928.5714 | 1071.4286 | 142.8571 | 11113.67 KB |
| Aegis         | 10000 |  39,119.6 us |   365.40 us |   341.80 us | 1923.0769 | 1076.9231 | 153.8462 | 11113.52 KB |
| MapDataReader | 10000 |  76,819.2 us |   497.02 us |   464.91 us | 1714.2857 |  857.1429 |        - | 11120.04 KB |
| Dapper        | 50000 | 159,453.5 us | 1,727.51 us | 1,615.92 us | 8666.6667 | 4333.3333 |        - | 55306.83 KB |
| Aegis         | 50000 | 167,858.0 us |   699.59 us |   620.17 us | 8666.6667 | 4333.3333 |        - | 55306.65 KB |
| MapDataReader | 50000 | 418,186.3 us | 3,357.29 us | 3,140.41 us | 9000.0000 | 4000.0000 |        - | 55314.51 KB |

I also repeated this benchmark with its first implementation and it works out almost 2 times faster than Mapdatareader. The check of the current column on each iteration is affected. Such a strategy is faster than a regular dictionary, but to a certain number of lines. Most likely, the use of vectorization for comparison of lines will help to correct this problem (I think to use this, if possible, in comparing the speakers in the future implementation).

| Method        | count | Mean       | Error     | StdDev    | Gen0      | Gen1      | Allocated |
|-------------- |------ |-----------:|----------:|----------:|----------:|----------:|----------:|
| Dapper        | 1000  |   1.690 ms | 0.0221 ms | 0.0196 ms |  197.2656 |  166.0156 |   1.08 MB |
| Aegis         | 1000  |   3.335 ms | 0.0076 ms | 0.0063 ms |  203.1250 |  171.8750 |   1.18 MB |
| MapDataReader | 1000  |   6.537 ms | 0.0256 ms | 0.0227 ms |  195.3125 |  140.6250 |   1.08 MB |
| Dapper        | 50000 | 159.800 ms | 0.9779 ms | 0.9147 ms | 8666.6667 | 4333.3333 |  54.01 MB |
| Aegis         | 50000 | 231.845 ms | 1.4657 ms | 1.2993 ms | 8666.6667 | 4333.3333 |  54.11 MB |
| MapDataReader | 50000 | 429.325 ms | 4.7726 ms | 4.4643 ms | 8000.0000 | 4000.0000 |  54.02 MB |

In the alternative MapDataReader, it would be possible to use the numbers in the buffer of comparison of the columns for the mapping columns and properties (the position in the buffer would indicate a column, and the very value on a kind of "id" properties), although in this case it would be possible to use vectorization for comparison "ID" with a property. I realized something similar in my last attempt to optimize the static parser.

Analyzing the advantages of Dapper, I decided to try to realize sequential reading from IDataReader. The idea was to record the final number of variables, the meaning in which was storing indices in an increase (a variable for one index will always make a lesser value than the next variable for the index) and thus would create a procedure for a faster downloading from Heap . Also, a link to a stack with a variable is applied to these indices, which is semantically connected to the specific property of the model, the very value for the variable, which simply refers to the target, is set when comparing the columns.Due to the restrictions on the controlled code, I can’t just announce the link to the stack as I want, what spilled into a pile of the generated code. In general, if the generator has the opportunity to create an unsafe code, then the generated code could turn out to be less, and here there will be more space for optimizations, but since the codogenerator is not a separate project, it is not the best solution to declare the unsafe block, because This can break the assembly to many users, while Dapper is deprived of such a shortage.
Benchmarks demonstrate the results slightly lagging from the previous implementation, but it can be seen that on large models this implementation shows a good result against the backdrop of MapDataReader.

| Method        | count | Mean       | Error     | StdDev    | Gen0      | Gen1      | Allocated |
|-------------- |------ |-----------:|----------:|----------:|----------:|----------:|----------:|
| Dapper        | 1000  |   1.673 ms | 0.0109 ms | 0.0096 ms |  197.2656 |  156.2500 |   1.08 MB |
| Aegis_V3      | 1000  |   3.252 ms | 0.0163 ms | 0.0127 ms |  203.1250 |  156.2500 |   1.08 MB |
| MapDataReader | 1000  |   6.481 ms | 0.0802 ms | 0.0711 ms |  195.3125 |  148.4375 |   1.08 MB |
| Dapper        | 50000 | 161.592 ms | 1.0071 ms | 0.9420 ms | 8750.0000 | 4250.0000 |  54.01 MB |
| Aegis_V3      | 50000 | 174.872 ms | 2.2153 ms | 1.9638 ms | 8666.6667 | 4333.3333 |  54.01 MB |
| MapDataReader | 50000 | 430.625 ms | 4.0478 ms | 3.7864 ms | 9000.0000 | 4000.0000 |  54.02 MB |

## Summary

So far, the fastest implementation of Aegis is more efficient with small and medium size models in comparison with the rest, which should be enough for most scenarios. I will continue to look for ways to optimize the parser for models with a large number of properties, maybe this is possible without IL magic from Dapper
