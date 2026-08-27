using Microsoft.CodeAnalysis.Text;
using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class RoslynSourceMap
{
    private readonly int _documentContentOffset;
    private readonly int _usingLength;
    private readonly int _bodyRelativeStart;
    private readonly int _syntheticBodyStart;
    private readonly int _bodyLength;

    public RoslynSourceMap(
        int documentContentOffset,
        int usingLength,
        int bodyRelativeStart,
        int syntheticBodyStart,
        int bodyLength)
    {
        _documentContentOffset = documentContentOffset;
        _usingLength = usingLength;
        _bodyRelativeStart = bodyRelativeStart;
        _syntheticBodyStart = syntheticBodyStart;
        _bodyLength = bodyLength;
    }

    public SquareSourceRange ToDocumentRange(TextSpan span)
    {
        if (span.Start < _usingLength)
        {
            var length = Math.Min(span.Length, _usingLength - span.Start);
            return new SquareSourceRange(_documentContentOffset + span.Start, Math.Max(0, length));
        }

        if (span.Start >= _syntheticBodyStart)
        {
            var bodyOffset = Math.Min(_bodyLength, span.Start - _syntheticBodyStart);
            var length = Math.Min(span.Length, _bodyLength - bodyOffset);
            return new SquareSourceRange(
                _documentContentOffset + _bodyRelativeStart + bodyOffset,
                Math.Max(0, length));
        }

        return new SquareSourceRange(_documentContentOffset + _bodyRelativeStart, 0);
    }
}
