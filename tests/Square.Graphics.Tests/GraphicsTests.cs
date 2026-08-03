using System;
using Square.Graphics;
using Square.Graphics.Codecs;
using System.Buffers.Binary;
using System.IO;
using Xunit;

namespace Square.Graphics.Tests;

public class ColorTests
{
    [Fact]
    public void ParseHex3()
    {
        var c = Color.Parse("#f00");
        Assert.Equal(255, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(0, c.B);
        Assert.Equal(255, c.A);
    }

    [Fact]
    public void ParseHex6()
    {
        var c = Color.Parse("#0078d4");
        Assert.Equal(0, c.R);
        Assert.Equal(120, c.G);
        Assert.Equal(212, c.B);
    }

    [Fact]
    public void ParseHex8()
    {
        var c = Color.Parse("#FF0078d4");
        Assert.Equal(255, c.A);
        Assert.Equal(0, c.R);
    }

    [Fact]
    public void Equality()
    {
        Assert.Equal(Color.Red, Color.FromRgb(255, 0, 0));
        Assert.NotEqual(Color.Red, Color.Blue);
    }

    [Fact]
    public void ToPackedBgra()
    {
        var c = Color.FromRgba(1, 2, 3, 4);
        var packed = c.ToPackedBgra();
        Assert.Equal(4u, (packed >> 24) & 0xFF);
    }
}

public class BoxShadowTests
{
    [Fact]
    public void ParseOffsetBlurSpreadAndRgba()
    {
        Assert.True(BoxShadow.TryParse("2px 6px 18px 1px rgba(0, 0, 0, 0.22)", out var shadow));
        Assert.Equal(2, shadow.OffsetX);
        Assert.Equal(6, shadow.OffsetY);
        Assert.Equal(18, shadow.BlurRadius);
        Assert.Equal(1, shadow.SpreadRadius);
        Assert.Equal(56, shadow.Color.A);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("inset 0 2px 4px #000")]
    [InlineData("0 2px 4px #000, 0 1px 2px #000")]
    public void UnsupportedShadowFormsAreRejected(string value)
    {
        Assert.False(BoxShadow.TryParse(value, out _));
    }

    [Fact]
    public void ParseMultipleOuterShadowsWithoutSplittingFunctionalColorCommas()
    {
        Assert.True(BoxShadow.TryParseList(
            "2px 4px 8px rgba(0, 0, 0, 0.2), -1px 0 0 1px rgb(10, 20, 30)",
            out var shadows));

        Assert.Collection(shadows,
            shadow =>
            {
                Assert.Equal(2, shadow.OffsetX);
                Assert.Equal(4, shadow.OffsetY);
                Assert.Equal(8, shadow.BlurRadius);
                Assert.Equal(51, shadow.Color.A);
            },
            shadow =>
            {
                Assert.Equal(-1, shadow.OffsetX);
                Assert.Equal(1, shadow.SpreadRadius);
                Assert.Equal(Color.FromRgb(10, 20, 30), shadow.Color);
            });
    }

    [Theory]
    [InlineData("0 2px 4px #000,")]
    [InlineData("0 2px 4px #000,, 0 1px 2px #000")]
    [InlineData("0 2px 4px #000, inset 0 1px 2px #000")]
    [InlineData("none, 0 1px 2px #000")]
    public void MultipleShadowDeclarationIsAllOrNothing(string value)
    {
        Assert.False(BoxShadow.TryParseList(value, out _));
    }

    [Fact]
    public void NoneProducesAnEmptyShadowList()
    {
        Assert.True(BoxShadow.TryParseList("none", out var shadows));
        Assert.Empty(shadows);
    }

    [Fact]
    public void MultipleShadowBoundsIncludeEveryDirection()
    {
        Assert.True(BoxShadow.TryParseList(
            "-8px -6px 0 2px #000000, 10px 7px 0 3px #000000",
            out var shadows));

        var bounds = BoxShadowRendering.GetVisualBounds(new Rect(20, 20, 30, 20), shadows);

        Assert.Equal(10, bounds.Left);
        Assert.Equal(12, bounds.Top);
        Assert.Equal(63, bounds.Right);
        Assert.Equal(50, bounds.Bottom);
    }
}

public class RectTests
{
    [Fact]
    public void Contains()
    {
        var r = new Rect(10, 10, 100, 100);
        Assert.True(r.Contains(50, 50));
        Assert.False(r.Contains(5, 5));
    }

    [Fact]
    public void IntersectsWith()
    {
        var a = new Rect(0, 0, 100, 100);
        var b = new Rect(50, 50, 100, 100);
        Assert.True(a.IntersectsWith(b));
    }

    [Fact]
    public void Union()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(100, 100, 50, 50);
        var u = Rect.Union(a, b);
        Assert.Equal(0, u.X);
        Assert.Equal(0, u.Y);
        Assert.Equal(150, u.Width);
        Assert.Equal(150, u.Height);
    }

