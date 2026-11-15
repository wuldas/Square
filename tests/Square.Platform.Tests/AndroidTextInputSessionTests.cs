using Square.Controls;
using Square.Platform.Android;
using Xunit;

namespace Square.Platform.Tests;

public sealed class AndroidTextInputSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveSessionCommitsToItsOriginalEditor(bool multiline)
    {
        TextEditorBase editor = multiline ? new TextArea() : new Input();
        var session = new AndroidTextInputSession(() => editor, () => true);

        Assert.True(session.IsActive);
        Assert.Same(editor, session.Client);
        session.Client!.CommitText("accepted");

        Assert.Equal("accepted", editor.Value);
        Assert.True(session.IsActive);
    }

    [Fact]
    public void SwitchingEditorsClosesOriginalSessionWithoutEditingNewClient()
    {
        var original = new Input { Value = "a" };
        original.SetSelection(1, 1);
        original.SetComposingText("ni");
        var next = new TextArea { Value = "b" };
        next.SetSelection(1, 1);
        next.SetComposingText("hao");
        ITextInputClient current = original;
        var session = new AndroidTextInputSession(() => current, () => true);

        current = next;
        session.Client?.CommitText("late");

        Assert.False(session.IsActive);
        Assert.Null(session.Client);
        Assert.Equal("ani", original.Value);
        Assert.Equal(-1, original.CompositionStart);
        Assert.Equal("bhao", next.Value);
        Assert.Equal(1, next.CompositionStart);
    }

    [Fact]
    public void ReturningToOriginalEditorDoesNotReactivateClosedSession()
    {
        var original = new Input { Value = "a" };
        ITextInputClient current = original;
        var session = new AndroidTextInputSession(() => current, () => true);

        current = new TextArea();
        Assert.False(session.IsActive);
        current = original;
        session.Client?.CommitText("late");

        Assert.False(session.IsActive);
        Assert.Null(session.Client);
        Assert.Equal("a", original.Value);
    }

    [Fact]
    public void LosingEditorFocusPermanentlyClosesSessionEvenWithSameClient()
    {
        var editor = new Input();
        var hasFocus = true;
        var session = new AndroidTextInputSession(() => editor, () => hasFocus);

        hasFocus = false;
        Assert.False(session.IsActive);
        hasFocus = true;
        session.Client?.CommitText("late");

        Assert.False(session.IsActive);
        Assert.Equal("", editor.Value);
    }

    [Fact]
    public void LosingClientWhileSuspendedClosesCapturedComposition()
    {
        var editor = new TextArea { Value = "a" };
        editor.SetSelection(1, 1);
        editor.SetComposingText("ni");
        ITextInputClient? current = editor;
        var session = new AndroidTextInputSession(() => current, () => true);

        current = null;
        Assert.False(session.IsActive);
        current = editor;

        Assert.False(session.IsActive);
        Assert.Equal("ani", editor.Value);
        Assert.Equal(-1, editor.CompositionStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseFinishesCompositionAsOneUndoableEdit(bool multiline)
    {
        TextEditorBase editor = multiline ? new TextArea() : new Input();
        editor.Value = "a";
        editor.SetSelection(1, 1);
        var session = new AndroidTextInputSession(() => editor, () => true);
        editor.SetComposingText("ni");
        editor.SetComposingText("你");

        session.Close();
        session.Close();

        Assert.False(session.IsActive);
        Assert.Null(session.Client);
        Assert.Equal("a你", editor.Value);
        Assert.Equal(-1, editor.CompositionStart);
        Assert.True(editor.CanUndo);
        editor.HandleKey(90, control: true);
        Assert.Equal("a", editor.Value);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void RepeatedCloseDoesNotFinishLaterCompositionOnSameEditor()
    {
        var editor = new Input();
        var session = new AndroidTextInputSession(() => editor, () => true);
        editor.SetComposingText("first");
        session.Close();
        editor.SetComposingText("second");

        session.Close();

        Assert.False(session.IsActive);
        Assert.Equal("firstsecond", editor.Value);
        Assert.Equal(5, editor.CompositionStart);
    }

    [Fact]
    public void LegacyEditorSessionStaysActiveOnlyUntilFocusIsLost()
    {
        var hasFocus = true;
        var session = new AndroidTextInputSession(() => null, () => hasFocus);

        Assert.True(session.IsActive);
        Assert.Null(session.Client);
        hasFocus = false;
        Assert.False(session.IsActive);
        hasFocus = true;

        Assert.False(session.IsActive);
        Assert.Null(session.Client);
    }

    [Fact]
    public void LegacyEditorIdentitySwitchPermanentlyClosesSession()
    {
        var original = new object();
        object current = original;
        var session = new AndroidTextInputSession(() => null, () => true, () => current);

        Assert.True(session.IsActive);
        current = new object();
        Assert.False(session.IsActive);
        current = original;

        Assert.False(session.IsActive);
        Assert.Null(session.Client);
    }

    [Fact]
    public void SameLegacyEditorIdentityKeepsSessionActive()
    {
        var editor = new object();
        var session = new AndroidTextInputSession(() => null, () => true, () => editor);

        Assert.True(session.IsActive);
        Assert.Null(session.Client);
        Assert.True(session.IsActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionCreatedWithoutEditorFocusNeverActivates(bool hasClientAtCreation)
    {
        var hasFocus = false;
        var editor = new Input();
        ITextInputClient? current = hasClientAtCreation ? editor : null;
        var session = new AndroidTextInputSession(() => current, () => hasFocus);
        current = editor;
        hasFocus = true;

        Assert.False(session.IsActive);
        Assert.Null(session.Client);
        Assert.Equal("", editor.Value);
    }

    [Fact]
    public void NewSessionCanCommitAfterOldSessionCloses()
    {
        var original = new Input { Value = "a" };
        var next = new TextArea { Value = "b" };
        next.SetSelection(1, 1);
        ITextInputClient current = original;
        var previousSession = new AndroidTextInputSession(() => current, () => true);
        previousSession.Close();
        current = next;
        var nextSession = new AndroidTextInputSession(() => current, () => true);

        previousSession.Client?.CommitText("late");
        nextSession.Client!.CommitText("accepted");

        Assert.False(previousSession.IsActive);
        Assert.True(nextSession.IsActive);
        Assert.Equal("a", original.Value);
        Assert.Equal("baccepted", next.Value);
    }
}
