using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

[Generator]
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

            int indentLevel = 0;

            sourceCode
                .Append("using System;\n")
                .Append("using System.Data;\n")
                .Append("using System.Data.Common;\n")
                .Append("using System.Collections.Generic;\n")
                .Append("using System.Collections.ObjectModel;\n")
                .Append("using System.Runtime.CompilerServices;\n")
                .Append('\n');
            
            var typeNamespace = type.ContainingNamespace is { IsGlobalNamespace: false } ? type.ContainingNamespace : null;

            if (typeNamespace != null)
            {
                var namespaceString = typeNamespace.ToDisplayString();

                sourceCode.AppendIndented(indentLevel,
                    $$"""
                    namespace {{namespaceString}}
                    {
                    """);

                indentLevel += 1;
            }

            sourceCode.AppendIndented(indentLevel, 
                $$"""
                public sealed partial class {{type.Name}}AegisAgent
                {
                """);

            IEnumerable<Action> methods = [
                () => AppendReadList(sourceCode, type, indentLevel + 1, token),
                () => AppendReadSchemaIndexes(sourceCode, type, token),
                () => AppendReadSchemaColumnIndex(sourceCode, type, matchCase, token),
            ];

            var first = true;
            foreach (var method in methods)
            {
                if(first) first = false;
                else sourceCode.Append('\n');

                if (token.IsCancellationRequested) return;

                method();
            }

            sourceCode.AppendIndented(indentLevel,
                $$"""
                }
                """);

            if(typeNamespace != null)
            {
                sourceCode.AppendLine().Append('}');
            }

            if (token.IsCancellationRequested) return;

            var sourceCodeText = sourceCode.ToString();
            sourceCode.Clear();

            var fileName = typeNamespace != null
                ? $"{typeNamespace}.{type.Name}AegisAgent.g.cs"
                : $"{type.Name}AegisAgent.g.cs";

            productionContext.AddSource(fileName, sourceCodeText);
        }
    }

    internal static IndentInterpolatedStringHandler AppendReadList(StringBuilder sourceCode, ITypeSymbol type, int indent = 0, CancellationToken token = default)
    {
        var properties = type.GetSettableProperties().ToArray();

        return sourceCode.AppendIndented(indent,
            $$"""

            internal static List<{{type.Name}}> ReadList<TReader>(TReader reader)
                where TReader : IDataReader
            {
                var result = new List<{{type.Name}}>();
            
                ReadSchemaIndexes(reader{{properties.Select(x => $", out var col{x.Name}")}});

                while(reader.Read())
                {
                    var parsed = new {{type.Name}}();

                    {{properties.ToSyntax(x => x.Type.IsReferenceType
                        ? $"if(col{x.Name} != -1) parsed.{x.Name} = reader[col{x.Name}] as {x.Type.ToDisplayString()};\n"
                        : $"if(col{x.Name} != -1 && reader[col{x.Name}] is {x.Type.ToDisplayString()} p{x.Name}) parsed.{x.Name} = p{x.Name};\n")}}
                    result.Add(parsed);
                }

                return result;
            }
            """
        );
    }

    internal static void AppendReadSchemaIndexes(StringBuilder sourceCode, ITypeSymbol type, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;

        sourceCode.Append(@"
        internal static void ReadSchemaIndexes<TReader>(TReader reader");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(
                ", out int column{0}",
                property.Name
            );
        }

        sourceCode.Append(@")
            where TReader : IDataReader
        {");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;
            sourceCode.AppendFormat(@"
            column{0} = -1;",
            property.Name);
        }

        sourceCode.Append('\n');

        sourceCode.Append(@"
            for(int i = 0; i != reader.FieldCount; i++)
            {
                ReadSchemaColumnIndex(reader.GetName(i), i");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(
                ", ref column{0}",
                property.Name
            );
        }

        sourceCode.Append(@");
            }
        }");
    }

    internal static void AppendReadSchemaColumnIndex(StringBuilder sourceCode, ITypeSymbol type, int matchCases, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;

        var properties = type.GetSettableProperties().ToArray();

        var namesToMatch = new SortedDictionary<int, List<(string name, IPropertySymbol property)>>();
        var names = new List<string>();

        foreach (var property in properties)
        {
            if (token.IsCancellationRequested) return;

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

        sourceCode.AppendIndented(2,
            $$"""

            internal static void ReadSchemaColumnIndex(string c, int i{{properties.Select(x => $", ref int col{x.Name}")}})
            {
                switch(c.Length)
                {
                    {{namesToMatch.ToSyntax(n =>$$"""
                    case {{n.Key}}:
                        {{n.Value.ToSyntax(x => $$"""
                            {{(MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.IgnoreCase)
                        ? $"if(col{x.property.Name} == -1 && string.Equals(c, \"{x.name}\", StringComparison.OrdinalIgnoreCase))"
                        : $"if(col{x.property.Name} == -1 && c == \"{x.name}\")")}}
                            {
                                col{{x.property.Name}} = i;
                                return;
                            }
                            """)
                        }}
                        break;

                    """)}}
                }
            }
            """);
    }


    internal static void AppendReadSchemaColumnIndex2(StringBuilder sourceCode, ITypeSymbol type, int matchCases, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;

        sourceCode.Append(@"
        internal static void ReadSchemaColumnIndex(string c, int i");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(
                ", ref int col{0}",
                property.Name
            );
        }

        if (token.IsCancellationRequested) return;

        sourceCode.Append(@")
        {");

        var namesToMatch = new SortedDictionary<int, List<(string name, IPropertySymbol property)>>();
        var names = new List<string>();

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

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

        sourceCode.Append(@"
            switch(c.Length)
            {");

        foreach (var sameLengthNames in namesToMatch)
        {
            if (token.IsCancellationRequested) return;

            var length = sameLengthNames.Key;

            sourceCode.AppendFormat(@"
                case {0}:", length);

            bool writedFirst = false;

            sameLengthNames.Value.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));

            int i = 0;

            for(; i < sameLengthNames.Value.Count; i++)
            {
                var (name, property) = sameLengthNames.Value[i];

                if (writedFirst)
                {
                    sourceCode.Append('\n');
                }

                writedFirst = true;

                sourceCode.AppendFormat(@"
                    if(col{0} == -1 && string.Equals(c, ""{1}""", property.Name, name);

                if (MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.IgnoreCase))
                {
                    sourceCode.Append(", StringComparison.OrdinalIgnoreCase");
                }

                sourceCode.Append(')');

                sourceCode.Append(@")
                    {");

                sourceCode.AppendFormat(@"
                        col{0} = i;
                        return;", property.Name);

                sourceCode.Append(@"
                    }");
            }

            sourceCode
                .Append(@"
                    break;")
                .Append('\n');
        }

        sourceCode.Append(@"
                default:
                    break;
            }
        }");
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

public static class StringBuilderExtensions
{
    public static unsafe void Append(this StringBuilder builder, ReadOnlySpan<char> span)
    {
        fixed(char* ptr = span)
        {
            builder.Append(ptr, span.Length);
        }
    }

    public static IndentInterpolatedStringHandler AppendIndented(
        this StringBuilder sourceCode,
        int indent,
        [InterpolatedStringHandlerArgument(nameof(indent), nameof(sourceCode))]
        IndentInterpolatedStringHandler handler
    )
    {
        return handler;
    }

    public static IndentInterpolatedStringHandler AppendIndented(
        this StringBuilder sourceCode,
        int indent,
        CancellationToken token,
        [InterpolatedStringHandlerArgument(nameof(indent), nameof(sourceCode), nameof(token))]
        IndentInterpolatedStringHandler handler
    )
    {
        return handler;
    }
}

internal static class EnumerableExntensions
{
    internal static RepeatableSyntaxExpression<T> ToSyntax<T>(this IEnumerable<T> source, Func<T, string> expression)
    {
        return new RepeatableSyntaxExpression<T>(source, expression);
    }
}

internal abstract class ExpressionResult
{
    public abstract void Write(IndentInterpolatedStringHandler sourceCode, ReadOnlyMemory<char> whiteSpaceBefore, CancellationToken token);

    public static implicit operator ExpressionResult(string simpleString) => new StringExressionResult(simpleString);
}

internal sealed class StringExressionResult(string _source) : ExpressionResult
{
    public override void Write(IndentInterpolatedStringHandler sourceCode, ReadOnlyMemory<char> whiteSpaceBefore, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        sourceCode.AppendLiteral(whiteSpaceBefore);
        sourceCode.AppendLiteral(_source);
    }
}

[InterpolatedStringHandler]
internal sealed class InterpolatedStringExpressionResult : ExpressionResult
{
    private readonly StringBuilder _tempStorage;

    public InterpolatedStringExpressionResult(int literalLength, int formattedCount)
    {
        _tempStorage = new(literalLength);
    }

    public override void Write(IndentInterpolatedStringHandler sourceCode, ReadOnlyMemory <char> whiteSpaceBefore, CancellationToken token)
    {
        var result = ToStringAndClear();

    }

    public void AppendLiteral(string literal)
        => _tempStorage.Append(literal);

    public void AppendFormatted<T>(T value)
    {
        if(value != null)
            AppendLiteral(value.ToString());
    }

    public void AppendFormatted<T>(T value, string format)
        where T : IFormattable
    {
        _tempStorage.Append(value.ToString(format, null));
    }

    private string ToStringAndClear()
    {
        var result = _tempStorage.ToString();

        _tempStorage.Clear();

        return result;
    }
}

internal sealed class RepeatableSyntaxExpression<T> : ISourceCodeWriter
{
    private readonly IEnumerable<T> _source;
    private readonly Func<T, string> _expression;

    public RepeatableSyntaxExpression(IEnumerable<T> source, Func<T, string> expression)
    {
        _source = source;
        _expression = expression;
    }

    public void Accept(IndentInterpolatedStringHandler sourceCode, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        using var enumerator = _source.GetEnumerator();

        if(!enumerator.MoveNext()) return;

        var indent = sourceCode.LastWhiteSpace;

        var first = _expression(enumerator.Current);
        sourceCode.AppendLiteral(first);

        while (enumerator.MoveNext() && !token.IsCancellationRequested)
        {
            if(sourceCode.IsLastAddedNewLine)
                sourceCode.AppendLiteral(indent);

            var row = _expression(enumerator.Current);
            sourceCode.AppendLiteral(row);
        }
    }

    public override string ToString()
    {
        return string.Concat(_source.Select(_expression));
    }
}

internal interface ISourceCodeWriter
{
    void Accept(IndentInterpolatedStringHandler sourceCode, CancellationToken token);
}

[InterpolatedStringHandler]
public ref struct IndentInterpolatedStringHandler
{
    private readonly StringBuilder _sourceCode;
    private readonly int _indent;
    private readonly char _indentSymbol;
    private readonly CancellationToken _token;

    private bool _isFirstAppend = true;
    private bool _isLastAddedNewLine = false;
    private ReadOnlyMemory<char> _lastWhiteSpace;

    public IndentInterpolatedStringHandler(int literalLength, int formattedCount, int indent, StringBuilder sourceCode, CancellationToken token = default, char indentSymbol = '\t')
    {
        _sourceCode = sourceCode;
        _sourceCode.EnsureCapacity(_sourceCode.Length + literalLength);

        _token = token;
        _indent = indent;
        _indentSymbol = indentSymbol;
    }

    public readonly StringBuilder SourcCode => _sourceCode;
    public readonly bool IsLastAddedNewLine => _isLastAddedNewLine;
    public readonly ReadOnlyMemory<char> LastWhiteSpace => _lastWhiteSpace;


    public void AppendLiteral(string literal)
        => AppendLiteral(literal.AsMemory());

    public void AppendLiteral(ReadOnlyMemory<char> literal)
    {
        if (_token.IsCancellationRequested)
            return;

        if (literal is not { Length: > 0 })
            return;

        bool end;

        do
        {
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
            end = !LineSplitter.FindNextLine(ref literal, out var part);
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope

#if DEBUG
            var rem = literal.ToString();
            var partStr = part.ToString();
#endif

            if(_isFirstAppend || _isLastAddedNewLine)
            {
                _isFirstAppend = false;
                _isLastAddedNewLine = false;

                AppendIndent();
            }

            _sourceCode.Append(part);

            _isLastAddedNewLine = part.Length > 0 &&
                part.Span.Slice(part.Length - 1, 1) is { Length: 1 } partEnd &&
                partEnd[0] is '\r' or '\n';

#pragma warning disable CS9080
            _lastWhiteSpace = part.Span.IsWhiteSpace() ? part : default;
#pragma warning restore CS9080

        } while (!end && !_token.IsCancellationRequested);
    }

    public void AppendFormatted<T>(T value)
    {
        if (_token.IsCancellationRequested)
            return;

        // It has already added data to targeted StringBuilder
        // just used for visualization
        if (typeof(IndentInterpolatedStringHandler) == typeof(T))
        {
            return;
        }

        if(value is ISourceCodeWriter writer)
        {
            writer.Accept(this, _token);
            return;
        }

        if(value is string str)
        {
            AppendLiteral(str);
            return;
        }

        if (value is not IEnumerable<string> strings)
        {
            _sourceCode.Append(value);
            return;
        }

        foreach(var item in strings)
        {
            if (_token.IsCancellationRequested)
                return;

            AppendLiteral(item);
        }
    }

    public void AppendFormatted<T>(T value, string format)
        where T : IFormattable
    {
        _sourceCode.Append(value.ToString(format, null));
    }

    public override string ToString()
    {
        return _sourceCode.ToString();
    }

    private void AppendIndent()
        => _sourceCode.Append(_indentSymbol, _indent);
}

internal ref struct IndentStackWriter
{
    private readonly StringBuilder _sourceCode;

    private Memory<char> _indent;
    private Memory<int> _indentSlices;
    private int _slicesCount = 0;
    private int _sliceEnd = 0;

    private ArraySegment<char> _lastLine;

    public IndentStackWriter(StringBuilder sourceCode, int indentInitial = 0, char indentSymbol = '\t')
    {
        _sourceCode = sourceCode;
        AddIndent(indentInitial, indentSymbol);
    }

    public IndentStackWriter(StringBuilder sourceCode, ReadOnlySpan<char> initialIndent)
    {
        _sourceCode = sourceCode;
        AddIndent(initialIndent);
    }

    public readonly ReadOnlyMemory<char> Indent => _indent.Slice(0, _sliceEnd);

    public readonly ReadOnlyMemory<char> LastLine => _lastLine.AsMemory();

    public readonly void AppendLineSplitted(ReadOnlySpan<char> source)
    {
        if (source.IsEmpty) return;

        bool end;
        ReadOnlySpan<char> last = default;
        ReadOnlySpan<char> line = default;

        do
        {
            end = !LineSplitter.FindNextLine(ref source, ref line);
            if (line.IsEmpty) continue;

            AppendIndent();
            _sourceCode.Append(line);
            last = line;

        } while (!end);

#pragma warning disable CS8656 // For using this method after stackalloc 
        WriteLastLine(last);
#pragma warning restore CS8656
    }

    public readonly void Append(ReadOnlySpan<char> source)
    {
        if (source.IsEmpty) return;

        AppendIndent();
        _sourceCode.Append(source);
    }

    public void AddIndent(int times, char symbol)
    {
        if (times <= 0) return;

        const int partSize = 8;

        ReadOnlySpan<char> buffer = stackalloc char[partSize]
        {
            symbol, symbol,
            symbol, symbol,
            symbol, symbol,
            symbol, symbol,
        };

        var remainder = times & (partSize - 1);
        var part = remainder != 0 ? remainder : partSize;

        do
        {
            times -= part;

            EnsureBufferSizes(part);
            CopyIndent(buffer.Slice(0, part));
            IncrementStats(part);

            part = partSize;

        } while (times != 0);
    }

    public void AddIndent(ReadOnlySpan<char> indent)
    {
        if (indent.IsEmpty) return;

        EnsureBufferSizes(indent.Length);
        CopyIndent(indent);
        IncrementStats(indent.Length);
    }

    private readonly void AppendIndent()
    {
        _sourceCode.Append(_indent.Span.Slice(0, _sliceEnd));
    }

    private readonly void CopyIndent(ReadOnlySpan<char> indent)
    {
        var targetSlice = _indent.Span.Slice(_sliceEnd, indent.Length);
        indent.CopyTo(targetSlice);
    }

    private void IncrementStats(int indentLength)
    {
        _indentSlices.Span[_slicesCount] = indentLength;
        _slicesCount += 1;
        _sliceEnd += indentLength;
    }

    private void EnsureBufferSizes(int indentLength)
        => EnsureBufferSizes(_indent.Length + indentLength, _slicesCount + 1);

    private void EnsureBufferSizes(int length, int slicesLength)
    {
        EnsureIndentBufferSize(length);
        EnsureSlicesBufferSize(slicesLength);
    }

    private void EnsureSlicesBufferSize(int length)
    {
        if (_indentSlices.Length >= length)
            return;

        var newSize = CalculateNewSize(_indentSlices.Length, length);

        Memory<int> newBuffer = new int[newSize];
        _indentSlices.CopyTo(newBuffer);

        _indentSlices = newBuffer;
    }

    private void EnsureIndentBufferSize(int length)
    {
        if (_indent.Length >= length)
            return;

        var newSize = CalculateNewSize(_indent.Length, length);

        Memory<char> newBuffer = new char[newSize];
        _indent.CopyTo(newBuffer);

        _indent = newBuffer;
    }

    private void WriteLastLine(ReadOnlySpan<char> line)
    {
        EnsureLastLineSize(line.Length);

        var lastLineBuffer = _lastLine.Array;
        line.CopyTo(lastLineBuffer);

        SetLastLineCount(line.Length);
    }

    private void SetLastLineCount(int count)
    {
        _lastLine = new(_lastLine.Array, 0, count);
    }

    private void EnsureLastLineSize(int length)
    {
        if (_lastLine.Array.Length >= length)
            return;

        var newSize = CalculateNewSize(_lastLine.Array.Length, length);
        var newBuffer = new char[newSize];
        _lastLine = new ArraySegment<char>(newBuffer);
    }

    private static int CalculateNewSize(int current, int atLeast)
    {
        current = current <= 0 ? 4 : current;

        var newSize = (current * 3) >> 1;
        return Math.Max(newSize, atLeast);
    }
}

public static class LineSplitter
{
    public static bool FindNextLine(ref ReadOnlyMemory<char> remainingMemory, out ReadOnlyMemory<char> part)
    {
        var remaining = remainingMemory.Span;
        part = remainingMemory;

        var endline = remaining.IndexOfAny('\r', '\n');

        if (endline == -1 || endline == remaining.Length - 1)
            return false;

        // \r\n - single unit for eof
        if (remaining[endline] == '\r' && remaining[endline + 1] == '\n')
            endline += 1;

        part = remainingMemory.Slice(0, endline);
        remainingMemory = remainingMemory.Slice(endline + 1);

        return true;
    }

    public static bool FindNextLine(ref ReadOnlySpan<char> remaining, ref ReadOnlySpan<char> part)
    {
        part = remaining;

        var endline = remaining.IndexOfAny('\r', '\n');

        if (endline == -1 || endline == remaining.Length - 1)
            return false;

        // \r\n - single unit for eof
        if (remaining[endline] == '\r' && remaining[endline + 1] == '\n')
            endline += 1;

        part = remaining.Slice(0, endline);
        remaining = remaining.Slice(endline + 1);

        return true;
    }
}
