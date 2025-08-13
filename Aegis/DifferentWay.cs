using Aegis.IndentWriter;
using Aegis.Options;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Aegis;

internal static class DifferentWay
{
    internal static void GenerateDataReaderParsers(
        SourceProductionContext productionContext,
        ImmutableArray<MatchingModel?> models)
    {
        var token = productionContext.CancellationToken;

        var sourceCode = new StringBuilder();

        foreach (var model in models)
        {
            if (token.IsCancellationRequested) return;

            if (model == null) continue;

            var matchCase = model.Value.MatchingSettings.MatchCase;

            if (matchCase <= MatchCase.None)
            {
                continue;
            }

            var type = model.Value.Type;
            var _ = new IndentStackWriter(sourceCode);

            var typeNamespace = !type.Namespace.IsGlobal ? type.Namespace.DisplayString : null;

            var m = ParseMf(model.Value);
            var collected = SettableCrawlerEnumerator2.Collect(m);

            var sb = new StringBuilder();
            var wr = new IndentStackWriter(sb);
            SettableCrawlerEnumerator2.IterateThrough(collected, wr);

            var cc = sb.ToString();

            continue;

            //MethodsGenerator.ConstructIndexesReading(m, _);
            var variables = MethodsGenerator.CollectVariables(m);
            //var indexReadingMethod = MethodsGenerator.ConstructIndexReading(m, variables, MatchCase.IgnoreCase | MatchCase.MatchOriginal);

            //_.WriteScoped(indexReadingMethod);


            if (token.IsCancellationRequested) return;

            var sourceCodeText = sourceCode.ToString();
            sourceCode.Clear();
        }
    }

    private static ModelToParse ParseMf(MatchingModel model)
    {
        return new()
        {
            Type = new TypeToParse()
            {
                DisplayName = model.Type.DisplayString,
            },
            Settables = model.Settables.Select(ParseMf).ToList(),
            ComplexSettables = model.Settables
                .Where(x => !x.IsPrimitive)
                .ToDictionary(
                    ParseMf,
                    v => ParseMf(model.Inner![v.Type.DisplayString])
                ),
        };
    }

    private static SettableToParse ParseMf(Settable settable)
    {
        return new SettableToParse()
        {
            FieldSource = settable.FieldSource,
            IsComplex = !settable.IsPrimitive,
            IsRequired = settable.Required,
            Name = settable.Name,
            TypeDisplayName = settable.Type.DisplayString,
        };
    }
}



internal interface ISmthWriter
{
    void Write(IndentStackWriter writer);
}

internal interface IClassLevel
{
}

internal interface IMethodDeclarator
{
}

internal interface IMethodArgument
{

}

internal interface IMethodBody;

internal sealed class Pipeline
{

}

internal sealed class FileLevel(ISmthWriter classLevel, IEnumerable<string> additionalNamespaces)
    : ISmthWriter
{
    private static IEnumerable<string> _defaultNamespaces = """
        using System;
        using System.Data;
        using System.Data.Common;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        """.Split('\n');

    private readonly IncludeNamespaces _namespaces = new(_defaultNamespaces.Concat(additionalNamespaces));
    private readonly ISmthWriter _classLevel = classLevel;

    public void Write(IndentStackWriter writer)
    {
        _namespaces.Write(writer);
        _classLevel.Write(writer);
    }
}

internal sealed class ClassLevel(IEnumerable<ISmthWriter> content)
{
    private readonly string _modifiers;
    private readonly string _name;

    public void Write(IndentStackWriter writer)
    {
        _ = writer[$$"""
            {{_modifiers}} class {{_name}}
            {
                {{writer.WriteScoped(content)}}
            }
            """];
    }
}

internal sealed class NamespaceLevel(string @namespace, IEnumerable<ISmthWriter> content) : ISmthWriter
{
    private readonly string _namespace = @namespace;

    public void Write(IndentStackWriter writer)
    {
        _ = writer[$$"""
        {{_namespace}}
        {
            {{writer.WriteScoped(content)}}
        }
        """];
    }
}

internal sealed class MethodsGenerator
{
    private readonly ISmthWriter _type;
    private readonly ModelToParse _modelToParse;
    private readonly MatchCase _matchCase;

    public IEnumerable<ISmthWriter> Generate()
    {
        var unbuffredSignature = ConstructSignature("internal static IEnumerable<", _type, "> ReadUnbuffered<TReader>(TReader reader)\r\n    where TReader : IDataReader");
        var unbuffredAsyncSignature = ConstructSignature("internal static async IAsyncEnumerable<", _type, "> ReadUnbufferedAsync<TReader>(TReader reader)\r\n    where TReader : DbDataReader");
        var bufferedSignature = ConstructSignature("internal static List<", _type, "> ReadList<TReader>(TReader reader)\r\n    where TReader : IDataReader");
        var bufferedAsyncSignature = ConstructSignature("internal static async Task<List<", _type, ">> ReadListAsync<TReader>(TReader reader)\r\n    where TReader : DbDataReader");
        var bufferedAsynValuecSignature = ConstructSignature("internal static async ValueTask<List<", _type, ">> ReadListAsync<TReader>(TReader reader)\r\n    where TReader : DbDataReader");

        var unbufferedEarlyEscape = ConstructEarlyEscape("!reader.Read()", "yield break;");
        var unbufferedAsyncEarlyEscape = ConstructEarlyEscape("!(await reader.ReadAsync(token).ConfigureAwait(false))", "yield break;");
        var bufferedEarlyEscape = ConstructEarlyEscape("!reader.Read()", "return result;");
        var bufferedAsyncEarlyEscape = ConstructEarlyEscape("!(await reader.ReadAsync(token).ConfigureAwait(false))", "return result;");

        var variables = CollectVariables(_modelToParse);
        var indexesReadingCall = ConstructCallIndexesReading(variables);
        var indexesReading = ConstructIndexesReading(variables);
        var indexReading = ConstructIndexReading(variables, _matchCase);

        var unbuffered = new BracesWriter(
            before: new ConcatWriter("internal static IEnumerable<", _type, "> ReadUnbuffered<TReader>(TReader reader)\r\n    where TReader : IDataReader"),
            joinBefore: "\r\n",
            separator: "\r\n\r\n",
            contents: [
                ConstructEarlyEscape("!reader.Read()", "yield break;"),
                indexesReadingCall,
                new LambdaWriter<object>(null, (_, writer) => writer[$$"""
                    do
                    {
                        var parsed = new {{writer.Write(_type)}}()
                        {
                            
                        };
                    
                        yield return parsed;
                    } while (!reader.Read());
                    """]
                ),
                new JustStringWriter($$"""
                    do
                    {
                        var parsed = new {{_type}}()
                        {
                            
                        };

                        yield return parsed;
                    } while (!reader.Read());
                    """)
            ]
        );



        IEnumerable<ISmthWriter> results = [

        ];

        yield break;
    }

    private static ISmthWriter ConstructMethod(JustStringWriter signature, ISmthWriter earlyEscape, ISmthWriter indexesReading, ISmthWriter parsing, ISmthWriter loopingAndReturn)
    {
        return new ReadMethod(signature, earlyEscape, indexesReading, loopingAndReturn);
    }

    private static ISmthWriter ConstructSignature(string beforeType, ISmthWriter returningType, string afterType)
    {
        return new ConcatWriter([(JustStringWriter)beforeType, returningType, (JustStringWriter)afterType]);
    }

    private static JustStringWriter ConstructEarlyEscape(string condition, string returning)
    {
        return $$"""
            if({{condition}})
            {
                {{returning}}
            }
            """;
    }

    private static void ConcstructParsing(ModelToParse root, IndentStackWriter writer)
    {
        /*
         var parsed = new <Type>()
         {
             required1 = reader[colrequired1] is <t> p ? p : default,
             required2 = reader[colrequired2] is <t> p ? p : default,
             required3 = new <T>(), // no required primitives
             required4 = new <T>() // required and contains required complex types
             {
                 r4_r1 = new <T>(),
                 r4_r2 = new <T>()
                 {
                     r4_r2_r1 = reader[col_r4_r2_r1] is <t> p ? p : default,
                 }
             },
         }

         if(col_complex_r1 != -1 && col_complex_r2 != -1)
         {
             parsed.complex = new <T>()
             {
                 r1 = reader[col_complex_r1] is <t> p ? p : default,
                 r2 = reader[col_complex_r2] is <t> p ? p : default,
             }

             if(col_complex_nr1 != -1)
             {
                 parsed.complex.nr1 = reader[col_complex_nr1] is <t> p ? p : default;
             }
         }

         if(c_cmp_nr1 != -1 || c_cmp_nr2 != -1 || c_cmp_nr3 != -1)
         {
             parsed.cmp = new T();
             
             if(c_cmp_nr1 != -1)
             {
                 parsed.cmp.nr1 = reader[c_cmp_nr1] is <t> p ? p : default;
             }

             if(c_cmp_nr2 != -1)
             {
                 parsed.cmp.nr2 = reader[c_cmp_nr2] is <t> p ? p : default;
             }

             if(c_cmp_nr3 != -1)
             {
                 parsed.cmp.nr3 = reader[c_cmp_nr3] is <t> p ? p : default;
             }
         }
         */

        var requiredCrawler = new RequiredOnlySettablesCrawler(root);
        var requiredVariables = new List<CrawlerCollected>();

        while(requiredCrawler.Next())
        {
            requiredVariables.Add(requiredCrawler.GetVariableAndSource());
        }

        var anyRequired = requiredVariables.Count > 0;

        if(!anyRequired)
        {

        }
    }

