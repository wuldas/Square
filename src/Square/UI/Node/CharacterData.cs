namespace Square.UI;

/// <summary>
/// DOM character data node base, aligned with <c>CharacterData</c>.
/// </summary>
public abstract class CharacterData : Node
{
    private string _data;

    /// <summary>构造并指定初始字符数据。</summary>
    protected CharacterData(string data = "")
    {
        _data = data ?? "";
    }

    /// <summary>字符数据内容。</summary>
    public string Data
    {
        get => _data;
        set
        {
            var next = value ?? "";
            if (_data == next) return;
            _data = next;
            ParentElement?.InvalidateLayout();
        }
    }

    /// <summary>字符数据长度。</summary>
    public int Length => _data.Length;

    /// <summary>从指定偏移处截取指定长度的子串（对齐 <c>substringData</c>）。</summary>
    public string SubstringData(int offset, int count)
    {
        ValidateOffset(offset);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return _data.Substring(offset, Math.Min(count, _data.Length - offset));
    }

    /// <summary>追加字符数据（对齐 <c>appendData</c>）。</summary>
    public void AppendData(string data) => Data = _data + (data ?? "");

    /// <summary>在指定偏移处插入字符数据（对齐 <c>insertData</c>）。</summary>
    public void InsertData(int offset, string data)
    {
        ValidateOffset(offset);
        Data = _data.Insert(offset, data ?? "");
    }

    /// <summary>从指定偏移处删除指定长度的字符（对齐 <c>deleteData</c>）。</summary>
    public void DeleteData(int offset, int count)
    {
        ValidateOffset(offset);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Data = _data.Remove(offset, Math.Min(count, _data.Length - offset));
    }

    /// <summary>替换指定区间的字符数据（对齐 <c>replaceData</c>）。</summary>
    public void ReplaceData(int offset, int count, string data)
    {
        DeleteData(offset, count);
        InsertData(offset, data);
    }

    private void ValidateOffset(int offset)
    {
        if (offset < 0 || offset > _data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
    }
}
