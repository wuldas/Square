using Square.Controls;
using Square.Graphics;
using Square.UI;

namespace Square.CSS.Tests;

public static class Css21ConformanceFixtureCatalog
{
    private static readonly IReadOnlyDictionary<string, Rect> EmptyGeometry =
        new Dictionary<string, Rect>();

    private static readonly IReadOnlyDictionary<string, Css21PaintFlags> EmptyPaint =
        new Dictionary<string, Css21PaintFlags>();

    public static IReadOnlyList<Css21Fixture> All { get; } =
    [
        new Css21Fixture(
            "CSS21-SYNTAX-001",
            "CSS21-SYNTAX",
            "Syntax",
            "/* comments are removed */ View { color: red; width: 40px; }",
            () =>
            {
                var root = new View();
                return Document(root, ("root", root));
            },
            new Dictionary<string, string?>
            {
                ["root.color"] = "red",
                ["root.width"] = "40px"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(120, 40)),

        new Css21Fixture(
            "CSS21-SELECTORS-001",
            "CSS21-SELECTORS",
            "Selectors",
            "View#root > .target { color: red; } View .target { background-color: #00ff00; } .target { font-weight: bold; }",
            () =>
            {
                var root = new View { Id = "root" };
                var target = new View();
                target.ClassList.Add("target");
                root.Children.Add(target);
                return Document(root, ("root", root), ("target", target));
            },
            new Dictionary<string, string?>
            {
                ["target.color"] = "red",
                ["target.background-color"] = "#00ff00",
                ["target.font-weight"] = "bold"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(160, 60)),

        new Css21Fixture(
            "CSS21-CASCADE-001",
            "CSS21-CASCADE",
            "Cascade",
            "View { font-size: 14px; color: #111111; } Text { color: red; } .target { color: blue; } #target { color: green; } Text { color: black !important; }",
            () =>
            {
                var root = new View();
                var target = new Square.Controls.Text("cascade") { Id = "target" };
                target.ClassList.Add("target");
                root.Children.Add(target);
                return Document(root, ("root", root), ("target", target));
            },
            new Dictionary<string, string?>
            {
                ["target.color"] = "black",
                ["target.font-size"] = "14px"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(180, 60)),

        new Css21Fixture(
            "CSS21-VALUES-001",
            "CSS21-VALUES",
            "Values",
            "Text { font-family: FixtureSans, sans-serif; font-size: 18px; font-weight: 700; font-style: italic; line-height: 1.5; color: #123456; }",
            () =>
            {
                var root = new View();
                var target = new Square.Controls.Text("values");
                root.Children.Add(target);
                return Document(root, ("root", root), ("target", target));
            },
            new Dictionary<string, string?>
            {
                ["target.font-family"] = "FixtureSans, sans-serif",
                ["target.font-size"] = "18px",
                ["target.font-weight"] = "700",
                ["target.font-style"] = "italic",
                ["target.line-height"] = "1.5",
                ["target.color"] = "#123456"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(220, 60)),

        new Css21Fixture(
            "CSS21-BOX-001",
            "CSS21-BOX",
            "Box model",
            "View { display: block; } .box { display: block; width: 100px; height: 20px; padding: 10px; margin-left: auto; margin-right: auto; }",
            () =>
            {
                var root = new View();
                var box = new View();
                box.ClassList.Add("box");
                root.Children.Add(box);
                return Document(root, ("root", root), ("box", box));
            },
            new Dictionary<string, string?>
            {
                ["box.width"] = "100px",
                ["box.padding"] = "10px",
                ["box.margin-left"] = "auto"
            },
            new Dictionary<string, Rect>
            {
                ["root"] = new Rect(0, 0, 200, 100),
                ["box"] = new Rect(40, 0, 120, 40)
            },
            BoxPaintFlags("root", "box"),
            new Size(200, 100)),

        new Css21Fixture(
            "CSS21-VISUAL-001",
            "CSS21-VISUAL-FORMATTING",
            "Visual formatting",
            "View { display: block; } .relative { display: block; position: relative; width: 30px; height: 20px; left: 7px; top: 9px; } .following { display: block; height: 10px; }",
            () =>
            {
                var root = new View();
                var relative = new View();
                relative.ClassList.Add("relative");
                var following = new View();
                following.ClassList.Add("following");
                root.Children.Add(relative);
                root.Children.Add(following);
                return Document(root, ("root", root), ("relative", relative), ("following", following));
            },
            new Dictionary<string, string?>
            {
                ["relative.position"] = "relative",
                ["relative.left"] = "7px",
                ["relative.top"] = "9px"
            },
            new Dictionary<string, Rect>
            {
                ["relative"] = new Rect(7, 9, 30, 20),
                ["following"] = new Rect(0, 20, 120, 10)
            },
            EmptyPaint,
            new Size(120, 80)),

        new Css21Fixture(
            "CSS21-GENERATED-001",
            "CSS21-GENERATED-CONTENT",
            "Generated content",
            ".target::before { content: \"[\"; } .target::after { content: \"]\"; }",
            () =>
            {
                var root = new View();
                root.ClassList.Add("target");
                return Document(root, ("root", root));
            },
            new Dictionary<string, string?>(),
            EmptyGeometry,
            EmptyPaint,
            new Size(140, 40),
            ExpectedChildText: new Dictionary<string, IReadOnlyList<string>>
            {
                ["root"] = ["[", "]"]
            }),

        new Css21Fixture(
            "CSS21-TABLES-001",
            "CSS21-TABLES",
            "Tables",
            "Table { width: 200px; table-layout: fixed; border-spacing: 0; } TableCell { height: 20px; }",
            () =>
            {
                var table = new Table();
                var row = new TableRow();
                var first = new TableCell();
                var second = new TableCell();
                row.Children.Add(first);
                row.Children.Add(second);
                table.Children.Add(row);
                return Document(table, ("table", table), ("row", row), ("first", first), ("second", second));
            },
            new Dictionary<string, string?>
            {
                ["table.table-layout"] = "fixed",
                ["first.height"] = "20px"
            },
            new Dictionary<string, Rect>
            {
                ["first"] = new Rect(0, 0, 100, 20),
                ["second"] = new Rect(100, 0, 100, 20)
            },
            EmptyPaint,
            new Size(200, 40)),

        new Css21Fixture(
            "CSS21-FONTS-001",
            "CSS21-FONTS",
            "Fonts",
            "View { font-family: FixtureSans; font-size: 17px; font-weight: bold; font-style: italic; } Text { line-height: 1.4; }",
            () =>
            {
                var root = new View();
                var target = new Square.Controls.Text("font fixture");
                root.Children.Add(target);
                return Document(root, ("root", root), ("target", target));
            },
            new Dictionary<string, string?>
            {
                ["target.font-family"] = "FixtureSans",
                ["target.font-size"] = "17px",
                ["target.font-weight"] = "bold",
                ["target.font-style"] = "italic",
                ["target.line-height"] = "1.4"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(240, 60)),

        new Css21Fixture(
            "CSS21-MEDIA-SCREEN-001",
            "CSS21-MEDIA",
            "Media",
            "View { base: yes; } @media screen { View { mode: screen; } } @media print { View { mode: print; } }",
            () =>
            {
                var root = new View();
                return Document(root, ("root", root));
            },
            new Dictionary<string, string?>
            {
                ["root.base"] = "yes",
                ["root.mode"] = "screen"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(140, 40),
            "screen"),

        new Css21Fixture(
            "CSS21-MEDIA-PRINT-001",
            "CSS21-MEDIA",
            "Media",
            "@media screen { View { mode: screen; } } @media print { View { mode: print; } }",
            () =>
            {
                var root = new View();
                return Document(root, ("root", root));
            },
            new Dictionary<string, string?>
            {
                ["root.mode"] = "print"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(140, 40),
            "print"),

        new Css21Fixture(
            "CSS21-AT-FONT-FACE-001",
            "CSS21-AT-FONT-FACE",
            "Font face",
            "@font-face { font-family: FixtureLocal; src: url(fixture.ttf); font-weight: 600; font-style: italic; } View { font-family: FixtureLocal; font-weight: 600; font-style: italic; }",
            () =>
            {
                var root = new View();
                return Document(root, ("root", root));
            },
            new Dictionary<string, string?>
            {
                ["root.font-family"] = "FixtureLocal",
                ["root.font-weight"] = "600",
                ["root.font-style"] = "italic"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(160, 40),
            ExpectedFontFaces: new Css21FontFaceExpectation(
                1,
                [new Css21FontFaceDescriptorExpectation("FixtureLocal", "fixture.ttf", true)])),

        new Css21Fixture(
            "CSS21-BIDI-001",
            "CSS21-BIDI",
            "Bidirectional text",
            "View { direction: rtl; unicode-bidi: embed; } Text { direction: ltr; unicode-bidi: bidi-override; }",
            () =>
            {
                var root = new View();
                var target = new Square.Controls.Text("abc \u05e9\u05dc\u05d5\u05dd");
                root.Children.Add(target);
                return Document(root, ("root", root), ("target", target));
            },
            new Dictionary<string, string?>
            {
                ["root.direction"] = "rtl",
                ["root.unicode-bidi"] = "embed",
                ["target.direction"] = "ltr",
                ["target.unicode-bidi"] = "bidi-override"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(220, 60)),

        new Css21Fixture(
            "CSS21-BIDI-002",
            "CSS21-BIDI",
            "Bidirectional text",
            "View { direction: ltr; unicode-bidi: normal; } Text { direction: ltr; unicode-bidi: normal; }",
            () =>
            {
                var root = new View();
                var target = new Square.Controls.Text("A \u05d0\u05d1\u05d2 123");
                root.Children.Add(target);
                return Document(root, ("root", root), ("target", target));
            },
            new Dictionary<string, string?>
            {
                ["root.direction"] = "ltr",
                ["root.unicode-bidi"] = "normal",
                ["target.direction"] = "ltr",
                ["target.unicode-bidi"] = "normal"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(240, 60),
            ExpectedTextLayout: new Css21TextLayoutExpectation(
                "target",
                [0, 1, 4, 3, 2, 5, 6, 7, 8],
                [
                    new Css21TextHitTestExpectation(2, true, 5),
                    new Css21TextHitTestExpectation(2, false, 4)
                ])),

        new Css21Fixture(
            "CSS21-MEDIA-SWITCH-001",
            "CSS21-MEDIA",
            "Media switching",
            "View { base: yes; } @media screen { View { mode: screen; } } @media print { View { mode: print; } }",
            () =>
            {
                var root = new View();
                return Document(root, ("root", root));
            },
            new Dictionary<string, string?>
            {
                ["root.base"] = "yes",
                ["root.mode"] = "screen"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(140, 40),
            ExpectedMediaSwitch: new Css21MediaSwitch(
                "print",
                new Dictionary<string, string?>
                {
                    ["root.base"] = "yes",
                     ["root.mode"] = "print"
                 })),

        new Css21Fixture(
            "CSS22-FONT-FAMILY-KEYWORDS-001",
            "CSS22-FONT-FAMILY-KEYWORDS",
            "CSS2.2 revision/errata",
            "Text { font-family: serif, sans-serif; }",
            () =>
            {
                var root = new View();
                var target = new Square.Controls.Text("font keywords");
                root.Children.Add(target);
                return Document(root, ("root", root), ("target", target));
            },
            new Dictionary<string, string?>
            {
                ["target.font-family"] = "serif, sans-serif"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(220, 60)),

        new Css21Fixture(
            "CSS22-MARGIN-COLLAPSE-001",
            "CSS22-MARGIN-COLLAPSE",
            "CSS2.2 revision/errata",
            "View { display: block; } .first { display: block; height: 20px; margin-bottom: 30px; } .second { display: block; height: 20px; margin-top: 20px; }",
            () =>
            {
                var root = new View();
                var first = new View();
                first.ClassList.Add("first");
                var second = new View();
                second.ClassList.Add("second");
                root.Children.Add(first);
                root.Children.Add(second);
                return Document(root, ("root", root), ("first", first), ("second", second));
            },
            new Dictionary<string, string?>
            {
                ["first.margin-bottom"] = "30px",
                ["second.margin-top"] = "20px"
            },
            new Dictionary<string, Rect>
            {
                ["first"] = new Rect(0, 0, 120, 20),
                ["second"] = new Rect(0, 50, 120, 20)
            },
            EmptyPaint,
            new Size(120, 100)),

        new Css21Fixture(
            "CSS22-FORMATTING-CONTEXTS-CONTAINING-BLOCKS-001",
            "CSS22-FORMATTING-CONTEXTS-CONTAINING-BLOCKS",
            "CSS2.2 revision/errata",
            "View { display: block; } .absolute { display: block; position: absolute; left: 10px; right: 20px; top: 5px; bottom: 15px; }",
            () =>
            {
                var root = new View();
                var absolute = new View();
                absolute.ClassList.Add("absolute");
                root.Children.Add(absolute);
                return Document(root, ("root", root), ("absolute", absolute));
            },
            new Dictionary<string, string?>
            {
                ["absolute.position"] = "absolute"
            },
            new Dictionary<string, Rect>
            {
                ["absolute"] = new Rect(10, 5, 90, 70)
            },
            EmptyPaint,
            new Size(120, 90)),

        new Css21Fixture(
            "CSS22-OVERFLOW-TABLE-BEHAVIOR-001",
            "CSS22-OVERFLOW-TABLE-BEHAVIOR",
            "CSS2.2 revision/errata",
            "Table { width: 120px; table-layout: fixed; overflow: hidden; }",
            () =>
            {
                var table = new Table();
                var row = new TableRow();
                row.Children.Add(new TableCell());
                table.Children.Add(row);
                return Document(table, ("table", table), ("row", row));
            },
            new Dictionary<string, string?>
            {
                ["table.overflow"] = "hidden",
                ["table.table-layout"] = "fixed"
            },
            new Dictionary<string, Rect>
            {
                ["table"] = new Rect(0, 0, 120, 40)
            },
            EmptyPaint,
            new Size(120, 40)),

        new Css21Fixture(
            "CSS22-TABLE-WRAPPER-001",
            "CSS22-TABLE-WRAPPER",
            "CSS2.2 revision/errata",
            "InlineTable { width: 120px; table-layout: fixed; } TableCell { height: 20px; }",
            () =>
            {
                var table = new InlineTable();
                var row = new TableRow();
                var first = new TableCell();
                var second = new TableCell();
                row.Children.Add(first);
                row.Children.Add(second);
                table.Children.Add(row);
                return Document(table, ("table", table), ("row", row), ("first", first), ("second", second));
            },
            new Dictionary<string, string?>
            {
                ["table.display"] = "inline-table",
                ["first.height"] = "20px"
            },
            new Dictionary<string, Rect>
            {
                ["first"] = new Rect(0, 0, 60, 20),
                ["second"] = new Rect(60, 0, 60, 20)
            },
            EmptyPaint,
            new Size(120, 40)),

        new Css21Fixture(
            "CSS22-HEIGHT-PERCENTAGE-COMPUTED-001",
            "CSS22-HEIGHT-PERCENTAGE-COMPUTED",
            "CSS2.2 revision/errata",
            "View { display: block; height: 80px; } .child { display: block; height: 50%; }",
            () =>
            {
                var root = new View();
                var child = new View();
                child.ClassList.Add("child");
                root.Children.Add(child);
                return Document(root, ("root", root), ("child", child));
            },
            new Dictionary<string, string?>
            {
                ["child.height"] = "50%"
            },
            EmptyGeometry,
            EmptyPaint,
            new Size(120, 80))
    ];

    private static IReadOnlyDictionary<string, Css21PaintFlags> BoxPaintFlags(params string[] names) =>
        names.ToDictionary(
            name => name,
            _ => Css21PaintFlags.NeedsPaint | Css21PaintFlags.FullPaintDirty |
                 Css21PaintFlags.HasGeometry | Css21PaintFlags.Displayed,
            StringComparer.Ordinal);

    private static Css21FixtureDocument Document(Element root, params (string Name, Element Element)[] elements) =>
        new(root, elements.ToDictionary(pair => pair.Name, pair => pair.Element, StringComparer.Ordinal));
}
