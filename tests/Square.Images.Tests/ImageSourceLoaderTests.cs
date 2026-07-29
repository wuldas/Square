using System.Net;
using Square.Graphics;
using Xunit;

namespace Square.Images.Tests;

public sealed class ImageSourceLoaderTests
{
    [Fact]
    public async Task LocalLoaderStillLoadsRelativeApplicationResources()
    {
        var loader = new LocalImageSourceLoader();

        using var image = await loader.LoadAsync("Fixtures/animation.apng");

        Assert.Equal(2, image.FrameCount);
    }

    [Fact]
    public async Task HttpLoaderUsesInjectedClientAndDisposesResponseStream()
    {
        var trackingStream = new TrackingStream(CodecTestData.Png(1, 1, 8, 6, 0, [0, 1, 2, 3, 4]));
        using var client = new HttpClient(new FakeHttpMessageHandler((request, _) =>
        {
            Assert.Equal(new Uri("https://images.example/test.png"), request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(trackingStream)
            });
        }));
        var loader = new HttpImageSourceLoader(client);

        using var image = await loader.LoadAsync("https://images.example/test.png");

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.True(trackingStream.IsDisposed);
    }

    [Fact]
    public async Task HttpLoaderBoundsUnknownLengthResponses()
    {
        using var client = new HttpClient(new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(new byte[9]))
            })));
        var loader = new HttpImageSourceLoader(client, new ImageDecoderOptions { MaxEncodedBytes = 8 });

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await loader.LoadAsync("http://images.example/large.png"));
    }

    [Fact]
    public async Task HttpLoaderPropagatesCancellationToHandler()
    {
        var cancellationObserved = false;
        using var client = new HttpClient(new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The request should have been cancelled.");
            }
            catch (OperationCanceledException)
            {
                cancellationObserved = cancellationToken.IsCancellationRequested;
                throw;
            }
        }));
        var loader = new HttpImageSourceLoader(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await loader.LoadAsync("https://images.example/cancel.png", cancellation.Token));
        Assert.True(cancellationObserved);
    }

    [Fact]
    public async Task EmbeddedLoaderLoadsFixtureFromExplicitAssembly()
    {
        var assembly = typeof(ImageSourceLoaderTests).Assembly;
        var loader = new EmbeddedResourceImageSourceLoader(assembly);
        var source = $"embedded://{assembly.GetName().Name}/Square.Images.Tests.Fixtures.embedded.apng";

        Assert.True(loader.CanLoad(source));
        using var image = await loader.LoadAsync(source);

        Assert.Equal(2, image.FrameCount);
        Assert.False(loader.CanLoad("embedded://Other.Assembly/Square.Images.Tests.Fixtures.embedded.apng"));
    }

    [Fact]
    public async Task EmbeddedLoaderBoundsResourceReads()
    {
        var assembly = typeof(ImageSourceLoaderTests).Assembly;
        var loader = new EmbeddedResourceImageSourceLoader(assembly,
            new ImageDecoderOptions { MaxEncodedBytes = 8 });
        var source = $"embedded://{assembly.GetName().Name}/Square.Images.Tests.Fixtures.embedded.apng";

        await Assert.ThrowsAsync<InvalidDataException>(async () => await loader.LoadAsync(source));
    }

    [Fact]
    public async Task RegistrationApisRegisterAndReturnRemovableLoaders()
    {
        using var client = new HttpClient(new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        var httpLoader = ImageSourceRegistration.RegisterHttp(client);
        var embeddedLoader = ImageSourceRegistration.RegisterEmbeddedResources(typeof(ImageSourceLoaderTests).Assembly);

        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
                await ImageSourceLoaderRegistry.LoadAsync("https://images.example/missing.png"));
            Assert.True(embeddedLoader.CanLoad(
                "embedded://Square.Images.Tests/Square.Images.Tests.Fixtures.embedded.apng"));
        }
        finally
        {
            Assert.True(ImageSourceLoaderRegistry.Unregister(httpLoader));
            Assert.True(ImageSourceLoaderRegistry.Unregister(embeddedLoader));
        }
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => sendAsync(request, cancellationToken);
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