    private static ISmthWriter ConcstructParsing(List<CrawlerCollected> variables, ISmthWriter type)
    {
        IndentStackWriter writer = null!;

        var rootRequiredSettables = variables.TakeWhile(x => x.IsRoot).Where(x => x.Property.IsRequired).ToList();
        var other = variables.Where(x => !x.Property.IsRequired).ToList();

        if (rootRequiredSettables.Count != 0)
        {
            var parsingInline = rootRequiredSettables.Select(x => $"{x.Property.Name} = reader[{x.VariableName}] is {x.Property.TypeDisplayName} val{x.VariableName} ? val{x.VariableName} : default");

            other.Where(x => x.Depth == 1);

            var f = !other.Any() ? null : new LambdaWriter<object>(null, (_, writer) => writer[$$"""
                
                """]
            );

            new LambdaWriter<IEnumerable<string>>(parsingInline, (parsingInline, writer) => _ = writer[$$"""
                var parsed = new {{writer.Write(type)}}()
                {
                    {{writer.Scope[parsingInline, joinBy: ",\r\n"]}}
                };
                """]
            );
        }

        return null;
    }

    internal static ISmthWriter ConstructCallIndexesReading(List<CrawlerCollected> variables)
    {
        return new LambdaWriter<List<CrawlerCollected>>(
            variables,
            (variables, writer) => _ = writer[$"ReadSchemaIndexes<TReader>(TReader reader{writer[variables.Select(x => $", out int {x.VariableName}")]});"]
        );
    }

    internal static ISmthWriter ConstructIndexesReading(List<CrawlerCollected> variables)
    {
        return new LambdaWriter<List<CrawlerCollected>>(
            variables,
            (variables, writer) => _ = writer[$$"""
            public static void ReadSchemaIndexes<TReader>(TReader reader{{writer[variables.Select(x => $", out int {x.VariableName}")]}})
                where TReader : IDbDataReader
            {
                {{writer.Scope[variables.Select(x => x.Source.TryGetOrder(out int index) ? $"{x.VariableName} = {index};" : $"{x.VariableName} = -1;"), joinBy: "\n"]}}

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    ReadSchemaIndex(reader.GetName(i), i{{variables.Select(x => $", ref {x.VariableName}")}});
                }
            }
            """]
        );
    }

    internal static List<CrawlerCollected> CollectVariables(ModelToParse model)
    {
        var crawler = new SettableCrawlerEnumerator(model);
        var variables = new List<CrawlerCollected>();

        while (crawler.Next())
        {
            var variable = crawler.GetVariableAndSource();
            variables.Add(variable);
        }

        return variables;
    }

    internal static ISmthWriter ConstructIndexReading(List<CrawlerCollected> variables, MatchCase matching)
    {
        var groups = new SortedDictionary<int, List<(string variableName, string source)>>();

        var set = matching.Has(MatchCase.IgnoreCase) ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : [];

        foreach (var collected in variables)
        {
            var(variable, source) = (collected.VariableName, collected.Source);

            if (!source.TryGetFields(out var sourceFields))
            {
                continue;
            }

            foreach (var sourceCase in sourceFields.SelectMany(x => matching.ToAllCasesForCompare(x)))
                set.Add(sourceCase);

            foreach (var sourceCase in set)
            {
                (groups.TryGetValue(sourceCase.Length, out var group)
                    ? group
                    : groups[sourceCase.Length] = group = new(set.Count)
                ).Add((variable, sourceCase));
            }

            set.Clear();
        }

        IEnumerable<string> signature = [
            "public static void ReadSchemaIndex<TReader>(string c, int i",
            ..variables.Select(x => x.VariableName)
        ];


        IEnumerable<string> switchAnchor = ["switch (c.Length)\r\n"];

        var casseWriters = groups.Select(x => new LambdaWriter<KeyValuePair<int, List<(string variableName, string source)>>>(x, AppendCases));

        var switchCase = new BracesWriter(
            before: new JustStringWriter("switch (c.Length)"),
            joinBefore: new JustStringWriter("\r\n"),
            separator: new JustStringWriter("\r\n\r\n"),
            contents: [..casseWriters, new JustStringWriter("default: \r\n\tbreak;")]
        );

        var method = new BracesWriter(
            before: new ConcatWriter([new ConcatSeparatedWriter(", ref int ", signature), new JustStringWriter(")")]),
            joinBefore: "\r\n",
            contents: [switchCase]
        );

        return method;

        IndentedInterpolatedStringHandler AppendCases(KeyValuePair<int, List<(string variableName, string source)>> caseGroup, IndentStackWriter writer)
        {
            var comparingModifier = matching.Has(MatchCase.IgnoreCase)
                ? $", {nameof(StringComparison)}.{nameof(StringComparison.OrdinalIgnoreCase)}"
                : string.Empty;

            var (length, group) = (caseGroup.Key, caseGroup.Value);

            return writer[
                $$"""
                case {{length}}:
                    {{writer.Scope.ForEach(group, (w, x) => w[$$"""
                    if({{x.variableName}} == -1 && "{{x.source}}".Equals(c{{comparingModifier}}))
                    {
                        {{x.variableName}} = i;
                        return;
                    }
                    """])}}
                    break;
                """];
        }
    }
}

internal sealed class LambdaWriter<T>(T source, Func<T, IndentStackWriter, IndentedInterpolatedStringHandler> func)
    : ISmthWriter
{
    public LambdaWriter(Func<T, IndentStackWriter, IndentedInterpolatedStringHandler> func, T source)
        : this(source, func)
    { }


    public void Write(IndentStackWriter writer)
    {
        _ = func(source, writer);
    }
}

internal struct DefferedCrawlingTarget
{
    public IEnumerator<(SettableToParse link, ModelToParse next)> Complex;
    public int ParentIndex;
}

internal readonly struct CrawlerSection2(
    int index,
    ModelToParse source,
    SettableToParse link,
    IEnumerator<(SettableToParse link, ModelToParse next)> complex,
    bool isRequired,
    int childIndex,
    int parentIndex
)
{
    public SettableToParse Link { get; } = link;
    public ModelToParse Source { get; } = source;
    public IEnumerator<(SettableToParse link, ModelToParse next)> Complex { get; } = complex;
    public int Index => index;
    public int ChildIndex => childIndex;
    public bool IsRequired => isRequired;
    public int ParentIndex => parentIndex;
}

internal struct CrawlerSlice(ModelToParse source, int requiredSimpleIndex, int requiredSimpleCount, int notRequiredSimpleIndex, int notRequiredSimpleCount)
{
    public ModelToParse Source { set; get; } = source;

    public SettableToParse ParentLink { get; set; } = default!;

    public string TypeDisplayName { get; set; } = source.Type.DisplayName;

    public int ParentIndex { get; set; } = -1;
    public bool ParentIsRequired { get; set; }
    public bool IsRequired { get; set; }

    public int RequiredSimpleIndex { get; set; } = requiredSimpleIndex;
    public int RequiredSimpleCount { get; set; } = requiredSimpleCount;

    public readonly int AllRequiredSimpleCount => RequiredSimpleCount + ReqChildsReqSimplesCount;

    public int NotRequiredSimpleIndex { get; set; } = notRequiredSimpleIndex;
    public int NotRequiredSimpleCount { get; set; } = notRequiredSimpleCount;

    public readonly int AllNotRequiredSimpleCount => NotRequiredSimpleCount + NotReqChildsSimplesCount;

    public int ReqChildsReqSimplesCount { get; set; }
    public int NotReqChildsSimplesCount { get; set; }

    public int FirstChildIndex { get; set; } = -1;

    public int LastReqRecursiveChildIndex { get; set; } = -1;
    public int LastRecursiveChildIndex { get; set; } = -1;

    public int ReqComplexCount { get; set; }

    public int FirstRequiredChildIndex { get; set; } = -1;
    public int RequiredChildCount { get; set; }
    public int RequiredRecursiveChildCount { get; set; }

    public readonly int AllRequiredChildCount => RequiredChildCount + RequiredRecursiveChildCount;

    public int FirstOptionalChildeIndex { get; set; }
    public int OptionalChildCount { get; set; }
    public int OptionalRecursiveChildCount { get; set; }

    public readonly int AllOptionalChildCount => OptionalChildCount + OptionalRecursiveChildCount;
}

internal sealed class SettablesCollected
    (Memory<CrawlerSlice> optionalSlices,
    Memory<CrawlerSlice> requiredSlices,
    Memory<SettableToParse> requiredPrimitives,
    Memory<SettableToParse> notRequiredPrimitives)
{
    public Memory<CrawlerSlice> Slices { get; set; } = optionalSlices;
    public Memory<CrawlerSlice> RequiredSlices { get; set; } = requiredSlices;
    public Memory<SettableToParse> RequiredPrimitives { get; set; } = requiredPrimitives;
    public Memory<SettableToParse> OptionalPrimitives { get; set; } = notRequiredPrimitives;
}

