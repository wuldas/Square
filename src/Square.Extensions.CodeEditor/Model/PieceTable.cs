using System.Text;

namespace Square.Extensions.CodeEditor;

/// <summary>Piece table 缓冲：原始快照 + 追加缓冲 + 片段链。</summary>
internal sealed class PieceTable
{
    private enum Source : byte { Original, Add }

    private readonly record struct Piece(Source Source, int Start, int Length);

    private string _original = "";
    private readonly StringBuilder _add = new();
    private readonly List<Piece> _pieces = [];
    private int _length;
    private int[] _lineStarts = [0];

    public int Length => _length;
    public int LineCount => _lineStarts.Length;

    public string GetValue()
    {
        if (_pieces.Count == 0) return "";
        if (_pieces.Count == 1 &&
            _pieces[0].Source == Source.Original &&
            _pieces[0].Start == 0 &&
            _pieces[0].Length == _original.Length)
            return _original;
        var sb = new StringBuilder(_length);
        AppendTo(sb, 0, _length);
        return sb.ToString();
    }

    public void SetValue(string text)
    {
        _original = text;
        _add.Clear();
        _pieces.Clear();
        if (text.Length > 0)
            _pieces.Add(new Piece(Source.Original, 0, text.Length));
        _length = text.Length;
        RebuildLineStarts();
    }

    public string GetLineContent(int lineNumber)
    {
        var start = GetLineStart(lineNumber);
        var end = lineNumber + 1 < _lineStarts.Length ? _lineStarts[lineNumber + 1] - 1 : _length;
        if (end < start) end = start;
        return GetText(start, end - start);
    }

    public int GetLineStart(int lineNumber)
    {
        if (lineNumber < 0 || lineNumber >= _lineStarts.Length)
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        return _lineStarts[lineNumber];
    }

