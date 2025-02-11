using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

            sourceCode
                .Append("using System;\n")
                .Append("using System.Data;\n")
                .Append("using System.Data.Common;\n")
                .Append("using System.Collections.Generic;\n")
                .Append("using System.Collections.ObjectModel;\n")
                .Append("using System.Runtime.CompilerServices;\n")
                .Append('\n');
            
            var typeNamespace = type.ContainingNamespace is { IsGlobalNamespace: false } ? type.ContainingNamespace : null;
            var namespaceAdded = false;

            if (typeNamespace != null)
            {
                var namespaceString = typeNamespace.ToDisplayString();
                sourceCode.AppendFormat("namespace {0}\n", namespaceString);

                namespaceAdded = true;
            }

            if (token.IsCancellationRequested) return;

            if (namespaceAdded)
            {
                sourceCode.Append('{');
            }

            sourceCode.AppendFormat(@"
    public sealed partial class {0}AegisAgent
    {{
        internal static IEnumerable<{0}> Read<TReader>(TReader reader)
            where TReader : DbDataReader
        {{
            var schema = reader.GetColumnSchema();

            ReadSchema(schema", type.Name);

            foreach (var member in type.GetMembers())
            {
                if (token.IsCancellationRequested) return;

                if (!TryGetSettableProperty(member, out var property))
                    continue;

                sourceCode.AppendFormat(", out var col{0}, out var f{0}", property.Name);
            }

            sourceCode.Append(");\n");

            sourceCode.Append(@"
            while(reader.Read())
            {");

            foreach (var property in type.GetSettableProperties())
            {
                if (token.IsCancellationRequested) return;

                var propertyType = property.Type.ToDisplayString();

                sourceCode.AppendFormat(@"
                var v{0} = f{0} && reader[col{0}] is {1} p{0} ? p{0} : default({1});", property.Name, propertyType);
            }
            
            sourceCode.Append('\n');

            sourceCode.AppendFormat(@"
                var parsed = new {0}()
                {{", type.Name);

            var writedFirst = false;

            foreach (var property in type.GetSettableProperties())
            {
                if (token.IsCancellationRequested) return;

                if (writedFirst) sourceCode.Append(',');
                writedFirst = true;

                sourceCode.AppendFormat(@"
                    {0} = v{0}", property.Name);
            }

            sourceCode.Append(@"
                };

                yield return parsed;
            }
        }");

            sourceCode.Append('\n');

            if (token.IsCancellationRequested) return;

            AppendReadList(sourceCode, type, token);

            if (token.IsCancellationRequested) return;

            sourceCode.Append('\n');

            AppendReadSingleItemStream(sourceCode, type, token);

            sourceCode.Append('\n');

            AppendReadList3(sourceCode, type, token);

            sourceCode.Append('\n');

            if (token.IsCancellationRequested) return;

            AppendReadList2(sourceCode, type, token);

            if (token.IsCancellationRequested) return;

            sourceCode.Append('\n');

            AppendReadSchemaColumnIndex2(sourceCode, type, matchCase, token);

            sourceCode.Append('\n');

            if (token.IsCancellationRequested) return;

            AppendReadSchemaIndexes(sourceCode, type, token);

            sourceCode.Append('\n');

            if (token.IsCancellationRequested) return;

            AppendReadSchemaColumnIndex(sourceCode, type, matchCase, token);

            sourceCode.Append('\n');

            if (token.IsCancellationRequested) return;

            AppendReadSchema(sourceCode, type, token);

            if (token.IsCancellationRequested) return;

            sourceCode.Append('\n');

            // todo: Provide attribute's values
            AppendReadSchemaColumn(sourceCode, type, matchCase, token);

            if (token.IsCancellationRequested) return;

            sourceCode.Append(@"
    }");

            if (namespaceAdded)
                sourceCode.Append("\n}");

            if (token.IsCancellationRequested) return;

            var sourceCodeText = sourceCode.ToString();
            sourceCode.Clear();

            var fileName = typeNamespace != null
                ? $"{typeNamespace}.{type.Name}AegisAgent.g.cs"
                : $"{type.Name}AegisAgent.g.cs";

            productionContext.AddSource(fileName, sourceCodeText);
        }
    }

    internal static void AppendReadList2(StringBuilder sourceCode, ITypeSymbol type, CancellationToken token = default)
    {
        sourceCode.AppendFormat(@"
        internal static List<{0}> ReadList2<TReader>(TReader reader)
            where TReader : IDataReader
        {{
            var result = new List<{0}>();", type.Name);

        var propertiesCount = 0;

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

                sourceCode.AppendFormat(@"
            {1} v{2} = default({1});
            ref {1} v{0} = ref v{2};
            int ind{0} = 0;", propertiesCount, "object", property.Name);

            sourceCode.Append('\n');

            propertiesCount++;
        }


        sourceCode.Append(@"
            int qCurrent = -1;

            {");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            var propertyType = property.Type.ToDisplayString();

            sourceCode.AppendFormat(@"
                bool f{0} = false;", property.Name);
        }

        sourceCode.Append('\n');

        sourceCode.Append(@"
                var fields = reader.FieldCount;

                for(int i = 0; i != fields; i++)
                {");

        sourceCode.Append(@"
                    var d = ReadSchemaColumnIndex2(reader.GetName(i)");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            var propertyType = property.Type.ToDisplayString();

            sourceCode.AppendFormat(", ref f{0}", property.Name);
        }

        sourceCode.Append(@");
                    if(d == -1) continue;

                    qCurrent += 1;

                    switch(qCurrent)
                    {");

        var map = new StringBuilder();
        map.Append(@"ref (");

        var propI = 0;
        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            if (propI != 0)
            {
                map.Append(" : ");
            }

            if(propI != (propertiesCount - 1))
            {
                if(propI > 0)
                {
                    map.Append("ref (");
                }

                map.AppendFormat("d == {0} ? ref v{1}", propI, property.Name);
            }
            else
            {
                map.AppendFormat("ref v{0}", property.Name);
                map.Append(')', propertiesCount - 1);
            }

            propI++;
        }

        var mapString = map.ToString();

        int propertyId = 0;

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            var propertyType = property.Type.ToDisplayString();

            if(propertiesCount - 1 != propertyId)
            {
                sourceCode.AppendFormat(@"
                        case {0}:
                            v{0} = {1};
                            ind{0} = i;
                            break;", propertyId, map);
            }
            else
            {
                sourceCode.AppendFormat(@"
                        default:
                            v{0} = {1};
                            ind{0} = i;
                            break;", propertyId, map);
            }

            propertyId++;
        }

        sourceCode.Append(@"
                    }");

        sourceCode.Append(@"
                }");

        sourceCode.Append(@"
            }");

        sourceCode.Append('\n');

        sourceCode.Append(@"
            if(qCurrent == -1)
            {
                return result;
            }

            while(reader.Read())
            {
                switch(qCurrent)
                {");

        for(int i = propertiesCount - 1; i >= 0; i--)
        {
            if(i > 1)
            {
                sourceCode.AppendFormat(@"
                    case {0}:
                        v{0} = reader[ind{0}];
                        goto case {1};", i, i - 1);
            }
            else if (i == 1)
            {
                sourceCode.AppendFormat(@"
                    case {0}:
                        v{0} = reader[ind{0}];
                        goto default;", i);
            }
            else
            {
                sourceCode.AppendFormat(@"
                    default:
                        v{0} = reader[ind{0}];
                        break;", i);
            }
        }

        sourceCode.Append(@"
                }");

        sourceCode.Append('\n');

        sourceCode.AppendFormat(@"
                var parsed = new {0}()
                {{", type.Name);

        var writedFirst = false;

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            if (writedFirst) sourceCode.Append(',');
            writedFirst = true;

            var propertyType = property.Type.ToDisplayString();

            if (property.Type.IsReferenceType)
            {
                sourceCode.AppendFormat(@"
                    {0} = v{0} as {1}", property.Name, propertyType);
            }
            else
            {
                sourceCode.AppendFormat(@"
                    {0} = v{0} is {1} p{0} ? p{0} : default({1})", property.Name, propertyType);
            }
        }

        sourceCode.Append(@"
                };

                result.Add(parsed);
            }
            
            return result;
        }");
    }

    internal static void AppendReadSingleItemStream(StringBuilder sourceCode, ITypeSymbol type, CancellationToken token = default)
    {
        sourceCode.AppendFormat(@"
        internal static IEnumerable<{0}> ReadSingleItemStream<TReader>(TReader reader)
            where TReader : IDataReader
        {{
            {0} parsed = default({0});
            ReadSchemaIndexes(reader", type.Name);


        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(", out var col{0}", property.Name);
        }

        sourceCode.Append(");\n");

        sourceCode.Append(@"
            while(reader.Read())
            {");

        sourceCode.Append('\n');

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            var propertyType = property.Type.ToDisplayString();

            if (property.Type.IsReferenceType)
            {
                sourceCode.AppendFormat(@"
                var v{0} = col{0} != -1 ? reader[col{0}] as {1} : default({1});", property.Name, propertyType);
            }
            else
            {
                sourceCode.AppendFormat(@"
                var v{0} = col{0} != -1 && reader[col{0}] is {1} p{0} ? p{0} : default({1});", property.Name, propertyType);
            }
        }

        sourceCode.Append('\n');

        sourceCode.AppendFormat(@"
                parsed ??= new {0}();", type.Name);

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(@"
                parsed.{0} = v{0};", property.Name);
        }

        sourceCode.Append(@"
                yield return parsed;
            }
        }");
    }

    internal static void AppendReadList(StringBuilder sourceCode, ITypeSymbol type, CancellationToken token = default)
    {
        sourceCode.AppendFormat(@"
        internal static List<{0}> ReadList<TReader>(TReader reader)
            where TReader : IDataReader
        {{
            var result = new List<{0}>();

            ReadSchemaIndexes(reader", type.Name);


        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(", out var col{0}", property.Name);
        }

        sourceCode.Append(");\n");

        sourceCode.Append(@"
            while(reader.Read())
            {");

        sourceCode.Append('\n');

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            var propertyType = property.Type.ToDisplayString();

            if (property.Type.IsReferenceType)
            {
                sourceCode.AppendFormat(@"
                var v{0} = col{0} != -1 ? reader[col{0}] as {1} : default({1});", property.Name, propertyType);
            }
            else
            {
                sourceCode.AppendFormat(@"
                var v{0} = col{0} != -1 && reader[col{0}] is {1} p{0} ? p{0} : default({1});", property.Name, propertyType);
            }
        }

        sourceCode.Append('\n');

        sourceCode.AppendFormat(@"
                var parsed = new {0}()
                {{", type.Name);

        var writedFirst = false;

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            if (writedFirst) sourceCode.Append(',');
            writedFirst = true;

            sourceCode.AppendFormat(@"
                    {0} = v{0}", property.Name);
        }

        sourceCode.Append(@"
                };

                result.Add(parsed);
            }
            
            return result;
        }");
    }

    internal static void AppendReadList3(StringBuilder sourceCode, ITypeSymbol type, CancellationToken token = default)
    {
        sourceCode.AppendFormat(@"
        internal static List<{0}> ReadList3<TReader>(TReader reader)
            where TReader : IDataReader
        {{
            var result = new List<{0}>();

            ReadSchemaIndexes(reader", type.Name);


        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(", out var col{0}", property.Name);
        }

        sourceCode.Append(");\n");

        sourceCode.AppendFormat(@"
            while(reader.Read())
            {{
                var parsed = new {0}();", type.Name);

        sourceCode.Append('\n');

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            var propertyType = property.Type.ToDisplayString();

            if (property.Type.IsReferenceType)
            {
                sourceCode.AppendFormat(@"
                if(col{0} != -1) parsed.{0} = reader[col{0}] as {1};", property.Name, propertyType);
            }
            else
            {
                sourceCode.AppendFormat(@"
                if(col{0} != -1 && reader[col{0}] is {1} p{0}) parsed.{0} = p{0};", property.Name, propertyType);
            }
        }

        sourceCode.Append('\n');

        sourceCode.Append(@"

                result.Add(parsed);
            }
            
            return result;
        }");
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

    internal static void AppendReadSchemaColumnIndex2(StringBuilder sourceCode, ITypeSymbol type, int matchCases, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;

        sourceCode.Append(@"
        internal static int ReadSchemaColumnIndex2(string c");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(
                ", ref bool f{0}",
                property.Name
            );
        }

        if (token.IsCancellationRequested) return;

        sourceCode.Append(@")
        {");

        var namesToMatch = new SortedDictionary<int, List<(string name, IPropertySymbol property, int id)>>();
        var names = new List<string>();

        int id = -1;

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            ++id;

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

                    sameLength.Add((lowerCase, property, id));
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

                    sameLength.Add((snake, property, id));
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

                sameLength.Add((nameCase, property, id));
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

            foreach (var (name, property, propertyId) in sameLengthNames.Value)
            {
                if (writedFirst)
                {
                    sourceCode.Append('\n');
                }

                writedFirst = true;

                sourceCode.AppendFormat(@"
                    if(!f{0} && string.Equals(c, ""{1}""", property.Name, name);

                if (MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.IgnoreCase))
                {
                    sourceCode.Append(", StringComparison.OrdinalIgnoreCase");
                }

                sourceCode.Append(')');

                sourceCode.Append(@")
                    {");

                sourceCode.AppendFormat(@"
                        f{0} = true;
                        return {1};", property.Name, propertyId);

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
                    return -1;
                    break;
            }

            return -1;
        }");
    }


    internal static void AppendReadSchemaColumnIndex(StringBuilder sourceCode, ITypeSymbol type, int matchCases, CancellationToken token = default)
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

            foreach (var (name, property) in sameLengthNames.Value)
            {
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

    internal static void AppendReadSchema(StringBuilder sourceCode, ITypeSymbol type, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;

        sourceCode.Append(@"
        internal static void ReadSchema(ReadOnlyCollection<DbColumn> schema");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(
                ", out string column{0}, out bool finded{0}",
                property.Name
            );
        }

        sourceCode.Append(@")
        {");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;
            sourceCode.AppendFormat(@"
            column{0} = default;
            finded{0} = false;",
            property.Name);
        }

        sourceCode.Append('\n');

        sourceCode.Append(@"
            foreach (var column in schema)
            {
                ReadSchemaColumn(column");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(
                ", ref column{0}, ref finded{0}",
                property.Name
            );
        }

        sourceCode.Append(@");
            }
        }");
    }

    internal static void AppendReadSchemaColumn(StringBuilder sourceCode, ITypeSymbol type, int matchCases, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;

        sourceCode.Append(@"
        internal static void ReadSchemaColumn(DbColumn column");

        foreach (var member in type.GetMembers())
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;

            sourceCode.AppendFormat(
                ", ref string column{0}, ref bool finded{0}",
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

            if(MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.IgnoreCase))
            {
                var lowerCase =
                    MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.MatchOriginal) ||
                    MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.Camel) ||
                    MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.Pascal)
                    ? property!.Name.ToLower()
                    : null;

                if(lowerCase != null)
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

                if(snake != null && snake != lowerCase)
                {
                    if (!namesToMatch.TryGetValue(snake.Length, out var sameLength))
                    {
                        namesToMatch[snake.Length] = sameLength = [];
                    }

                    sameLength.Add((snake, property));
                }

                continue;
            }

            if(MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.MatchOriginal))
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

            foreach(var nameCase in names)
            {
                if(!namesToMatch.TryGetValue(nameCase.Length, out var sameLength))
                {
                    namesToMatch[nameCase.Length] = sameLength = [];
                }

                sameLength.Add((nameCase, property));
            }

            names.Clear();
        }

        sourceCode.Append(@"
            string c = column.ColumnName;

            switch(c.Length)
            {");

        foreach (var sameLengthNames in namesToMatch)
        {
            if (token.IsCancellationRequested) return;

            var length = sameLengthNames.Key;

            sourceCode.AppendFormat(@"
                case {0}:", length);

            bool writedFirst = false;

            foreach(var (name, property) in sameLengthNames.Value)
            {
                if (writedFirst)
                {
                    sourceCode.Append('\n');
                }

                writedFirst = true;

                sourceCode.AppendFormat(@"
                    if(finded{0} == false && string.Equals(c, ""{1}""", property.Name, name);

                if(MatchCaseGenerator.HasFlag(matchCases, MatchCaseGenerator.IgnoreCase))
                {
                    sourceCode.Append(", StringComparison.OrdinalIgnoreCase");
                }
                
                sourceCode.Append(')');

                sourceCode.Append(@")
                    {");

                sourceCode.AppendFormat(@"
                        finded{0} = true;
                        column{0} = c;
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

    internal static void AppendReadSingle(StringBuilder sourceCode, ITypeSymbol type, CancellationToken token = default)
    {
        sourceCode.Append(@"
        internal static {0} ReadSingle<TReader>(TReader reader)
            where TReader : System.Data.Common.DbDataReader
        {
");
    }

    internal static void AppendPropertiesFilling(StringBuilder sourceCode, ImmutableArray<ISymbol> members, CancellationToken token = default)
    {
        var writedFirst = false;

        foreach (var member in members)
        {
            if (token.IsCancellationRequested) return;

            if (!TryGetSettableProperty(member, out var property))
                continue;
            
            if (writedFirst) sourceCode.Append(',');
            writedFirst = true;

            var propertyType = property.Type.ToDisplayString();

            sourceCode.AppendFormat(@"
                    {0} = reader[""{0}""] is {1} p{0} ? p{0} : default", property.Name, propertyType);
        }
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

internal struct AutoIndent(StringBuilder code, int indent = 0, char indendator = '\t', int indentIncrementor = 1)
{
    private readonly char _indendator = indendator;
    private readonly int _indentIncrementor = indentIncrementor;

    private readonly StringBuilder _code = code;
    private int _indent = indent;

    public readonly char Indendator => _indendator;
    public readonly int IndentIncrementor => _indentIncrementor;

    public readonly AutoIndent AppendIndented(char value, int repeatCount)
        => WriteIndent().Append(value, repeatCount);

    public readonly AutoIndent AppendIndented(bool value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(char value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(ulong value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(uint value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(byte value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(string value, int startIndex, int count)
        => WriteIndent().Append(value, startIndex, count);

    public readonly AutoIndent AppendIndented(string value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(float value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(ushort value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(object value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(char[] value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(char[] value, int startIndex, int charCount)
        => WriteIndent().Append(value, startIndex, charCount);

    public readonly AutoIndent AppendIndented(sbyte value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(decimal value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(short value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(int value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(long value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendIndented(double value)
        => WriteIndent().Append(value);

    public readonly AutoIndent AppendFormatIndented(string format, object arg0)
        => WriteIndent().AppendFormat(format, arg0);

    public readonly AutoIndent AppendFormatIndented(string format, object arg0, object arg1)
        => WriteIndent().AppendFormat(format, arg0, arg1);

    public readonly AutoIndent AppendFormatIndented(string format, object arg0, object arg1, object arg2)
        => WriteIndent().AppendFormat(format, arg0, arg1, arg2);

    public readonly AutoIndent AppendFormatIndented(string format, params object[] args)
        => WriteIndent().AppendFormat(format, args);


    public readonly AutoIndent Append(char value, int repeatCount)
    {
        _code.Append(value, repeatCount);
        return this;
    }

    public readonly AutoIndent Append(bool value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(char value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(ulong value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(uint value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(byte value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(string value, int startIndex, int count)
    {
        _code.Append(value, startIndex, count);
        return this;
    }

    public readonly AutoIndent Append(string value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(float value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(ushort value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(object value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(char[] value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(char[] value, int startIndex, int charCount)
    {
        _code.Append(value, startIndex, charCount);
        return this;
    }

    public readonly AutoIndent Append(sbyte value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(decimal value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(short value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(int value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(long value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent Append(double value)
    {
        _code.Append(value);
        return this;
    }

    public readonly AutoIndent AppendFormat(string format, object arg0)
    {
        _code.AppendFormat(format, arg0);
        return this;
    }

    public readonly AutoIndent AppendFormat(string format, object arg0, object arg1)
    {
        _code.AppendFormat(format, arg0, arg1);
        return this;
    }

    public readonly AutoIndent AppendFormat(string format, object arg0, object arg1, object arg2)
    {
        _code.AppendFormat(format, arg0, arg1, arg2);
        return this;
    }

    public readonly AutoIndent AppendFormat(string format, params object[] args)
    {
        _code.AppendFormat(format, args);
        return this;
    }

    public readonly AutoIndent AppendLine()
    {
        _code.Append('\n');
        return this;
    }

    public readonly AutoIndent AppendLine(string value)
        => AppendLine().Append(value);

    public readonly AutoIndent OpenCurlyBraces()
        => AppendLine().AppendIndented('{').IncrementIntend();

    public readonly AutoIndent CloseCurlyBraces()
        => AppendLine().DecrementIntend().AppendIndented('}');

    public readonly AutoIndent WriteIndent()
    {
        _code.Append(_indendator, _indent);
        return this;
    }

    public AutoIndent DecrementIntend()
    {
        _indent += _indentIncrementor;
        return this;
    }

    public AutoIndent IncrementIntend()
    {
        _indent += _indentIncrementor;
        return this;
    }
}
