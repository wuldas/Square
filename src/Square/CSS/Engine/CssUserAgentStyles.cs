using Square.CSS.Ast;
using Square.CSS.Tokenizer;

namespace Square.CSS.Engine;

internal static class CssUserAgentStyles
{
    // Chrome html.css form-control subset for light color-scheme.
    // Source: chromium third_party/blink/renderer/core/html/resources/html.css
    // Internal Blink features (-internal-*, @supports blink-feature, AppearanceBase)
    // are omitted; Square maps Button/Input/TextArea/Select/CheckBox/Radio selectors.
    internal const string Source = """
        Button, Input, TextArea, Select {
            margin: 0;
            color: FieldText;
            letter-spacing: normal;
            word-spacing: normal;
            line-height: normal;
            text-transform: none;
            text-indent: 0;
            text-align: start;
        }
        Button {
            appearance: auto;
            cursor: default;
            box-sizing: border-box;
            text-align: center;
            padding: 1px 6px;
            border: 2px outset ButtonBorder;
            background-color: ButtonFace;
            color: ButtonText;
        }
        Button:active {
            border-style: inset;
        }
        Button:active:disabled {
            border-style: outset;
        }
        Button:disabled {
            background-color: rgba(239, 239, 239, 0.3);
            border-color: rgba(118, 118, 118, 0.3);
            color: rgba(16, 16, 16, 0.3);
        }
        Input {
            appearance: auto;
            cursor: text;
            padding: 1px 2px;
            border: 2px inset #767676;
            background-color: Field;
        }
        TextArea {
            appearance: auto;
            cursor: text;
            white-space: pre-wrap;
            font-family: monospace;
            border: 1px solid #767676;
            background-color: Field;
            padding: 2px;
        }
        Select {
            appearance: auto;
            box-sizing: border-box;
            white-space: pre;
            color: FieldText;
            background-color: Field;
            border: 1px solid #767676;
            cursor: default;
            border-radius: 0;
        }
        Input:disabled, TextArea:disabled {
            cursor: default;
            background-color: rgba(239, 239, 239, 0.3);
            color: #545454;
            border-color: rgba(118, 118, 118, 0.3);
        }
        Select:disabled {
            opacity: 0.7;
            color: GrayText;
            border-color: rgba(118, 118, 118, 0.3);
        }
        :focus-visible {
            outline: 1px solid Highlight;
        }
        Input:focus-visible, TextArea:focus-visible, Select:focus-visible, Button:focus-visible {
            outline-offset: 0;
        }
        CheckBox:focus-visible, Radio:focus-visible {
            outline-offset: 2px;
        }
        CheckBox, Radio {
            appearance: auto;
            box-sizing: border-box;
            cursor: default;
        }
        CheckBox:disabled, Radio:disabled {
            color: GrayText;
            cursor: default;
        }
        """;

    internal static CssStyleSheet Sheet { get; } =
        new CssParser(new CssTokenizer(Source).Tokenize()).Parse();
}