    [Fact]
    public void Intersect()
    {
        var a = new Rect(0, 0, 100, 100);
        var b = new Rect(50, 50, 100, 100);
        var i = Rect.Intersect(a, b);
        Assert.Equal(50, i.X);
        Assert.Equal(50, i.Y);
        Assert.Equal(50, i.Width);
        Assert.Equal(50, i.Height);
    }
}

public class SizeTests
{
    [Fact]
    public void Arithmetic()
    {
        var a = new Size(100, 200);
        var b = new Size(50, 100);
        Assert.Equal(new Size(150, 300), a + b);
        Assert.Equal(new Size(50, 100), a - b);
        Assert.Equal(new Size(200, 400), a * 2);
    }

    [Fact]
    public void IsEmpty()
    {
        Assert.False(new Size(100, 100).IsEmpty);
        Assert.True(Size.Empty.IsEmpty);
    }
}

public class TextLayoutTests
{
    [Fact]
    public void MeasuresHalfWidthAndFullWidthCharacters()
    {
        var font = new Font("Segoe UI", 20);
        var latin = new TextLayout("AB", font).Measure().Width;
        var fullWidthLatin = new TextLayout("ＡＢ", font).Measure().Width;
        var halfWidthKana = new TextLayout("ｱｲ", font).Measure().Width;
        var kana = new TextLayout("アイ", font).Measure().Width;
        var mixed = new TextLayout("A中", font).Measure().Width;

        Assert.True(latin > 0);
        Assert.True(fullWidthLatin > 0);
        Assert.True(halfWidthKana > 0);
        Assert.True(kana > 0);
        Assert.True(mixed > 0);
        Assert.Equal(latin, new TextLayout("AB", font).MeasureOffset(2), 1);
    }

    [Fact]
    public void ConvertsBetweenOffsetsAndHorizontalPositions()
    {
        var layout = new TextLayout("A中B", new Font("Segoe UI", 20));
        var first = layout.MeasureOffset(1);
        var second = layout.MeasureOffset(2);
        var end = layout.MeasureOffset(3);

        Assert.Equal(0, layout.MeasureOffset(0));
        Assert.True(first > 0);
        Assert.True(second > first);
        Assert.True(end > second);
        Assert.Equal(0, layout.HitTestOffset(first * 0.25f));
        Assert.Equal(1, layout.HitTestOffset(first * 0.75f));
        Assert.Equal(2, layout.HitTestOffset(first + (second - first) * 0.75f));
        Assert.Equal(3, layout.HitTestOffset(second + (end - second) * 0.75f));
    }
}

public class BitmapCodecTests
{
#if false
    [Fact]
    private void RemovedDecodesJpegAndGif()
    {
        var jpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD82qKKK9M88//Z");
        jpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAgAAAQABAAD//gAPTGF2YzYyLjMuMTAxAP/bAEMACAQEBAQEBQUFBQUFBgYGBgYGBgYGBgYGBgcHBwgICAcHBwYGBwcICAgICQkJCAgICAkJCgoKDAwLCw4ODhERFP/EAEwAAQEAAAAAAAAAAAAAAAAAAAAGAQEBAAAAAAAAAAAAAAAAAAAGBxABAAAAAAAAAAAAAAAAAAAAABEBAAAAAAAAAAAAAAAAAAAAAP/AABEIAAgACAMBIgACEQADEQD/2gAMAwEAAhEDEQA/AIsATX9//9k=");
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

        using var decodedJpeg = ImageDecoder.Decode(jpeg);
        using var decodedGif = ImageDecoder.Decode(gif);

        Assert.Equal((8, 8), (decodedJpeg.Width, decodedJpeg.Height));
        Assert.Equal((1, 1), (decodedGif.Width, decodedGif.Height));
        Assert.Equal(255, decodedGif.Pixels[3]);
    }

#endif
    [Fact]
    public void SavesBitmapAsPng()
    {
        using var bitmap = new Bitmap(1, 1);
        var pixel = bitmap.GetPixel(0, 0);
        pixel[0] = 3;
        pixel[1] = 2;
        pixel[2] = 1;
        pixel[3] = 4;

        using var stream = new MemoryStream();
        BitmapPngEncoder.Save(bitmap, stream);
        var bytes = stream.ToArray();

        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], bytes[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Contains(bytes, b => b == (byte)'I');
    }

    [Fact]
    public void ConvertsTopDownBmpToPng()
    {
        var directory = Path.Combine(Path.GetTempPath(), "square-codec-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bmpPath = Path.Combine(directory, "source.bmp");
            var pngPath = Path.Combine(directory, "target.png");
            File.WriteAllBytes(bmpPath, CreateTopDownBmp());

            BmpPngConverter.Convert(bmpPath, pngPath);

            var bytes = File.ReadAllBytes(pngPath);
            Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], bytes[..8]);
            Assert.True(bytes.Length > 50);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateTopDownBmp()
    {
        var bytes = new byte[14 + 40 + 8];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2, 4), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10, 4), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), -1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28, 2), 24);
        bytes[54] = 30;
        bytes[55] = 20;
        bytes[56] = 10;
        bytes[57] = 60;
        bytes[58] = 50;
        bytes[59] = 40;
        return bytes;
    }
}