    public int GetLineNumberAt(int offset)
    {
        offset = Math.Clamp(offset, 0, _length);
        var lo = 0;
        var hi = _lineStarts.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var start = _lineStarts[mid];
            var next = mid + 1 < _lineStarts.Length ? _lineStarts[mid + 1] : _length + 1;
            if (offset < start) hi = mid - 1;
            else if (offset >= next) lo = mid + 1;
            else return mid;
        }
        return Math.Max(0, _lineStarts.Length - 1);
    }

    public string GetText(int offset, int length)
    {
        if (length <= 0) return "";
        offset = Math.Clamp(offset, 0, _length);
        length = Math.Min(length, _length - offset);
        var sb = new StringBuilder(length);
        AppendTo(sb, offset, length);
        return sb.ToString();
    }

    public void Replace(int offset, int deleteLength, string insert)
    {
        if (offset < 0 || offset > _length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (deleteLength < 0 || offset + deleteLength > _length) throw new ArgumentOutOfRangeException(nameof(deleteLength));
        if (deleteLength == 0 && insert.Length == 0) return;

        DeleteRange(offset, deleteLength);
        if (insert.Length > 0)
            Insert(offset, insert);
        RebuildLineStarts();
    }

    private void Insert(int offset, string text)
    {
        var addStart = _add.Length;
        _add.Append(text);
        var newPiece = new Piece(Source.Add, addStart, text.Length);

        if (_pieces.Count == 0)
        {
            _pieces.Add(newPiece);
            _length = text.Length;
            return;
        }

        if (offset >= _length)
        {
            _pieces.Add(newPiece);
            _length += text.Length;
            return;
        }

        if (offset == 0)
        {
            _pieces.Insert(0, newPiece);
            _length += text.Length;
            return;
        }

        Locate(offset, out var index, out var local);
        var piece = _pieces[index];
        if (local == 0)
        {
            _pieces.Insert(index, newPiece);
        }
        else if (local >= piece.Length)
        {
            _pieces.Insert(index + 1, newPiece);
        }
        else
        {
            var left = new Piece(piece.Source, piece.Start, local);
            var right = new Piece(piece.Source, piece.Start + local, piece.Length - local);
            _pieces[index] = left;
            _pieces.Insert(index + 1, newPiece);
            _pieces.Insert(index + 2, right);
        }
        _length += text.Length;
        Coalesce();
    }

    private void DeleteRange(int offset, int length)
    {
        if (length == 0) return;
        if (offset == 0 && length == _length)
        {
            _pieces.Clear();
            _length = 0;
            return;
        }

        var end = offset + length;
        Locate(offset, out var startIndex, out var startLocal);
        Locate(end - 1, out var endIndex, out var endLocal);
        endLocal++;

        if (startIndex == endIndex)
        {
            var piece = _pieces[startIndex];
            if (startLocal == 0 && endLocal >= piece.Length)
                _pieces.RemoveAt(startIndex);
            else if (startLocal == 0)
                _pieces[startIndex] = new Piece(piece.Source, piece.Start + endLocal, piece.Length - endLocal);
            else if (endLocal >= piece.Length)
                _pieces[startIndex] = new Piece(piece.Source, piece.Start, startLocal);
            else
            {
                var left = new Piece(piece.Source, piece.Start, startLocal);
                var right = new Piece(piece.Source, piece.Start + endLocal, piece.Length - endLocal);
                _pieces[startIndex] = left;
                _pieces.Insert(startIndex + 1, right);
            }
        }
        else
        {
            var startPiece = _pieces[startIndex];
            var endPiece = _pieces[endIndex];
            Piece? keepStart = startLocal > 0
                ? new Piece(startPiece.Source, startPiece.Start, startLocal)
                : null;
            Piece? keepEnd = endLocal < endPiece.Length
                ? new Piece(endPiece.Source, endPiece.Start + endLocal, endPiece.Length - endLocal)
                : null;

            _pieces.RemoveRange(startIndex, endIndex - startIndex + 1);
            var insertAt = startIndex;
            if (keepEnd.HasValue) _pieces.Insert(insertAt, keepEnd.Value);
            if (keepStart.HasValue) _pieces.Insert(insertAt, keepStart.Value);
        }

        _length -= length;
        if (_length < 0) _length = 0;
        Coalesce();
    }

    private void Locate(int offset, out int pieceIndex, out int localOffset)
    {
        if (offset < 0 || offset > _length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (_pieces.Count == 0)
        {
            pieceIndex = 0;
            localOffset = 0;
            return;
        }
        if (offset == _length)
        {
            pieceIndex = _pieces.Count - 1;
            localOffset = _pieces[pieceIndex].Length;
            return;
        }

        var remaining = offset;
        for (var i = 0; i < _pieces.Count; i++)
        {
            if (remaining < _pieces[i].Length)
            {
                pieceIndex = i;
                localOffset = remaining;
                return;
            }
            remaining -= _pieces[i].Length;
        }
        throw new ArgumentOutOfRangeException(nameof(offset));
    }

    private void AppendTo(StringBuilder sb, int offset, int length)
    {
        var remainingSkip = offset;
        var remainingTake = length;
        foreach (var piece in _pieces)
        {
            if (remainingTake <= 0) break;
            if (remainingSkip >= piece.Length)
            {
                remainingSkip -= piece.Length;
                continue;
            }
            var from = piece.Start + remainingSkip;
            var take = Math.Min(piece.Length - remainingSkip, remainingTake);
            if (piece.Source == Source.Original)
                sb.Append(_original, from, take);
            else
                sb.Append(_add, from, take);
            remainingSkip = 0;
            remainingTake -= take;
        }
    }

    private void Coalesce()
    {
        for (var i = 0; i < _pieces.Count - 1;)
        {
            var a = _pieces[i];
            var b = _pieces[i + 1];
            if (a.Source == b.Source && a.Start + a.Length == b.Start)
            {
                _pieces[i] = new Piece(a.Source, a.Start, a.Length + b.Length);
                _pieces.RemoveAt(i + 1);
                continue;
            }
            i++;
        }
    }

    private void RebuildLineStarts()
    {
        var starts = new List<int> { 0 };
        var offset = 0;
        foreach (var piece in _pieces)
        {
            for (var i = 0; i < piece.Length; i++)
            {
                var ch = piece.Source == Source.Original
                    ? _original[piece.Start + i]
                    : _add[piece.Start + i];
                if (ch == '\n')
                    starts.Add(offset + i + 1);
            }
            offset += piece.Length;
        }
        _lineStarts = starts.ToArray();
    }

}
