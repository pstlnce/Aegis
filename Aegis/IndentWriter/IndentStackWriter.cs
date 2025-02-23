using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Aegis.IndentWriter;

internal sealed class IndentStackWriter
{
    private readonly StringBuilder _sourceCode;

    private Memory<char> _indent;
    private Memory<int> _indentSlices;
    private int _slicesCount = 0;
    private int _sliceEnd = 0;

    private ArraySegment<char> _lastLine = new([]);

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

    public ReadOnlyMemory<char> Indent => _indent.Slice(0, _sliceEnd);

    public ReadOnlyMemory<char> LastLine => _lastLine.AsMemory();

    public bool IsLastAddedLine => _lastLine.AsSpan().IsAddedNewLine();

    /// <summary> Don't store in variable to reuse </summary>
    public IndentScopeHook Scope => new(this);

    public IndentedInterpolatedStringHandler this[[InterpolatedStringHandlerArgument("")] IndentedInterpolatedStringHandler value] => value;

    public void AppendLineSplitted(ReadOnlySpan<char> source)
    {
        if (source.IsEmpty) return;

        bool end;
        ReadOnlySpan<char> last = default;
        ReadOnlySpan<char> line = default;

        var lastAddedLine = IsLastAddedLine;
        var noLineAdded = !lastAddedLine;

        do
        {
            end = !LineSplitter.FindNextLine(ref source, ref line);
            if (line.IsEmpty) continue;

#if false && DEBUG
            var rem = source.ToString();
            var partStr = line.ToString();
#endif
            if (lastAddedLine)
                AppendIndent();

            lastAddedLine = true;

            _sourceCode.Append(line);
            last = line;

            if (noLineAdded && line.IsAddedNewLine())
            {
                noLineAdded = false;
            }

        } while (!end);

        if (false && noLineAdded)
        {
            AppendToLastLine(last);
            return;
        }

        WriteLastLine(last);
    }

    public void Append(ReadOnlySpan<char> source)
    {
        if (source.IsEmpty) return;

        AppendIndent();
        _sourceCode.Append(source);
    }

    public void AddIndent(int times, char symbol)
    {
        if (times <= 0) return;

        const int partSize = 4;

        ReadOnlySpan<char> buffer = stackalloc char[partSize]
        {
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

    public void PopIndent()
    {
        if (_slicesCount == 0) return;

        _slicesCount -= 1;
        _sliceEnd -= _indentSlices.Span[_slicesCount];
    }

    public void AddIndent(ReadOnlySpan<char> indent)
    {
        if (indent.IsEmpty) return;

        EnsureBufferSizes(indent.Length);
        CopyIndent(indent);
        IncrementStats(indent.Length);
    }

    private void AppendIndent()
    {
        _sourceCode.Append(_indent.Span.Slice(0, _sliceEnd));
    }

    private void CopyIndent(ReadOnlySpan<char> indent)
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

    private void AppendToLastLine(ReadOnlySpan<char> line)
    {
        var currentCount = _lastLine.Count;
        EnsureLastLineSize(currentCount + line.Length, copy: true);

        var lastLineBuffer = _lastLine.Array.AsSpan(currentCount);
        line.CopyTo(lastLineBuffer);

        SetLastLineCount(currentCount + line.Length);
    }

    private void WriteLastLine(ReadOnlySpan<char> line)
    {
        EnsureLastLineSize(line.Length, copy: false);

        var lastLineBuffer = _lastLine.Array;
        line.CopyTo(lastLineBuffer);

        SetLastLineCount(line.Length);
    }

    private void SetLastLineCount(int count)
    {
        _lastLine = new(_lastLine.Array, 0, count);
    }

    private void EnsureLastLineSize(int length, bool copy)
    {
        if (_lastLine.Array.Length >= length)
            return;

        var newSize = CalculateNewSize(_lastLine.Array.Length, length);
        var newBuffer = new char[newSize];

        if (copy)
        {
            _lastLine.Array.CopyTo(newBuffer, 0);
        }

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
    public static bool FindNextLine(ref ReadOnlySpan<char> remaining, ref ReadOnlySpan<char> part)
    {
        part = remaining;

        var endOfLine = remaining.IndexOfAny('\r', '\n');

        if (endOfLine == -1 || endOfLine == remaining.Length - 1)
            return false;

        // \r\n - single unit for eof
        if (remaining[endOfLine] == '\r' && remaining[endOfLine + 1] == '\n')
            endOfLine += 1;

        part = remaining.Slice(0, endOfLine + 1);
        remaining = remaining.Slice(endOfLine + 1);

        return true;
    }

    public static bool IsAddedNewLine(this ReadOnlySpan<char> line)
    {
        return line is { Length: > 0 } && line[line.Length - 1] is '\r' or '\n';
    }

    public static bool IsAddedNewLine(this Span<char> line)
    {
        return line is { Length: > 0 } && line[line.Length - 1] is '\r' or '\n';
    }
}

internal static class StringBuilderExtensions
{
    public static unsafe void Append(this StringBuilder builder, ReadOnlySpan<char> span)
    {
        fixed (char* ptr = span)
        {
            builder.Append(ptr, span.Length);
        }
    }
}