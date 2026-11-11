using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Square.Backends.Direct2D;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
#if PLATFORM_WIN32
using Square.Platform.Win32;
#endif
using Xunit;

namespace Square.Backends.Conformance.Tests;

[CollectionDefinition("Direct2DTextPreparation", DisableParallelization = true)]
public sealed class Direct2DTextPreparationCollection;

[Collection("Direct2DTextPreparation")]
public sealed class Direct2DTextPreparationTests
{
    [Fact]
    public void EquivalentLayoutsReuseExactOriginalLineAndVisualRuneResults()
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache();
        var layout = Sample();
        var first = cache.Get(layout);
        Assert.NotNull(first);
        EqualPreparation(Original(layout), first);
        var builds = cache.BuildCount;
        var hits = cache.HitCount;
        var clone = Clone(layout);
        Assert.NotSame(layout.Font, clone.Font);
        Assert.Same(first, cache.Get(clone));
        Assert.Equal(builds, cache.BuildCount);
        Assert.Equal(hits + 1, cache.HitCount);
        Assert.Equal(1, cache.Count);
    }

    [Theory]
    [InlineData("family")]
    [InlineData("size")]
    [InlineData("weight")]
    [InlineData("style")]
    [InlineData("text")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("alignment")]
    [InlineData("lineHeight")]
    [InlineData("direction")]
    [InlineData("unicodeBidi")]
    [InlineData("whiteSpace")]
    [InlineData("letterSpacing")]
    [InlineData("wordSpacing")]
    [InlineData("transform")]
    [InlineData("indent")]
    [InlineData("collapse")]
    [InlineData("decoration")]
    public void EachMutableLayoutOrFontFieldInvalidatesItsValueKey(string field)
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache();
        var layout = Sample();
        var first = cache.Get(layout)!;
        var original = Snapshot(first);
        var builds = cache.BuildCount;
        switch (field)
        {
            case "family": layout.Font.Family = "serif"; break;
            case "size": layout.Font.Size += 1.25f; break;
            case "weight": layout.Font.Weight = FontWeight.Bold; break;
            case "style": layout.Font.Style = FontStyle.Italic; break;
            case "text": layout.Text += "!"; break;
            case "width": layout.MaxSize = new Size(43.5f, layout.MaxSize.Height); break;
            case "height": layout.MaxSize = new Size(layout.MaxSize.Width, 60); break;
            case "alignment": layout.Alignment = TextAlignment.Right; break;
            case "lineHeight": layout.LineHeight = 1.75f; break;
            case "direction": layout.Direction = BidiDirection.Rtl; break;
            case "unicodeBidi": layout.UnicodeBidi = BidiTextMode.BidiOverride; break;
            case "whiteSpace": layout.WhiteSpace = TextWhiteSpaceMode.Normal; break;
            case "letterSpacing": layout.LetterSpacing = .375f; break;
            case "wordSpacing": layout.WordSpacing = 1.25f; break;
            case "transform": layout.TextTransform = TextTransformMode.Uppercase; break;
            case "indent": layout.TextIndent = 3.75f; break;
            case "collapse": layout.CollapseNewlines = true; break;
            case "decoration": layout.TextDecorationLines = TextDecorationLine.Underline; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        var changed = cache.Get(layout);
        Assert.NotNull(changed);
        Assert.Equal(builds + 1, cache.BuildCount);
        EqualPreparation(Original(layout), changed);
        EqualPreparation(original, first);
    }

    [Fact]
    public void PreparedSnapshotsStayReadOnlyAfterTheInputObjectsChange()
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache();
        var layout = Sample();
        var prepared = cache.Get(layout)!;
        var before = Snapshot(prepared);
        Assert.False(prepared.Lines is TextLineRange[]);
        if (prepared.Lines is IList<TextLineRange> lines)
        {
            Assert.True(lines.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => lines[0] = default);
        }
        var visual = prepared.GetVisualRunes(0);
        Assert.False(visual is TextVisualRune[]);
        if (visual is IList<TextVisualRune> runes)
        {
            Assert.True(runes.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => runes[0] = default);
        }
        layout.Text = "different paragraph";
        layout.Font.Family = "monospace";
        layout.Font.Size = 31;
        layout.LetterSpacing = 7;
        cache.Get(layout);
        cache.Clear();
        EqualPreparation(before, prepared);
    }

    [Fact]
    public void CacheDoesNotKeepMutableLayoutOrFontInstancesAlive()
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache();
        var retained = CreateTransient(cache);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(retained.Layout.IsAlive);
        Assert.False(retained.Font.IsAlive);
        Assert.NotEmpty(retained.Prepared.Lines);
        Assert.Equal(1, cache.Count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Layout, WeakReference Font, Direct2DTextPreparationCache.Preparation Prepared) CreateTransient(Direct2DTextPreparationCache cache)
    {
        var layout = Sample();
        return (new WeakReference(layout), new WeakReference(layout.Font), cache.Get(layout)!);
    }

    [Fact]
    public void LruEvictsTheLeastRecentlyUsedValue()
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache(maxEntries: 2, maxBytes: 1024 * 1024);
        var a = Sample("alpha"); var b = Sample("bravo"); var c = Sample("charlie");
        cache.Get(a); cache.Get(b); cache.Get(a); cache.Get(c);
        var builds = cache.BuildCount;
        cache.Get(a);
        Assert.Equal(builds, cache.BuildCount);
        cache.Get(b);
        Assert.Equal(builds + 1, cache.BuildCount);
        Assert.Equal(2, cache.Count);
    }

    [Theory]
    [InlineData(0, 1048576L)]
    [InlineData(20, 0L)]
    [InlineData(20, 1L)]
    public void DisabledOrOversizedEntriesStillPrepareCorrectResults(int entries, long bytes)
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache(entries, bytes);
        var layout = Sample();
        EqualPreparation(Original(layout), cache.Get(layout)!);
        EqualPreparation(Original(layout), cache.Get(layout)!);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(2, cache.BuildCount);
        Assert.Equal(0, cache.HitCount);
    }

    [Fact]
    public void ByteBudgetRemainsBoundedIncludingRetainedText()
    {
        using var providers = new StandardProviders();
        var probe = new Direct2DTextPreparationCache();
        probe.Get(Sample("aaaa"));
        var budget = probe.Bytes + 16;
        var cache = new Direct2DTextPreparationCache(100, budget);
        foreach (var text in new[] { "aaaa", "bbbb", "cccc", "dddd" })
        {
            cache.Get(Sample(text));
            Assert.InRange(cache.Bytes, 0, budget);
        }
        var before = cache.Count;
        var huge = Sample(new string('W', 2048));
        huge.MaxSize = new Size(float.MaxValue, 200);
        EqualPreparation(Original(huge), cache.Get(huge)!);
        Assert.Equal(before, cache.Count);
        Assert.InRange(cache.Bytes, 0, budget);
    }

    [Fact]
    public void ClearReleasesEntriesWithoutResettingCumulativeCounters()
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache();
        var layout = Sample();
        cache.Get(layout); cache.Get(layout);
        var builds = cache.BuildCount; var hits = cache.HitCount;
        cache.Clear();
        Assert.Equal(0, cache.Count); Assert.Equal(0, cache.Bytes);
        Assert.Equal(builds, cache.BuildCount); Assert.Equal(hits, cache.HitCount);
        cache.Get(layout);
        Assert.Equal(builds + 1, cache.BuildCount);
    }

    [Fact]
    public void DynamicCustomMetricsProviderBypassesEvenAnExistingStandardEntry()
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache();
        var layout = Sample();
        var standard = cache.Get(layout);
        var builds = cache.BuildCount; var hits = cache.HitCount;
        var custom = new DynamicProvider { Advance = 5 };
        TextMetrics.RegisterProvider(custom);
        Assert.Null(cache.Get(layout));
        Assert.Equal(5, TextLayout.MeasureRuneAdvance(new Rune('A'), layout.Font));
        custom.Advance = 19;
        Assert.Null(cache.Get(layout));
        Assert.Equal(19, TextLayout.MeasureRuneAdvance(new Rune('A'), layout.Font));
        Assert.Equal(builds, cache.BuildCount); Assert.Equal(hits, cache.HitCount);
        providers.UseStandard();
        Assert.Same(standard, cache.Get(layout));
    }

    [Fact]
    public void ArbitraryAdvanceDelegateBypassesAndKeepsItsDynamicFallbackBehavior()
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache();
        var layout = Sample();
        cache.Get(layout);
        var builds = cache.BuildCount; var hits = cache.HitCount;
        var advance = 4f;
        TextLayout.RegisterAdvanceProvider((_, _) => advance);
        Assert.Null(cache.Get(layout)); // Metrics identity is still standard here.
        TextMetrics.RegisterProvider(new DynamicProvider { Decline = true });
        Assert.Equal(4, TextLayout.MeasureRuneAdvance(new Rune('A'), layout.Font));
        advance = 23;
        Assert.Null(cache.Get(layout));
        Assert.Equal(23, TextLayout.MeasureRuneAdvance(new Rune('A'), layout.Font));
        Assert.Equal(builds, cache.BuildCount); Assert.Equal(hits, cache.HitCount);
        providers.UseStandard();
        TextLayout.RegisterAdvanceProvider((rune, font) => providers.Advance(rune, font));
        Assert.Null(cache.Get(layout)); // A forwarding wrapper is not the registered standard identity.
    }

    [Fact]
    public void AnyScopedProviderBypassesIncludingADecliningProvider()
    {
        using var providers = new StandardProviders();
        var cache = new Direct2DTextPreparationCache();
        var layout = Sample();
        var standard = cache.Get(layout);
        var builds = cache.BuildCount; var hits = cache.HitCount;
        var custom = new DynamicProvider { Advance = 7 };
        using (TextLayoutProviderContext.Push(custom))
        {
            Assert.Null(cache.Get(layout));
            Assert.Equal(7, TextLayout.MeasureRuneAdvance(new Rune('A'), layout.Font));
            custom.Advance = 13;
            Assert.Null(cache.Get(layout));
            Assert.Equal(13, TextLayout.MeasureRuneAdvance(new Rune('A'), layout.Font));
            custom.Decline = true;
            Assert.Null(cache.Get(layout));
        }
        Assert.Equal(builds, cache.BuildCount); Assert.Equal(hits, cache.HitCount);
        Assert.Same(standard, cache.Get(layout));
    }

    [Fact]
    [Trait("Category", "RealDirect2D")]
    [SupportedOSPlatform("windows6.1")]
    public void CachedFallbackTextMatchesOriginalPreparationOnSameTargetAndDpi()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1) ||
            Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_DIRECT2D_CONFORMANCE") != "1") return;
        using var providers = new StandardProviders();
        using var warm = new TestWindow();
        var layouts = new[]
        {
            new TextLayout("abc אב (12)\n中文 e\u0301", new Font("Segoe UI", 12.5f))
            { MaxSize = new Size(60, 90), WhiteSpace = TextWhiteSpaceMode.PreWrap, Alignment = TextAlignment.Center, LetterSpacing = .125f, TextIndent = 1.5f },
            new TextLayout("RTL אבג 42\nmix Åg", new Font("Segoe UI", 10.75f, FontWeight.Bold, FontStyle.Italic))
            { MaxSize = new Size(56, 90), Direction = BidiDirection.Rtl, UnicodeBidi = BidiTextMode.Embed, WhiteSpace = TextWhiteSpaceMode.PreWrap, LineHeight = 1.1f },
            new TextLayout("wrap 123 中文", new Font("Segoe UI", 11.25f))
            { MaxSize = new Size(114, 28), LetterSpacing = .25f, WordSpacing = .5f }
        };
        var brush = new SolidColorBrush(new Color(20, 40, 200, 173));
        var gradient = new LinearGradientBrush(new Point(64, 0), new Point(124, 64),
            new GradientStop(0, new Color(230, 20, 50, 91)), new GradientStop(1, new Color(20, 80, 220, 230)));
        void Draw(Direct2DRenderContext context)
        {
            context.Clear(Color.White);
            context.PushTransform(Matrix3x2.CreateTranslation(.375f, .625f));
            context.DrawText(layouts[0], new Point(4.25f, 3.75f), brush);
            context.PopTransform();
            context.PushTransform(Matrix3x2.CreateRotation(.07f, new Vector2(96, 28)));
            context.DrawText(layouts[1], new Point(66.125f, 4.375f), gradient);
            context.PopTransform();
            context.DrawText(layouts[2], new Point(4.5f, 70.25f), brush);
            context.Present();
        }
        Bitmap CaptureOriginalPath()
        {
            var standard = providers.Advance;
            try
            {
                // The wrapper has identical numerical behavior but intentionally
                // fails the exact standard identity required to enable preparation.
                TextLayout.RegisterAdvanceProvider((rune, font) => standard(rune, font));
                Draw(warm.Context);
                Draw(warm.Context); // Warm the native glyph/brush resources first.
                return warm.Capture();
            }
            finally { TextLayout.RegisterAdvanceProvider(standard); }
        }
        void CompareCurrentTarget(string stage)
        {
            Assert.Equal(0, warm.Context.TextPreparationCacheCount);
            var builds = warm.Context.TextPreparationBuildCount;
            var hits = warm.Context.TextPreparationHitCount;
            using var reference = CaptureOriginalPath();
            Assert.True(CountInk(reference) > 50, "Original native path did not contain text ink.");
            Assert.Equal(0, warm.Context.TextPreparationCacheCount);
            Assert.Equal(builds, warm.Context.TextPreparationBuildCount);
            Assert.Equal(hits, warm.Context.TextPreparationHitCount);
            Draw(warm.Context);
            using var first = warm.Capture();
            EqualPixels(reference, first, $"{stage} prep first build");
            Assert.Equal(builds + layouts.Length, warm.Context.TextPreparationBuildCount);
            Assert.Equal(layouts.Length, warm.Context.TextPreparationCacheCount);
            hits = warm.Context.TextPreparationHitCount;
            builds = warm.Context.TextPreparationBuildCount;
            Draw(warm.Context);
            using var cached = warm.Capture();
            EqualPixels(reference, cached, $"{stage} prep hit");
            Assert.Equal(builds, warm.Context.TextPreparationBuildCount);
            Assert.Equal(hits + layouts.Length, warm.Context.TextPreparationHitCount);
        }
        foreach (var multiplier in new[] { 1f, 1.25f })
        {
            var dpi = warm.Host.DpiScale * multiplier;
            warm.Resize(dpi);
            Console.WriteLine($"Text preparation comparison multiplier={multiplier}, dpi={dpi}");
            CompareCurrentTarget($"DPI={dpi} resized target");
            var requests = warm.RecreationRequests;
            typeof(Direct2DRenderContext).GetMethod("HandleDrawResult", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(warm.Context, [new DirectN.HRESULT(unchecked((int)0x8899000C)), "Simulated target loss"]);
            Assert.Equal(requests + 1, warm.RecreationRequests);
            Assert.Equal(0, warm.Context.TextPreparationCacheCount);
            CompareCurrentTarget($"DPI={dpi} recreated target");
        }
    }

    private static TextLayout Sample(string? text = null) => new(text ?? "ab  cd\nאב (12)\n中文\tß 😀 e\u0301", new Font("Segoe UI", 14))
    { MaxSize = new Size(83.25f, 200), WhiteSpace = TextWhiteSpaceMode.PreWrap };
    private static TextLayout Clone(TextLayout t) => new(t.Text, new Font(t.Font.Family, t.Font.Size, t.Font.Weight, t.Font.Style))
    {
        MaxSize = t.MaxSize, Alignment = t.Alignment, LineHeight = t.LineHeight, Direction = t.Direction, UnicodeBidi = t.UnicodeBidi,
        WhiteSpace = t.WhiteSpace, LetterSpacing = t.LetterSpacing, WordSpacing = t.WordSpacing, TextTransform = t.TextTransform,
        TextIndent = t.TextIndent, CollapseNewlines = t.CollapseNewlines, TextDecorationLines = t.TextDecorationLines
    };
    private static PreparationSnapshot Original(TextLayout text)
    {
        var lines = TextWrapping.Wrap(text.Text, text.MaxSize.Width, (_, rune) => TextLayout.MeasureRuneAdvance(rune, text.Font), text.WrappingOptions).ToArray();
        var height = TextMetrics.GetLineHeight(text.Font, text.LineHeight);
        return new PreparationSnapshot(lines, lines.Select(line => text.EnumerateVisualRunes(line).ToArray()).ToArray(),
            height, TextMetrics.GetBaselineOffset(text.Font, height));
    }
    private static PreparationSnapshot Snapshot(Direct2DTextPreparationCache.Preparation p)
        => new(p.Lines.ToArray(), Enumerable.Range(0, p.Lines.Count).Select(i => p.GetVisualRunes(i).ToArray()).ToArray(), p.LineHeight, p.BaselineOffset);
    private static void EqualPreparation(PreparationSnapshot expected, Direct2DTextPreparationCache.Preparation actual)
    {
        Assert.Equal(Bits(expected.LineHeight), Bits(actual.LineHeight));
        Assert.Equal(Bits(expected.Baseline), Bits(actual.BaselineOffset));
        Assert.Equal(expected.Lines.Length, actual.Lines.Count);
        for (var i = 0; i < expected.Lines.Length; i++)
        {
            Assert.Equal(expected.Lines[i].StartOffset, actual.Lines[i].StartOffset);
            Assert.Equal(expected.Lines[i].EndOffset, actual.Lines[i].EndOffset);
            Assert.Equal(Bits(expected.Lines[i].Width), Bits(actual.Lines[i].Width));
            var runes = actual.GetVisualRunes(i).ToArray();
            Assert.Equal(expected.Visual[i].Length, runes.Length);
            var expectedPrefix = 0f; var actualPrefix = 0f;
            for (var j = 0; j < runes.Length; j++)
            {
                var e = expected.Visual[i][j]; var a = runes[j];
                Assert.Equal(e.Rune, a.Rune); Assert.Equal(e.StartOffset, a.StartOffset); Assert.Equal(e.EndOffset, a.EndOffset);
                Assert.Equal(e.Direction, a.Direction); Assert.Equal(Bits(e.Advance), Bits(a.Advance));
                expectedPrefix += e.Advance; actualPrefix += a.Advance;
                Assert.Equal(Bits(expectedPrefix), Bits(actualPrefix));
            }
        }
    }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private sealed record PreparationSnapshot(TextLineRange[] Lines, TextVisualRune[][] Visual, float LineHeight, float Baseline);

    private sealed class DynamicProvider : ITextLayoutProvider
    {
        public float Advance { get; set; }
        public bool Decline { get; set; }
        public bool TryCreateLayout(TextLayout layout, out ITextLayoutSnapshot? snapshot) { snapshot = null; return false; }
        public bool TryGetFontMetrics(Font font, out FontMetrics metrics) { metrics = new FontMetrics(-10, -8, 2, 3, 1); return !Decline; }
        public bool TryGetGlyphMetrics(Font font, Rune rune, out GlyphMetrics metrics) { metrics = new GlyphMetrics(Advance, new Rect(0, -8, Advance, 10)); return !Decline; }
    }

    private sealed class StandardProviders : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo AdvanceField = typeof(TextLayout).GetField("_advanceProvider", Flags)!;
        private static readonly FieldInfo MetricsField = typeof(TextMetrics).GetField("_provider", Flags)!;
        private readonly object? _previousAdvance = AdvanceField.GetValue(null);
        private readonly object? _previousMetrics = MetricsField.GetValue(null);
        private readonly IDisposable _scope = TextLayoutProviderContext.Suppress();
        private readonly ITextMetricsProvider _standardMetrics;
        public Func<Rune, Font, float?> Advance { get; }
        public StandardProviders()
        {
            // Core does not friend this test assembly. Restore the exact built-in
            // registration, never a behaviorally similar wrapper that is ineligible.
            var type = typeof(TextLayout).Assembly.GetType("Square.Text.Glyph.SystemTextMeasurementRegistration")!;
            _standardMetrics = (ITextMetricsProvider)type.GetField("MetricsProvider", Flags)!.GetValue(null)!;
            Advance = type.GetMethod("MeasureAdvance", Flags)!.CreateDelegate<Func<Rune, Font, float?>>();
            UseStandard();
        }
        public void UseStandard() { TextMetrics.RegisterProvider(_standardMetrics); TextLayout.RegisterAdvanceProvider(Advance); }
        public void Dispose()
        {
            try { AdvanceField.SetValue(null, _previousAdvance); MetricsField.SetValue(null, _previousMetrics); }
            finally { _scope.Dispose(); }
        }
    }

    private static int CountInk(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p[0] < 240 || p[1] < 240 || p[2] < 240) count++;
        }
        return count;
    }
    private static void EqualPixels(Bitmap expected, Bitmap actual, string stage)
    {
        Assert.True(expected.Width == actual.Width && expected.Height == actual.Height,
            $"{stage}: capture size expected={expected.Width}x{expected.Height}, actual={actual.Width}x{actual.Height}");
        var different = 0;
        var maximum = 0;
        var firstX = -1;
        var firstY = -1;
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            var e = expected.GetPixel(x, y);
            var a = actual.GetPixel(x, y);
            var pixelDiffers = false;
            for (var channel = 0; channel < 4; channel++)
            {
                var delta = Math.Abs(e[channel] - a[channel]);
                maximum = Math.Max(maximum, delta);
                pixelDiffers |= delta != 0;
            }
            if (!pixelDiffers) continue;
            different++;
            if (firstX < 0) { firstX = x; firstY = y; }
        }
        var detail = $"{stage}: {expected.Width}x{expected.Height}, differentPixels={different}, maxChannelError={maximum}";
        if (different != 0)
        {
            var e = expected.GetPixel(firstX, firstY);
            var a = actual.GetPixel(firstX, firstY);
            detail += $", first=({firstX},{firstY}) expectedRGBA=({e[2]},{e[1]},{e[0]},{e[3]}), actualRGBA=({a[2]},{a[1]},{a[0]},{a[3]})";
        }
        Assert.True(different == 0, detail);
    }

    [SupportedOSPlatform("windows6.1")]
    private sealed class TestWindow : IDisposable
    {
        private readonly string? _hardware = Environment.GetEnvironmentVariable("SQUARE_DIRECT2D_REQUIRE_HARDWARE");
        public IPlatformHost Host { get; }
        public Direct2DRenderContext Context { get; }
        public int RecreationRequests { get; private set; }
        public TestWindow()
        {
#if PLATFORM_WIN32
            Environment.SetEnvironmentVariable("SQUARE_DIRECT2D_REQUIRE_HARDWARE", "1");
            Host = new Win32PlatformFactory().CreateHost(new PlatformHostCreateInfo
            {
                Title = "Direct2D prepared text conformance", Width = 128, Height = 96, RenderBackend = "Direct2D",
                TitleStyle = TitleStyle.Hidden, BorderStyle = BorderStyle.None
            });
            Direct2DRenderContext? created = null;
            try
            {
                Host.Show();
                var handle = (IntPtr)Host.GetType().GetProperty("Handle")!.GetValue(Host)!;
                var scale = Host.DpiScale;
                Assert.True(SetWindowPos(handle, new IntPtr(-1), 32, 32,
                    (int)MathF.Round(128 * scale), (int)MathF.Round(96 * scale), 0x0050));
                Context = created = new Direct2DRenderContext(new RenderContextCreateInfo
                {
                    CanvasSize = new Size(128, 96), DpiScale = scale, VSync = false,
                    RequestRender = () => RecreationRequests++
                }, new Win32RenderTarget(handle, IntPtr.Zero));
                Context.Clear(Color.White); Context.Present(); Host.ShowAfterFirstFrame();
            }
            catch
            {
                try { created?.Dispose(); }
                finally { Host.Dispose(); Environment.SetEnvironmentVariable("SQUARE_DIRECT2D_REQUIRE_HARDWARE", _hardware); }
                throw;
            }
#else
            throw new PlatformNotSupportedException();
#endif
        }
        public void Resize(float dpi)
        {
            var handle = (IntPtr)Host.GetType().GetProperty("Handle")!.GetValue(Host)!;
            Assert.True(SetWindowPos(handle, new IntPtr(-1), 32, 32,
                (int)MathF.Round(128 * dpi), (int)MathF.Round(96 * dpi), 0x0050));
            Context.Resize(new Size(128, 96), dpi);
        }
        public Bitmap Capture()
        {
            var handle = (IntPtr)Host.GetType().GetProperty("Handle")!.GetValue(Host)!;
            Assert.True(SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0053));
            var cornerPreference = 1; // DWMWCP_DONOTROUND
            var result = DwmSetWindowAttribute(handle, 33, ref cornerPreference, sizeof(int));
            Assert.True(result >= 0, $"DwmSetWindowAttribute(DONOTROUND) failed: 0x{result:X8}");
            DwmFlush();
            Thread.Sleep(160);
            var image = VisibleHwndCapture.Capture(Host);
            Console.WriteLine($"Capture dpi={Context.DpiScale}, canvas={Context.CanvasSize}, hostDpi={Host.DpiScale}, hostClient={Host.ClientSize}, bitmap={image.Width}x{image.Height}");
            Assert.Equal((int)MathF.Round(128 * Context.DpiScale), image.Width);
            Assert.Equal((int)MathF.Round(96 * Context.DpiScale), image.Height);
            return image;
        }
        public void Dispose()
        {
            try { Context.Dispose(); }
            finally { Host.Dispose(); Environment.SetEnvironmentVariable("SQUARE_DIRECT2D_REQUIRE_HARDWARE", _hardware); }
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
