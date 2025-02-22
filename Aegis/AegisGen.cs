using Aegis.IndentWriter;
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
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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

        static (INamedTypeSymbol? symbol, AegisAttributeParse parser) transform(GeneratorAttributeSyntaxContext gen, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var src = AegisAttributeGenerator.MarkerAttributeSourceCode;
            var attributeInstance = gen.Attributes[0];

            var parser = new AegisAttributeParse(attributeInstance);
            var symbol = gen.TargetSymbol as INamedTypeSymbol;

            return (symbol, parser);
        }
    }

    private static void GenerateDataReaderParsers(SourceProductionContext productionContext, ImmutableArray<(INamedTypeSymbol? symbol, AegisAttributeParse parser)> types)
    {
        var token = productionContext.CancellationToken;

        if (token.IsCancellationRequested)
            return;

        var sourceCode = new StringBuilder();

        foreach (var (type, parser) in types)
        {
            if (token.IsCancellationRequested) return;

            if (type == null) continue;

            if (parser.MatchCasePropertyValue is not int matchCase ||
                !MatchCaseGenerator.IsValid(matchCase))
            {
                continue;
            }

            var _ = new IndentStackWriter(sourceCode);
            
            var typeNamespace = type.ContainingNamespace is { IsGlobalNamespace: false } ? type.ContainingNamespace : null;

            var ignore = _[$$"""
                using System;
                using System.Data;
                using System.Data.Common;
                using System.Collections.Generic;
                using System.Collections.ObjectModel;
                using System.Runtime.CompilerServices;

                {{_[
                    typeNamespace == null
                    ? AppendClass(_, type, matchCase, token)
                    : _[$$""" 
                        namespace {{typeNamespace}}
                        {
                            {{_.Scope[AppendClass(_, type, matchCase, token)]}}
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
                ? $"{typeNamespace}.{type.Name}AegisAgent.g.cs"
                : $"{type.Name}AegisAgent.g.cs";

            productionContext.AddSource(fileName, sourceCodeText);
        }
    }

    internal static IndentedInterpolatedStringHandler AppendClass(IndentStackWriter _, ITypeSymbol type, int matchCases, CancellationToken token = default)
    {
        return _[$$"""
            public sealed partial class {{type.Name}}AegisAgent
            {
                {{_.Scope[AppendReadList(_, type)]}}

                {{_.Scope[AppendReadSchemaIndexes(_, type)]}}

                {{_.Scope[AppendReadSchemaColumnIndex(_, type, matchCases)]}}
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadList(IndentStackWriter writer, ITypeSymbol type, CancellationToken token = default)
    {
        var properties = type.GetSettableProperties().ToArray();

        return writer[
            $$"""
            internal static List<{{type.Name}}> ReadList<TReader>(TReader reader)
                where TReader : IDataReader
            {
                var result = new List<{{type.Name}}>();
            
                ReadSchemaIndexes(reader{{properties.Select(x => $", out var col{x.Name}")}});
            
                while(reader.Read())
                {
                    var parsed = new {{type.Name}}();
            
                    {{writer.Scope.ForEach(properties, (w, x) => x.Type.IsReferenceType
                        ? w[$"if(col{x.Name} != -1) parsed.{x.Name} = reader[col{x.Name}] as {x.Type.ToDisplayString()};"]
                        : w[$"if(col{x.Name} != -1 && reader[col{x.Name}] is {x.Type.ToDisplayString()} p{x.Name}) parsed.{x.Name} = p{x.Name};"],
                        joinBy: "\n")}}

                    result.Add(parsed);
                }
            
                return result;
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadSchemaIndexes(IndentStackWriter writer, ITypeSymbol type, CancellationToken token = default)
    {
        var properties = type.GetSettableProperties().ToArray();

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

    internal static IndentedInterpolatedStringHandler AppendReadSchemaColumnIndex(IndentStackWriter writer, ITypeSymbol type, int matchCases, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return default;

        var properties = type.GetSettableProperties().ToArray();

        var namesToMatch = new SortedDictionary<int, List<(string name, IPropertySymbol property)>>();
        var names = new List<string>();

        foreach (var property in properties)
        {
            if (token.IsCancellationRequested) return default;

            if (MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.IgnoreCase))
            {
                var lowerCase =
                    MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.MatchOriginal) ||
                    MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.Camel) ||
                    MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.Pascal)
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

                var snake = MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.Snake)
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

            if (MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.MatchOriginal))
            {
                var original = property.Name;
                names.Add(original);
            }

            if (MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.Snake))
            {
                var snake = MatchCaseGenerator.ToSnakeCase(property.Name);
                if (!names.Contains(snake)) names.Add(snake);
            }

            if (MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.Camel))
            {
                var camel = MatchCaseGenerator.ToCamelCase(property.Name);
                if (!names.Contains(camel)) names.Add(camel);
            }

            if (MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.Pascal))
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

        return writer[
            $$"""
            internal static void ReadSchemaColumnIndex(string c, int i{{properties.Select(x => $", ref int col{x.Name}")}})
            {
                switch(c.Length)
                {
                    {{writer.Scope.ForEach(namesToMatch, (_, n) => _[$$"""
                    case {{n.Key}}:
                        {{writer.Scope.ForEach(n.Value, (_, x) => _[$$"""
                            {{(MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.IgnoreCase)
                        ? $"if(col{x.property.Name} == -1 && string.Equals(c, \"{x.name}\", StringComparison.OrdinalIgnoreCase))"
                        : $"if(col{x.property.Name} == -1 && c == \"{x.name}\")")}}
                            {
                                col{{x.property.Name}} = i;
                                return;
                            }
                            """])}}
                        break;
                    """])}}
                }
            }
            """];
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

