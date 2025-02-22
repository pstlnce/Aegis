using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Aegis.IndentWriter;

[InterpolatedStringHandler]
internal readonly struct IndentedInterpolatedStringHandler
{
    private readonly IndentStackWriter _writer;

    public IndentedInterpolatedStringHandler(int literalLength, int formattedCount, IndentStackWriter writer)
    {
        _writer = writer;
    }

    public readonly void AppendLiteral(string literal)
        => AppendLiteral(literal.AsSpan());

    public readonly void AppendLiteral(ReadOnlySpan<char> literal)
    {
        _writer.AppendLineSplitted(literal);
    }

    public readonly void AppendFormatted<T>(T value)
    {
        // It has already added data to targeted StringBuilder
        // just used for visualization
        if (typeof(IndentedInterpolatedStringHandler) == typeof(T))
        {
            value!.ToString();
            return;
        }

        if (value is string str)
        {
            AppendLiteral(str);
            return;
        }

        if (value is not IEnumerable<string> strings)
        {
            if (value is not null)
                AppendLiteral(value.ToString());

            return;
        }

        foreach (var item in strings)
        {
            AppendLiteral(item);
        }
    }

    public void AppendFormatted<T>(T value, string format)
        where T : IFormattable
    {
        AppendLiteral(value.ToString(format, null));
    }

    public readonly override string ToString()
    {
        return string.Empty;
    }
}