internal static class SettableCrawlerEnumerator2
{
    public static IEnumerable<(SettableToParse link, ModelToParse next)> EnumerateRequiredFirst(Dictionary<SettableToParse, ModelToParse> complexSettables)
    {
        var core = complexSettables.Select(static x => (link: x.Key, next: x.Value));
        return core.Where(static x => x.link.IsRequired)
            .Concat(core.Where(static x => !x.link.IsRequired));
    }

    public static SettablesCollected Collect(this ModelToParse root)
    {
        var path = new Stack<CrawlerSection2>();

        var deffered = new Stack<DefferedCrawlingTarget>();

        var slices = new List<CrawlerSlice>();
        var optionalSlices = new List<CrawlerSlice>();

        var allRequiredSimple = new List<SettableToParse>();
        var allNotRequiredSimple = new List<SettableToParse>();

        var current = root;
        var complex = EnumerateRequiredFirst(current.ComplexSettables).GetEnumerator();

        var previousIndex = 0;
        var currentRequired = false;

        var parentIndex = -1;
        var defferedCount = 0;

        while(true)
        {
            var reqSimpleCount = 0;
            var simpleCount = 0;

            foreach (var settable in current.Settables)
            {
                if (!settable.IsComplex)
                {
                    (settable.IsRequired
                        ? allRequiredSimple
                        : allNotRequiredSimple
                    ).Add(settable);

                    (settable.IsRequired ? ref reqSimpleCount : ref simpleCount)++;
                }
            }

            var reqSimpleIndex = allRequiredSimple.Count - reqSimpleCount;
            var simpleIndex = allNotRequiredSimple.Count - simpleCount;

            var index = slices.Count;

            slices.Add(new(
                source: current,
                requiredSimpleIndex: reqSimpleIndex,
                requiredSimpleCount: reqSimpleCount,
                notRequiredSimpleIndex: simpleIndex,
                notRequiredSimpleCount: simpleCount
            )
            {
                FirstRequiredChildIndex = index,
                ParentIndex = parentIndex,
            });

            while (true)
            {
                if (complex.MoveNext())
                {
                    var (link, next) = complex.Current;

                    var destination = currentRequired ? slices.AsSpan() : optionalSlices.AsSpan();

                    destination[index].RequiredChildCount += link.IsRequired ? 1 : 0;
                    destination[index].OptionalChildCount += link.IsRequired ? 0 : 1;

                    path.Push(new(
                        index: index,
                        source: current,
                        link: link,
                        complex: complex,
                        childIndex: previousIndex,
                        isRequired: currentRequired,
                        parentIndex: parentIndex
                    ));

                    current = next;
                    currentRequired = link.IsRequired;
                    complex = EnumerateRequiredFirst(next.ComplexSettables).GetEnumerator();
                    parentIndex = index;

                    previousIndex = -1;
                }
                else if(defferedCount != 0)
                {
                    // TODO: maybe add onto path
                    // The problem is that parent slices needs to be updated as child elements are added
                    // so the algorothm should take path back to this point through all parents,
                    // while we are referencing maybe deep nested node directly from relatively top-level node
                    Debug.Assert(deffered.Count != 0, "There's should be deffered slices");
                    Debug.Assert(defferedCount <= deffered.Count, "There's can't be more deffered elements wait to unrol than in the buffer");

                    var defferedSlice = deffered.Pop();
                    defferedCount -= 1;

                    index = defferedSlice.ParentIndex;
                    complex = defferedSlice.Complex;


                }
                else if(path.Count != 0)
                {
                    var poped = path.Pop();

                    var span = slices.AsSpan();

                    span[index].ParentLink = poped.Link;
                    span[index].IsRequired = poped.Link.IsRequired; // @Speed
                    span[index].ParentIsRequired = poped.IsRequired;

                    // TODO: remove later
                    span[poped.Index].ReqChildsReqSimplesCount += (span[index].RequiredSimpleCount + span[index].ReqChildsReqSimplesCount) * (poped.Link.IsRequired ? 1 : 0);
                    span[poped.Index].NotReqChildsSimplesCount += (span[index].NotRequiredSimpleCount + span[index].NotReqChildsSimplesCount) * (!poped.Link.IsRequired ? 1 : 0);

                    // @Keep
                    span[poped.Index].RequiredRecursiveChildCount += (span[index].RequiredChildCount + span[index].RequiredRecursiveChildCount) * (poped.Link.IsRequired ? 1 : 0);

                    span[poped.Index].OptionalRecursiveChildCount += (span[index].OptionalChildCount + span[index].OptionalRecursiveChildCount) * (poped.Link.IsRequired ? 0 : 1);

                    if(span[index].IsRequired)
                    {
                        span[poped.Index].LastReqRecursiveChildIndex = span[index].LastReqRecursiveChildIndex >= 0
                            ? span[index].LastReqRecursiveChildIndex
                            : index;
                    }

                    span[poped.Index].LastRecursiveChildIndex = span[index].LastRecursiveChildIndex >= 0 ? span[index].LastRecursiveChildIndex : index;

                    previousIndex = index;

                    if (span[poped.Index].FirstRequiredChildIndex < 0)
                    {
                        span[poped.Index].FirstRequiredChildIndex = index;
                    }

                    // TODO: remove later
                    if(span[poped.Index].FirstChildIndex < 0)
                    {
                        span[poped.Index].FirstChildIndex = index;
                    }

                    index = poped.Index;
                    current = poped.Source;
                    complex = poped.Complex;
                    currentRequired = poped.IsRequired;
                    parentIndex = poped.ParentIndex;

                    continue;
                }
                else
                {
                    goto Exit;
                }

                break;
            }
        }

    Exit:
        Debug.Assert(optionalSlices.Count > 0);
        //Debug.Assert(slices[0].RequiredSimpleCount + slices[0].ReqChildsReqSimplesCount == allRequiredSimple.Count);
        //Debug.Assert(slices[0].NotRequiredSimpleCount + slices[0].NotReqChildsSimplesCount == allNotRequiredSimple.Count);
        //Debug.Assert(slices.Count == 1 || slices[0].FirstChildIndex == 1);

#if DEBUG
#endif

#if DEBUG
        for (int i = 1; slices.Count > 2 && i < slices.Count; i++)
        {
            // invalidated due to removal of SiblingIndex
            /*
            var itemLinkin = slices.FindIndex(x => x.FirstChildIndex == i || x.SiblingIndex == i);
            Debug.Assert(itemLinkin >= 0, "There should be a link to an item in some way", "There's no link to item at index: {0}", i);
            Debug.Assert(slices[i].ParentIndex >= 0, "Every child nodes should have a link to a parent node");
            */

            // TODO: Fix checks
            if (false && slices[i].AllRequiredSimpleCount != slices[i].RequiredSimpleCount)
            {
                var rootSlice = slices[i];

                var requiredEndSliceIndex = slices.FindIndex(x => !x.IsRequired && x.ParentIndex == i);
                var requiredEndSlice = slices[requiredEndSliceIndex];

                Debug.Assert(
                    rootSlice.RequiredSimpleIndex + rootSlice.AllRequiredSimpleCount == requiredEndSlice.RequiredSimpleIndex + requiredEndSlice.RequiredSimpleCount,
                    "The root element should point to all it's required child elements required primitives",
                    "Root: start - {0} count - {1}, last required child: start - {2} count - {3}",
                    rootSlice.RequiredSimpleIndex,
                    rootSlice.AllRequiredSimpleCount,
                    requiredEndSlice.RequiredSimpleIndex,
                    requiredEndSlice.RequiredSimpleCount
                );
            }
        }
#endif
        return new SettablesCollected(
            optionalSlices: optionalSlices.AsMemory(),
            requiredSlices: slices.AsMemory(),
            requiredPrimitives: allRequiredSimple.AsMemory(),
            notRequiredPrimitives: allNotRequiredSimple.AsMemory()
        );
    }

    internal struct PrintingStep
    {
        public int RootIndex;
        public int Depth;

        public int PreviousIndex;
        public int PreviousParentIndex;
        public bool PreviousBraced;
        public bool PreviousHasRequired;
        public bool PreviousRequired;

        public bool RootHaveRequiredMembers;
        public bool RootHaveAnyMembers;
        public bool Inited;

        public int BracesOpened;
    }

    internal static void EndNestings(
        int nestings,
        IndentStackWriter w
    )
    {
        for(; nestings > 0; nestings--)
        {
            w.PopIndent();
            w.Append("\n}");
        }
    }

    internal static void EndStep(
        ref PrintingStep step,
        IndentStackWriter w,
        ref int nestingDepth
    )
    {
        if(!step.Inited)
        {
            return;
        }

        for (; step.BracesOpened > 0; step.BracesOpened -= 1)
        {
            w.Append(")");
        }

        w.Append("\n{\n\t").TryAddIndent();
        nestingDepth += 1;
    }

