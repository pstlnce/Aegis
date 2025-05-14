using Aegis.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Aegis;

internal static class CustomParsersScraper
{
    public static IncrementalValuesProvider<(ParserStaticMethod?, Diagnostic?)> ForCustomParsers(this SyntaxValueProvider syntaxProvider)
    {
        return syntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: CustomParserAttribute.FullName,
            predicate: (SyntaxNode _, CancellationToken token) => !token.IsCancellationRequested,
            transform: TransformCustomParser
        );
    }

    private static (ParserStaticMethod?, Diagnostic?) TransformCustomParser(GeneratorAttributeSyntaxContext context, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return default!;

        var semantic = context.SemanticModel.GetDeclaredSymbol(context.TargetNode as MethodDeclarationSyntax ?? throw new Exception());

        if (semantic is not IMethodSymbol methodSemantic ||
           !methodSemantic.IsStatic ||
           !(methodSemantic.Parameters.Length == 0 ||
               (methodSemantic.Parameters.First().Type.SpecialType == SpecialType.System_Object &&
               methodSemantic.Parameters.Skip(1).All(x => x.HasExplicitDefaultValue))
           )
        )
        {
            var diagnostic = Diagnostic.Create(
                location: context.TargetNode.GetLocation(),
                messageArgs: default,
                descriptor: new DiagnosticDescriptor(
                    id: "lowiq001",
                    title: "Only static methods with single parameter with 'object' type are supported for now",
                    messageFormat: string.Empty,
                    category: "custom parsing",
                    defaultSeverity: DiagnosticSeverity.Warning,
                    description: "Only static methods with single parameter with 'object' type are supported for now",
                    isEnabledByDefault: true
                )
            );

            return (default, diagnostic);
        }

        var call = semantic!.ToDisplayString();
        var open = call.IndexOf('(');

        if (open != -1)
        {
            call = call.Substring(0, open);
        }

        var targetType = methodSemantic.ReturnType.ToDisplayString();
        var callMethod = new ParserStaticMethod(call, targetType);

        return (callMethod, default);
    }

    public static Dictionary<string, string> ToParsers(this ImmutableArray<(ParserStaticMethod?, Diagnostic?)> models, SourceProductionContext productionContext)
    {
        var parsers = new Dictionary<string, string>(models.Length);

        foreach (var model in models)
        {
            var (parser, diagnostic) = model;

            if (parser.HasValue)
            {
                parsers[parser.Value.TargetType] = parser.Value.TargetType;
            }

            if (diagnostic != null)
            {
                productionContext.ReportDiagnostic(diagnostic);
            }
        }

        return parsers;
    }
}
