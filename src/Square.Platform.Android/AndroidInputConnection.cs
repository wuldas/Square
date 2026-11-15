using Java.Lang;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Square.Controls;
using AndroidView = global::Android.Views.View;

namespace Square.Platform.Android;

/// <summary>将 Android IME 的提交文本接入 Square 文本输入事件。</summary>
public sealed class AndroidInputConnection : BaseInputConnection
{
    private readonly AndroidPlatformHost _host;
    private readonly AndroidTextInputSession _session;
    private string _pendingComposition = "";

    /// <summary>创建绑定当前焦点编辑器的输入连接。</summary>
    public AndroidInputConnection(AndroidView target, AndroidPlatformHost host)
        : base(target, true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        host.PrepareInputConnection();
        _session = new AndroidTextInputSession(
            () => host.TextInputClientQuery?.Invoke(), () => host.HasTextInputFocus,
            () => host.TextInputEditorIdentity);
        host.RegisterInputConnection(this);
    }

    private ITextInputClient? Client => _session.Client;

    internal static InputTypes GetInputType(ITextInputClient? client) => client switch
    {
        Input { Type: "password" } => InputTypes.ClassText | InputTypes.TextVariationPassword | InputTypes.TextFlagNoSuggestions,
        Input { Type: "number" } => InputTypes.ClassNumber | InputTypes.NumberFlagSigned | InputTypes.NumberFlagDecimal,
        { IsMultiline: true } => InputTypes.ClassText | InputTypes.TextFlagMultiLine,
        _ => InputTypes.ClassText
    };

    internal static void ConfigureEditorInfo(EditorInfo info, ITextInputClient? client)
    {
        info.InputType = GetInputType(client);
        info.ImeOptions = ImeFlags.NoExtractUi;
        if (client is Input { Type: "password" }) info.ImeOptions |= ImeFlags.NoPersonalizedLearning;
        info.InitialSelStart = client?.SelectionStart ?? 0;
        info.InitialSelEnd = client?.SelectionEnd ?? 0;
    }

    internal void Deactivate()
    {
        _session.Close();
        _pendingComposition = "";
    }

    /// <inheritdoc />
    public override void CloseConnection()
    {
        Deactivate();
        base.CloseConnection();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) Deactivate();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override bool CommitText(ICharSequence? text, int newCursorPosition)
    {
        if (!_session.IsActive) return false;
        var value = text?.ToString() ?? "";
        if (Client is { } client)
            client.CommitText(value, newCursorPosition);
        else
        {
            _pendingComposition = "";
            _host.RaiseTextInput(value);
        }
        _host.NotifyTextInputChanged();
        return true;
    }

    /// <inheritdoc />
    public override bool SetComposingText(ICharSequence? text, int newCursorPosition)
    {
        if (!_session.IsActive) return false;
        var value = text?.ToString() ?? "";
        if (Client is { } client)
            client.SetComposingText(value, newCursorPosition);
        else
            _pendingComposition = value;
        _host.NotifyTextInputChanged();
        return true;
    }

    /// <inheritdoc />
    public override bool FinishComposingText()
    {
        if (!_session.IsActive) return false;
        if (Client is { } client)
            client.FinishComposingText();
        else if (_pendingComposition.Length > 0)
        {
            var text = _pendingComposition;
            _pendingComposition = "";
            _host.RaiseTextInput(text);
        }
        _host.NotifyTextInputChanged();
        return true;
    }

    /// <inheritdoc />
    public override bool DeleteSurroundingText(int beforeLength, int afterLength)
    {
        if (!_session.IsActive) return false;
        if (Client is { } client)
            client.DeleteSurroundingText(beforeLength, afterLength);
        else
        {
            _pendingComposition = "";
            for (var i = 0; i < System.Math.Max(0, beforeLength); i++)
            {
                _host.HandleKeyEvent(Keycode.Del, null, Square.Platform.KeyAction.Down);
                _host.HandleKeyEvent(Keycode.Del, null, Square.Platform.KeyAction.Up);
            }
        }
        _host.NotifyTextInputChanged();
        return true;
    }

    /// <inheritdoc />
    public override bool SetSelection(int start, int end)
    {
        if (!_session.IsActive) return false;
        if (Client is not { } client) return false;
        client.SetSelection(start, end);
        _host.NotifyTextInputChanged();
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
    public override bool SendKeyEvent(KeyEvent? e)
    {
        if (!_session.IsActive || e == null) return false;
        if (e.Action is not (KeyEventActions.Down or KeyEventActions.Up)) return false;
        return _host.HandleKeyEvent(e.KeyCode, e,
            e.Action == KeyEventActions.Down ? Square.Platform.KeyAction.Down : Square.Platform.KeyAction.Up);
    }

    /// <inheritdoc />
    public override bool PerformEditorAction(ImeAction actionCode)
    {
        if (!_session.IsActive) return false;
        if (Client?.PerformEditorAction((int)actionCode) == true)
        {
            _host.NotifyTextInputChanged();
            return true;
        }
        if (actionCode == ImeAction.None) return false;
        _host.HandleKeyEvent(Keycode.Enter, null, Square.Platform.KeyAction.Down);
        _host.HandleKeyEvent(Keycode.Enter, null, Square.Platform.KeyAction.Up);
        return true;
    }
}