    internal static void PrintStep(
        ref PrintingStep step,
        int sliceIndex,
        int parentIndex,
        IndentStackWriter w,
        string settableName,
        //StringBuilder col,
        ref string colstr,
        bool containsRequired,
        bool isRequired
    )
    {
        if(!step.Inited && step.RootIndex == sliceIndex)
        {
            if (!step.RootHaveAnyMembers)
            {
                return;
            }

            step.Inited = true;

            w.Append("if(");

            AppendColumnCheck(w, sliceIndex, colstr, settableName);

            step.BracesOpened += 1;
        }
        else if (parentIndex == step.PreviousParentIndex)
        {
            // previous -> (col1 != -1 && col2 != -1 ... colN != -1)
            // current -> (col3 != -1 && col4 != -1 ... colM != -1)
            // expect -> (col1 != -1 && col2 != -1 ... colN != -1) || (col3 != -1 && col4 != -1 ... colM != -1)
            var closeAndReopen =
                !step.RootHaveRequiredMembers
                && parentIndex == step.PreviousParentIndex
                && sliceIndex != step.PreviousIndex
                && step.PreviousHasRequired;

            var delimiter = containsRequired
                ? " && "
                : " || ";

            if(closeAndReopen)
            {
                delimiter = ") || (";
            }

            w.Append(delimiter);

            AppendColumnCheck(w, sliceIndex, colstr, settableName);
        }
        else if (parentIndex > step.PreviousParentIndex)
        {
            step.Depth += 1;
            step.PreviousParentIndex = parentIndex;

            // TODO: weird...
            var delimiter = step.RootHaveRequiredMembers && containsRequired
                ? " && "
                : " || ";

            w.Append(delimiter);

            if(!step.RootHaveRequiredMembers && containsRequired)
            {
                step.BracesOpened += 1;
                step.PreviousBraced = true;

                w.Append("(");
            }

            AppendColumnCheck(w, sliceIndex, colstr, settableName);
        }
        else if(parentIndex < step.PreviousParentIndex)
        {
            step.PreviousParentIndex = parentIndex;

            if (!step.RootHaveRequiredMembers && isRequired)
            {
                step.BracesOpened -= 1;
                w.Append(")");
            }

            // TODO: test it
            //step.RootHaveRequiredMembers = containsRequired;
        }
        else
        {
            Debug.Assert(false, "All cases are handled");
        }

        step.PreviousIndex = sliceIndex;
        step.PreviousParentIndex = parentIndex;
        step.PreviousHasRequired = containsRequired;

        return; // implicity

        static void AppendColumnCheck(IndentStackWriter w, int sliceIndex, string colstr, string settableName)
        {
            w.Append(sliceIndex switch
            {
                0 => $"col{settableName} != -1",
                _ => $"colInner__{colstr}_{settableName} != -1"
            });
        }
    }

    internal struct ParseStep
    {
        public int Depth;
        public int PreviousParentIndex;
        public bool PreviousIsEmpty;
    }

    internal static void EndParseStep(
        int depth,
        IndentStackWriter w
    )
    {
        for (int i = 0; i < depth; i++)
        {
            w.PopIndent();
            w.Append("\n}");
        }
    }

    // TODO: passing per settable fails when it comes to complex settable that doesn't have any required settables, but you need to create them anyway
    internal static void PrintParseStep(
        ref ParseStep step,
        ref Span<CrawlerSlice> slices,
        Span<SettableToParse> settables,
        ref CrawlerSlice current,
        int sliceIndex,
        int parentIndex,
        IndentStackWriter w,
        bool notFirstParentElement,
        bool hasAnyRequired,
        string colstr,
        string typeName,
        string access
    )
    {
        if(step.Depth == 0)
        { }
        else if (false && step.PreviousParentIndex > parentIndex)
        {
            step.Depth -= 1;

            w.PopIndent();
            w.Append("\n}");
        }
        else if(false && step.PreviousParentIndex == parentIndex && !step.PreviousIsEmpty)
        {
            step.Depth -= 1;

            w.PopIndent();
            w.Append("\n}");
        }

        if (notFirstParentElement)
        {
            w.Append(",\n");
        }

        w.Append($"{access} = new {typeName}()");

        if (hasAnyRequired)
        {
            step.Depth += 1;

            w.Append("\n{\n\t")
                .TryAddIndent();
        }

        for (int i = 0; i < settables.Length; i++)
        {
            if(i != 0)
            {
                w.Append(",\n");
            }

            var settable = settables[i];
            var settableName = settable.Name;

            w.Append(settableName).Append(" = reader[");

            if (sliceIndex == 0)
                w.Append($"col{settableName}");
            else
                w.Append($"colInner__{colstr}_{settableName}");

            w.Append("] is p ? p : default");
        }

        if(current.ParentIndex >= 0 && current.LastReqRecursiveChildIndex < 0)
        {
            if(settables.Length > 0)
            {
                step.Depth -= 1;
                Debug.Assert(step.Depth >= 0, "Closed more scope braces than opened");

                w.PopIndent();
                w.Append("\n}");
            }

            var ccurrent = slices[current.ParentIndex];

            // TODO::
            while(ccurrent.LastReqRecursiveChildIndex == sliceIndex && ccurrent.ParentIndex > 0)
            {
                step.Depth -= 1;
                Debug.Assert(step.Depth >= 0, "Closed more scope braces than opened");

                w.PopIndent();
                w.Append("\n}");

                ccurrent = slices[ccurrent.ParentIndex];
            }
        }

        step.PreviousParentIndex = parentIndex;
        step.PreviousIsEmpty = !hasAnyRequired;
    }

