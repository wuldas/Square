using Square.PackageConsumer;
using Square.Graphics;
using Square.Graphics.Codecs;
using Square.Images;
using Square.Platform;
using System.Reflection;

var component = new Main();
component.BuildElementTree();

if (component.Children.Count != 1)
    throw new InvalidOperationException("The packaged source generator did not build the SQX component.");

if (!component.CodeBehindLoaded)
    throw new InvalidOperationException("The SQX code-behind partial class was not compiled.");

var publicFile = Path.Combine(AppContext.BaseDirectory, "site.txt");
if (!File.Exists(publicFile) || File.ReadAllText(publicFile).Trim() != "Square public file")
    throw new InvalidOperationException("Public files were not copied to the application output root.");

var assembly = Assembly.GetExecutingAssembly();
var assetName = assembly.GetManifestResourceNames()
    .SingleOrDefault(name => name.EndsWith("Assets.theme.txt", StringComparison.Ordinal));
if (assetName == null)
    throw new InvalidOperationException("Assets files were not embedded in the application assembly.");
using (var assetStream = assembly.GetManifestResourceStream(assetName))
using (var reader = new StreamReader(assetStream ?? throw new InvalidOperationException("Embedded asset stream is missing.")))
{
    if (reader.ReadToEnd().Trim() != "Square embedded asset")
        throw new InvalidOperationException("Embedded asset content was incorrect.");
}

var platform = PlatformRegistry.Get();
#if PLATFORM_WIN32
if (platform.Name != "Win32")
    throw new InvalidOperationException("The Win32 platform package was not automatically registered.");
#elif PLATFORM_X11
if (platform.Name != "X11")
    throw new InvalidOperationException("The X11 platform package was not automatically registered.");
#elif PLATFORM_MACOS
if (platform.Name != "MacOS")
    throw new InvalidOperationException("The macOS platform package was not automatically registered.");
#endif

using var source = new Bitmap(2, 1);
new byte[] { 30, 20, 10, 40, 60, 50, 40, 255 }.CopyTo(source.Pixels, 0);
using var encoded = new MemoryStream();
BitmapPngEncoder.Save(source, encoded);
using var decodedDocument = ImageDecoder.Decode(encoded.ToArray());
if (!source.Pixels.AsSpan().SequenceEqual(decodedDocument.PrimaryBitmap.Pixels))
    throw new InvalidOperationException("The packaged Square.Images PNG decoder returned incorrect pixels.");

var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
using var decodedGif = ImageDecoder.Decode(gif);
if (decodedGif.Format != ImageFormat.Gif || decodedGif.Items.Count != 1 ||
    decodedGif.PrimaryBitmap.Width != 1 || decodedGif.PrimaryBitmap.Height != 1 || decodedGif.PrimaryBitmap.Pixels[3] != 255)
    throw new InvalidOperationException("The packaged Square.Images GIF decoder returned an invalid document.");

var webp = Convert.FromBase64String("UklGRhwAAABXRUJQVlA4TA8AAAAvB8ABAAcQ9Y/+ByKi/wEA");
using var decodedWebp = ImageDecoder.Decode(webp);
if (decodedWebp.PrimaryBitmap.Width != 8 || decodedWebp.PrimaryBitmap.Height != 8 ||
    !decodedWebp.PrimaryBitmap.Pixels.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 0, 254, 255 }))
    throw new InvalidOperationException("The packaged Square.Images WebP decoder returned incorrect pixels.");

var lossyWebp = Convert.FromBase64String(
    "UklGRjoAAABXRUJQVlA4IC4AAADQAQCdASoIAAgAASA0J7ACdLoB+AADsAD+7nZ//lKvBY3R5/9lY+pZ+pZ/eQAA");
using var decodedLossyWebp = ImageDecoder.Decode(lossyWebp);
if (decodedLossyWebp.PrimaryBitmap.Width != 8 || decodedLossyWebp.PrimaryBitmap.Height != 8 ||
    !decodedLossyWebp.PrimaryBitmap.Pixels.AsSpan(0, 4).SequenceEqual(new byte[] { 16, 52, 203, 255 }))
    throw new InvalidOperationException("The packaged Square.Images VP8 lossy WebP decoder returned incorrect pixels.");

