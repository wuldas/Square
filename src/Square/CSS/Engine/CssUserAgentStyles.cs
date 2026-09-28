using Square.CSS.Ast;
using Square.CSS.Tokenizer;

namespace Square.CSS.Engine;

internal static class CssUserAgentStyles
{
    // Chrome html.css form-control subset for light color-scheme.
    // Source: chromium third_party/blink/renderer/core/html/resources/html.css
    // Internal Blink features (-internal-*, @supports blink-feature, AppearanceBase)
    // are omitted; Square maps Button/Input/TextArea/Select/CheckBox/Radio selectors, with
    // type-qualified Input selectors (input[type=checkbox i]/[type=radio i]) covering the HTML hosts.
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
            font-family: Arial;
            font-size: 13.3333px;
            text-align: center;
            padding: 1px 6px;
            border: 2px outset ButtonBorder;
            background-color: ButtonFace;
            color: ButtonText;
        }
        Button:active {
            border-style: inset;
            /* Themes may barely show the inset bevel; make the pressed face visible. */
            background-color: #dedede;
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
            font-family: Arial;
            font-size: 13.3333px;
            min-height: 21px;
            padding: 1px 2px;
            border: 2px inset #767676;
            background-color: Field;
        }
        TextArea {
            appearance: auto;
            cursor: text;
            white-space: pre-wrap;
            font-family: monospace;
            font-size: 13.3333px;
            border: 1px solid #767676;
            background-color: Field;
            padding: 2px;
        }
        Select {
            appearance: auto;
            box-sizing: border-box;
            white-space: pre;
            font-family: Arial;
            font-size: 13.3333px;
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
        CheckBox:focus-visible, Radio:focus-visible,
        Input[type="checkbox" i]:focus-visible, Input[type="radio" i]:focus-visible {
            outline-offset: 2px;
        }
        CheckBox, Radio, Input[type="checkbox" i], Input[type="radio" i] {
            appearance: auto;
            box-sizing: border-box;
            cursor: default;
        }
        CheckBox, Input[type="checkbox" i] {
            margin: 3px 3px 3px 4px;
        }
        Radio, Input[type="radio" i] {
            margin: 3px 3px 0 5px;
        }
        CheckBox:disabled, Radio:disabled,
        Input[type="checkbox" i]:disabled, Input[type="radio" i]:disabled {
            color: GrayText;
            cursor: default;
        }
        /* HTML checkbox/radio hosts drop text-field padding, border, background and min-height.
           Their font remains the browser's Input default; author CSS still overrides UA rules. */
        Input[type="checkbox" i], Input[type="radio" i] {
            padding: initial;
            background-color: initial;
            border: initial;
            min-height: initial;
        }
        """;

    internal static CssStyleSheet Sheet { get; } =
        new CssParser(new CssTokenizer(Source).Tokenize()).Parse();
}