    public static void IterateThrough(SettablesCollected collected, IndentStackWriter w)
    {
        var slices   = collected.Slices.Span;
        var rSlices  = collected.RequiredSlices.Span;
        var required = collected.RequiredPrimitives.Span;
        var optional = collected.OptionalPrimitives.Span;

        var column = new StringBuilder(capacity: 256);
        var access = new StringBuilder("parsed", capacity: 256);

        var pprev = new CrawlerSlice() { ParentIndex = -1 };
        Debug.Assert(slices.Length == 0 || slices[0].ParentIndex == pprev.ParentIndex, "Zero element should have ParentIndex = -1");

        int depth = 0;

        ref var first = ref slices[0];

        // Main looping
        for(int i = 0; i < slices.Length; i++)
        {
            var root = slices[i];

            NextAccessPath(column, target: ref root, previous: pprev, slices, requiredSlices: rSlices, separator: "__", initialValue: "");
            NextAccessPath(access, target: ref root, previous: pprev, slices, requiredSlices: rSlices, separator: ".", initialValue: "parsed");

            var columnInitialLength = column.Length;
            var accessInitialLength = access.Length;

            // Object that is returned from parsing should be created anyway
            if (i == 0) goto AfterCheck;

            // required parts of this model should be already parsed by loop after label "AfterCheck"
            if (root.IsRequired) goto ParsingOptionals;

            depth += 1;

            if(i != 0)
            {
                w.Append("\n\n");
            }

            PrintingStep step = default;

            step.PreviousParentIndex = root.ParentIndex;
            step.RootIndex = i;
            step.RootHaveRequiredMembers = root.AllRequiredSimpleCount > 0;
            step.RootHaveAnyMembers = root.AllRequiredSimpleCount > 0 || root.AllNotRequiredSimpleCount > 0;

            // Deciding whether to create or not an object: < - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
            //     - When the type contains any required member then we checking all required primitives                |
            //     - Otherwise whe checking any optional primitive members to have a value in data reader               |
            //         - If we encounter optional complex type then we apply that logic recursively - - - - - - - - - - -
            
            //w.Append("\n\nif(");

            //TODO: fix. If root have required primitives, check only them and not any other required members from non-required complex settable
            var previous = root;

            int endOfIteration;

            /*
            if (root.LastReqRecursiveChildIndex >= 0)
            {
                endOfIteration = root.LastReqRecursiveChildIndex;
            }
            else if(root.AllRequiredSimpleCount > 0)
            {
                endOfIteration = i;
            }
            else if(root.LastRecursiveChildIndex >= 0)
            {
                endOfIteration = root.LastRecursiveChildIndex;
            }
            else
            {
                endOfIteration = i;
            }
            */

            // TODO: doubles check if(ch == i)
            if(root.RequiredChildCount > 0)
            {
                endOfIteration = i;
            }
            else if(root.AllOptionalChildCount > 0)
            {
                endOfIteration = i + root.AllOptionalChildCount;
            }
            else
            {
                endOfIteration = i;
            }

            for(var ch = i; ch <= endOfIteration; ++ch)
            {
                var child = slices[ch];

                NextAccessPath(column, ref child, previous: previous, slices: slices, requiredSlices: rSlices, separator: "__", initialValue: "");

                previous = child;

                if(child.AllRequiredSimpleCount > 0)
                {
                    var columnPrefix = column.ToString();
                    for(var q = child.RequiredSimpleIndex; q < child.RequiredSimpleIndex + child.RequiredSimpleCount; ++q)
                    {
                        PrintStep(ref step, sliceIndex: ch, parentIndex: child.ParentIndex, w, required[q].Name, ref columnPrefix, containsRequired: true, isRequired: true);
                    }

                    for(var r = child.FirstRequiredChildIndex; r < child.FirstRequiredChildIndex + child.AllRequiredChildCount; ++r)
                    {
                        var inner = rSlices[r];

                        NextAccessPath(sb: column, target: ref inner, previous: previous, slices: slices, requiredSlices: rSlices, separator: "__", initialValue: "");

                        previous = inner;

                        columnPrefix = column.ToString();
                        for(var q = child.RequiredSimpleIndex; q < child.RequiredSimpleIndex + child.RequiredSimpleCount; ++q)
                        {
                            PrintStep(ref step, sliceIndex: ch, parentIndex: child.ParentIndex, w, required[q].Name, ref columnPrefix, containsRequired: true, isRequired: true);
                        }
                    }

                    if (ch == i)
                    {
                        break; // for root only required primitives need to be ckecked
                    }
                }
                else if(child.AllNotRequiredSimpleCount > 0)
                {
                    var columnPrefix = column.ToString();

                    var endIndex = child.NotRequiredSimpleIndex + child.NotRequiredSimpleCount;

                    for (var q = child.NotRequiredSimpleIndex; q < endIndex; q++)
                    {
                        PrintStep(ref step, sliceIndex: ch, parentIndex: child.ParentIndex, w, optional[q].Name, ref columnPrefix, containsRequired: false, isRequired: false);
                    }
                }
            }

            int nestingDepth = default;
            EndStep(ref step, w, ref nestingDepth);

            // cleanup
            KeepPrefixOnly(column, columnInitialLength);
            KeepPrefixOnly(access, accessInitialLength);

        AfterCheck:

            if(root.IsRequired)
            {
                goto ParsingOptionals;
            }

            if(i == 0)
            {
                w.Append($"{root.TypeDisplayName} ");
            }

            var rootAccess = access.ToString();

            var parseStep = default(ParseStep);
            parseStep.PreviousParentIndex = root.ParentIndex;

            PrintParseStep(
                step: ref parseStep,
                slices: ref rSlices,
                settables: required.Slice(root.RequiredSimpleIndex, root.RequiredSimpleCount),
                current: ref root,
                sliceIndex: i,
                parentIndex: root.ParentIndex,
                w: w,
                notFirstParentElement: false,
                hasAnyRequired: root.AllRequiredSimpleCount > 0 || root.LastReqRecursiveChildIndex > 0,
                colstr: column.ToString(),
                typeName: root.TypeDisplayName,
                access: rootAccess
            );

            previous = root;
            for (var r = root.FirstRequiredChildIndex; r < root.FirstRequiredChildIndex + root.AllRequiredChildCount; ++r)
            {
                var reqChild = rSlices[r];

                NextAccessPath(column, target: ref reqChild, previous: previous, slices, requiredSlices: rSlices, separator: "__", initialValue: "");

                previous = reqChild;

                PrintParseStep(
                    step: ref parseStep,
                    slices: ref rSlices,
                    settables: required.Slice(reqChild.RequiredSimpleIndex, reqChild.RequiredSimpleCount),
                    current: ref reqChild,
                    sliceIndex: r,
                    parentIndex: reqChild.ParentIndex,
                    w: w,
                    notFirstParentElement:
                        reqChild.ParentIndex == i
                        ? root.RequiredSimpleCount > 0 || r != root.FirstRequiredChildIndex
                        : rSlices[reqChild.ParentIndex].RequiredSimpleCount > 0 || r != rSlices[reqChild.ParentIndex].FirstChildIndex,
                    hasAnyRequired: reqChild.AllRequiredSimpleCount > 0 || reqChild.LastReqRecursiveChildIndex > 0,
                    colstr: column.ToString(),
                    typeName: reqChild.TypeDisplayName,
                    access: r == i ? rootAccess : reqChild.ParentLink!.Name
                );
            }
            
            KeepPrefixOnly(column, columnInitialLength);

            EndParseStep(parseStep.Depth, w);

            w.Append(";");

        ParsingOptionals:

            // parsing optional primitives of root
            {
                var rootAccess2 = access.ToString();
                var rootColumn = column.ToString();

                for (var opt = root.NotRequiredSimpleIndex; opt < root.NotRequiredSimpleIndex + root.NotRequiredSimpleCount; opt++)
                {
                    var settable = optional[opt];

                    w.Append("\n\n");

                    if(i == 0)
                    {
                        w.Append($$"""
                        if(col{{settable.Name}} != -1)
                        {
                            {{rootAccess2}}.{{settable.Name}} = reader[col{{settable.Name}}] is {{settable.TypeDisplayName}} p ? p : default;
                        }
                        """);
                    }
                    else
                    {
                        w.Append($$"""
                        if(colInner__{{rootColumn}}_{{settable.Name}} != -1)
                        {
                            {{rootAccess2}}.{{settable.Name}} = reader[colInner__{{rootColumn}}_{{settable.Name}}] is {{settable.TypeDisplayName}} p ? p : default;
                        }
                        """);
                    }
                }

                previous = root;
                for(var ch = root.FirstRequiredChildIndex; ch < root.FirstRequiredChildIndex + root.AllRequiredChildCount; ++ch)
                {
                    var inner = rSlices[ch];

                    NextAccessPath(column, target: ref inner, previous: previous, slices, requiredSlices: rSlices, separator: "__", initialValue: "");
                    NextAccessPath(access, target: ref inner, previous: previous, slices, requiredSlices: rSlices, separator: ".", initialValue: "parsed");

                    previous = inner;

                    rootAccess2 = access.ToString();
                    rootColumn = column.ToString();

                    for (var opt = root.NotRequiredSimpleIndex; opt < root.NotRequiredSimpleIndex + root.NotRequiredSimpleCount; opt++)
                    {
                        var settable = optional[opt];

                        w.Append("\n\n");
                        w.Append($$"""
                        if(colInner__{{rootColumn}}_{{settable.Name}} != -1)
                        {
                            {{rootAccess2}}.{{settable.Name}} = reader[colInner__{{rootColumn}}_{{settable.Name}}] is {{settable.TypeDisplayName}} p ? p : default;
                        }
                        """);
                    }
                }

                KeepPrefixOnly(column, columnInitialLength);
                KeepPrefixOnly(access, accessInitialLength);
            }

            // escaping if scopes
            {
                if(i != 0 && root.LastRecursiveChildIndex < 0)
                {
                    // it means we added if(...) { ...
                    if(true && // TODO
                        first.LastReqRecursiveChildIndex < i // we omitted if for the top lvl element
                        && !root.IsRequired
                        && root.AllRequiredSimpleCount + root.AllNotRequiredSimpleCount > 0)
                    {
                        w.PopIndent();
                        w.Append("\n}");

                        depth -= 1;
                    }

                    var scopeOwner = (root.ParentIsRequired ? rSlices : slices)[root.ParentIndex];
                    var parentIndex = root.ParentIndex;
                    
                    while (parentIndex > 0 && scopeOwner.LastRecursiveChildIndex == i)
                    {
                        w.PopIndent();
                        w.Append("\n}");

                        depth -= 1;
                        Debug.Assert(depth >= 0);

                        scopeOwner = (scopeOwner.ParentIsRequired ? rSlices : slices)[parentIndex];
                        parentIndex = scopeOwner.ParentIndex;
                    }
                }
            }

            pprev = root;
        }

        {
            for(; depth > 0; depth--)
            {
                w.PopIndent();
                w.Append("\n}");
            }
        }
    }

    internal static void KeepPrefixOnly(StringBuilder sb, int prefixSize)
    {
        // TODO: change after changing the algorithm
        return;
        
        var deleteSize = sb.Length - prefixSize;
        sb.Remove(prefixSize, deleteSize);
    }

    internal static void NextAccessPath(StringBuilder sb, ref CrawlerSlice target, CrawlerSlice previous, Span<CrawlerSlice> slices, Span<CrawlerSlice> requiredSlices, string separator, string initialValue)
    {
        // TODO: temporary amgorithm
        var stack = new Stack<string>();

        var current = target;
        while (current.ParentIndex > 0)
        {
            stack.Push(current.ParentLink.Name);
            current = (current.ParentIsRequired ? requiredSlices : slices)[current.ParentIndex];
        }

        sb.Clear();
        sb.Append(initialValue);

        var notInitial = false;

        while(stack.Count > 0)
        {
            sb.Append(stack.Pop());
            if(notInitial)
            {
                sb.Append(separator);
            }

            notInitial = true;
        }

        return;

        if(target.ParentIndex > previous.ParentIndex)
        {
            WindUp(sb, ref target, separator);
        }
        else if(target.ParentIndex == previous.ParentIndex && target.ParentIndex != -1)
        {
            var removeAmount = previous.ParentLink.Name.Length + separator.Length;
            Debug.Assert(removeAmount <= sb.Length || removeAmount - sb.Length == separator.Length, "We count each name size in StringBuilder as <separator><Name> but that not case for the first name in StringBuilder, so it not contains any prefix as any other");

            removeAmount = Math.Min(removeAmount, sb.Length);
            var end = sb.Length - removeAmount;
            sb.Remove(end, removeAmount);

            WindUp(sb, ref target, separator);
        }
        else if(target.ParentIndex != previous.ParentIndex)
        {
            Unroll(sb, ref target, previous, slices, requiredSlices, separator);
            WindUp(sb, ref target, separator);
        }
    }

    internal static void WindUp(StringBuilder sb, ref CrawlerSlice target, string separator)
    {
        if(sb.Length != 0)
        {
            sb.Append(separator);
        }

        sb.Append(target.ParentLink.Name);
    }

