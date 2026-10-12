using System.Globalization;
using Square.Backends;
using Square.Controls;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Graphics;
using Square.Html;
using Square.Native.Html;
using Square.Rendering;
using Square.Runtime.Binding;
using Square.Runtime.State;
using Square.UI;
using Square.UI.Svg;
using Xunit;
using BoundText = Square.Controls.Text;
using DomText = Square.UI.Text;

namespace Square.Native.Html.Tests;

/// <summary>
/// 加载真实 HTML 后的原生属性与渲染验证：SVG 别名必须落在正确的原生字段上，
/// 作者作用域必须在 mount/unmount/替换循环中只影响自己的子树。
/// </summary>
public class HtmlLoaderRenderingTests
{
    [Fact]
    public void SvgAttributesMapToNativePropertiesAndPaintThroughSoftwareRenderer()
    {
        var root = HtmlLoader.LoadFile(FixturePath("svg.html"));
        try
        {
            Assert.IsType<HTMLHtmlElement>(root);
            Assert.IsType<HTMLBodyElement>(root.QuerySelector("body"));
            var svg = Assert.IsType<SVGSVGElement>(root.QuerySelector("svg"));
            var circle = Assert.IsType<SVGCircleElement>(svg.QuerySelector("circle"));
            Assert.Same(svg, circle.Parent);

            // 别名必须落在真实原生字段：viewBox→ViewBox、cx/cy→CenterX/CenterY、r→Radius。
            Assert.Equal("0 0 40 40", svg.ViewBox);
            Assert.Equal(20f, NumericProperty(circle, "CenterX"));
            Assert.Equal(20f, NumericProperty(circle, "CenterY"));
            Assert.Equal(8f, NumericProperty(circle, "Radius"));

            var layout = new LayoutEngine();
            layout.MeasureAndArrange(root, new Size(96, 96));
            Assert.True(svg.Geometry.Width >= 40f && svg.Geometry.Height >= 40f, $"svg={svg.Geometry}");

            var tree = new DisplayTree();
            tree.BuildFrom(root);
            const int canvas = 96;
            using var surface = new BitmapSoftwareRenderSurface(canvas, canvas);
            using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
            {
                CanvasSize = new Size(canvas, canvas),
                DpiScale = 1f,
                SoftwareSurface = surface,
            });
            context.Clear(Color.White);
            tree.Render(context);

            // 半径环带采样：内 6px 必须红、外 10px 必须白。cx/cy/r 若映射到错误字段
            // （半径错位、中心平移、viewBox 解析失败），中心或环带像素立即失配。
            var origin = svg.Geometry.Position;
            AssertPixel(surface, origin.X + 20, origin.Y + 20, IsRed, "circle center");
            AssertPixel(surface, origin.X + 26, origin.Y + 20, IsRed, "inside right");
            AssertPixel(surface, origin.X + 14, origin.Y + 20, IsRed, "inside left");
            AssertPixel(surface, origin.X + 20, origin.Y + 26, IsRed, "inside bottom");
            AssertPixel(surface, origin.X + 20, origin.Y + 14, IsRed, "inside top");
            AssertPixel(surface, origin.X + 30, origin.Y + 20, IsWhite, "outside right");
            AssertPixel(surface, origin.X + 10, origin.Y + 20, IsWhite, "outside left");
            AssertPixel(surface, origin.X + 20, origin.Y + 30, IsWhite, "outside bottom");
            AssertPixel(surface, origin.X + 20, origin.Y + 10, IsWhite, "outside top");
            AssertPixel(surface, origin.X + 52, origin.Y + 20, IsWhite, "beside the svg box");
        }
        finally
        {
            root.DiscardGeneratedSubtree();
        }
    }

    [Fact]
    public void AuthorScopeSurvivesMountUnmountAndFreshReplacementCycle()
    {
        var root = HtmlLoader.LoadFile(FixturePath("scope.html"));
        try
        {
            var header = Assert.IsType<HTMLHeaderElement>(root.QuerySelector("header"));
            var footer = Assert.IsType<HTMLFooterElement>(root.QuerySelector("footer"));
            var firstSlot = Assert.IsType<HTMLDivElement>(root.QuerySelector("#first-slot"));
            var secondSlot = Assert.IsType<HTMLDivElement>(root.QuerySelector("#second-slot"));
            var loadedBody = Assert.IsType<HTMLBodyElement>(root.QuerySelector("body"));
            var firstPlaceholder = Assert.IsType<DomText>(Assert.Single(firstSlot.ChildNodes));
            Assert.Equal("First slot placeholder", firstPlaceholder.Data);

            // 作者作用域在加载时即已解析到静态树，无需任何文档参与。
            Assert.Equal("#eef2f7", root.Style.Get("background-color"));
            Assert.Equal("7px", loadedBody.Style.Get("padding-top"));
            Assert.Equal("#8a4b08", header.Style.Get("color"));
            Assert.Equal("#4c5b70", footer.Style.Get("color"));
            Assert.Equal("11px", firstSlot.Style.Get("padding-left"));
            Assert.Equal("29px", secondSlot.Style.Get("padding-left"));

            var document = new UIDocument();
            var shellBodyPadding = document.Body.Style.Get("padding-top");
            var shellBackground = document.Ui.Style.Get("background-color");
            document.Body.Children.Add(root);
            document.FlushPendingUpdates();

            using var firstMount = root.Mount("#first-slot", new ScopedCounter("first 0"));
            var first = (ScopedCounter)firstMount.Component;
            using var secondMount = secondSlot.Mount(new ScopedCounter("second 0"));
            var second = (ScopedCounter)secondMount.Component;
            document.FlushPendingUpdates();

            // 挂载替换了占位文本，但保留目标元素本身与已应用的作者样式。
            Assert.Same(first, Assert.Single(firstSlot.ChildNodes));
            Assert.Null(firstPlaceholder.ParentNode);
            Assert.Equal("first-slot", firstSlot.Id);
            Assert.Equal("11px", firstSlot.Style.Get("padding-left"));
            Assert.Same(document, first.OwnerDocument);
            Assert.Equal("first 0", first.Label.TextContent);
            Assert.Equal("second 0", second.Label.TextContent);
            // 作者后代规则命中动态子树，组件自身 scope 同时生效。
            Assert.Equal("#12406e", first.Label.Style.Get("color"));
            Assert.Equal("#f5f7fa", first.Label.Style.Get("background-color"));
            Assert.Equal("#12406e", second.Label.Style.Get("color"));
            Assert.Equal("#f5f7fa", second.Label.Style.Get("background-color"));

            // 绑定的真实控件文本随响应式值更新，且只影响各自组件。
            first.Display.Value = "first 1";
            document.FlushPendingUpdates();
            Assert.Equal("first 1", first.Label.TextContent);
            Assert.Equal("second 0", second.Label.TextContent);

            // 卸载后容器保留，作者作用域在容器被清空后仍然存活。
            firstMount.Unmount();
            Assert.False(firstMount.IsMounted);
            Assert.Empty(firstSlot.ChildNodes);
            Assert.Equal("first-slot", firstSlot.Id);
            Assert.Equal("11px", firstSlot.Style.Get("padding-left"));
            Assert.Equal("#8a4b08", header.Style.Get("color"));
            Assert.Equal("#4c5b70", footer.Style.Get("color"));

            // 已释放组件的绑定不再跟踪；作用域释放只影响被卸载的子树。
            first.Display.Value = "first stale";
            document.FlushPendingUpdates();
            Assert.Equal("first 1", first.Label.TextContent);
            // 生成作用域与登记资源随永久卸载恰好释放一次，但静态树与兄弟挂载不受影响。
            Assert.Equal(1, first.Resource.DisposeCount);
            second.Display.Value = "second 1";
            document.FlushPendingUpdates();
            Assert.Equal("second 1", second.Label.TextContent);
            Assert.Equal("#12406e", second.Label.Style.Get("color"));
            Assert.Equal("#f5f7fa", second.Label.Style.Get("background-color"));

            // 重新挂载必须使用新实例，默认文本与两组样式完整重建。
            using var freshMount = firstSlot.Mount(new ScopedCounter("fresh 0"));
            var fresh = (ScopedCounter)freshMount.Component;
            document.FlushPendingUpdates();
            Assert.Same(fresh, Assert.Single(firstSlot.ChildNodes));
            Assert.Equal("fresh 0", fresh.Label.TextContent);
            Assert.Equal("#12406e", fresh.Label.Style.Get("color"));
            Assert.Equal("#f5f7fa", fresh.Label.Style.Get("background-color"));
            fresh.Display.Value = "fresh 1";
            document.FlushPendingUpdates();
            Assert.Equal("fresh 1", fresh.Label.TextContent);

            // 静态外壳可正常布局：header、两个槽、footer 依次排列且非退化。
            var layout = new LayoutEngine();
            layout.MeasureAndArrange(root, new Size(320, 240));
            Assert.All(new Element[] { header, firstSlot, secondSlot, footer },
                static element => Assert.True(element.Geometry.Height > 0, $"{element.TagName} has no layout height"));
            Assert.True(header.Geometry.Y < firstSlot.Geometry.Y, $"header={header.Geometry} first={firstSlot.Geometry}");
            Assert.True(firstSlot.Geometry.Y < secondSlot.Geometry.Y, $"first={firstSlot.Geometry} second={secondSlot.Geometry}");
            Assert.True(secondSlot.Geometry.Y + secondSlot.Geometry.Height <= footer.Geometry.Y + 0.5f,
                $"second={secondSlot.Geometry} footer={footer.Geometry}");

            // html/body 规则从未越过加载根影响外层 UIDocument 壳。
            Assert.Equal(shellBodyPadding, document.Body.Style.Get("padding-top"));
            Assert.Equal(shellBackground, document.Ui.Style.Get("background-color"));
        }
        finally
        {
            root.DiscardGeneratedSubtree();
        }
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "html-render", fileName);

    private static float NumericProperty(Element element, string name)
    {
        var value = element.GetProperty<object>(name);
        Assert.True(value is not null, $"native property '{name}' was not set");
        return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    private static void AssertPixel(BitmapSoftwareRenderSurface surface, float x, float y,
        Func<byte[], bool> expected, string label)
    {
        var px = (int)MathF.Round(x);
        var py = (int)MathF.Round(y);
        Assert.True(px >= 0 && px < surface.Width && py >= 0 && py < surface.Height,
            $"{label} sample ({px},{py}) is outside the {surface.Width}x{surface.Height} canvas");
        var sample = new byte[4];
        surface.Bitmap.GetPixel(px, py).CopyTo(sample);
        Assert.True(expected(sample),
            $"{label} at ({px},{py}) expected {(expected == IsRed ? "red" : "white")} but got BGRA=({sample[0]},{sample[1]},{sample[2]},{sample[3]})");
    }

    private static bool IsRed(byte[] pixel) =>
        pixel[3] == 255 && pixel[2] >= 224 && pixel[1] <= 80 && pixel[0] <= 80;

    private static bool IsWhite(byte[] pixel) =>
        pixel[3] == 255 && pixel[0] >= 224 && pixel[1] >= 224 && pixel[2] >= 224;

    /// <summary>带真实生成作用域与可跟踪资源的绑定组件，模拟编译组件的样式安装路径。</summary>
    private sealed class ScopedCounter : UIElement
    {
        private const string ComponentCss = ".counter-value { background-color: #f5f7fa; }";

        public ScopedCounter(string initial) => Display = new ObservableValue<string>(initial);

        public ObservableValue<string> Display { get; }
        public BoundText Label { get; } = new();
        public TrackedResource Resource { get; } = new();

        public override void BuildElementTree()
        {
            Label.ClassList.Add("mount-label");
            Label.ClassList.Add("counter-value");
            Children.Add(Label);
            Label.BindProperty("TextContent", (IReactiveValue<string>)Display);

            // 与编译组件 EmitStyleSupport 完全一致的生成作用域安装方式。
            var sheet = new CssParser(new CssTokenizer(ComponentCss).Tokenize()).Parse();
            var engine = new CssEngine();
            engine.LoadStyleSheet(sheet);
            RegisterGeneratedResource(engine.ApplyGeneratedStylesToTree(this));
            RegisterGeneratedResource(Resource);
        }
    }

    private sealed class TrackedResource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
