using System.Runtime.CompilerServices;
using System.Threading;

namespace Aegis.IndentWriter;

internal readonly struct IfTrueWriter
{
    private static readonly IndentStackWriter _writingIgnorer = new(new(), [], new CancellationToken(true));

    private readonly IndentStackWriter _writer;
    private readonly bool _result;
    private readonly IndentScopeHook? _hook;

    public IfTrueWriter(bool result, IndentStackWriter writer)
    {
        _result = result;
        _writer = writer;
    }

    public IfTrueWriter(bool result, IndentScopeHook hook)
    {
        _result = result;
        _writer = hook.Writer;
        _hook = hook;
    }

    public readonly IndentStackWriter Writer => _result ? _writer : _writingIgnorer;

    public readonly IndentedInterpolatedStringHandler this[[InterpolatedStringHandlerArgument("")] IndentedInterpolatedStringHandler handler]
    {
        get
        {
            _hook?.End();
            return handler;
        }
    }
}