    internal static void Unroll(StringBuilder sb, ref CrawlerSlice target, CrawlerSlice previous, Span<CrawlerSlice> slices, Span<CrawlerSlice> requiredSlices, string separator)
    {
        Debug.Assert(
            previous.ParentIndex >= target.ParentIndex,
            "Unrolling requires previous slice to be deeper",
            "Previous parent index: {0}, target parent index: {1}",
            previous.ParentIndex,
            target.ParentIndex
        );

        var removeAmount = previous.ParentLink.Name.Length + separator.Length;

        while(previous.ParentIndex != target.ParentIndex)
        {
            Debug.Assert(previous.ParentIndex >= 0);
            previous = (previous.ParentIsRequired ? requiredSlices : slices)[previous.ParentIndex];

            removeAmount += previous.ParentLink.Name.Length;
            removeAmount += separator.Length;
        }

        Debug.Assert(removeAmount <= sb.Length || removeAmount - sb.Length == separator.Length, "We count each name size in StringBuilder as <separator><Name> but that not case for the first name in StringBuilder, so it not contains any prefix as any other");

        removeAmount = Math.Min(removeAmount, sb.Length);
        var end = sb.Length - removeAmount;
        sb.Remove(end, removeAmount);
    }
}




internal sealed class BracesWriter(
    IEnumerable<ISmthWriter> contents,
    ISmthWriter? separator = null,
    ISmthWriter? before = null,
    ISmthWriter? joinBefore = null,
    ISmthWriter? after = null,
    ISmthWriter? joinAfter = null) : ISmthWriter
{
    private static readonly ISmthWriter _empty = new JustStringWriter(string.Empty);

    public BracesWriter(params IEnumerable<ISmthWriter> contents) : this(contents: contents, separator: default(ISmthWriter?), null, null, null, null)
    { }

    public BracesWriter(ISmthWriter separator, params IEnumerable<ISmthWriter> contents) : this(contents, separator)
    { }

    public BracesWriter(string separator = "\n\n", params IEnumerable<ISmthWriter> contents) : this(contents, new JustStringWriter(separator))
    { }

    public BracesWriter(char separator = '\n', params IEnumerable<ISmthWriter> contents) : this(contents, new JustStringWriter(separator.ToString()))
    { }

    public BracesWriter(params IEnumerable<string> contents) : this(contents.Select(x => new JustStringWriter(x)), separator: default(ISmthWriter))
    { }

    public BracesWriter(ISmthWriter separator, params IEnumerable<string> contents) : this(contents.Select(x => new JustStringWriter(x)), separator)
    { }

    public BracesWriter(string separator = "\n\n", params IEnumerable<string> contents) : this(contents.Select(x => new JustStringWriter(x)), new JustStringWriter(separator))
    { }

    public BracesWriter(char separator = '\n', params IEnumerable<string> contents) : this(contents.Select(x => new JustStringWriter(x)), new JustStringWriter(separator.ToString()))
    { }

    public BracesWriter(ISmthWriter before, string joinBefore, string separator = "\n\n", params IEnumerable<ISmthWriter> contents)
        : this(contents, new JustStringWriter(separator), before: before, joinBefore: new JustStringWriter(joinBefore))
    { }

    public BracesWriter(
        IEnumerable<ISmthWriter> contents,
        string? separator = null,
        string? before = null,
        ISmthWriter? joinBefore = null,
        string? after = null,
        ISmthWriter? joinAfter = null)

        : this(
              contents: contents,
              separator: separator == null ? null : new JustStringWriter(separator),
              before: before == null ? null : new JustStringWriter(before),
              joinBefore: joinBefore,
              after: after != null ? new JustStringWriter(after) : null,
              joinAfter: joinAfter
        )
    { }

    private readonly ISmthWriter _befre = before ?? _empty;
    private readonly ISmthWriter _joinBefore = joinBefore ?? _empty;

    private readonly IEnumerable<ISmthWriter> _contents = contents;

    private readonly ISmthWriter _joinAfter = joinAfter ?? _empty;
    private readonly ISmthWriter _after = after ?? _empty;

    private readonly ISmthWriter _separator = separator ?? _empty;

    public void Write(IndentStackWriter writer)
    {
        using var enumerator = _contents.GetEnumerator();

        if (!enumerator.MoveNext()) return;

        _befre.Write(writer);
        _joinBefore.Write(writer);

        _ = writer["{\r\n\t"];

        var indented = writer.TryAddIndent();

        enumerator.Current.Write(writer);

        while (enumerator.MoveNext())
        {
            _separator.Write(writer);
            enumerator.Current.Write(writer);
        }

        writer.RemoveIndentIfAdded(indented);

        _ = writer["\r\n}"];

        _joinAfter.Write(writer);
        _after.Write(writer);
    }
}


internal sealed class RequiredOnlySettablesCrawler(ModelToParse target)
{
    private SettableToParse? _current;
    private ModelToParse _target = target;

    private IEnumerator<SettableToParse> _settables = target.Settables.Where(x => !x.IsComplex && x.IsRequired).GetEnumerator();
    private IEnumerator<(SettableToParse link, ModelToParse next)> _complex = EnumerateComplex(target);

    private readonly Stack<CrawlerSection> _pathToCurrent = [];
    private StringBuilder _variableContainer = new();

    public SettableToParse? Current => _current;
    public ModelToParse Target => _target;

    public CrawlerCollected GetVariableAndSource()
    {
        if (_current == null)
            return new(default!, default!, default, _target, _current!, _pathToCurrent.Count);

        var variable = GetVariableName();
        var access = GetFieldAccess();
        var source = _current.FieldSource;

        return new CrawlerCollected(variable, access, source, _target, _current, _pathToCurrent.Count);
    }

    public string GetFieldAccess()
    {
        _variableContainer.Clear();

        if (_current is null) return string.Empty;

        if (_pathToCurrent.Count == 0) return _current.Name;

        const char chainCell = '.';

        using var enumerator = _pathToCurrent.GetEnumerator();

        if (!enumerator.MoveNext()) return _current.Name;

        try
        {
            _variableContainer.Append(enumerator.Current.Link.Name);

            while (enumerator.MoveNext())
            {
                _variableContainer
                    .Append(chainCell)
                    .Append(enumerator.Current.Link.Name);
            }

            _variableContainer
                .Append(chainCell)
                .Append(_current.Name);

            return _variableContainer.ToString();
        }
        finally
        {
            _variableContainer.Clear();
        }
    }

    public string GetVariableName()
    {
        if (_current == null) return string.Empty;

        if (_pathToCurrent.Count == 0)
        {
            return $"col{_current.Name}";
        }

        const string separator = "__";

        _variableContainer.Clear();

        var result = _variableContainer;

        result.Append("col");

        using var enumerator = _pathToCurrent.GetEnumerator();

        enumerator.MoveNext();

        result.Append(enumerator.Current.Link.Name);

        while (enumerator.MoveNext())
        {
            result.Append(separator);
            result.Append(enumerator.Current.Link.Name);
        }

        result.Append(separator);
        result.Append(_current.Name);

        var variable = result.ToString();

        result.Clear();

        return variable;
    }

    [MemberNotNullWhen(true, nameof(_current))]
    public bool Next()
    {
        if (_settables.MoveNext())
        {
            _current = _settables.Current;
            return true;
        }

        var complex = _complex;
        var target = _target;

        while (true)
        {
            IEnumerator<SettableToParse> settables;
            SettableToParse link;
            ModelToParse next;

            if (complex.MoveNext())
            {
                (link, next) = complex.Current;

                settables = next.Settables.GetEnumerator();

                if (!settables.MoveNext())
                {
                    var nextComplex = EnumerateComplex(next);

                    _pathToCurrent.Push(new(target, link, complex));
                    (complex, target) = (nextComplex, next);

                    continue;
                }
            }
            else
            {
                if (_pathToCurrent.Count == 0)
                {
                    return false;
                }

                var poped = _pathToCurrent.Pop();
                (target, complex) = (poped.Source, poped.Complex);

                continue;
            }

            _pathToCurrent.Push(new(target, link, complex));
            _target = next;
            _settables = settables;
            _complex = EnumerateComplex(next);
            _current = settables.Current;

            return true;
        }
    }

    private static IEnumerator<(SettableToParse link, ModelToParse next)> EnumerateComplex(ModelToParse model)
    {
        return model.ComplexSettables
            .Where(x => x.Key.IsRequired)
            .Select(x => (x.Key, x.Value))
            .GetEnumerator();
    }
}


internal sealed class NotRequiredOnlySettablesCrawler(ModelToParse target)
{
    private SettableToParse? _current;
    private ModelToParse _target = target;

    private IEnumerator<SettableToParse> _settables = target.Settables.Where(x => !x.IsComplex && !x.IsRequired).GetEnumerator();
    private IEnumerator<(SettableToParse link, ModelToParse next)> _complex = EnumerateComplex(target);

    private readonly Stack<CrawlerSection> _pathToCurrent = [];
    private StringBuilder _variableContainer = new();

    public SettableToParse? Current => _current;
    public ModelToParse Target => _target;

    public CrawlerCollected GetVariableAndSource()
    {
        if (_current == null)
            return new(default!, default!, default, _target, _current!, _pathToCurrent.Count);

        var variable = GetVariableName();
        var access = GetFieldAccess();
        var source = _current.FieldSource;

        return new CrawlerCollected(variable, access, source, _target, _current, _pathToCurrent.Count);
    }

