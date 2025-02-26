﻿using Aegis.IndentWriter;
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
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Aegis;

internal sealed class AegisConverterAttribute : Attribute
{
    public Expression<Func<object, object>> Expression { get; private set; }

    public AegisConverterAttribute(Expression<Func<object, object>> expression)
    {
        Expression = expression;
    }
}

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


            ImmutableArray<SettableProperty> properties = target.GetMembers()
                .Where(static member => member is IPropertySymbol)
                .Select(static member => (IPropertySymbol)member)
                .Where(static property => !property.IsReadOnly)
                .Select((property, i) =>
                {
                    var fielSourcedAttribute = property.GetAttributes().FirstOrDefault(attribute =>
                            attribute.AttributeClass?.ContainingNamespace.Name == AegisAttributeGenerator.Namespace
                            && attribute.AttributeClass.Name == FieldSourceAttrubteGenerator.AttributeName);

                    if(fielSourcedAttribute != null)
                    {
                        var a = new FieldSourceAttributeParse(fielSourcedAttribute);
                        a.Debug();
                    }

                    var type = property.Type;
                    var typeNamespace = type.ContainingNamespace;

                    var namespaceSnapshot = new NamespaceSnapshot(typeNamespace.Name, typeNamespace.ToDisplayString(), typeNamespace.IsGlobalNamespace);
                    var typeSnapshot = new TypeSnapshot(type.Name, type.ToDisplayString(), type.IsReferenceType, namespaceSnapshot);

                    return new SettableProperty(property.Name, typeSnapshot, i, property.IsRequired);
                })
                .ToImmutableArray();

            var targetNamespace = target.ContainingNamespace;

            var namespaceSnapshot = new NamespaceSnapshot(targetNamespace.Name, targetNamespace.ToDisplayString(), targetNamespace.IsGlobalNamespace);
            var typeSnapshot = new TypeSnapshot(target.Name, target.ToDisplayString(), target.IsReferenceType, namespaceSnapshot);

            var targetModel = new MatchingModel(typeSnapshot, properties, new MatchingSettings(parser.MatchCasePropertyValue == null ? MatchCase.None : (MatchCase)parser.MatchCasePropertyValue));

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
                            {{_.Scope[AppendClass(_, model.Value, token)]}}
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
        return _[$$"""
            internal sealed partial class {{model.Type.Name}}Parser
            {
                {{_.Scope[AppendReadList(_, model)]}}

                {{_.Scope[AppendReadUnbuffered(_, model)]}}

                {{_.Scope[AppendReadListAsync(_, model, isValueTask: false, token)]}}

                {{_.Scope[AppendReadListAsync(_, model, isValueTask: true, token)]}}

                {{_.Scope[AppendReadUnbufferedAsync(_, model)]}}

                {{_.Scope[AppendReadSchemaIndexes(_, model)]}}

                {{_.Scope[AppendReadSchemaColumnIndex(_, model)]}}
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadUnbuffered(IndentStackWriter _, MatchingModel model, CancellationToken token = default)
    {
        var properties = model.Properties;
        var type = model.Type;

        return _[
            $$"""
            internal static IEnumerable<{{type.Name}}> ReadUnbuffered<TReader>(TReader reader)
                where TReader : IDataReader
            {
                if(!reader.Read())
                {
                    yield break;
                }

                ReadSchemaIndexes(reader{{properties.Select(x => $", out var col{x.Name}")}});
            
                do
                {
                    var parsed = new {{type.Name}}();
            
                    {{_.Scope.ForEach(properties, (w, x) => x.Type.IsReference
                        ? w[$"if(col{x.Name} != -1) parsed.{x.Name} = reader[col{x.Name}] as {x.Type.DisplayString};"]
                        : w[$"if(col{x.Name} != -1 && reader[col{x.Name}] is {x.Type.DisplayString} p{x.Name}) parsed.{x.Name} = p{x.Name};"],
                        joinBy: "\n")}}

                    yield return parsed;
                } while(reader.Read());
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadList(IndentStackWriter writer, MatchingModel model, CancellationToken token = default)
    {
        var properties = model.Properties;
        var type = model.Type;

        return writer[
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
                    var parsed = new {{type.Name}}();
            
                    {{writer.Scope.ForEach(properties, (w, x) => x.Type.IsReference
                        ? w[$"if(col{x.Name} != -1) parsed.{x.Name} = reader[col{x.Name}] as {x.Type.DisplayString};"]
                        : w[$"if(col{x.Name} != -1 && reader[col{x.Name}] is {x.Type.DisplayString} p{x.Name}) parsed.{x.Name} = p{x.Name};"],
                        joinBy: "\n")}}

                    result.Add(parsed);
                } while(reader.Read());
            
                return result;
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadListAsync(IndentStackWriter _, MatchingModel model, bool isValueTask, CancellationToken token = default)
    {
        var properties = model.Properties;
        var type = model.Type;

        return _[
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
            
                ReadSchemaIndexes(reader{{properties.Select(x => $", out var col{x.Name}")}});
            
                Task<bool> reading;

                while(true)
                {
                    var parsed = new {{type.Name}}();
            
                    {{_.Scope.ForEach(properties, (w, x) => x.Type.IsReference
                        ? w[$"if(col{x.Name} != -1) parsed.{x.Name} = reader[col{x.Name}] as {x.Type.DisplayString};"]
                        : w[$"if(col{x.Name} != -1 && reader[col{x.Name}] is {x.Type.DisplayString} p{x.Name}) parsed.{x.Name} = p{x.Name};"],
                        joinBy: "\n")}}

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
        var properties = model.Properties;
        var type = model.Type;

        return _[
            $$"""
            internal static async IAsyncEnumerable<{{type.Name}}> ReadUnbufferedAsync<TReader>(TReader reader, [EnumeratorCancellationAttribute] CancellationToken token = default)
                where TReader : DbDataReader
            {
                if(!(await reader.ReadAsync(token).ConfigureAwait(false)))
                {
                    yield break;
                }

                ReadSchemaIndexes(reader{{properties.Select(x => $", out var col{x.Name}")}});
            
                Task<bool> reading;

                while(true)
                {
                    var parsed = new {{type.Name}}();
            
                    {{_.Scope.ForEach(properties, (w, x) => x.Type.IsReference
                        ? w[$"if(col{x.Name} != -1) parsed.{x.Name} = reader[col{x.Name}] as {x.Type.DisplayString};"]
                        : w[$"if(col{x.Name} != -1 && reader[col{x.Name}] is {x.Type.DisplayString} p{x.Name}) parsed.{x.Name} = p{x.Name};"],
                        joinBy: "\n")}}

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

    internal static IndentedInterpolatedStringHandler AppendReadSchemaIndexes(IndentStackWriter writer, MatchingModel model, CancellationToken token = default)
    {
        var properties = model.Properties;
        var type = model.Type;

        return writer[
            $$"""
            internal static void ReadSchemaIndexes<TReader>(TReader reader{{properties.Select(x => $", out int column{x.Name}")}})
                where TReader : IDataReader
            {
                {{writer.Scope[properties.Select(x => $"column{x.Name} = -1;"), joinBy: "\n"]}}

                for(int i = 0; i != reader.FieldCount; i++)
                {
                    ReadSchemaColumnIndex(reader.GetName(i), i{{properties.Select(x => $", ref column{x.Name}")}});
                }
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadSchemaColumnIndex(IndentStackWriter writer, MatchingModel model, CancellationToken token = default)
    {
        var properties = model.Properties;
        var matchCases = model.MatchingSettings.MatchCase;

        var namesToMatch = GetNamesToMatch(model, token);

        return writer[
            $$"""
            internal static void ReadSchemaColumnIndex(string c, int i{{properties.Select(x => $", ref int col{x.Name}")}})
            {
                switch(c.Length)
                {
                    {{writer.Scope.ForEach(namesToMatch, (_, n) => _[$$"""
                    case {{n.Key}}:
                        {{writer.Scope.ForEach(n.Value, (_, x) => _[$$"""
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

    internal static new SortedDictionary<int, List<(string name, SettableProperty property)>> GetNamesToMatch(MatchingModel model, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return [];

        var properties = model.Properties;
        var type = model.Type;
        var matchCases = model.MatchingSettings.MatchCase;

        var namesToMatch = new SortedDictionary<int, List<(string name, SettableProperty property)>>();
        var names = new List<string>();

        foreach (var property in properties)
        {
            if (token.IsCancellationRequested) return [];

            if (matchCases.HasFlag(MatchCase.IgnoreCase))
            {
                var lowerCase =
                    matchCases.HasFlag(MatchCase.MatchOriginal) ||
                    matchCases.HasFlag(MatchCase.CamalCase) ||
                    matchCases.HasFlag(MatchCase.PascalCase)
                    ? property.Name.ToLower()
                    : null;

                if (lowerCase != null)
                {
                    if (!namesToMatch.TryGetValue(lowerCase.Length, out var sameLength))
                    {
                        namesToMatch[lowerCase.Length] = sameLength = [];
                    }

                    sameLength.Add((lowerCase, property));
                }

                var snake = matchCases.HasFlag(MatchCase.SnakeCase)
                    ? MatchCaseGenerator.ToSnakeCase(property.Name)
                    : null;

                if (snake != null && snake != lowerCase)
                {
                    if (!namesToMatch.TryGetValue(snake.Length, out var sameLength))
                    {
                        namesToMatch[snake.Length] = sameLength = [];
                    }

                    sameLength.Add((snake, property));
                }

                continue;
            }

            if (matchCases.HasFlag(MatchCase.MatchOriginal))
            {
                var original = property.Name;
                names.Add(original);
            }

            if (matchCases.HasFlag(MatchCase.SnakeCase))
            {
                var snake = MatchCaseGenerator.ToSnakeCase(property.Name);
                if (!names.Contains(snake)) names.Add(snake);
            }

            if (matchCases.HasFlag(MatchCase.CamalCase))
            {
                var camel = MatchCaseGenerator.ToCamelCase(property.Name);
                if (!names.Contains(camel)) names.Add(camel);
            }

            if (matchCases.HasFlag(MatchCase.PascalCase))
            {
                var pascal = MatchCaseGenerator.ToPascalCase(property.Name);
                if (!names.Contains(pascal)) names.Add(pascal);
            }

            foreach (var nameCase in names)
            {
                if (!namesToMatch.TryGetValue(nameCase.Length, out var sameLength))
                {
                    namesToMatch[nameCase.Length] = sameLength = [];
                }

                sameLength.Add((nameCase, property));
            }

            names.Clear();
        }

        foreach (var sameLengthNames in namesToMatch)
        {
            sameLengthNames.Value.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));
        }

        return namesToMatch;
    }

    internal static bool TryGetSettableProperty(ISymbol? member, [NotNullWhen(true)] out IPropertySymbol? settableProperty)
    {
        if (member is IPropertySymbol property &&
            property.SetMethod is { IsReadOnly: false })
        {
            settableProperty = property;
            return true;
        }

        settableProperty = null;
        return false;
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

internal readonly struct FieldSourceAttributeParse(AttributeData source)
{
    private readonly AttributeData _source = source;

    public void Debug()
    {
        for (int i = 0; i != _source.ConstructorArguments.Length; i++)
        {
            var argument = _source.ConstructorArguments[i];
        }
    }

    private TypedConstant? FindNamedArg(string parameter)
    {
        for (int i = 0; i != _source.NamedArguments.Length; i++)
        {
            var argument = _source.NamedArguments[i];

            if (argument.Key == parameter)
            {
                return argument.Value;
            }
        }

        return default;
    }
}

internal readonly struct MatchingModel
{
    public readonly TypeSnapshot Type;
    public readonly ImmutableArray<SettableProperty> Properties;
    public readonly MatchingSettings MatchingSettings;

    public MatchingModel(TypeSnapshot type, ImmutableArray<SettableProperty> properties, MatchingSettings matchingSettings)
        => (Type, Properties, MatchingSettings) = (type, properties, matchingSettings);
}

internal readonly struct SettableProperty
{
    public readonly TypeSnapshot Type;
    public readonly string Name;
    public readonly int DeclarationOrder;
    public readonly bool Required;

    public SettableProperty(string name, TypeSnapshot type, int declarationOrder, bool required)
        => (Name, Type, DeclarationOrder, Required)
        = (name, type, declarationOrder, required);
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