var lossyAlphaWebp = Convert.FromBase64String(
    "UklGRgwCAABXRUJQVlA4WAoAAAAQAAAAHAAAFAAAQUxQSCQAAAABDzD/ERFCMWwr1PIpvD4gaSWSViChvG0AEf0fhJqjbCfkRRNWUDggwgEAANAIAJ0BKh0AFQA+1U6bRSSiIRgEAEwNRPYATplCWt8QJBV6ANsBzxuQAdKF+zdCBxgygmmekBpAmWdAAFLiRKI0OPJjspeRZeXc0W/XKgAAzdf0UYz0XRZhpwOQ4qv4hQIr+skq1+C7gMc27ya8AvtibPC8SODdWy4B3wUyNPpxY1r93l32BhM+shK0mznr39t7klZ0gUZ7lJ7Qk5JuLlybIhfm3qWbd6xNHRQfDBE1xLw+7u/YkCDe2mUVgCXRq9CbIveG7/x7fuO8H/RXWi+Lr8vH59fEORhGt9eXbM80pZueLCt1xJJz6B5AwTyCXFxwW1jrKj5PFot7Avr74xJxl3ho11F7//pc+Nvm/08+Xvn8fsg7xIpiTAbPbpr0JBXozq/osO1zuVjoNy//GxWYV9Lph+XA5BjR38Xf8ykKOsnYcpVfkhf3cw3ve7FGtdm8ihd5VMbPG5+B1//qajxxib138Q6K0EBoOZAbfasCwz5Mrc2FB82yP0qz019PqfQQEC/iQ//u9ftf9YyfA2jq8Hjqrk/ul4DR+8MNyL7fezhXEooY298ZlF+vSbydCdT2BW4+f+XepFEq88rQqsgAAA==");
using var decodedLossyAlphaWebp = ImageDecoder.Decode(lossyAlphaWebp);
var transparentPixel = decodedLossyAlphaWebp.PrimaryBitmap.GetPixel(0, 0);
if (decodedLossyAlphaWebp.PrimaryBitmap.Width != 29 || decodedLossyAlphaWebp.PrimaryBitmap.Height != 21 ||
    transparentPixel[3] != 0 || (transparentPixel[0] == 0 && transparentPixel[1] == 0 && transparentPixel[2] == 0))
    throw new InvalidOperationException("The packaged Square.Images VP8 ALPH decoder returned incorrect pixels.");

var animatedLossyWebp = Convert.FromBase64String(
    "UklGRqYDAABXRUJQVlA4WAoAAAASAAAAHQAAEQAAQU5JTQYAAAAAAAAAAABBTk1GdAEAAAAAAAEAABUAAA0AAFoAAAJWUDggXAEAALAHAJ0BKhYADgA+vUSZRKQiIRgEAEQLxPIATplBjBM1WmS7xwV4J3lz7mbRUA/S3/Y/mFxjH6mja6IwnDFlrS/6zND2dJAA4jCos8lmJOYiUJwcNtGB3QIhBGxQs0IcmrAeI7AxDtWBp+x2fjr35dtDHTC8s2niu8T/aoHFdDlIjqdrmMbVzE8JVCBkdCsO3rqiBky+u4vOjxpy6vTX8cxJx0h2Z/MI1t1/mNq3k9SNnRMsNuFRRbvRCIDvxbXL7OUIs9f/Nxrd7P42/nBMd/+rnbzTf0B7RaoGBXT/jmRv79ma7S5yigs5V92m3/vNUR80ogKa1xUETLGyq/0JO8tI4ZwJu/8N/1Esf+qxb1sqFRb8k9bJDOp+5//t5ZAOzkxSDg/E3RfnW1VG4FzN4GzX52zqneq4cZqsmtVpJaHt2y58Tx8MCyaMvr/7ocs2C0dwHNYp4/vt/J/AAEFOTUZmAQAABgAAAgAAEQAACwAAbgAAAUFMUEhrAAAAAbkuRPQ/LE6zbduOL/5oqolUVVVVXTWDqqqqqlrpvQ8zXIcnPTtcIWICcgo7i5VxBzcKWCagbmk5HuDKLAMwS/UJLjbMcgDtrxc4s409UAE3TOANTrQdB/BVTkDbAEqP1Fb9w4dltAOw1BkAVlA4INoAAADwBACdASoSAAwAPsFImkUkIiEYBABEDATgCdMoHCAeuAMPKzMkRydbHiyqTwGJv1AA9j0XPtcxGF6sRF7Dkjjv9LdSrEh0iYr6AgfNnlyVG5gVlUDw3SJK+dcHFcCnGx+sN7osfx5g++NFeHmHTZtutqN6QGih+Z1ZcePEiV5ifyH0gn8dOUb6gUlZydHyqceFsBUi3vsTXtx+CCk7/4FL1+IfiCHotKGVQM9yrycVsV5vBIjiF8iuj7mf7R1zVoMDhyatflVyTUflVI2Fs0dkfNB8qqj5XWAAAEFOTUaQAAAAAgAABAAACQAABwAAPAAAAFZQOCB4AAAAEAIAnQEqCgAIAAFgOCeQAnQGLQZHfAXZAADOPr0+Osp8XPS4XvQpnvA9dv6cfd1wExv6fOrWWqm/ITWl0IMm3vjvIXTHs/DA57sd2rXLq74oPGyc8I6YS7L4pBHUt039+EypZcafd+xrT3TdXed46Mpi4DSE6yAA");