    public string GetFieldAccess()
    {
        _variableContainer.Clear();

        if (_current is null) return string.Empty;

        if (_pathToCurrent.Count == 0) return _current.Name;

        const char chainCell = '.';

        using var enumerator = _pathToCurrent.GetEnumerator();

        if (!enumerator.MoveNext()) return _current.Name;

        try
        {
            _variableContainer.Append(enumerator.Current.Link.Name);

            while (enumerator.MoveNext())
            {
                _variableContainer
                    .Append(chainCell)
                    .Append(enumerator.Current.Link.Name);
            }

            _variableContainer
                .Append(chainCell)
                .Append(_current.Name);

            return _variableContainer.ToString();
        }
        finally
        {
            _variableContainer.Clear();
        }
    }

    public string GetVariableName()
    {
        if (_current == null) return string.Empty;

        if (_pathToCurrent.Count == 0)
        {
            return $"col{_current.Name}";
        }

        const string separator = "__";

        _variableContainer.Clear();

        var result = _variableContainer;

        result.Append("col");

        using var enumerator = _pathToCurrent.GetEnumerator();

        enumerator.MoveNext();

        result.Append(enumerator.Current.Link.Name);

        while (enumerator.MoveNext())
        {
            result.Append(separator);
            result.Append(enumerator.Current.Link.Name);
        }

        result.Append(separator);
        result.Append(_current.Name);

        var variable = result.ToString();

        result.Clear();

        return variable;
    }

    [MemberNotNullWhen(true, nameof(_current))]
    public bool Next()
    {
        if (_settables.MoveNext())
        {
            _current = _settables.Current;
            return true;
        }

        var complex = _complex;
        var target = _target;

        while (true)
        {
            IEnumerator<SettableToParse> settables;
            SettableToParse link;
            ModelToParse next;

            if (complex.MoveNext())
            {
                (link, next) = complex.Current;

                settables = next.Settables.GetEnumerator();

                if (!settables.MoveNext())
                {
                    var nextComplex = EnumerateComplex(next);

                    _pathToCurrent.Push(new(target, link, complex));
                    (complex, target) = (nextComplex, next);

                    continue;
                }
            }
            else
            {
                if (_pathToCurrent.Count == 0)
                {
                    return false;
                }

                var poped = _pathToCurrent.Pop();
                (target, complex) = (poped.Source, poped.Complex);

                continue;
            }

            _pathToCurrent.Push(new(target, link, complex));
            _target = next;
            _settables = settables;
            _complex = EnumerateComplex(next);
            _current = settables.Current;

            return true;
        }
    }

    private static IEnumerator<(SettableToParse link, ModelToParse next)> EnumerateComplex(ModelToParse model)
    {
        return model.ComplexSettables
            .Where(x => !x.Key.IsRequired)
            .Select(x => (x.Key, x.Value))
            .GetEnumerator();
    }
}



internal sealed class CreationCrawler(ModelToParse target)
{
    private SettableToParse? _current;
    private ModelToParse _target = target;

    private bool _anyRequired = false;
    private IEnumerator<SettableToParse> _settables = target.Settables.GetEnumerator();
    private IEnumerator<(SettableToParse link, ModelToParse next)> _complex = EnumerateComplex(target);

    private readonly Stack<CrawlerSection> _pathToCurrent = [];
    private StringBuilder _variableContainer = new();

    public SettableToParse? Current => _current;
    public ModelToParse Target => _target;

    public CrawlerCollected GetVariableAndSource()
    {
        if (_current == null)
            return new(default!, default!, default, _target, _current!, _pathToCurrent.Count);

        var variable = GetVariableName();
        var access = GetFieldAccess();
        var source = _current.FieldSource;

        return new CrawlerCollected(variable, access, source, _target, _current, _pathToCurrent.Count);
    }

    public string GetFieldAccess()
    {
        _variableContainer.Clear();

        if (_current is null) return string.Empty;

        if (_pathToCurrent.Count == 0) return _current.Name;

        const char chainCell = '.';

        using var enumerator = _pathToCurrent.GetEnumerator();

        if (!enumerator.MoveNext()) return _current.Name;

        try
        {
            _variableContainer.Append(enumerator.Current.Link.Name);

            while (enumerator.MoveNext())
            {
                _variableContainer
                    .Append(chainCell)
                    .Append(enumerator.Current.Link.Name);
            }

            _variableContainer
                .Append(chainCell)
                .Append(_current.Name);

            return _variableContainer.ToString();
        }
        finally
        {
            _variableContainer.Clear();
        }
    }

    public string GetVariableName()
    {
        if (_current == null) return string.Empty;

        if (_pathToCurrent.Count == 0)
        {
            return $"col{_current.Name}";
        }

        const string separator = "__";

        _variableContainer.Clear();

        var result = _variableContainer;

        result.Append("col");

        using var enumerator = _pathToCurrent.GetEnumerator();

        enumerator.MoveNext();

        result.Append(enumerator.Current.Link.Name);

        while (enumerator.MoveNext())
        {
            result.Append(separator);
            result.Append(enumerator.Current.Link.Name);
        }

        result.Append(separator);
        result.Append(_current.Name);

        var variable = result.ToString();

        result.Clear();

        return variable;
    }

    public void Move()
    {

    }

    [MemberNotNullWhen(true, nameof(_current))]
    public bool Next()
    {
        if (_settables.MoveNext())
        {
            _current = _settables.Current;
            return true;
        }

        var complex = _complex;
        var target = _target;

        while (true)
        {
            IEnumerator<SettableToParse> settables;
            SettableToParse link;
            ModelToParse next;

            if (complex.MoveNext())
            {
                (link, next) = complex.Current;

                settables = next.Settables.GetEnumerator();

                if (!settables.MoveNext())
                {
                    var nextComplex = EnumerateComplex(next);

                    _pathToCurrent.Push(new(target, link, complex));
                    (complex, target) = (nextComplex, next);

                    continue;
                }
            }
            else
            {
                if (_pathToCurrent.Count == 0)
                {
                    return false;
                }

                var poped = _pathToCurrent.Pop();
                (target, complex) = (poped.Source, poped.Complex);

                continue;
            }

            _pathToCurrent.Push(new(target, link, complex));
            _target = next;
            _settables = settables;
            _complex = EnumerateComplex(next);
            _current = settables.Current;

            return true;
        }
    }

    private static IEnumerator<(SettableToParse link, ModelToParse next)> EnumerateComplex(ModelToParse model)
    {
        return model.ComplexSettables
            .Where(x => !x.Key.IsRequired)
            .Select(x => (x.Key, x.Value))
            .GetEnumerator();
    }
}


internal sealed class SettableCrawlerEnumerator(ModelToParse target)
{
    private static IComparer<SettableToParse> _orderRequired = Comparer<SettableToParse>.Create((x, y) =>
    {
        return (x, y) switch
        {
            ({ IsRequired: true }, { IsRequired: false }) => 1,
            ({ IsRequired: false }, { IsRequired: true }) => -1,
            _ => 0
        };
    });

    private static IComparer<(SettableToParse link, ModelToParse next)> _orderRequiredComplex = Comparer<(SettableToParse link, ModelToParse next)>.Create((x, y) =>
    {
        return (x.link, y.link) switch
        {
            ({ IsRequired: true }, { IsRequired: false }) => 1,
            ({ IsRequired: false }, { IsRequired: true }) => -1,
            _ => 0
        };
    });

    private SettableToParse? _current;
    private ModelToParse _target = target;

    private IEnumerator<SettableToParse> _settables = target.Settables.Where(x => !x.IsComplex).OrderByDescending(x => x, _orderRequired).GetEnumerator();
    private IEnumerator<(SettableToParse link, ModelToParse next)> _complex = EnumerateComplex(target);

    private readonly Stack<CrawlerSection> _pathToCurrent = [];
    private StringBuilder _variableContainer = new();

    public SettableToParse? Current => _current;
    public ModelToParse Target => _target;

    public CrawlerCollected GetVariableAndSource()
    {
        if(_current == null) return default;

        var variable = GetVariableName();
        var access = GetFieldAccess();
        var source = _current.FieldSource;

        return new CrawlerCollected(variable, access, source, _target, _current, _pathToCurrent.Count);
    }

    public string GetFieldAccess()
    {
        _variableContainer.Clear();

        if(_current is null) return string.Empty;

        if(_pathToCurrent.Count == 0) return _current.Name;

        const char chainCell = '.';

        using var enumerator = _pathToCurrent.GetEnumerator();

        if(!enumerator.MoveNext()) return _current.Name;

        try
        {
            _variableContainer.Append(enumerator.Current.Link.Name);

            while(enumerator.MoveNext())
            {
                _variableContainer
                    .Append(chainCell)
                    .Append(enumerator.Current.Link.Name);
            }

            _variableContainer
                .Append(chainCell)
                .Append(_current.Name);

            return _variableContainer.ToString();
        }
        finally
        {
            _variableContainer.Clear();
        }
    }

    public string GetVariableName()
    {
        if (_current == null) return string.Empty;

        if(_pathToCurrent.Count == 0)
        {
            return $"col{_current.Name}";
        }

        const string separator = "__";

        _variableContainer.Clear();

        var result = _variableContainer;

        result.Append("col");

        using var enumerator = _pathToCurrent.GetEnumerator();

        enumerator.MoveNext();

        result.Append(enumerator.Current.Link.Name);

        while (enumerator.MoveNext())
        {
            result.Append(separator);
            result.Append(enumerator.Current.Link.Name);
        }

        result.Append(separator);
        result.Append(_current.Name);

        var variable = result.ToString();

        result.Clear();

        return variable;
    }

