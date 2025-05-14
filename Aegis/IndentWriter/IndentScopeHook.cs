using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Aegis.IndentWriter;

internal readonly struct IndentScopeHook
{
    private readonly bool _indentAdded;
    private readonly IndentStackWriter _writer;

    public IndentScopeHook(IndentStackWriter writer)
    {
        _writer = writer;

        var lastLine = writer.LastLine.Span;

        if (_indentAdded = !lastLine.IsEmpty && !lastLine.IsAddedNewLine() && lastLine.IsWhiteSpace())
        {
            _writer.AddIndent(lastLine);
        }
    }

    public readonly IndentStackWriter Writer => _writer;

    public readonly IfTrueWriter If(bool condition) => new(condition, this);

    public IndentedInterpolatedStringHandler this[[InterpolatedStringHandlerArgument("")] IndentedInterpolatedStringHandler val]
    {
        get
        {
            End();
            return val;
        }
    }

    public IndentedInterpolatedStringHandler this[string val]
    {
        get
        {
            _writer.AppendLineSplitted(val.AsSpan());

            End();
            return default;
        }
    }

    public IndentedInterpolatedStringHandler this[IEnumerable<string> repeatable, string joinBy = "\n\n"]
    {
        get
        {
            using var enumerator = repeatable.GetEnumerator();

            if (!enumerator.MoveNext())
            {
                End();
                return default;
            }

            var previous = enumerator.Current;
            bool end;

            do
            {
                _writer.AppendLineSplitted(previous.AsSpan());

                if (end = !enumerator.MoveNext())
                {
                    continue;
                }

                _writer.AppendLineSplitted(joinBy.AsSpan());
                previous = enumerator.Current;

            } while (!end);

            End();
            return default;
        }
    }

    public string ForEach<T>(ImmutableArray<T> source, Func<IndentStackWriter, T, IndentedInterpolatedStringHandler> write, string joinBy = "\n\n")
    {
        var enumerator = source.GetEnumerator();

        if (!enumerator.MoveNext())
        {
            End();
            return string.Empty;
        }

        var previous = enumerator.Current;
        bool end;

        do
        {
            write(_writer, previous);

            if (end = !enumerator.MoveNext())
            {
                continue;
            }

            _writer.AppendLineSplitted(joinBy.AsSpan());
            previous = enumerator.Current;

        } while (!end);

        End();
        return string.Empty;
    }

    public string ForEach<T>(IEnumerable<T> source, Func<IndentStackWriter, T, IndentedInterpolatedStringHandler> write, string joinBy = "\n\n")
    {
        using var enumerator = source.GetEnumerator();

        if (!enumerator.MoveNext())
        {
            End();
            return string.Empty;
        }

        var previous = enumerator.Current;
        bool end;

        do
        {
            write(_writer, previous);

            if (end = !enumerator.MoveNext())
            {
                continue;
            }

            _writer.AppendLineSplitted(joinBy.AsSpan());
            previous = enumerator.Current;

        } while (!end);

        End();
        return string.Empty;
    }

    public void End()
    {
        if (_indentAdded)
        {
            _writer.PopIndent();
        }
    }
}

