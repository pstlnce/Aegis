using Aegis.IndentWriter;
using Aegis.Options;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;

namespace Aegis;

[Generator(LanguageNames.CSharp)]
internal sealed class AegisGen : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG
        Debugger.Launch();
#endif

        context.RegisterPostInitializationOutput(postInitContext =>
        {
            if (!postInitContext.CancellationToken.IsCancellationRequested)
                postInitContext.AddSource($"{AegisAttributeGenerator.AttributeFullName}.g.cs", AegisAttributeGenerator.MarkerAttributeSourceCode);
        });

        var customParsers = context.SyntaxProvider.ForCustomParsers();

        var dbParseTargets = context.SyntaxProvider.ForParseTargets();

        var customParsersCollected = customParsers.Collect();
        var dbParseCollected = dbParseTargets.Collect();

        var collectionEpilogue = customParsersCollected.Combine(dbParseCollected);

        context.RegisterSourceOutput(collectionEpilogue, (context, items) =>
        {
            var parsers = items.Left.ToParsers(context);
            GenerateDataReaderParsers(context, items.Right, parsers);
        });
    }

    private static void GenerateDataReaderParsers(SourceProductionContext productionContext, ImmutableArray<MatchingModel?> models, Dictionary<string, string> parsers)
    {
        var token = productionContext.CancellationToken;

        if (token.IsCancellationRequested)
            return;

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
            
            try
            {

            var ignore = _[$$"""
                using System;
                using System.Data;
                using System.Data.Common;
                using System.Collections.Generic;
                using System.Collections.ObjectModel;
                using System.Runtime.CompilerServices;
                using System.Threading;
                using System.Threading.Tasks;

                {{_[
                    typeNamespace == null
                    ? AppendClass(_, model.Value, token)
                    : _[$$""" 
                        namespace {{typeNamespace}}
                        {
                            {{_[AppendClass(_, model.Value, token)]}}
                        }
                        """
                    ]
                ]}}
                """
            ];
            }
            catch (Exception ex)
            {

                throw;
            }

            if (token.IsCancellationRequested) return;

            var sourceCodeText = sourceCode.ToString();
            sourceCode.Clear();

            var fileName = typeNamespace != null
                ? $"{typeNamespace}.{type.Name}Parser.g.cs"
                : $"{type.Name}Parser.g.cs";

            productionContext.AddSource(fileName, sourceCodeText);
        }
    }

    internal static IndentedInterpolatedStringHandler AppendClass(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        return _.Scope[$$"""
            internal sealed partial class {{model.Type.Name}}Parser
            {
                {{_[AppendReadList(_, model)]}}

                {{_[AppendReadUnbuffered(_, model)]}}

                {{_[AppendReadListAsync(_, model, isValueTask: false, token)]}}

                {{_[AppendReadListAsync(_, model, isValueTask: true, token)]}}

                {{_[AppendReadUnbufferedAsync(_, model)]}}

                {{_[AppendReadSchemaIndexes(_, model)]}}

                {{_[AppendReadSchemaColumnIndex(_, model)]}}
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadUnbuffered(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var settables = model.Settables;
        var type = model.Type;

        return _.Scope[
            $$"""
            internal static IEnumerable<{{type.Name}}> ReadUnbuffered<TReader>(TReader reader)
                where TReader : IDataReader
            {
                if(!reader.Read())
                {
                    yield break;
                }

                {{_[AppendIndexesReading(_, model)]}}
            
                do
                {
                    {{_[AppendParsingBody(_, model, token)]}}

                    yield return parsed;
                } while(reader.Read());
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadList(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var properties = model.Settables;
        var type = model.Type;

        return _.Scope[
            $$"""
            internal static List<{{type.Name}}> ReadList<TReader>(TReader reader)
                where TReader : IDataReader
            {
                var result = new List<{{type.Name}}>();

                if(!reader.Read())
                {
                    return result;
                }

                {{_[AppendIndexesReading(_, model)]}}
            
                do
                {
                    {{_[AppendParsingBody(_, model, token)]}}

                    result.Add(parsed);
                } while(reader.Read());
            
                return result;
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadListAsync(IndentStackWriter _, MatchingModel model, bool isValueTask, CancellationToken token = default)
    {
        var settables = model.Settables;
        var type = model.Type;

        return _.Scope[
            $$"""
            {{_[isValueTask
              ? _[$"internal static async ValueTask<List<{type.Name}>> ReadListAsyncValue<TReader>(TReader reader, CancellationToken token = default)"]
              : _[$"internal static async Task<List<{type.Name}>> ReadListAsync<TReader>(TReader reader, CancellationToken token = default)"]
            ]}}
                where TReader : DbDataReader
            {
                var result = new List<{{type.Name}}>();

                if(!(await reader.ReadAsync(token).ConfigureAwait(false)))
                {
                    return result;
                }
            
                {{_[AppendIndexesReading(_, model)]}}
            
                Task<bool> reading;

                while(true)
                {
                    {{_[AppendParsingBody(_, model, token)]}}

                    reading = reader.ReadAsync(token);

                    result.Add(parsed);

                    if(!(await reading.ConfigureAwait(false)))
                    {
                        break;
                    }
                }
            
                return result;
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadUnbufferedAsync(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var settables = model.Settables;
        var type = model.Type;

        return _.Scope[
            $$"""
            internal static async IAsyncEnumerable<{{type.Name}}> ReadUnbufferedAsync<TReader>(TReader reader, [EnumeratorCancellationAttribute] CancellationToken token = default)
                where TReader : DbDataReader
            {
                if(!(await reader.ReadAsync(token).ConfigureAwait(false)))
                {
                    yield break;
                }

                {{_[AppendIndexesReading(_, model)]}}
            
                Task<bool> reading;

                while(true)
                {
                    {{_[AppendParsingBody(_, model, token)]}}

                    reading = reader.ReadAsync(token);

                    yield return parsed;

                    if(!(await reading.ConfigureAwait(false)))
                    {
                        break;
                    }
                }
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadSchemaIndexes(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var complexTypes = model.Settables.Where(x => !x.IsPrimitive)
            .Select(x => (x, TraverseSettables(model.Inner![x.Type.DisplayString])))
            .ToDictionary(k => k.x, v => v.Item2);

        foreach(var complexType in complexTypes)
        {
            complexType.Value.ForWhomTheBellTolls = complexType.Key;
        }

        var settables = model.Settables
            .Where(x => x.IsPrimitive)
            .Select(x => (name: x.Name, columnId: x.FieldSource.IsOrder ? x.FieldSource.Order : -1))
            .Concat(
                complexTypes.SelectMany(x => x.Value.GetAllVariablesNames())
                    .Select(x => (name: x, columnId: -1))
            );

        var required = model.RequiredSettables
            .Where(x => x.IsPrimitive)
            .Select(x => x.Name)
            .Concat(
                complexTypes.SelectMany(x => x.Value.GetAllRequiredVariables())
            )
            .Distinct()
            .ToImmutableArray();

        var type = model.Type;

        var alreadySettedIndexes = settables
            .Where(x => x.columnId > -1)
            .Select(x => x.columnId)
            .ToList();

        var indexesRangeExcludeZero = Enumerable.Range(1, Math.Max(0, alreadySettedIndexes.Count - 1));

        return _.Scope[
            $$"""
            {{_.If(required.Length != 0)[
            $$"""
            internal static void ThrowIfNotEnoughFieldsForRequiredException(int expected, int actual)
            {
                if(expected > actual)
                    throw new {{NotEnoughReaderFieldsException.FullName}}(expected, actual);
            }

            internal static void ThrowNoFieldSourceMatchedRequiredSettableException(string[] settables)
            {
                throw new {{MissingRequiredFieldOrPropertyException.FullName}}(settables);
            }

            """]
            
            }}
            internal static void ReadSchemaIndexes<TReader>(TReader reader{{_[settables.Select(x => $", out int column{x.name}"), joinBy: ""]}})
                where TReader : IDataReader
            {
                {{_.If(required.Length != 0)[$"ThrowIfNotEnoughFieldsForRequiredException({required.Length}, reader.FieldCount);\n"]
                
                }}
                {{_.Scope[settables.Select(x => $"column{x.name} = {x.columnId};"), joinBy: "\n"]}}

                for(int i = 0; i != reader.FieldCount; i++)
                {
                    {{_[alreadySettedIndexes.Count == 0
                    ? _[default]
                    : _.Scope[_.If(alreadySettedIndexes.Count > 0)[
                    $$"""
                    if (i == {{alreadySettedIndexes[0]}}{{new string('\n', alreadySettedIndexes.Count > 1 ? 1 : 0)}} {{
                        _.Scope[
                            indexesRangeExcludeZero.Select(x => $"\t|| i == {alreadySettedIndexes[x]}"),
                            joinBy: "\n"
                        ]}})
                    {
                        continue;
                    }

                    """]]]

                    }}
                    {{_.Scope[$"ReadSchemaColumnIndex(reader.GetName(i), i{settables.Select(x => $", ref column{x.name}")})"]}};
                }
                {{_.Scope.If(required.Length != 0)[
                $$"""

                int missedCount =
                    {{_.Scope[required.Select(x => $"column{x} == -1 ? 1 : 0"), joinBy: "+\n"]}};

                if(missedCount > 0)
                {
                    string[] missed = new string[missedCount];
                    int writed = 0;

                    {{_.Scope[required.Select(x => $"if(column{x} == -1) missed[writed++] = \"{x}\";"), joinBy: "\n"]}}

                    ThrowNoFieldSourceMatchedRequiredSettableException(missed);
                }

                """
                ]}}
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadSchemaColumnIndex(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var matchCases = model.MatchingSettings.MatchCase;

        var namesToMatch = GetNamesToMatch(model, token);

        //var properties = model.Settables.Where(x => x.IsPrimitive).Select(x => x.Name)
        //    .Concat(namesToMatch.Values.SelectMany(x => x.Select(l => l.settableName)));

        var complexTypes = model.Settables.Where(x => !x.IsPrimitive)
            .Select(x => (x, TraverseSettables(model.Inner![x.Type.DisplayString])))
            .ToDictionary(k => k.x, v => v.Item2);

        foreach (var complexType in complexTypes)
        {
            complexType.Value.ForWhomTheBellTolls = complexType.Key;
        }

        var settables = model.Settables
            .Where(x => x.IsPrimitive)
            .Select(x => (name: x.Name, columnId: x.FieldSource.IsOrder ? x.FieldSource.Order : -1))
            .Concat(
                complexTypes.SelectMany(x => x.Value.GetAllVariablesNames())
                    .Select(x => (name: x, columnId: -1))
            )
            .Select(x => x.name)
            .ToList();

        return _.Scope[
            $$"""
            internal static void ReadSchemaColumnIndex(string c, int i{{settables.Select(x => $", ref int col{x}")}})
            {
                switch(c.Length)
                {
                    {{_.Scope.ForEach(namesToMatch, (_, n) => _[$$"""
                    case {{n.Key}}:
                        {{_.Scope.ForEach(n.Value, (_, x) => _[$$"""
                            {{(matchCases.HasFlag(MatchCase.IgnoreCase)
                        ? $"if(col{x.settableName} == -1 && string.Equals(c, \"{x.name}\", StringComparison.OrdinalIgnoreCase))"
                        : $"if(col{x.settableName} == -1 && c == \"{x.name}\")")}}
                            {
                                col{{x.settableName}} = i;
                                return;
                            }
                            """])}}
                        break;
                    """])
                    }}
                }
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendIndexesReading(IndentStackWriter _, MatchingModel model)
    {
        //var required = model.RequiredSettables;

        //var requiredComplex = model.RequiredSettables.Where(x => !x.IsPrimitive).ToList();
        //var requiredPrimitives = model.RequiredSettables.Where(x => x.IsPrimitive)
        //    .Select(x => (displayString: x.Type.DisplayString, x.Name))
        //    .ToList();

        //var optional = model.UsualSettables;





        //var namesToMatch = GetNamesToMatch(model);

        //var settables = model.Settables.Where(x => x.IsPrimitive).Select(x => x.Name)
        //    .Concat(namesToMatch.Values.SelectMany(x => x.Select(l => l.settableName))).Distinct();




        var complexTypes = model.Settables.Where(x => !x.IsPrimitive)
            .Select(x => (x, TraverseSettables(model.Inner![x.Type.DisplayString])))
            .ToDictionary(k => k.x, v => v.Item2);

        foreach (var complexType in complexTypes)
        {
            complexType.Value.ForWhomTheBellTolls = complexType.Key;
        }

        var settables = model.Settables
            .Where(x => x.IsPrimitive)
            .Select(x => (name: x.Name, columnId: x.FieldSource.IsOrder ? x.FieldSource.Order : -1))
            .Concat(
                complexTypes.SelectMany(x => x.Value.GetAllVariablesNames())
                    .Select(x => (name: x, columnId: -1))
            )
            .Select(x => x.name)
            .ToList();

        //Dictionary<Settable, Mental> complexMaps = model.Settables.Where(x => !x.IsPrimitive)
        //    .Select(x => (x, TraverseSettables(model.Inner![x.Type.DisplayString])))
        //    .ToDictionary(k => k.x, v => v.Item2);

        //var settables = model.Settables
        //    .Where(x => x.IsPrimitive)
        //    .Select(x => x.Name)
        //    .Concat(complexMaps.Values.SelectMany(x => x.GetAllVariablesNames()));

        return _[$"ReadSchemaIndexes(reader{_[settables.Select(x => $", out var col{x}"), joinBy: ""]});"];
    }

    internal static IndentedInterpolatedStringHandler AppendParsingBody(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var required = model.RequiredSettables;

        var requiredComplex = model.RequiredSettables.Where(x => !x.IsPrimitive).ToList();
        var requiredPrimitives = model.RequiredSettables.Where(x => x.IsPrimitive)
            .Select(x => (displayString: x.Type.DisplayString, x.Name))
            .ToList();

        var optional = model.UsualSettables;

        Dictionary<Settable, Mental> complexMaps = model.Settables.Where(x => !x.IsPrimitive)
            .Select(x => (x, TraverseSettables(model.Inner![x.Type.DisplayString])))
            .ToDictionary(k => k.x, v => v.Item2);

        foreach (var map in complexMaps)
        {
            map.Value.ForWhomTheBellTolls = map.Key;
        }

        //requiredPrimitives.AddRange(
        //    complexMaps.Where(x => x.Key.Required)
        //    .SelectMany(x => x.Value.GetRequiredVariablesAndTheirTypes())
        //    .Select(x => (displayString: x.settable.Type.DisplayString, varName: x.variableName))
        //);

        return _.Scope[
            $$"""
            {{_[required.Length == 0
                ? _[$"var parsed = new {model.Type.Name}();"]
                : _[$$"""

                    var parsed = new {{model.Type.Name}}()
                    {
                        {{_.Scope.ForEach(requiredComplex, (_, x) => appendInner(_, "parsed", x, complexMaps[x]), joinBy: ",\n")}}{{_.Scope[requiredPrimitives.Count > 0 && requiredComplex.Count > 0 ? ",\n" : string.Empty]}}
                        {{_.If(requiredPrimitives.Count > 0)[_.Scope[
                            requiredPrimitives.Select(x => $"{x.Name} = ({x.displayString})reader[col{x.Name}]"),
                            joinBy: ",\n"
                        ]]
                        }}
                    };
                    """]
            ]}}
            
            {{_.Scope.ForEach(optional, (w, x) => x.Type.IsReference
                ? w[$"if(col{x.Name} != -1) parsed.{x.Name} = reader[col{x.Name}] as {x.Type.DisplayString};"]
                : w[$"if(col{x.Name} != -1 && reader[col{x.Name}] is {x.Type.DisplayString} p{x.Name}) parsed.{x.Name} = p{x.Name};"],
                joinBy: "\n")
            
            }}{{_.Scope.ForEach(requiredComplex, (_, x) => appendInnerOptionals(_, "parsed", x, complexMaps[x]))}}
            """
        ];

        IndentedInterpolatedStringHandler appendInner(IndentStackWriter _, string accessPrefix, Settable settablya, Mental mental, bool checkRequired = false)
        {
            var required = mental!.GetAllRequiredVariables().ToList();
            var pairs = mental.GetRequiredVariablesPrimitives();
            var complex = mental.GetRequiredVariablesComplex().ToList();

            var access = accessPrefix + "." + settablya.Name;

            if(!checkRequired)
            {
                return _[$$"""
                {{_[settablya.Name]}} = new {{_[settablya.Type.DisplayString]}}()
                {
                    {{_.Scope.ForEach(pairs, (_, x) => _[$"{x.settable.Name} = ({x.settable.Type.DisplayString})reader[col{x.variableName}]"], joinBy: ",\n")}}
                    {{_[complex.Count > 0 ? ",\n" : string.Empty]}}
                    {{_.Scope.ForEach(complex, (_, x) => appendInner(_, access, x.settable, x.mental, false))}}
                }
                """];
            }

            // TODO: if a property doesn't have required settables, then it instance needs to be created if one of its optional settable exists in reader instance
            //var zeroRequiredWithOptionalsComplex = mental.GetZeroRequiredComplexWithOptionals().ToList();
            //var zeroOptionals = zeroRequiredWithOptionalsComplex.SelectMany(x => x.instances.Select(instance => instance.variableName)).ToList();

            return _.Scope[$$"""
                if({{_[required.Select(x => $"col{x} != -1"), joinBy: " && "]}})
                {
                    {{_[access]}} = new {{_[settablya.Type.DisplayString]}}()
                    {
                        {{_.Scope.ForEach(pairs, (_, x) => _[$"{x.settable.Name} = ({x.settable.Type.DisplayString})reader[col{x.variableName}]"], joinBy: ",\n")
                        
                        }}{{_[complex.Count > 0 ? ",\n" : string.Empty]
                        
                        }}{{
                            _.Scope.ForEach(complex, (_, x) => appendInner(_, access, x.settable, x.mental, false))
                        }}
                    };{{appendInnerOptionals(_, accessPrefix, settablya, mental)}}
                }
                """
            ];
        }

        IndentedInterpolatedStringHandler appendInnerOptionals(IndentStackWriter _, string accessPrefix, Settable settablya, Mental mental)
        {
            var access = accessPrefix + "." + settablya.Name;

            var optional = mental.GetOptionalVariablesPrimitives().ToList();

            if (optional.Count > 0)
            {
                var skip = _.Scope[$$"""

                    {{_.Scope.ForEach(optional, (w, x) => x.settable.Type.IsReference
                        ? w[$"if(col{x.variableName} != -1) {access}.{x.settable.Name} = reader[col{x.settable.Name}] as {x.settable.Type.DisplayString};"]
                        : w[$"if(col{x.variableName} != -1 && reader[col{x.variableName}] is {x.settable.Type.DisplayString} p{x.variableName}) {access}.{x.settable.Name} = p{x.variableName};"],
                        joinBy: "\n")}}

                    """
                ];
            }

            var optionalComplex = mental.GetOptionalVariablesComplex().ToList();

            foreach (var inner in optionalComplex)
            {
                appendInner(_, access, inner.settable, inner.mental, checkRequired: true);
            }

            return default;
        }
    }

    internal static SortedDictionary<int, List<(string name, string settableName)>> GetNamesToMatch(MatchingModel model, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return [];

        var settables = model.Settables;
        var type = model.Type;
        var matchCases = model.MatchingSettings.MatchCase;

        var namesToMatch = new SortedDictionary<int, List<(string name, string settableName)>>();
        var names = new HashSet<string>();

        foreach (var settable in settables)
        {
            if (token.IsCancellationRequested) return [];

            if (!settable.FieldSource.TryGetFields(out var fieldSources))
            {
                continue;
            }

            var settableName = settable.Name;

            var sourcesAndVariables = fieldSources.Select(x => (variable: settableName, fieldSource: x));

            if(!settable.IsPrimitive)
            {
                sourcesAndVariables = [];

                foreach (var field in fieldSources)
                {
                    var mental = TraverseSettables(model.Inner![settable.Type.DisplayString], field, settable.Name + "_");
                    var r = mental.GetVariablesAndTheirSources()
                        .SelectMany(x =>
                            x.sources.Select(s =>
                                (x.variableName, s)
                            )
                        );

                    sourcesAndVariables = sourcesAndVariables.Concat(r);
                }
            }

            foreach (var (variableName, fieldSoruce) in sourcesAndVariables)
            {
                IEnumerable<string> cases;

                cases = matchCases.ToAllCasesForCompare(fieldSoruce);

                foreach (var fieldSourcInCase in cases)
                {
                    names.Add(fieldSoruce);
                }

                foreach (var nameCase in names)
                {
                    if (!namesToMatch.TryGetValue(nameCase.Length, out var sameLength))
                    {
                        namesToMatch[nameCase.Length] = sameLength = [];
                    }

                    sameLength.Add((nameCase, variableName));
                }

                names.Clear();
            }
            
            //names.Clear();
        }

        foreach (var sameLengthNames in namesToMatch)
        {
            sameLengthNames.Value.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));
        }

        return namesToMatch;
    }

    internal static Mental TraverseSettables(MatchingModel model, string prefixSettableName = "", string prefixVarName = "")
    {
        var mental = new Mental() { Orig = model };

        foreach (var settable in model.Settables)
        {
            var ff = settable.FieldSource.TryGetFields(out var fields)
                ? fields
                : [settable.Name];

            if (settable.IsPrimitive)
            {
                var varName = prefixVarName + settable.Name;

                foreach (var field in ff)
                {
                    mental.Add(settable, varName, prefixSettableName + field);
                }
            }
            else
            {
                foreach (var field in ff)
                {
                    var inner = TraverseSettables(model.Inner![settable.Type.DisplayString], prefixSettableName + field, prefixVarName + settable.Name + "_");

                    inner.ForWhomTheBellTolls = settable;
                    
                    mental.Inner.Add((inner, settable.Required));
                }
            }
        }

        return mental;
    }

    internal sealed class Mental
    {
        public Settable? ForWhomTheBellTolls { get; set; }

        public List<(Mental inner, bool required)> Inner { get; set; } = [];

        public MatchingModel Orig { get; set; }

        public Dictionary<Settable, (string variableName, List<string> sources)> RequiredColumnSourcesFixed { get; set; } = [];
        public Dictionary<Settable, (string variableName, List<string> sources)> OtherColumnSourcesFixed { get; set; } = [];

        public void Add(Settable settable, string variableName, string source)
        {
            var dest = settable.Required
                ? RequiredColumnSourcesFixed
                : OtherColumnSourcesFixed;

            if (!dest.TryGetValue(settable, out var finded))
            {
                dest[settable] = finded = (variableName, []);
            }

            finded.sources.Add(source);
        }

        public IEnumerable<(Mental inner, Settable Value, List<(Settable settable, string variableName)> instances)> GetZeroRequiredComplexWithOptionals()
        {
            var result = Inner
                .Where(x => !x.required && x.inner.RequiredColumnSourcesFixed.Count == 0 && x.inner.OtherColumnSourcesFixed.Count(x => x.Key.IsPrimitive) > 0)
                .Select(x => (x.inner, x.inner.ForWhomTheBellTolls!.Value, instances: x.inner.OtherColumnSourcesFixed.Where(x => x.Key.IsPrimitive).Select(x => (settable: x.Key, (ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + "_" + x.Value.variableName)).ToList()));

            foreach(var inner in Inner)
            {
                result = result.Concat(inner.inner.GetZeroRequiredComplexWithOptionals());
            }

            return result;
        }

        public IEnumerable<(Settable settable, string variableName)> GetRequiredVariablesPrimitives()
        {
            var result = RequiredColumnSourcesFixed.Where(x => x.Key.IsPrimitive)
                .Select(x => (x.Key, (ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + x.Value.variableName));

            return result;
        }

        public IEnumerable<(Settable settable, Mental mental)> GetRequiredVariablesComplex()
        {
            var result = Inner.Where(x => x.required)
                .Select(x => (x.inner.ForWhomTheBellTolls!.Value, x.inner));

            return result;
        }

        public IEnumerable<(Settable settable, string variableName)> GetOptionalVariablesPrimitives()
        {
            var result = OtherColumnSourcesFixed.Where(x => x.Key.IsPrimitive)
                .Select(x => (x.Key, (ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + x.Value.variableName));

            return result;
        }

        public IEnumerable<(Settable settable, Mental mental)> GetOptionalVariablesComplex()
        {
            var result = Inner.Where(x => !x.required)
                .Select(x => (x.inner.ForWhomTheBellTolls!.Value, x.inner));

            return result;
        }

        public IEnumerable<(Settable settable, string variableName, List<string> sources)> GetRequiredVariablesAndTheirTypes()
        {
            var result = RequiredColumnSourcesFixed.Select(x => (x.Key, (ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + x.Value.variableName, x.Value.sources));

            foreach (var inner in Inner.Select(x => x.inner))
            {
                result = result.Concat(inner.GetRequiredVariablesAndTheirTypes());
            }

            return result;
        }

        public IEnumerable<(Settable settable, List<string> sources)> GetVariablesAndTheirTypes()
        {
            var result = RequiredColumnSourcesFixed.Select(x => (x.Key, x.Value.sources))
                    .Concat(OtherColumnSourcesFixed.Select(x => (x.Key, x.Value.sources)));

            foreach (var inner in Inner.Select(x => x.inner))
            {
                result = result.Concat(inner.GetVariablesAndTheirTypes());
            }

            return result;
        }

        public IEnumerable<(string variableName, List<string> sources)> GetVariablesAndTheirSources()
        {
            var result = RequiredColumnSourcesFixed.Select(x => ((ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + x.Value.variableName, x.Value.sources))
                    .Concat(OtherColumnSourcesFixed.Select(x => ((ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + x.Value.variableName, x.Value.sources)));

            foreach (var inner in Inner.Select(x => x.inner))
            {
                result = result.Concat(inner.GetVariablesAndTheirSources());
            }

            return result;
        }

        public IEnumerable<string> GetAllRequiredVariables()
        {
            IEnumerable<string> anchor = RequiredColumnSourcesFixed.Select(x => (ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + x.Value.variableName);

            foreach (var inner in Inner.Where(x => x.required).Select(x => x.inner))
            {
                anchor = anchor.Concat(inner.GetAllRequiredVariables());
            }

            return anchor;
        }

        public IEnumerable<string> GetAllVariablesNames()
        {
            var others = OtherColumnSourcesFixed.Select(x => (ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + x.Value.variableName);
            var anchor = RequiredColumnSourcesFixed
                    .Select(x => (ForWhomTheBellTolls != null ? ForWhomTheBellTolls.Value.Name + "_" : "") + x.Value.variableName)
                    .Concat(others);

            foreach (var inner in Inner.Select(x => x.inner))
            {
                anchor = anchor.Concat(inner.GetAllVariablesNames().Select(x => inner.ForWhomTheBellTolls!.Value.Name + "_" + x));
            }

            return anchor;
        }
    }
}

internal readonly struct AegisAttributeParse(AttributeData source)
{
    private readonly AttributeData _source = source;

    public int? MatchCasePropertyValue => FindNamedArg(AegisAttributeGenerator.MatchCaseProperty) switch
    {
        TypedConstant caseValue
            when caseValue.Kind != TypedConstantKind.Error => caseValue.Value as int?,

        TypedConstant => null,
        _ => AegisAttributeGenerator.DefaultCase.value
    };

    public string? ClassName => FindNamedArg(AegisAttributeGenerator.ClassNameProperty) switch
    {
        TypedConstant className
            when className.Kind != TypedConstantKind.Error => className.Value as string,

        _ => default
    };

    private TypedConstant? FindNamedArg(string parameter)
    {
        for (int i = 0; i != _source.NamedArguments.Length; i++)
        {
            var argument = _source.NamedArguments[i];

            if(argument.Key == parameter)
            {
                return argument.Value;
            }
        }

        return default;
    }
}

internal static class FieldSourceAttributeParse
{
    public static FieldsOrOrder? ParseToFieldSource(this AttributeData? source)
    {
        if(source is null)
        {
            return default;
        }

        var arguments = source.ConstructorArguments;

        if (arguments.Length != 1)
            return default;

        var argument = source.ConstructorArguments[0];

        var type = argument.Type?.ToDisplayString();

        return type switch
        {
            "int" when argument.Value is int fieldOrder => new(fieldOrder),
            
            "string[]" when argument.Values
                .Where(x => !x.IsNull)
                .Select(x => (string)x.Value!)
                .ToList() is { Count: > 0 } fields => new(fields),

            _ => default
        };
    }
}

internal readonly struct MatchingModel
{
    public readonly ImmutableArray<Settable> RequiredSettables;
    public readonly ImmutableArray<Settable> UsualSettables;
    public readonly ImmutableArray<Settable> Settables;

    public readonly TypeSnapshot Type;
    public readonly MatchingSettings MatchingSettings;

    public readonly Dictionary<string, MatchingModel>? Inner = default;

    public MatchingModel(TypeSnapshot type, Settable[] settables, MatchingSettings matchingSettings, Dictionary<string, MatchingModel>? inner = null)
    {
        Type = type;
        MatchingSettings = matchingSettings;

        Array.Sort(settables, _comparer);

        var requiredCount = settables.TakeWhile(x => x.Required).Count();

        var requiredSettables = ImmutableArray.Create(settables.AsSpan(0, requiredCount));
        var otherSettables = ImmutableArray.Create(settables.AsSpan(requiredCount));

        Settables = ImmutableArray.Create(settables);
        RequiredSettables = requiredSettables;
        UsualSettables = otherSettables;
        Inner = inner;
    }

    private static readonly Comparer<Settable> _comparer = Comparer<Settable>.Create(static (x, y) => (x, y) switch
    {
        ({ Required: true }, { Required: false }) => -1,
        ({ Required: false }, { Required: true }) => 1,
        _ => y.DeclarationOrder - x.DeclarationOrder
    });
}
internal readonly struct ParserStaticMethod
{
    public readonly string CallId;
    public readonly string TargetType;

    public ParserStaticMethod(string callId, string targetType)
        => (CallId, TargetType) = (callId, targetType);
}


internal readonly struct Settable
{
    public readonly TypeSnapshot Type;
    public readonly FieldsOrOrder FieldSource;
    public readonly string Name;
    public readonly int DeclarationOrder;
    public readonly bool Required;

    public bool IsPrimitive => Type.IsPrimitive;

    public Settable(TypeSnapshot type, string name, FieldsOrOrder fieldSource, bool required, int declarationOrder)
        => (Name, Type, FieldSource, DeclarationOrder, Required)
        = (name, type, fieldSource, declarationOrder, required);
}

internal readonly record struct TypeSnapshot(string Name, string DisplayString, bool IsReference, bool IsPrimitive, NamespaceSnapshot Namespace);
//{
    //public readonly NamespaceSnapshot Namespace;
    //public readonly string Name;
    //public readonly string DisplayString;
    //public readonly bool IsReference;
    //public readonly bool IsPrimitive;

    //public TypeSnapshot(string name, string displayString, bool isReference, bool isPrimitive, NamespaceSnapshot namespaceShapshot)
    //    => (Name, DisplayString, IsReference, IsPrimitive, Namespace)
    //    = (name, displayString, isReference, isPrimitive, namespaceShapshot);
//}

internal readonly struct NamespaceSnapshot
{
    public readonly string Name;
    public readonly string DisplayString;
    public readonly bool IsGlobal;

    public NamespaceSnapshot(string name, string display, bool isGlobal)
        => (Name, DisplayString, IsGlobal) = (name, display, isGlobal);
}

internal readonly struct GenerationSettings
{
    public readonly string ClassName;
}

internal readonly struct MatchingSettings
{
    public readonly MatchCase MatchCase;

    public MatchingSettings(MatchCase matchCase)
        => (MatchCase) = (matchCase);
}

internal readonly struct FieldsOrOrder
{
    private const int FIELDS = 1;
    private const int ORDER = 2;

    private readonly IEnumerable<string> _fields = default!;
    private readonly int _order;
    private readonly int _state;

    public FieldsOrOrder(IEnumerable<string> fields)
    {
        _fields = fields;
        _state = FIELDS;
    }

    public FieldsOrOrder(int order)
    {
        _order = order;
        _state = ORDER;
    }

    public bool IsFields => _state == FIELDS;
    public bool IsOrder => _state == ORDER;

    public IEnumerable<string> Fields => _state switch
    {
        FIELDS => _fields,
        _ => throw new StateMismatchinException(FIELDS, _state)
    };

    public int Order => _state switch
    {
        ORDER => _order,
        _ => throw new StateMismatchinException(ORDER, _state)
    };

    public bool TryGetFields([NotNullWhen(true)] out IEnumerable<string> fiels)
    {
        var (match, result) = _state switch
        {
            FIELDS => (true, _fields),
            ORDER => (false, null!),
            _ => throw new Exception($"Invalid state: {_state}, allowed only: {Order} or {Fields}"),
        };

        fiels = result;

        return match;
    }

    public bool TryGetOrder([NotNullWhen(true)] out int order)
    {
        var (match, result) = _state switch
        {
            FIELDS => (false, default),
            ORDER => (true, _order),
            _ => throw new Exception($"Invalid state: {_state}, allowed only: {Order} or {Fields}"),
        };

        order = result;

        return match;
    }

    public static implicit operator FieldsOrOrder(string[] fields) => new(fields);
    public static implicit operator FieldsOrOrder(List<string> fields) => new(fields);
    public static implicit operator FieldsOrOrder(ImmutableArray<string> fields) => new(fields);

    public static implicit operator FieldsOrOrder(int order) => new(order);
}

[Serializable]
public class StateMismatchinException(int expected, int actual)
    : Exception($"The union state expected to be {expected} but in fact {actual}")
{
}