    [MemberNotNullWhen(true, nameof(_current))]
    public bool Next()
    {
        if(_settables.MoveNext())
        {
            _current = _settables.Current;
            return true;
        }

        var complex = _complex;
        var target = _target;

        while (true)
        {
            IEnumerator<SettableToParse> settables;
            SettableToParse link;
            ModelToParse next;

            if (complex.MoveNext())
            {
                (link, next) = complex.Current;

                settables = next.Settables.GetEnumerator();

                if(!settables.MoveNext())
                {
                    var nextComplex = EnumerateComplex(next);

                    _pathToCurrent.Push(new(target, link, complex));
                    (complex, target) = (nextComplex, next);

                    continue;
                }
            }
            else
            {
                if (_pathToCurrent.Count == 0)
                {
                    return false;
                }

                var poped = _pathToCurrent.Pop();
                (target, complex) = (poped.Source, poped.Complex);

                continue;
            }

            _pathToCurrent.Push(new(target, link, complex));
            _target = next;
            _settables = settables;
            _complex = EnumerateComplex(next);
            _current = settables.Current;

            return true;
        }
    }

    private static IEnumerator<(SettableToParse link, ModelToParse next)> EnumerateComplex(ModelToParse model)
    {
        return model.ComplexSettables
            .Select(x => (x.Key, x.Value))
            .OrderByDescending(x => x, _orderRequiredComplex)
            .GetEnumerator();
    }
}

internal readonly struct CrawlerCollected(string variableName, string access, FieldsOrOrder source, ModelToParse parent, SettableToParse property, int depth)
{
    public string VariableName { get; init; } = variableName;
    public string Access { get; init; } = access;
    public FieldsOrOrder Source { get; init; } = source;
    public ModelToParse Parent { get; init; } = parent;
    public SettableToParse Property { get; init; } = property;
    public bool IsRoot => Depth == 0;
    public int Depth { get; init; } = depth;
}

internal readonly struct CrawlerSection(
    ModelToParse source,
    SettableToParse link,
    IEnumerator<(SettableToParse link, ModelToParse next)> complex
)
{
    public SettableToParse Link { get; } = link;
    public ModelToParse Source { get; } = source;
    public IEnumerator<(SettableToParse link, ModelToParse next)> Complex { get; } = complex;
}

internal sealed class ModelToParse
{
    public required TypeToParse Type { get; set; }
    public required IEnumerable<SettableToParse> Settables { get; set; }
    public required Dictionary<SettableToParse, ModelToParse> ComplexSettables { get; set; }
}

internal sealed class SettableToParse
{
    public required bool IsRequired { get; set; }
    public required bool IsComplex { get; set; }
    public required string Name { get; set; }
    public required string TypeDisplayName { get; set; }
    public required FieldsOrOrder FieldSource { get; set; }
}

internal sealed class TypeToParse
{
    public required string DisplayName { get; set; }
}

internal sealed class ReadMethod(
    ISmthWriter signature,
    ISmthWriter earlyEscape,
    ISmthWriter indexesReading,
    ISmthWriter loopingAndReturning) : ISmthWriter
{
    private readonly ISmthWriter _signature = signature;
    private readonly ISmthWriter _earlyEscape = earlyEscape;
    private readonly ISmthWriter _indexesReading = indexesReading;
    private readonly ISmthWriter _loopingAndReturning = loopingAndReturning;

    public void Write(IndentStackWriter writer)
    {
        _ = writer[$$"""
            {{writer.Write(_signature)}}
            {
                {{writer.WriteScoped(
                    [
                        _earlyEscape,
                        _indexesReading,
                        _loopingAndReturning
                    ]
                )}}
            }
            """
        ];
    }
}

internal sealed class JustStringWriter(string source) : ISmthWriter
{
    private readonly string _source = source;

    public void Write(IndentStackWriter writer)
        => _ = writer[_source];

    public static implicit operator JustStringWriter(string source) => new(source);
}

internal sealed class ConcatSeparatedWriter(IEnumerable<ISmthWriter> contents, ISmthWriter separator) : ISmthWriter
{
    public ConcatSeparatedWriter(ISmthWriter separator, params IEnumerable<ISmthWriter> contents)
        : this(contents, separator)
    { }

    public ConcatSeparatedWriter(string separator, params IEnumerable<ISmthWriter> contents)
        : this(contents, new JustStringWriter(separator))
    { }

    public ConcatSeparatedWriter(string separator, params IEnumerable<string> contents)
        : this(contents.Select(x => new JustStringWriter(x)), new JustStringWriter(separator))
    { }

    public void Write(IndentStackWriter writer) => Write(contents, separator, writer);

    public static void Write(IEnumerable<ISmthWriter> contents, ISmthWriter separator, IndentStackWriter writer)
    {
        //var indented = writer.TryAddIndent();

        using var enumerator = contents.GetEnumerator();

        if (!enumerator.MoveNext()) return;

        writer.WriteNoScope(enumerator.Current);

        if (!enumerator.MoveNext()) return;

        do
        {
            writer.WriteNoScope(separator);
            writer.WriteNoScope(enumerator.Current);
        } while (enumerator.MoveNext());

        //writer.RemoveIndentIfAdded(indented);
    }
}

internal sealed class ConcatWriter(IEnumerable<ISmthWriter> contents) : ISmthWriter
{
    public ConcatWriter(params IEnumerable<string> contents)
        : this(contents: contents.Select(x => new JustStringWriter(x)))
    { }

    public ConcatWriter(string content, ISmthWriter middle, params IEnumerable<string> rest)
        : this([new JustStringWriter(content), middle, ..rest.Select(x => new JustStringWriter(x))])
    { }

    public void Write(IndentStackWriter writer) => Write(contents, writer);

    public static void Write(IEnumerable<ISmthWriter> contents, IndentStackWriter writer)
    {
        foreach(var content in contents)
        {
            content.Write(writer);
        }
    }
}

internal sealed class ReadUnbufferedSync : ISmthWriter
{
    private readonly ISmthWriter _signature;
    private readonly ISmthWriter _indexesReading;
    private readonly ISmthWriter _parsing;

    private readonly ISmthWriter _parsedName;

    public void Write(IndentStackWriter writer)
    {
        _ = writer[$$"""
            {{writer.WriteScoped(_signature)}}
            {
                if(!reader.Read())
                {
                    yield break;
                }

                {{writer.WriteScoped(_indexesReading)}}

                do
                {
                    {{writer.WriteScoped(_parsing)}}

                    yield return {{writer.Write(_parsedName)}};
                } while (reader.Read());
            }
            """];
    }
}

internal sealed class ReadListSync : ISmthWriter
{
    private readonly ISmthWriter _signature;
    private readonly ISmthWriter _indexesReading;
    private readonly ISmthWriter _parsing;

    private readonly ISmthWriter _type;
    private readonly ISmthWriter _parsedName;

    public void Write(IndentStackWriter writer)
    {
        _ = writer[$$"""
            {{writer.Write(_signature)}}
            {
                if(!reader.Read())
                {
                    yield break;
                }

                List<{{writer.Write(_type)}}> result = new List<{{writer.Write(_type)}}>();

                {{writer.Write(_indexesReading)}}

                do
                {
                    {{writer.Write(_parsing)}}

                    result.Add({{writer.Write(_parsedName)}});
                } while (reader.Read());
            }
            """];
    }
}

internal sealed class IncludeNamespaces(IEnumerable<string> namespaces) : ISmthWriter
{
    public void Write(IndentStackWriter writer)
    {
        _ = writer[namespaces];
    }
}

internal sealed class MethodDeclarator : IMethodDeclarator
{
    private readonly string _name;
}

internal sealed class MethodArgument : IMethodArgument
{
    private readonly string _name;
    private readonly string _type;
    private readonly string _modifiers;
}

internal sealed class MethodBody : IMethodBody
{
}

internal static class WritingsExtension
{
    public static string AppendIt(this ISmthWriter writer, IndentStackWriter target)
    {
        writer.Write(target);
        return string.Empty;
    }

    public static IndentedInterpolatedStringHandler WriteScoped(this IndentStackWriter writer, IEnumerable<ISmthWriter> contents)
    {
        return WriteScoped(contents, writer);
    }

    public static IndentedInterpolatedStringHandler WriteScoped(this IndentStackWriter writer, ISmthWriter content)
    {
        return writer.Scope[writer.WriteNoScope(content)];
    }

    public static IndentedInterpolatedStringHandler Write(this IndentStackWriter writer, ISmthWriter content)
    {
        return writer.Scope[writer.WriteNoScope(content)];
    }

    public static IndentedInterpolatedStringHandler WriteNoScope(this IndentStackWriter writer, ISmthWriter content)
    {
        content.Write(writer);
        return default;
    }

    public static IndentedInterpolatedStringHandler WriteScoped(this IEnumerable<ISmthWriter> contents, IndentStackWriter writer)
    {
        writer.Scope.ForEach(contents, WriteContent);
        return default;
    }

    public static IndentedInterpolatedStringHandler WriteContent(IndentStackWriter writer, ISmthWriter content)
    {
        content.Write(writer);
        return default;
    }
}
