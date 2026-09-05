using Java.Lang;
using Android.Views;
using Android.Views.InputMethods;
using AndroidView = global::Android.Views.View;

namespace Square.Platform.Android;

/// <summary>将 Android IME 的提交文本接入 Square 文本输入事件。</summary>
public sealed class AndroidInputConnection : BaseInputConnection
{
    private readonly AndroidPlatformHost _host;
    private string _pendingComposition = "";

    /// <summary>创建输入连接。</summary>
    public AndroidInputConnection(AndroidView target, AndroidPlatformHost host)
        : base(target, true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    private Square.Controls.ITextInputClient? Client => _host.TextInputClientQuery?.Invoke();

    /// <inheritdoc />
    public override bool CommitText(ICharSequence? text, int newCursorPosition)
    {
        var value = text?.ToString() ?? "";
        if (Client is { } client)
        {
            client.CommitText(value, newCursorPosition);
            _host.RequestRenderFrame();
        }
        else
        {
            _pendingComposition = "";
            _host.RaiseTextInput(value);
        }
        return true;
    }

    /// <inheritdoc />
    public override bool SetComposingText(ICharSequence? text, int newCursorPosition)
    {
        var value = text?.ToString() ?? "";
        if (Client is { } client)
        {
            client.SetComposingText(value, newCursorPosition);
            _host.RequestRenderFrame();
        }
        else
            _pendingComposition = value;
        return true;
    }

    /// <inheritdoc />
    public override bool FinishComposingText()
    {
        if (Client is { } client)
        {
            client.FinishComposingText();
            _host.RequestRenderFrame();
        }
        else if (_pendingComposition.Length > 0)
        {
            var text = _pendingComposition;
            _pendingComposition = "";
            _host.RaiseTextInput(text);
        }
        return true;
    }

    /// <inheritdoc />
    public override bool DeleteSurroundingText(int beforeLength, int afterLength)
    {
        if (Client is { } client)
        {
            client.DeleteSurroundingText(beforeLength, afterLength);
            _host.RequestRenderFrame();
        }
        else
        {
            _pendingComposition = "";
            for (var i = 0; i < System.Math.Max(0, beforeLength); i++)
            {
                _host.HandleKeyEvent(Keycode.Del, null, Square.Platform.KeyAction.Down);
                _host.HandleKeyEvent(Keycode.Del, null, Square.Platform.KeyAction.Up);
            }
        }
        return true;
    }

    /// <inheritdoc />
    public override bool SetSelection(int start, int end)
    {
        if (Client is { } client)
        {
            client.SetSelection(start, end);
            _host.RequestRenderFrame();
        }
        return true;
    }

    /// <inheritdoc />
    public override ICharSequence? GetTextBeforeCursorFormatted(int n, GetTextFlags flags)
    {
        if (Client is not { } client) return null;
        var text = client.Text;
        var end = System.Math.Clamp(client.SelectionStart, 0, text.Length);
        var start = System.Math.Max(0, end - System.Math.Max(0, n));
        return new Java.Lang.String(text[start..end]);
    }

    /// <inheritdoc />
    public override ICharSequence? GetTextAfterCursorFormatted(int n, GetTextFlags flags)
    {
        if (Client is not { } client) return null;
        var text = client.Text;
        var start = System.Math.Clamp(client.SelectionEnd, 0, text.Length);
        var length = System.Math.Clamp(n, 0, text.Length - start);
        return new Java.Lang.String(text.Substring(start, length));
    }

    /// <inheritdoc />
    public override ICharSequence? GetSelectedTextFormatted(GetTextFlags flags)
    {
        if (Client is not { } client) return null;
        var text = client.Text;
        var start = System.Math.Clamp(client.SelectionStart, 0, text.Length);
        var end = System.Math.Clamp(client.SelectionEnd, start, text.Length);
        return new Java.Lang.String(text[start..end]);
    }

    /// <inheritdoc />
    public override bool PerformEditorAction(ImeAction actionCode)
    {
        if (Client?.PerformEditorAction((int)actionCode) == true)
        {
            _host.RequestRenderFrame();
            return true;
        }
        if (actionCode == ImeAction.None) return false;
        _host.HandleKeyEvent(Keycode.Enter, null, Square.Platform.KeyAction.Down);
        _host.HandleKeyEvent(Keycode.Enter, null, Square.Platform.KeyAction.Up);
        return true;
    }
}
