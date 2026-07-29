using System.Reflection;
using System.Runtime.CompilerServices;
using Square.Graphics;
using Square.Resources;

namespace Square.Images;

internal sealed class ImageDocumentFrameSource(ImageDocument document) : IImageFrameSource
{
    private ImageDocument? _document = document;

    public int Width => GetDocument().PrimaryItem.Width;
    public int Height => GetDocument().PrimaryItem.Height;
    public int FrameCount => GetDocument().Items.Count;
    public int PlayCount => GetDocument().Animation is { } animation
        ? animation.LoopsForever ? 0 : animation.PlayCount
        : 1;

    public Bitmap GetFrame(int index) => GetDocument().GetBitmap(index);
    public TimeSpan GetFrameDuration(int index)
    {
        var current = GetDocument();
        if ((uint)index >= (uint)current.Items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return current.Items[index].Duration;
    }

    public void Dispose() => Interlocked.Exchange(ref _document, null)?.Dispose();

    private ImageDocument GetDocument() => _document ?? throw new ObjectDisposedException(nameof(ImageDocumentFrameSource));
}

internal sealed class LocalImageSourceLoader : IImageSourceLoader
{
    public bool CanLoad(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        return !Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.IsFile;
    }

    public async ValueTask<IImageFrameSource> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var document = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = OpenSource(source);
            var decoded = ImageDecoder.Decode(stream);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return decoded;
            }
            catch
            {
                decoded.Dispose();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
        return new ImageDocumentFrameSource(document);
    }

    private static Stream OpenSource(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile) throw new NotSupportedException("Only local image file paths are supported.");
            return File.OpenRead(uri.LocalPath);
        }

        return ApplicationResource.Open(source);
    }
}

/// <summary>Loads HTTP and HTTPS image sources with a caller-provided <see cref="HttpClient"/>.</summary>
public sealed class HttpImageSourceLoader : IImageSourceLoader
{
    private readonly HttpClient _httpClient;
    private readonly ImageDecoderOptions _options;

    public HttpImageSourceLoader(HttpClient httpClient, ImageDecoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _options = options ?? new ImageDecoderOptions();
        _options.Validate();
        _httpClient = httpClient;
    }

    public bool CanLoad(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";

    public async ValueTask<IImageFrameSource> LoadAsync(string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new NotSupportedException("Only HTTP and HTTPS image sources are supported.");

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length > _options.MaxEncodedBytes)
            throw new InvalidDataException("Encoded image exceeds the configured byte limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ImageSourceDecoder.DecodeAsync(stream, _options, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Loads manifest resources from an explicitly supplied assembly.</summary>
public sealed class EmbeddedResourceImageSourceLoader : IImageSourceLoader
{
    private readonly Assembly _assembly;
    private readonly string _assemblyName;
    private readonly ImageDecoderOptions _options;

    public EmbeddedResourceImageSourceLoader(Assembly assembly, ImageDecoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _options = options ?? new ImageDecoderOptions();
        _options.Validate();
        _assembly = assembly;
        _assemblyName = assembly.GetName().Name
            ?? throw new ArgumentException("The assembly must have a simple name.", nameof(assembly));
    }

    public bool CanLoad(string source) => TryGetResourceName(source, out _);

    public async ValueTask<IImageFrameSource> LoadAsync(string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!TryGetResourceName(source, out var resourceName))
            throw new NotSupportedException($"The embedded image source does not target assembly '{_assemblyName}'.");

        await using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded image resource '{resourceName}' was not found in '{_assemblyName}'.",
                resourceName);
        return await ImageSourceDecoder.DecodeAsync(stream, _options, cancellationToken).ConfigureAwait(false);
    }

    private bool TryGetResourceName(string source, out string resourceName)
    {
        resourceName = string.Empty;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "embedded" ||
            !string.Equals(uri.Host, _assemblyName, StringComparison.OrdinalIgnoreCase)) return false;

        resourceName = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        return resourceName.Length != 0;
    }
}

internal static class ImageSourceDecoder
{
    public static async ValueTask<IImageFrameSource> DecodeAsync(Stream stream, ImageDecoderOptions options,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > options.MaxEncodedBytes)
                throw new InvalidDataException("Encoded image exceeds the configured byte limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var document = ImageDecoder.Decode(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), options);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ImageDocumentFrameSource(document);
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }
}

public static class ImageSourceRegistration
{
    private static readonly LocalImageSourceLoader Loader = new();

    public static void RegisterDefaults() => ImageSourceLoaderRegistry.Register(Loader);

    /// <summary>Registers an HTTP(S) loader that uses, but does not own, <paramref name="httpClient"/>.</summary>
    public static HttpImageSourceLoader RegisterHttp(HttpClient httpClient, ImageDecoderOptions? options = null)
    {
        var loader = new HttpImageSourceLoader(httpClient, options);
        ImageSourceLoaderRegistry.Register(loader);
        return loader;
    }

    /// <summary>Registers an <c>embedded://</c> loader for manifest resources in <paramref name="assembly"/>.</summary>
    public static EmbeddedResourceImageSourceLoader RegisterEmbeddedResources(Assembly assembly,
        ImageDecoderOptions? options = null)
    {
        var loader = new EmbeddedResourceImageSourceLoader(assembly, options);
        ImageSourceLoaderRegistry.Register(loader);
        return loader;
    }

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register() => RegisterDefaults();
#pragma warning restore CA2255
}