using var decodedAnimatedLossyWebp = ImageDecoder.Decode(animatedLossyWebp);
if (decodedAnimatedLossyWebp.Kind != ImageDocumentKind.Animation ||
    decodedAnimatedLossyWebp.Items.Count != 3 || decodedAnimatedLossyWebp.Animation is not { LoopsForever: true } ||
    decodedAnimatedLossyWebp.Items[0].Duration != TimeSpan.FromMilliseconds(90) ||
    decodedAnimatedLossyWebp.Items[1].Duration != TimeSpan.FromMilliseconds(110) ||
    decodedAnimatedLossyWebp.Items[2].Duration != TimeSpan.FromMilliseconds(60) ||
    decodedAnimatedLossyWebp.GetBitmap(2).GetPixel(20, 10)[3] != 0)
    throw new InvalidOperationException("The packaged Square.Images animated lossy WebP decoder returned invalid frames.");

var orientedWebp = Convert.FromBase64String(
    "UklGRrIAAABXRUJQVlA4WAoAAAAIAAAAEAAADAAAVlA4THIAAAAvEAADAGdAIGnjk533D/UaBtK2ibNN+q7sBIIQWWa7ZeY/+IPwBJwG5SgYYQOMattWgoxcKrBoQIUXAM3iDT4FbKwVPnO9uZAGEf0P8lxfDlJHfBpXzvKsVf4CeQadxmu7631QaARnQPtyN8CpP3MGtApFWElGGgAAAElJKgAIAAAAAQASAQMAAQAAAAYAAAAAAAAA");
using var decodedOrientedWebp = ImageDecoder.Decode(orientedWebp);
if (decodedOrientedWebp.PrimaryBitmap.Width != 13 || decodedOrientedWebp.PrimaryBitmap.Height != 17 ||
    decodedOrientedWebp.Metadata.OriginalOrientation != ImageOrientation.Rotate90 ||
    !decodedOrientedWebp.Metadata.OrientationApplied)
    throw new InvalidOperationException("The packaged Square.Images WebP EXIF orientation was not applied.");

var packBitsTiff = Convert.FromBase64String(
    "SUkqAAgAAAALAAABBAABAAAAAQAAAAEBBAABAAAAAQAAAAIBAwABAAAACAAAAAMBAwABAAAABYAAAAYBAwABAAAAAQAAABEBBAABAAAAkgAAABIBAwABAAAAAQAAABUBAwABAAAAAQAAABYBBAABAAAAAQAAABcBBAABAAAAAgAAABwBAwABAAAAAQAAAAAAAAAAKg==");
using var decodedTiff = ImageDecoder.Decode(packBitsTiff);
if (decodedTiff.Format != ImageFormat.Tiff ||
    !decodedTiff.PrimaryBitmap.Pixels.AsSpan().SequenceEqual(new byte[] { 42, 42, 42, 255 }))
    throw new InvalidOperationException("The packaged Square.Images PackBits TIFF decoder returned incorrect pixels.");