internal static class AegisAttributeGenerator
{
    public const string AttributeName = "AegisAgentAttribute";
    public const string Namespace = "Aegis";
    public const string AttributeFullName = $"{Namespace}.{AttributeName}";

    public const string ClassNameProperty = "ClassName";
    public const string MatchCaseProperty = "Case";

    public static readonly (string name, int value) DefaultCase = (MatchCaseGenerator.AllName, MatchCaseGenerator.All);

    public static readonly string MarkerAttributeSourceCode =
@$"using System;

namespace {Namespace}
{{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class {AttributeName} : Attribute
    {{
        public string? {ClassNameProperty} {{ get; set; }}
        public {MatchCaseGenerator.EnumName} {MatchCaseProperty} {{ get; set; }} = {MatchCaseGenerator.EnumName}.{DefaultCase.name};
    }}

    {MatchCaseGenerator.MatchCaseEnum}
}}
";
}

internal static class MatchCaseGenerator
{
    public const string EnumName = "MatchCase";
    public const string Namespace = AegisAttributeGenerator.Namespace;

    public const int MatchOriginal = 1;
    public const int IgnoreCase = 1 << 1;
    public const int Snake = 1 << 2;
    public const int Camel = 1 << 3;
    public const int Pascal = 1 << 4;
    public const int ApplyOnOverwritten = 1 << 5;
    public const int All = MatchOriginal | IgnoreCase | Snake | Camel | Pascal | ApplyOnOverwritten;

    public const int CamelAndPascal = Camel | Pascal;

    public const string MatchOriginalName = nameof(MatchOriginal);
    public const string IgnoreCaseName = nameof(IgnoreCase);
    public const string SnakeName = nameof(Snake);
    public const string CamalName = nameof(Camel);
    public const string PascalName = nameof(Pascal);
    public const string ApplyOnOverritenName = nameof(ApplyOnOverwritten);
    public const string AllName = nameof(All);

    public static readonly string MatchCaseEnum =
@$"[Flags]
    public enum {EnumName} : int
    {{
        {MatchOriginalName} = {MatchOriginal},
        {IgnoreCaseName} = 1 << {(int)Math.Log(IgnoreCase, 2)},
        {SnakeName} = 1 << {(int)Math.Log(Snake, 2)},
        {CamalName} = 1 << {(int)Math.Log(Camel, 2)},
        {PascalName} = 1 << {(int)Math.Log(Pascal, 2)},
        {ApplyOnOverritenName} = 1 << {(int)Math.Log(ApplyOnOverwritten, 2)},
        {AllName} = {MatchOriginalName} | {IgnoreCaseName} | {SnakeName} | {CamalName} | {PascalName} | {ApplyOnOverritenName}
    }}
";

    public static bool HasFlag(int value, int flag)
        => (value & flag) == flag;

