using Microsoft.CodeAnalysis;
using System;

namespace Aegis;

internal static class TypeSymbolExtensions
{
    public static bool IsComplex(this ITypeSymbol typeSymbol) => !IsPrimitive(typeSymbol);

    public static bool IsPrimitive(this ITypeSymbol typeSymbol)
    {
        var specialSimple = typeSymbol.SpecialType
            is SpecialType.System_Object
            or SpecialType.System_Boolean
            or SpecialType.System_Char
            or SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Decimal
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_DateTime
            or SpecialType.System_String;

        if (specialSimple) return true;

        return typeSymbol.ContainingNamespace.Name == nameof(System) &&
            typeSymbol.Name is nameof(DateTimeOffset)
            or nameof(DateTime)
            or nameof(TimeSpan)
            or "DateOnly"
            or "TimeOnly";
    }
}
