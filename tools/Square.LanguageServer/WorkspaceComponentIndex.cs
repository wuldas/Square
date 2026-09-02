using System.Runtime.CompilerServices;
using System.Text;
using Square.Compiler.LanguageServices;

[assembly: InternalsVisibleTo("Square.LanguageServer.Tests")]

namespace Square.LanguageServer;

internal interface IWorkspaceFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IEnumerable<string> EnumerateDirectories(string path);
    IEnumerable<string> EnumerateComponentFiles(string path);
    FileAttributes GetAttributes(string path);
    Stream OpenRead(string path);
}

internal sealed class PhysicalWorkspaceFileSystem : IWorkspaceFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);

    public IEnumerable<string> EnumerateComponentFiles(string path) =>
        Directory.EnumerateFiles(path, "*.sqx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(path, "*.sqv", SearchOption.TopDirectoryOnly));

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
}

internal sealed class WorkspaceComponentIndex
{
    private const int DefaultMaxComponentFiles = 4096;
    private const int DefaultMaxDirectories = 20000;
    private const long DefaultMaxComponentBytes = 1024 * 1024;

    private static readonly HashSet<string> IgnoredDirectories = new(
        new[] { ".git", ".vs", "bin", "obj", "node_modules" },
        StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _workspacePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly TemplateSemanticAnalyzer _analyzer = new();
    private readonly int _maxComponentFiles;
    private readonly int _maxDirectories;
    private readonly long _maxComponentBytes;
    private readonly IWorkspaceFileSystem _fileSystem;

    public WorkspaceComponentIndex(
        int maxComponentFiles = DefaultMaxComponentFiles,
        int maxDirectories = DefaultMaxDirectories,
        long maxComponentBytes = DefaultMaxComponentBytes,
        IWorkspaceFileSystem? fileSystem = null)
    {
        _maxComponentFiles = Math.Max(1, maxComponentFiles);
        _maxDirectories = Math.Max(1, maxDirectories);
        _maxComponentBytes = Math.Max(1, maxComponentBytes);
        _fileSystem = fileSystem ?? new PhysicalWorkspaceFileSystem();
    }

    public IReadOnlyCollection<TemplateComponentDescriptor> Components =>
        _entries.Values.Select(entry => entry.Component).ToArray();

    public void Index(IEnumerable<string> roots, CancellationToken cancellationToken)
    {
        _entries.Clear();
        _workspacePaths.Clear();
        var directories = new Stack<string>();
        var uniqueRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remainingDirectoryCandidates = _maxDirectories;
        using (var rootEnumerator = TryGetEnumerator(() => roots))
        {
            while (rootEnumerator != null && remainingDirectoryCandidates > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryMoveNext(rootEnumerator, out var candidate)) break;
                remainingDirectoryCandidates--;
                cancellationToken.ThrowIfCancellationRequested();
                var root = NormalizeDirectoryPath(candidate);
                if (root != null && _fileSystem.DirectoryExists(root) && uniqueRoots.Add(root))
                    directories.Push(root);
            }
        }
        var visitedDirectories = 0;
        var remainingFileCandidates = _maxComponentFiles;
        while (directories.Count > 0 &&
               visitedDirectories < _maxDirectories &&
               remainingFileCandidates > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            visitedDirectories++;
            if (remainingDirectoryCandidates > 0)
            {
                using var children = TryGetEnumerator(() => _fileSystem.EnumerateDirectories(directory));
                while (children != null &&
                       remainingDirectoryCandidates > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryMoveNext(children, out var child)) break;
                    remainingDirectoryCandidates--;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IgnoredDirectories.Contains(Path.GetFileName(child)))
                        continue;
                    try
                    {
                        if ((_fileSystem.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }
                    directories.Push(child);
                }
            }

            if (remainingFileCandidates == 0) continue;
            using var files = TryGetEnumerator(() => _fileSystem.EnumerateComponentFiles(directory));
            while (files != null &&
                   remainingFileCandidates > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryMoveNext(files, out var file)) break;
                remainingFileCandidates--;
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedFile = NormalizeComponentPath(file);
                if (normalizedFile == null) continue;
                _workspacePaths.Add(normalizedFile);
                try
                {
                    var text = ReadBoundedUtf8(normalizedFile, cancellationToken);
                    if (text != null)
                        Update(normalizedFile, text, preserveOnInvalid: false, cancellationToken);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public void Update(
        string path,
        string text,
        bool preserveOnInvalid = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = NormalizeComponentPath(path);
        if (normalizedPath == null) return;
        text ??= string.Empty;
        if (Encoding.UTF8.GetByteCount(text) > _maxComponentBytes) return;
        if (!_entries.ContainsKey(normalizedPath) && _entries.Count >= _maxComponentFiles) return;

        var input = new[] { (Path: normalizedPath, Content: text, Namespace: string.Empty) };
        var component = _analyzer.BuildGeneratedComponents(input).Values.SingleOrDefault();
        cancellationToken.ThrowIfCancellationRequested();
        if (component == null)
        {
            if (!preserveOnInvalid) _entries.Remove(normalizedPath);
            return;
        }
        var contracts = _analyzer.BuildEmbeddedPropContracts(input);
        cancellationToken.ThrowIfCancellationRequested();
        contracts.TryGetValue(component.TypeName, out var props);
        var eventContracts = _analyzer.BuildEmbeddedEventContracts(input);
        cancellationToken.ThrowIfCancellationRequested();
        eventContracts.TryGetValue(component.TypeName, out var events);
        var mergedEvents = new Dictionary<string, TemplateComponentEventDescriptor>(StringComparer.Ordinal);
        foreach (var componentEvent in events ?? Array.Empty<TemplateComponentEventDescriptor>())
            if (!mergedEvents.ContainsKey(componentEvent.MemberName))
                mergedEvents.Add(componentEvent.MemberName, componentEvent);
        var codeBehindPath = normalizedPath + ".cs";
        try
        {
            if (_fileSystem.FileExists(codeBehindPath))
            {
                var codeBehind = ReadBoundedUtf8(codeBehindPath, cancellationToken);
                var codeBehindContracts = codeBehind == null
                    ? null
                    : _analyzer.BuildCodeBehindEventContracts(codeBehind);
                var matches = codeBehindContracts?.Where(pair =>
                        pair.Key.Equals(component.TypeName, StringComparison.Ordinal) ||
                        pair.Key.EndsWith("." + component.TagName, StringComparison.Ordinal))
                    .Select(pair => pair.Value)
                    .ToArray();
                if (matches?.Length == 1)
                    foreach (var componentEvent in matches[0])
                        mergedEvents[componentEvent.MemberName] = componentEvent;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        _entries[normalizedPath] = new Entry(
            component,
            props ?? Array.Empty<TemplatePropDescriptor>(),
            mergedEvents.Values
                .GroupBy(componentEvent => componentEvent.NormalizedName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .Select(group => group.First())
                .ToArray());
    }

    public void RestoreFromDisk(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = NormalizeComponentPath(path);
        if (normalizedPath == null) return;
        try
        {
            var text = ReadBoundedUtf8(normalizedPath, cancellationToken);
            if (text == null)
            {
                _entries.Remove(normalizedPath);
                return;
            }
            Update(normalizedPath, text, preserveOnInvalid: false, cancellationToken);
        }
        catch (IOException)
        {
            _entries.Remove(normalizedPath);
        }
        catch (UnauthorizedAccessException)
        {
            _entries.Remove(normalizedPath);
        }
    }

    public void Close(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = NormalizeComponentPath(path);
        if (normalizedPath == null) return;
        if (_workspacePaths.Contains(normalizedPath))
        {
            RestoreFromDisk(normalizedPath, cancellationToken);
            return;
        }
        _entries.Remove(normalizedPath);
    }

    public bool TryGetProps(string tagName, out TemplatePropDescriptor[] props)
    {
        var entry = _entries.Values.FirstOrDefault(candidate =>
            candidate.Component.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            props = Array.Empty<TemplatePropDescriptor>();
            return false;
        }
        props = entry.Props;
        return true;
    }

    public bool TryGetEvents(string tagName, out TemplateComponentEventDescriptor[] events)
    {
        var entry = _entries.Values.FirstOrDefault(candidate =>
            candidate.Component.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            events = Array.Empty<TemplateComponentEventDescriptor>();
            return false;
        }
        events = entry.Events;
        return true;
    }

    private string? ReadBoundedUtf8(string path, CancellationToken cancellationToken)
    {
        using var stream = _fileSystem.OpenRead(path);
        if (stream.Length > _maxComponentBytes) return null;
        using var content = new MemoryStream((int)Math.Min(stream.Length, _maxComponentBytes));
        var buffer = new byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (content.Length + read > _maxComponentBytes) return null;
            content.Write(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = content.ToArray();
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }

    private static IEnumerator<string>? TryGetEnumerator(Func<IEnumerable<string>> source)
    {
        try
        {
            return source().GetEnumerator();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryMoveNext(IEnumerator<string> enumerator, out string current)
    {
        try
        {
            if (enumerator.MoveNext())
            {
                current = enumerator.Current;
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        current = string.Empty;
        return false;
    }

    private static string? NormalizeDirectoryPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? NormalizeComponentPath(string path)
    {
        if (!IsComponentPath(path)) return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsComponentPath(string path) =>
        path.EndsWith(".sqx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase);

    private sealed class Entry
    {
        public Entry(
            TemplateComponentDescriptor component,
            TemplatePropDescriptor[] props,
            TemplateComponentEventDescriptor[] events)
        {
            Component = component;
            Props = props;
            Events = events;
        }

        public TemplateComponentDescriptor Component { get; }
        public TemplatePropDescriptor[] Props { get; }
        public TemplateComponentEventDescriptor[] Events { get; }
    }
}
