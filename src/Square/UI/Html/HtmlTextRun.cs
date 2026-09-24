using Square.UI.ElementApi;

namespace Square.UI.Html;

/// <summary>
/// Square-measured text run whose HTML representation is bare escaped text. Runs are UA-declared
/// <c>display:inline</c> so the flattened inline layout writes per-line fragments back and the
/// base Text paint uses them; without fragments (e.g. inside table cells) the whole run paints.
/// </summary>
public sealed class HtmlTextRun : Square.Controls.Text
{
    public HtmlTextRun()
    {
        ApplyInlineDisplay();
    }

    public HtmlTextRun(string text) : base(text)
    {
        ApplyInlineDisplay();
    }

    private void ApplyInlineDisplay() =>
        Style.SetCascaded("display", "inline", new CssSpecificity(0, 0, 0), important: false,
            persistent: true, origin: CssCascadeOrigin.UserAgent, authorSpecified: false);
}