    public static bool IsValid(int caseValue)
        => caseValue >= 0 && caseValue <= All;

    public static int GetValueByName(string caseName) => caseName switch
    {
        IgnoreCaseName => IgnoreCase,
        SnakeName => Snake,
        CamalName => Camel,
        PascalName => Pascal,
        ApplyOnOverritenName => ApplyOnOverwritten,
        AllName => All,
        _ => -1,
    };

    public static readonly Regex _snake = new("([a-z])([A-Z])", RegexOptions.Compiled);

    public static string ToSnakeCase(string value)
    {
        var snakeCase = _snake.Replace(value, "$1_$2").ToLower();
        return snakeCase;
    }

    public static string ToPascalCase(string value)
    {
        var length = 0;

        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                length += 1;
            }
        }

        Span<char> span = length <= 1024 ? stackalloc char[length] : new char[length];

        int writeIndex = 0;
        bool newWord = true;

        for (int i = 0; i < value.Length; i++)
        {
            var symbol = value[i];

            if (!char.IsLetterOrDigit(symbol))
            {
                newWord = true;
                continue;
            }

            if(newWord)
            {
                symbol = char.ToUpper(symbol);
                newWord = false;
            }
            else
            {
                symbol = char.ToLower(symbol);
            }

            span[writeIndex++] = symbol;
        }

        var word = span.ToString();

        return word;
    }

    public static string ToCamelCase(string value)
    {
        var length = 0;

        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                length += 1;
            }
        }

        Span<char> span = length <= 1024 ? stackalloc char[length] : new char[length];

        int writeIndex = 0;
        bool newWord = false;

        for (int i = 0; i < value.Length; i++)
        {
            var symbol = value[i];

            if (!char.IsLetterOrDigit(symbol))
            {
                newWord = true;
                continue;
            }

            if (newWord)
            {
                symbol = char.ToUpper(symbol);
                newWord = false;
            }
            else
            {
                symbol = char.ToLower(symbol);
            }

            span[writeIndex++] = symbol;
        }

        var word = span.ToString();

        return word;
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

internal readonly struct SettableProperties(ImmutableArray<ISymbol> properties, CancellationToken token = default)
{
    private readonly ImmutableArray<ISymbol> _properties = properties;
    private readonly CancellationToken _token = token;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new(_properties, _token);

    public int Count()
    {
        var count = 0;

        for (int i = 0; i != _properties.Length && !_token.IsCancellationRequested; i++)
            count += _properties[i].IsSettableProperty(out _) ? 1 : 0;

        return count;
    }

    public IPropertySymbol[] ToArray()
    {
        var count = Count();

        var properties = new IPropertySymbol[count];

        var writeIndex = -1;
        
        for(int i = 0; i != _properties.Length && !_token.IsCancellationRequested; i++)
        {
            if (_properties[i].IsSettableProperty(out var settableProperty))
                properties[++writeIndex] = settableProperty;
        }

        return properties;
    }

    public struct Enumerator(ImmutableArray<ISymbol> source, CancellationToken token)
    {
        private int _index = -1;

        private ImmutableArray<ISymbol> _source = source;

        private readonly CancellationToken _token = token;

        public IPropertySymbol Current { get; private set; } = null!;

        [MemberNotNullWhen(true, nameof(Current))]
        public bool MoveNext()
        {
            Current = null!;

            if (_token.IsCancellationRequested || _index >= _source.Length)
            {
                return false;
            }

            while(++_index < _source.Length && !_token.IsCancellationRequested)
            {
                var member = _source[_index];
                if (member.IsSettableProperty(out var property))
                {
                    Current = property;
                    return true;
                }
            }

            return false;
        }
    }
}

internal static class SymbolExtensions
{
    public static SettableProperties GetSettableProperties(this ITypeSymbol type, CancellationToken token = default)
        => new(type.GetMembers(), token);

    public static SettableProperties GetSettableProperties(this INamedTypeSymbol type, CancellationToken token = default)
        => new(type.GetMembers(), token);

    public static bool IsSettableProperty(this ISymbol member, [NotNullWhen(true)] out IPropertySymbol? settableProperty)
    {
        (bool result, settableProperty) = member switch
        {
            IPropertySymbol property when !property.IsReadOnly => (true, property),
            _ => (false, null)
        };

        return result;
    }
}