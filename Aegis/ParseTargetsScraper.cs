using Aegis.Options;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Aegis;

internal static class ParseTargetsScraper
{
    public static IncrementalValuesProvider<MatchingModel?> ForParseTargets(this SyntaxValueProvider syntaxProvider)
    {
        return syntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: AegisAttributeGenerator.AttributeFullName,
            predicate: Predicate,
            transform: Transform
        );
    }

    public static bool Predicate(SyntaxNode syntaxNode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        return syntaxNode switch
        {
            StructDeclarationSyntax => true,
            ClassDeclarationSyntax classDecl =>
                !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword) &&
                !classDecl.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false,
        };
    }

    public static MatchingModel? Transform(GeneratorAttributeSyntaxContext gen, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var attributeInstance = gen.Attributes[0];

        var parser = new AegisAttributeParse(attributeInstance);

        if (gen.TargetSymbol is not INamedTypeSymbol target)
        {
            return default;
        }

        var settables = GetSettables(target)
            .ToSettablesSnapshots()
            .ToArray();

        if (settables.Length == 0)
            return null;

        var targetNamespace = target.ContainingNamespace;

        var namespaceSnapshot = new NamespaceSnapshot(targetNamespace.Name, targetNamespace.ToDisplayString(), targetNamespace.IsGlobalNamespace);
        var typeSnapshot = new TypeSnapshot(target.Name, target.ToDisplayString(), target.IsReferenceType, target.IsPrimitive(), namespaceSnapshot);

        var settings = new MatchingSettings(parser.MatchCasePropertyValue == null ? MatchCase.None : (MatchCase)parser.MatchCasePropertyValue);

        var targetModel = new MatchingModel(
            type: typeSnapshot,
            settables: settables,
            matchingSettings: settings,
            inner: SearchComplexTypes(target, settings)
        );

        return targetModel;
    }

    public static Dictionary<string, MatchingModel>? SearchComplexTypes(ITypeSymbol type, MatchingSettings settigns, int depth = 0, int maxDepth = 32)
    {
        if (depth >= maxDepth)
        {
            return default;
        }

        var result = default(Dictionary<string, MatchingModel>?);

        var settables = GetSettables(type);

        var complexTypes = settables.Select(x => (x as IPropertySymbol)?.Type ?? ((IFieldSymbol)x).Type)
            .Where(x => x.IsComplex());

        foreach (var complexType in complexTypes)
        {
            var namespaceSnap = new NamespaceSnapshot(name: complexType.Name,
                display: complexType.ToDisplayString(),
                isGlobal: complexType.ContainingNamespace.IsGlobalNamespace
            );

            var typeSnap = new TypeSnapshot(Name: complexType.Name,
                DisplayString: complexType.ToDisplayString(),
                IsReference: complexType.IsReferenceType,
                IsPrimitive: false,
                Namespace: namespaceSnap
            );

            var innerSettables = GetSettables(complexType)
                .ToSettablesSnapshots()
                .ToArray();

            var model = new MatchingModel(
                type: typeSnap,
                settables: innerSettables,
                matchingSettings: settigns,
                inner: SearchComplexTypes(complexType, settigns, depth + 1, maxDepth)
            );

            (result ??= [])[typeSnap.DisplayString] = model;
        }

        return result;
    }

    public static IEnumerable<ISymbol> GetSettables(this ITypeSymbol symbol)
        => symbol.GetMembers().Where(IsSettable);

    public static bool IsSettable(this ISymbol symbol) => symbol switch
    {
        IPropertySymbol property
            => property.SetMethod is { DeclaredAccessibility: Accessibility.Public or Accessibility.Internal },

        IFieldSymbol field
            => !field.IsConst
            && !field.IsStatic
            && !field.IsReadOnly
            && field.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal,

        _ => false
    };

    public static IEnumerable<Settable> ToSettablesSnapshots(this IEnumerable<ISymbol> members)
        => members.Select(ToSettableSnapshot);

    public static Settable ToSettableSnapshot(ISymbol member, int i)
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

        if (fieldSource?.TryGetOrder(out var order) == true && order < 0)
        {
            fieldSource = default;
        }

        var typeNamespace = type.ContainingNamespace;

        var namespaceSnapshot = new NamespaceSnapshot(typeNamespace.Name, typeNamespace.ToDisplayString(), typeNamespace.IsGlobalNamespace);
        var typeSnapshot = new TypeSnapshot(type.Name, type.ToDisplayString(), type.IsReferenceType, type.IsPrimitive(), namespaceSnapshot);

        return new Settable(typeSnapshot, member.Name, fieldSource ?? new([member.Name]), isRequired, i);
    }
}
