using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class WorkspaceComponentIndexTests
{
    [Fact]
    public void WorkspaceIndexStopsEnumeratingWhenTheFileLimitIsReached()
    {
        var fileSystem = new FakeWorkspaceFileSystem
        {
            Files = _ => EnumerateFilesPastLimit()
        };
        var index = new WorkspaceComponentIndex(
            maxComponentFiles: 1,
            maxComponentBytes: 80,
            fileSystem: fileSystem);

        index.Index(new[] { "workspace" }, CancellationToken.None);

        Assert.Equal("Card", Assert.Single(index.Components).TagName);
    }

    [Fact]
    public void WorkspaceIndexCountsRejectedFilesAgainstTheFileBudget()
    {
        var fileSystem = new FakeWorkspaceFileSystem
        {
            Files = _ => EnumerateRejectedFilePastLimit()
        };
        var index = new WorkspaceComponentIndex(
            maxComponentFiles: 1,
            maxComponentBytes: 80,
            fileSystem: fileSystem);

        index.Index(new[] { "workspace" }, CancellationToken.None);

        Assert.Empty(index.Components);
    }

    [Fact]
    public void WorkspaceIndexStopsEnumeratingRootsAtTheDirectoryLimit()
    {
        var index = new WorkspaceComponentIndex(
            maxComponentFiles: 1,
            maxDirectories: 1,
            maxComponentBytes: 80,
            fileSystem: new FakeWorkspaceFileSystem());

        index.Index(EnumerateRootsPastLimit(), CancellationToken.None);

        Assert.Empty(index.Components);
    }

    [Fact]
    public void WorkspaceIndexCountsRejectedRootsAgainstTheDirectoryBudget()
    {
        var fileSystem = new FakeWorkspaceFileSystem
        {
            Exists = _ => false
        };
        var index = new WorkspaceComponentIndex(
            maxComponentFiles: 1,
            maxDirectories: 1,
            maxComponentBytes: 80,
            fileSystem: fileSystem);

        index.Index(EnumerateRejectedRootPastLimit(), CancellationToken.None);

        Assert.Empty(index.Components);
    }

    [Fact]
    public void WorkspaceIndexIgnoresDirectoryMetadataRaces()
    {
        var fileSystem = new FakeWorkspaceFileSystem
        {
            Directories = _ => new[] { "removed" },
            Files = _ => new[] { "Card.sqx" },
            Attributes = _ => throw new IOException("Directory disappeared after enumeration.")
        };
        var index = new WorkspaceComponentIndex(
            maxComponentFiles: 1,
            maxDirectories: 2,
            maxComponentBytes: 80,
            fileSystem: fileSystem);

        index.Index(new[] { "workspace" }, CancellationToken.None);

        Assert.Equal("Card", Assert.Single(index.Components).TagName);
    }

    [Fact]
    public void WorkspaceIndexCountsRejectedChildrenAgainstTheDirectoryBudget()
    {
        var fileSystem = new FakeWorkspaceFileSystem
        {
            Directories = _ => EnumerateRejectedChildPastLimit(),
            Attributes = _ => throw new IOException("Directory disappeared after enumeration.")
        };
        var index = new WorkspaceComponentIndex(
            maxComponentFiles: 1,
            maxDirectories: 2,
            maxComponentBytes: 80,
            fileSystem: fileSystem);

        index.Index(new[] { "workspace" }, CancellationToken.None);

        Assert.Empty(index.Components);
    }

    [Fact]
    public void IncrementalUpdatesRespectEntryAndUtf8SizeLimits()
    {
        var index = new WorkspaceComponentIndex(maxComponentFiles: 2, maxComponentBytes: 80);
        index.Update("A.sqx", "<template><View /></template>");
        index.Update("B.sqv", "<template><View /></template>");
        index.Update("C.sqx", "<template><View /></template>");

        Assert.Equal(new[] { "A", "B" }, index.Components.Select(component => component.TagName).OrderBy(name => name));

        var sizeLimited = new WorkspaceComponentIndex(maxComponentFiles: 2, maxComponentBytes: 32);
        sizeLimited.Update("Large.sqx", "<template><View /></template>" + new string('界', 20));

        Assert.Empty(sizeLimited.Components);
    }

    [Fact]
    public void WorkspaceIndexHonorsPreCancelledInitialization()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var index = new WorkspaceComponentIndex(maxComponentFiles: 2, maxComponentBytes: 80);

        Assert.Throws<OperationCanceledException>(() =>
            index.Index(new[] { Path.GetTempPath() }, cancellation.Token));
    }

    [Fact]
    public void WorkspaceIndexChecksCancellationBeforeEnumeratingTheNextFile()
    {
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new FakeWorkspaceFileSystem
        {
            Files = _ => EnumerateUntilCancelled(cancellation.Token),
            Open = _ =>
            {
                cancellation.Cancel();
                return ComponentStream();
            }
        };
        var index = new WorkspaceComponentIndex(
            maxComponentFiles: 2,
            maxComponentBytes: 80,
            fileSystem: fileSystem);

        Assert.Throws<OperationCanceledException>(() =>
            index.Index(new[] { "workspace" }, cancellation.Token));
    }

    [Theory]
    [InlineData("HandleDidOpen")]
    [InlineData("HandleDidChange")]
    [InlineData("HandleDidClose")]
    public void IncrementalDocumentHandlersAcceptTheRunCancellationToken(string methodName)
    {
        var method = typeof(LanguageServerHost).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(
            new[] { typeof(System.Text.Json.JsonElement), typeof(CancellationToken) },
            method!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void WorkspaceRootEnumerationIsLazyAndCancellationAware()
    {
        using var request = System.Text.Json.JsonDocument.Parse(
            """{"params":{"workspaceFolders":[{"uri":"file:///C:/First"},{"uri":"file:///C:/Second"}]}}""");
        using var cancellation = new CancellationTokenSource();
        var roots = LanguageServerHost.EnumerateWorkspaceRoots(request.RootElement, cancellation.Token);
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => roots.ToArray());
    }

    [Fact]
    public void CancelledDidOpenDoesNotMutateTheDocumentStore()
    {
        const string uri = "file:///C:/Square/Cancelled.sqx";
        using var request = System.Text.Json.JsonDocument.Parse(
            """{"params":{"textDocument":{"uri":"file:///C:/Square/Cancelled.sqx","version":1,"text":"<template><View /></template>"}}}""");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var host = new LanguageServerHost(Stream.Null, Stream.Null);

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            InvokeDocumentHandler(host, "HandleDidOpen", request.RootElement, cancellation.Token));

        Assert.IsType<OperationCanceledException>(exception.InnerException);
        Assert.False(GetDocumentStore(host).TryGet(uri, out _));
    }

    [Fact]
    public void CancelledDidChangePreservesThePreviousDocumentState()
    {
        const string uri = "file:///C:/Square/CancelledChange.sqx";
        using var open = System.Text.Json.JsonDocument.Parse(
            """{"params":{"textDocument":{"uri":"file:///C:/Square/CancelledChange.sqx","version":1,"text":"<template><View /></template>"}}}""");
        using var change = System.Text.Json.JsonDocument.Parse(
            """{"params":{"textDocument":{"uri":"file:///C:/Square/CancelledChange.sqx","version":2},"contentChanges":[{"text":"<template><Button /></template>"}]}}""");
        var host = new LanguageServerHost(Stream.Null, Stream.Null);
        InvokeDocumentHandler(host, "HandleDidOpen", open.RootElement, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            InvokeDocumentHandler(host, "HandleDidChange", change.RootElement, cancellation.Token));

        Assert.IsType<OperationCanceledException>(exception.InnerException);
        Assert.True(GetDocumentStore(host).TryGet(uri, out var document));
        Assert.NotNull(document);
        Assert.Equal(1, document!.Version);
        Assert.Equal("<template><View /></template>", document.Text);
    }

    [Fact]
    public void CancelledDidCloseKeepsTheDocumentOpen()
    {
        const string uri = "file:///C:/Square/CancelledClose.sqx";
        using var open = System.Text.Json.JsonDocument.Parse(
            """{"params":{"textDocument":{"uri":"file:///C:/Square/CancelledClose.sqx","version":1,"text":"<template><View /></template>"}}}""");
        using var close = System.Text.Json.JsonDocument.Parse(
            """{"params":{"textDocument":{"uri":"file:///C:/Square/CancelledClose.sqx"}}}""");
        var host = new LanguageServerHost(Stream.Null, Stream.Null);
        InvokeDocumentHandler(host, "HandleDidOpen", open.RootElement, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            InvokeDocumentHandler(host, "HandleDidClose", close.RootElement, cancellation.Token));

        Assert.IsType<OperationCanceledException>(exception.InnerException);
        Assert.True(GetDocumentStore(host).TryGet(uri, out var document));
        Assert.NotNull(document);
    }

    private static IEnumerable<string> EnumerateFilesPastLimit()
    {
        yield return "Card.sqx";
        throw new InvalidOperationException("The index enumerated beyond its configured file limit.");
    }

    private static IEnumerable<string> EnumerateRejectedFilePastLimit()
    {
        yield return "Ignored.sqa";
        throw new InvalidOperationException("A rejected file did not consume the file budget.");
    }

    private static IEnumerable<string> EnumerateRootsPastLimit()
    {
        yield return "workspace";
        throw new InvalidOperationException("The index enumerated beyond its configured directory limit.");
    }

    private static IEnumerable<string> EnumerateRejectedRootPastLimit()
    {
        yield return "missing";
        throw new InvalidOperationException("A rejected root did not consume the directory budget.");
    }

    private static IEnumerable<string> EnumerateRejectedChildPastLimit()
    {
        yield return "removed";
        throw new InvalidOperationException("A rejected child did not consume the directory budget.");
    }

    private static IEnumerable<string> EnumerateUntilCancelled(CancellationToken cancellationToken)
    {
        yield return "Card.sqx";
        if (cancellationToken.IsCancellationRequested)
            throw new InvalidOperationException("MoveNext was called after cancellation.");
        yield return "Other.sqx";
    }

    private static Stream ComponentStream() =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<template><View /></template>"));

    private static void InvokeDocumentHandler(
        LanguageServerHost host,
        string methodName,
        System.Text.Json.JsonElement request,
        CancellationToken cancellationToken)
    {
        var method = typeof(LanguageServerHost).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(host, new object[] { request, cancellationToken });
    }

    private static DocumentStore GetDocumentStore(LanguageServerHost host)
    {
        var field = typeof(LanguageServerHost).GetField(
            "_documents",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<DocumentStore>(field!.GetValue(host));
    }

    private sealed class FakeWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public Func<string, IEnumerable<string>> Directories { get; set; } = _ => Array.Empty<string>();
        public Func<string, IEnumerable<string>> Files { get; set; } = _ => Array.Empty<string>();
        public Func<string, FileAttributes> Attributes { get; set; } = _ => FileAttributes.Normal;
        public Func<string, Stream> Open { get; set; } = _ => ComponentStream();
        public Func<string, bool> Exists { get; set; } = _ => true;

        public bool DirectoryExists(string path) => Exists(path);

        public IEnumerable<string> EnumerateDirectories(string path) => Directories(path);

        public IEnumerable<string> EnumerateComponentFiles(string path) => Files(path);

        public FileAttributes GetAttributes(string path) => Attributes(path);

        public Stream OpenRead(string path) => Open(path);
    }
}
