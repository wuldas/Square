using Square.Controls;

namespace Square.Platform.Android;

/// <summary>将输入连接绑定到创建时的编辑器，失效后永不切换到新的焦点。</summary>
internal sealed class AndroidTextInputSession
{
    private readonly Func<ITextInputClient?> _clientQuery;
    private readonly Func<bool> _editorFocusQuery;
    private readonly Func<object?> _editorIdentityQuery;
    private readonly ITextInputClient? _client;
    private readonly object? _editorIdentity;
    private readonly bool _hadEditorFocus;
    private bool _closed;

    public AndroidTextInputSession(
        Func<ITextInputClient?> clientQuery,
        Func<bool> editorFocusQuery,
        Func<object?>? editorIdentityQuery = null)
    {
        ArgumentNullException.ThrowIfNull(clientQuery);
        ArgumentNullException.ThrowIfNull(editorFocusQuery);
        _clientQuery = clientQuery;
        _editorFocusQuery = editorFocusQuery;
        _editorIdentityQuery = editorIdentityQuery ?? (() => clientQuery());
        _client = clientQuery();
        _editorIdentity = _editorIdentityQuery();
        _hadEditorFocus = editorFocusQuery();
    }

    public bool IsActive
    {
        get
        {
            if (_closed) return false;
            if (_hadEditorFocus && _editorFocusQuery() && ReferenceEquals(_client, _clientQuery()) &&
                ReferenceEquals(_editorIdentity, _editorIdentityQuery()))
                return true;
            Close();
            return false;
        }
    }

    public ITextInputClient? Client => IsActive ? _client : null;

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _client?.FinishComposingText();
    }
}
