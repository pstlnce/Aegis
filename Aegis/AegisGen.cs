using Aegis.IndentWriter;
using Aegis.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        var dbParseTargets = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: AegisAttributeGenerator.AttributeFullName,
            predicate: predicate,
            transform: transform
        );

        var dbParseCollected = dbParseTargets.Collect();

        context.RegisterSourceOutput(dbParseCollected, GenerateDataReaderParsers);

        static bool predicate(SyntaxNode syntaxNode, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            return syntaxNode switch
            {
                StructDeclarationSyntax structDeclare => true,
                ClassDeclarationSyntax classDecl =>
                    !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword) &&
                    !classDecl.Modifiers.Any(SyntaxKind.StaticKeyword),
                _ => false,
            };
        }

        static MatchingModel? transform(GeneratorAttributeSyntaxContext gen, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var src = AegisAttributeGenerator.MarkerAttributeSourceCode;
            var attributeInstance = gen.Attributes[0];

            var parser = new AegisAttributeParse(attributeInstance);
            var symbol = gen.TargetSymbol as INamedTypeSymbol;

            if(gen.TargetSymbol is not INamedTypeSymbol target)
            {
                return default;
            }

            var settables = target
                .GetMembers()
                .Where(static member => member switch
                {
                    IPropertySymbol property
                        => property.SetMethod is { DeclaredAccessibility: Accessibility.Public or Accessibility.Internal },

                    IFieldSymbol field
                        => !field.IsConst
                        && !field.IsStatic
                        && !field.IsReadOnly
                        && field.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal,

                    _ => false
                })
                .Select(static (member, i) =>
                {
                    var field = member as IFieldSymbol;

                    var (type, attributes, isRequired) = member is IPropertySymbol property
                        ? (property.Type, property.GetAttributes(), property.IsRequired)
                        : (field!.Type, field!.GetAttributes(), field!.IsRequired);

                    var sourcedAttribute = attributes.FirstOrDefault(attribute =>
                        attribute.AttributeClass?.ContainingNamespace.Name == AegisAttributeGenerator.Namespace
                        && attribute.AttributeClass.Name == FieldSourceAttrubteGenerator.AttributeName
                    );

                    var fieldSource = sourcedAttribute.ParseToFieldSource();

#if DEBUG
                    if(fieldSource?.IsOrder == true)
                    {

                    }
#endif

                    if(fieldSource?.TryGetOrder(out var order) == true && order < 0)
                    {
                        fieldSource = default;
                    }

                    var typeNamespace = type.ContainingNamespace;

                    var namespaceSnapshot = new NamespaceSnapshot(typeNamespace.Name, typeNamespace.ToDisplayString(), typeNamespace.IsGlobalNamespace);
                    var typeSnapshot = new TypeSnapshot(type.Name, type.ToDisplayString(), type.IsReferenceType, namespaceSnapshot);

                    return new Settable(typeSnapshot, member.Name, fieldSource ?? new([member.Name]), isRequired, i);
                })
                .ToArray();

            if(settables.Length == 0)
                return null;

            var targetNamespace = target.ContainingNamespace;

            var namespaceSnapshot = new NamespaceSnapshot(targetNamespace.Name, targetNamespace.ToDisplayString(), targetNamespace.IsGlobalNamespace);
            var typeSnapshot = new TypeSnapshot(target.Name, target.ToDisplayString(), target.IsReferenceType, namespaceSnapshot);
             
            var targetModel = new MatchingModel(
                type: typeSnapshot,
                settables: settables,
                matchingSettings: new MatchingSettings(parser.MatchCasePropertyValue == null ? MatchCase.None : (MatchCase)parser.MatchCasePropertyValue)
            );

            return targetModel;
        }
    }

    private static void GenerateDataReaderParsers(SourceProductionContext productionContext, ImmutableArray<MatchingModel?> models)
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

                ReadSchemaIndexes(reader{{settables.Select(x => $", out var col{x.Name}")}});
            
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

                ReadSchemaIndexes(reader{{properties.Select(x => $", out var col{x.Name}")}});
            
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
            
                ReadSchemaIndexes(reader{{settables.Select(x => $", out var col{x.Name}")}});
            
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

                ReadSchemaIndexes(reader{{settables.Select(x => $", out var col{x.Name}")}});
            
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
        var settables = model.Settables;
        var required = model.RequiredSettables;

        var type = model.Type;

        var alreadySettedIndexes = settables
            .Where(x => x.FieldSource.IsOrder)
            .Select(x => x.FieldSource.Order)
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
            internal static void ReadSchemaIndexes<TReader>(TReader reader{{settables.Select(x => $", out int column{x.Name}")}})
                where TReader : IDataReader
            {
                {{_.If(required.Length != 0)[$"ThrowIfNotEnoughFieldsForRequiredException({required.Length}, reader.FieldCount);\n"]
                
                }}
                {{_.Scope[settables.Select(x => x.FieldSource.IsOrder
                    ? $"column{x.Name} = {x.FieldSource.Order};"
                    : $"column{x.Name} = -1;"), joinBy: "\n"]}}

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
                    {{_.Scope[$"ReadSchemaColumnIndex(reader.GetName(i), i{settables.Select(x => $", ref column{x.Name}")})"]}};
                }
                {{_.Scope.If(required.Length != 0)[
                $$"""

                int missedCount =
                    {{_.Scope[required.Select(x => $"column{x.Name} == -1 ? 1 : 0"), joinBy: "+\n"]}};

                if(missedCount > 0)
                {
                    string[] missed = new string[missedCount];
                    int writed = 0;

                    {{_.Scope[required.Select(x => $"if(column{x.Name} == -1) missed[writed++] = \"{x.Name}\";"), joinBy: "\n"]}}

                    ThrowNoFieldSourceMatchedRequiredSettableException(missed);
                }

                """
                ]}}
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadSchemaColumnIndex(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var properties = model.Settables;
        var matchCases = model.MatchingSettings.MatchCase;

        var namesToMatch = GetNamesToMatch(model, token);

        return _.Scope[
            $$"""
            internal static void ReadSchemaColumnIndex(string c, int i{{properties.Select(x => $", ref int col{x.Name}")}})
            {
                switch(c.Length)
                {
                    {{_.Scope.ForEach(namesToMatch, (_, n) => _[$$"""
                    case {{n.Key}}:
                        {{_.Scope.ForEach(n.Value, (_, x) => _[$$"""
                            {{(matchCases.HasFlag(MatchCase.IgnoreCase)
                        ? $"if(col{x.property.Name} == -1 && string.Equals(c, \"{x.name}\", StringComparison.OrdinalIgnoreCase))"
                        : $"if(col{x.property.Name} == -1 && c == \"{x.name}\")")}}
                            {
                                col{{x.property.Name}} = i;
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

    internal static IndentedInterpolatedStringHandler AppendParsingBody(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var required = model.RequiredSettables;
        var optional = model.UsualSettables;

        return _.Scope[
            $$"""
            {{_[required.Length == 0
                ? _[$"var parsed = new {model.Type.Name}();"]
                : _[$$"""
                    {{_[required.Select(x => $"{x.Type.DisplayString} val{x.Name} = reader[col{x.Name}] as {x.Type.DisplayString};"), joinBy: "\n"]}}

                    var parsed = new {{model.Type.Name}}()
                    {
                        {{_.Scope.ForEach(required, (_, x) => _[$"{x.Name} = val{x.Name}"], joinBy: ",\n")}}
                    }; 
                    """]
            ]}}
            
            {{_.Scope.ForEach(optional, (w, x) => x.Type.IsReference
                ? w[$"if(col{x.Name} != -1) parsed.{x.Name} = reader[col{x.Name}] as {x.Type.DisplayString};"]
                : w[$"if(col{x.Name} != -1 && reader[col{x.Name}] is {x.Type.DisplayString} p{x.Name}) parsed.{x.Name} = p{x.Name};"],
                joinBy: "\n")}}
            """
        ];
    }

    internal static SortedDictionary<int, List<(string name, Settable property)>> GetNamesToMatch(MatchingModel model, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return [];

        var settables = model.Settables;
        var type = model.Type;
        var matchCases = model.MatchingSettings.MatchCase;

        var namesToMatch = new SortedDictionary<int, List<(string name, Settable property)>>();
        var names = new List<string>();

        foreach (var settable in settables)
        {
            if (token.IsCancellationRequested) return [];

            if(!settable.FieldSource.TryGetFields(out var fieldSources))
            {
                continue;
            }

            foreach (var fieldSoruce in fieldSources)
            {
                if (matchCases.HasFlag(MatchCase.IgnoreCase))
                {
                    var lowerCase =
                        matchCases.HasFlag(MatchCase.MatchOriginal) ||
                        matchCases.HasFlag(MatchCase.CamalCase) ||
                        matchCases.HasFlag(MatchCase.PascalCase)
                        ? fieldSoruce.ToLower()
                        : null;

                    if (lowerCase != null)
                    {
                        if (!namesToMatch.TryGetValue(lowerCase.Length, out var sameLength))
                        {
                            namesToMatch[lowerCase.Length] = sameLength = [];
                        }

                        sameLength.Add((lowerCase, settable));
                    }

                    var snake = matchCases.HasFlag(MatchCase.SnakeCase)
                        ? MatchCaseGenerator.ToSnakeCase(fieldSoruce)
                        : null;

                    if (snake != null && snake != lowerCase)
                    {
                        if (!namesToMatch.TryGetValue(snake.Length, out var sameLength))
                        {
                            namesToMatch[snake.Length] = sameLength = [];
                        }

                        sameLength.Add((snake, settable));
                    }

                    continue;
                }

                if (matchCases.HasFlag(MatchCase.MatchOriginal))
                {
                    var original = fieldSoruce;
                    names.Add(original);
                }

                if (matchCases.HasFlag(MatchCase.SnakeCase))
                {
                    var snake = MatchCaseGenerator.ToSnakeCase(fieldSoruce);
                    if (!names.Contains(snake)) names.Add(snake);
                }

                if (matchCases.HasFlag(MatchCase.CamalCase))
                {
                    var camel = MatchCaseGenerator.ToCamelCase(fieldSoruce);
                    if (!names.Contains(camel)) names.Add(camel);
                }

                if (matchCases.HasFlag(MatchCase.PascalCase))
                {
                    var pascal = MatchCaseGenerator.ToPascalCase(fieldSoruce);
                    if (!names.Contains(pascal)) names.Add(pascal);
                }

                foreach (var nameCase in names)
                {
                    if (!namesToMatch.TryGetValue(nameCase.Length, out var sameLength))
                    {
                        namesToMatch[nameCase.Length] = sameLength = [];
                    }

                    sameLength.Add((nameCase, settable));
                }
            }

            names.Clear();
        }

        foreach (var sameLengthNames in namesToMatch)
        {
            sameLengthNames.Value.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));
        }

        return namesToMatch;
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

    public MatchingModel(TypeSnapshot type, Settable[] settables, MatchingSettings matchingSettings)
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
    }

    private static readonly Comparer<Settable> _comparer = Comparer<Settable>.Create(static (x, y) => (x, y) switch
    {
        ({ Required: true }, { Required: false }) => -1,
        ({ Required: false }, { Required: true }) => 1,
        _ => y.DeclarationOrder - x.DeclarationOrder
    });
}

internal struct Settable
{
    public readonly TypeSnapshot Type;
    public readonly FieldsOrOrder FieldSource;
    public readonly string Name;
    public readonly int DeclarationOrder;
    public readonly bool Required;

    public Settable(TypeSnapshot type, string name, FieldsOrOrder fieldSource, bool required, int declarationOrder)
        => (Name, Type, FieldSource, DeclarationOrder, Required)
        = (name, type, fieldSource, declarationOrder, required);
}

internal readonly struct TypeSnapshot
{
    public readonly NamespaceSnapshot Namespace;
    public readonly string Name;
    public readonly string DisplayString;
    public readonly bool IsReference;

    public TypeSnapshot(string name, string displayString, bool isReference, NamespaceSnapshot namespaceShapshot)
        => (Name, DisplayString, IsReference, Namespace)
        = (name, displayString, isReference, namespaceShapshot);
}

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