using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.LanguageServices;

namespace Square.LanguageServer;

internal sealed record ProjectBufferSnapshot(string Path, int Version, string Text);

internal sealed class ProjectTemplateWorkspace : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static long _nextGeneration;
    private readonly Dictionary<string, LoadedProject> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateGate = new();
    private string[] _roots = Array.Empty<string>();
    private bool _disposed;

    internal event Action<IReadOnlyCollection<string>>? Invalidated;

    public void SetRoots(IEnumerable<string> roots)
    {
        _roots = roots
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ProjectTemplateContext?> GetContextAsync(
        string documentPath,
        IReadOnlyList<ProjectBufferSnapshot> buffers,
        CancellationToken cancellationToken)
    {
        if (_disposed || string.IsNullOrWhiteSpace(documentPath) || !Path.IsPathRooted(documentPath)) return null;
        if (!MsBuildLocatorInitializer.TryRegister(out var registrationError))
            return ProjectTemplateContext.Incomplete("MSBuild SDK discovery is unavailable: " + registrationError);
        documentPath = Path.GetFullPath(documentPath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loadedOwners = new List<LoadedProject>();
            lock (_stateGate)
            {
                foreach (var project in _projects.Values)
                    if (OwnsDocument(project.Project, documentPath)) loadedOwners.Add(project);
            }
            if (loadedOwners.Count > 0)
            {
                var loadedSelected = loadedOwners.OrderBy(owner => owner.ProjectPath, StringComparer.Ordinal).First();
                try
                {
                    var loadedContext = await CreateContextAsync(loadedSelected, documentPath, buffers, cancellationToken).ConfigureAwait(false);
                    if (loadedOwners.Count > 1)
                        loadedContext = loadedContext.WithWarning("Document belongs to multiple projects; selected '" +
                            loadedSelected.ProjectPath + "' by ordinal project path.");
                    return loadedContext;
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    return ProjectTemplateContext.Incomplete("Unable to evaluate '" + loadedSelected.ProjectPath + "': " + exception.Message);
                }
            }

            var failures = new List<string>();
            var candidates = FindCandidateProjects(documentPath, out var exhausted);
            foreach (var candidateGroup in candidates
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase))
            {
                var owners = new List<LoadedProject>();
                foreach (var projectPath in candidateGroup.OrderBy(path => path, StringComparer.Ordinal))
                {
                    try
                    {
                        var loaded = await GetOrLoadProjectAsync(projectPath, cancellationToken).ConfigureAwait(false);
                        if (OwnsDocument(loaded.Project, documentPath)) owners.Add(loaded);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
                    {
                        failures.Add("Unable to load '" + projectPath + "': " + exception.Message);
                    }
                }
                if (owners.Count == 0) continue;
                try
                {
                    var selected = owners.OrderBy(owner => owner.ProjectPath, StringComparer.Ordinal).First();
                    var context = await CreateContextAsync(selected, documentPath, buffers, cancellationToken).ConfigureAwait(false);
                    if (owners.Count > 1)
                        context = context.WithWarning("Document belongs to multiple projects; selected '" +
                            selected.ProjectPath + "' by ordinal project path.");
                    return context;
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    return ProjectTemplateContext.Incomplete("Unable to evaluate '" + owners[0].ProjectPath + "': " + exception.Message);
                }
            }
            if (failures.Count > 0)
                return ProjectTemplateContext.Incomplete(string.Join(Environment.NewLine, failures));
            if (exhausted)
                return ProjectTemplateContext.Incomplete(
                    "Project ownership search exceeded its discovery budget for '" + documentPath + "'.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        OnProjectInvalidated(Path.GetFullPath(path));
    }

    public void InvalidateBuffer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = Path.GetFullPath(path);
        var affected = new List<string>();
        lock (_stateGate)
        {
            foreach (var project in _projects.Values)
            {
                if (!project.MatchesPath(normalized)) continue;
                InvalidateCache(project, markDirty: false);
                affected.Add(project.ProjectPath);
            }
        }
        if (affected.Count > 0) Invalidated?.Invoke(affected);
    }

    private void OnProjectInvalidated(string path)
    {
        var normalized = Path.GetFullPath(path);
        var affected = new List<string>();
        lock (_stateGate)
        {
            var snapshot = _projects.Values.ToArray();
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in snapshot)
            {
                if (!project.MatchesPath(normalized)) continue;
                InvalidateCache(project, markDirty: true);
                changed.Add(project.ProjectPath);
            }
            var grew = changed.Count > 0;
            while (grew)
            {
                grew = false;
                foreach (var project in snapshot)
                {
                    if (changed.Contains(project.ProjectPath) || !DependsOnAny(project, snapshot, changed)) continue;
                    InvalidateCache(project, markDirty: true);
                    changed.Add(project.ProjectPath);
                    grew = true;
                }
            }
            foreach (var projectPath in changed) affected.Add(projectPath);
        }
        if (affected.Count > 0) Invalidated?.Invoke(affected);
    }

    private static void InvalidateCache(LoadedProject project, bool markDirty)
    {
        if (markDirty) project.Dirty = true;
        project.Generation = Interlocked.Increment(ref _nextGeneration);
        project.CachedContext = null;
        project.CachedFingerprint = null;
    }

    private static bool DependsOnAny(
        LoadedProject project,
        IReadOnlyList<LoadedProject> all,
        ISet<string> changed)
    {
        foreach (var reference in project.Project.ProjectReferences)
        {
            foreach (var candidate in all)
            {
                if (candidate.Project.Id != reference.ProjectId) continue;
                if (changed.Contains(candidate.ProjectPath)) return true;
            }
        }
        return false;
    }

    private async Task<LoadedProject> GetOrLoadProjectAsync(string projectPath, CancellationToken cancellationToken)
    {
        LoadedProject? existing;
        lock (_stateGate)
        {
            _projects.TryGetValue(projectPath, out existing);
        }
        if (existing != null && !existing.Dirty) return existing;
        if (existing != null)
        {
            lock (_stateGate)
            {
                _projects.Remove(projectPath);
            }
            existing.Dispose();
        }

        var hostServices = Microsoft.CodeAnalysis.Host.Mef.MefHostServices.Create(
            Microsoft.CodeAnalysis.Host.Mef.MSBuildMefHostServices.DefaultAssemblies
                .Add(typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly)
                .Add(typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions).Assembly));
        var workspace = MSBuildWorkspace.Create(
            new Dictionary<string, string>
            {
                ["DesignTimeBuild"] = "true",
                ["BuildingInsideVisualStudio"] = "true"
            },
            hostServices);
        workspace.LoadMetadataForReferencedProjects = false;
        try
        {
            var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            var loaded = new LoadedProject(projectPath, workspace, project, OnProjectInvalidated);
            lock (_stateGate)
            {
                _projects.Add(projectPath, loaded);
            }
            return loaded;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task<ProjectTemplateContext> CreateContextAsync(
        LoadedProject loaded,
        string documentPath,
        IReadOnlyList<ProjectBufferSnapshot> buffers,
        CancellationToken cancellationToken)
    {
        var solution = loaded.Project.Solution;
        var relevantPaths = new HashSet<string>(
            solution.Projects.SelectMany(candidate => candidate.Documents.Cast<TextDocument>().Concat(candidate.AdditionalDocuments))
                .Select(document => document.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))!
                .Select(path => Path.GetFullPath(path!)),
            StringComparer.OrdinalIgnoreCase);
        var fingerprint = loaded.Generation + "|" + documentPath + "|" + string.Join("|", buffers
            .Where(buffer => relevantPaths.Contains(Path.GetFullPath(buffer.Path)))
            .OrderBy(buffer => buffer.Path, StringComparer.OrdinalIgnoreCase)
            .Select(buffer => buffer.Path + ":" + buffer.Version + ":" + ContentDigest(buffer.Text)));
        if (loaded.CachedContext != null && string.Equals(loaded.CachedFingerprint, fingerprint, StringComparison.Ordinal))
            return loaded.CachedContext;
        foreach (var buffer in buffers)
        {
            foreach (var candidateProject in solution.Projects.ToArray())
            {
                foreach (var additional in candidateProject.AdditionalDocuments.Where(document => PathEquals(document.FilePath, buffer.Path)))
                    solution = solution.WithAdditionalDocumentText(additional.Id, SourceText.From(buffer.Text));
                foreach (var source in candidateProject.Documents.Where(document => PathEquals(document.FilePath, buffer.Path)))
                    solution = solution.WithDocumentText(source.Id, SourceText.From(buffer.Text));
            }
        }

        foreach (var candidateId in solution.ProjectIds.ToArray())
        {
            var candidateProject = solution.GetProject(candidateId);
            if (candidateProject == null) continue;
            var filtered = candidateProject.AnalyzerReferences
                .Where(reference => !IsSquareCompilerAnalyzer(reference))
                .ToArray();
            if (filtered.Length != candidateProject.AnalyzerReferences.Count)
                solution = candidateProject.WithAnalyzerReferences(filtered).Solution;
        }

        var project = solution.GetProject(loaded.Project.Id)!;
        var dependencyGraph = solution.GetProjectDependencyGraph();
        var selectedIds = new HashSet<ProjectId>(dependencyGraph.GetProjectsThatThisProjectTransitivelyDependsOn(project.Id))
            { project.Id };
        var order = dependencyGraph.GetTopologicallySortedProjects(cancellationToken)
            .Where(selectedIds.Contains)
            .ToArray();
        var originalCompilations = new Dictionary<ProjectId, Compilation>();
        foreach (var projectId in order)
        {
            var candidate = solution.GetProject(projectId);
            if (candidate == null) continue;
            var original = await candidate.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (original != null) originalCompilations[projectId] = original;
        }

        var analyses = new Dictionary<ProjectId, TemplateProjectAnalysis>();
        var namespaces = new Dictionary<ProjectId, List<(string Path, string Content, string Namespace)>>();
        string? graphWarning = null;
        foreach (var projectId in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = solution.GetProject(projectId);
            if (candidate == null || !originalCompilations.TryGetValue(projectId, out var compilation)) continue;
            foreach (var compilationReference in compilation.References.OfType<CompilationReference>().ToArray())
            {
                var matches = originalCompilations.Where(pair => ReferenceEquals(pair.Value, compilationReference.Compilation)).ToArray();
                if (matches.Length != 1 || !analyses.TryGetValue(matches[0].Key, out var dependencyAnalysis))
                {
                    graphWarning = "Unable to uniquely associate a project compilation reference in '" + candidate.FilePath + "'.";
                    continue;
                }
                compilation = compilation.RemoveReferences(compilationReference).AddReferences(
                    dependencyAnalysis.OutputCompilation.ToMetadataReference(
                        compilationReference.Properties.Aliases,
                        compilationReference.Properties.EmbedInteropTypes));
            }
            var inputs = await BuildTemplateInputsAsync(candidate, cancellationToken).ConfigureAwait(false);
            namespaces[projectId] = inputs;
            analyses[projectId] = new TemplateSemanticAnalyzer().AnalyzeProject(compilation, inputs, cancellationToken);
        }

        if (!analyses.TryGetValue(project.Id, out var analysis))
            return ProjectTemplateContext.Incomplete("Project compilation is unavailable.");
        var currentInputs = namespaces.GetValueOrDefault(project.Id) ?? new List<(string Path, string Content, string Namespace)>();
        var currentInput = currentInputs.FirstOrDefault(input => PathEquals(input.Path, documentPath));
        var warningList = loaded.Workspace.Diagnostics
            .Where(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            .Select(diagnostic => diagnostic.Message)
            .ToList();
        if (graphWarning != null) warningList.Add(graphWarning);
        var globalOptions = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        globalOptions.TryGetValue("build_property.ProjectAssetsFile", out var assetsFile);
        if (string.IsNullOrWhiteSpace(assetsFile))
            assetsFile = Path.Combine(Path.GetDirectoryName(project.FilePath) ?? string.Empty, "obj", "project.assets.json");
        if (!File.Exists(assetsFile)) warningList.Add("Project restore assets are unavailable: " + assetsFile);
        if (!originalCompilations.TryGetValue(project.Id, out var currentCompilation) ||
            currentCompilation.GetTypeByMetadataName("Square.UI.Element") == null)
            warningList.Add("Required Square framework metadata is unavailable.");
        else
            foreach (var reference in currentCompilation.References.OfType<PortableExecutableReference>())
                if (!string.IsNullOrWhiteSpace(reference.FilePath) && !File.Exists(reference.FilePath))
                    warningList.Add("Metadata reference is unavailable: " + reference.FilePath);
        var warningParts = warningList.Distinct(StringComparer.Ordinal).ToArray();
        var warning = warningParts.Length == 0 ? null : string.Join(Environment.NewLine, warningParts);
        var context = new ProjectTemplateContext(
            analysis,
            currentInput.Namespace ?? string.Empty,
            loaded.Generation,
            warningParts.Length == 0,
            warning,
            loaded.ProjectPath,
            Array.Empty<TemplateComponentDescriptor>());
        loaded.CachedFingerprint = fingerprint;
        loaded.CachedContext = context;
        return context;
    }

    private static string ContentDigest(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (var character in text ?? string.Empty)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static bool IsSquareCompilerAnalyzer(AnalyzerReference reference)
    {
        var display = reference.Display ?? string.Empty;
        return Path.GetFileName(display).Equals("Square.Compiler.dll", StringComparison.OrdinalIgnoreCase) ||
               display.IndexOf("Square.Compiler", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static async Task<List<(string Path, string Content, string Namespace)>> BuildTemplateInputsAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var globalOptions = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        globalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
        globalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projectDirectory);
        rootNamespace = string.IsNullOrWhiteSpace(rootNamespace) ? "Square.Sample" : rootNamespace;
        projectDirectory = string.IsNullOrWhiteSpace(projectDirectory) ? Path.GetDirectoryName(project.FilePath) : projectDirectory;
        var inputs = new List<(string Path, string Content, string Namespace)>();
        var additionalTexts = project.AnalyzerOptions.AdditionalFiles;
        foreach (var additional in project.AdditionalDocuments.Where(document => TemplateNamespaceResolver.IsTemplateFile(document.FilePath ?? string.Empty)))
        {
            var text = await additional.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var matchingText = additionalTexts.FirstOrDefault(item => PathEquals(item.Path, additional.FilePath));
            var logicalPath = string.Empty;
            if (matchingText != null)
                project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(matchingText)
                    .TryGetValue("build_metadata.AdditionalFiles.Link", out logicalPath);
            inputs.Add((
                additional.FilePath!,
                text.ToString(),
                TemplateNamespaceResolver.GetDefaultNamespace(
                    rootNamespace ?? "Square.Sample",
                    additional.FilePath!,
                    projectDirectory ?? string.Empty,
                    logicalPath ?? string.Empty)));
        }
        return inputs;
    }

    private IReadOnlyList<string> FindCandidateProjects(string documentPath, out bool exhausted)
    {
        exhausted = false;
        var results = new List<string>();
        var fullDocumentPath = Path.GetFullPath(documentPath);
        var insideRoot = _roots.Length == 0 || _roots.Any(root =>
            fullDocumentPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                        Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (insideRoot)
        {
            var directory = Path.GetDirectoryName(fullDocumentPath);
            var levels = 0;
            while (!string.IsNullOrEmpty(directory) && levels++ < 64)
            {
                IEnumerable<string> projects;
                try
                {
                    projects = Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFullPath)
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    projects = Array.Empty<string>();
                }
                results.AddRange(projects);
                if (_roots.Length > 0 && _roots.Any(root => PathEquals(root, directory))) break;
                var parent = Path.GetDirectoryName(directory);
                if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)) break;
                directory = parent;
            }
            if (levels >= 64) exhausted = true;
        }

        foreach (var root in _roots)
        {
            var directories = new Stack<string>();
            directories.Push(root);
            var visited = 0;
            var budgetExceeded = false;
            while (directories.Count > 0)
            {
                if (visited++ >= 20000)
                {
                    budgetExceeded = true;
                    break;
                }
                var directory = directories.Pop();
                string[] projects;
                string[] children;
                try
                {
                    projects = Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).ToArray();
                    children = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var project in projects.OrderBy(path => path, StringComparer.Ordinal)) results.Add(Path.GetFullPath(project));
                foreach (var child in children)
                {
                    var name = Path.GetFileName(child);
                    if (name is ".git" or ".vs" or "bin" or "obj" or "node_modules") continue;
                    directories.Push(child);
                }
            }
            if (budgetExceeded) exhausted = true;
        }
        return results;
    }

    private static bool OwnsDocument(Project project, string path) =>
        project.AdditionalDocuments.Any(document => PathEquals(document.FilePath, path)) ||
        project.Documents.Any(document => PathEquals(document.FilePath, path));

    private static bool IsTemplatePath(string? path) => path != null &&
        (path.EndsWith(".sqx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase));

    private static bool PathEquals(string? left, string? right) =>
        left != null && right != null && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var project in _projects.Values) project.Dispose();
        _projects.Clear();
        _gate.Dispose();
    }

    private sealed class LoadedProject : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly HashSet<string> _watchedFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _watchedDirectories = new();
        private readonly Action<string> _invalidated;

        public LoadedProject(string projectPath, MSBuildWorkspace workspace, Project project, Action<string> invalidated)
        {
            ProjectPath = projectPath;
            Workspace = workspace;
            Project = project;
            _invalidated = invalidated;
            Generation = Interlocked.Increment(ref _nextGeneration);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            DirectoryPrefix = projectDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            AddWatcher(projectDirectory, recursive: true);

            foreach (var propsFile in EnumerateAncestorBuildFiles(projectDirectory))
                AddWatchedFile(propsFile);

            var globalOptions = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
            globalOptions.TryGetValue("build_property.ProjectAssetsFile", out var assetsFile);
            if (string.IsNullOrWhiteSpace(assetsFile))
                assetsFile = Path.Combine(projectDirectory, "obj", "project.assets.json");
            AddWatchedFile(assetsFile);
            AddWatcher(Path.GetDirectoryName(assetsFile), recursive: false);

            foreach (var reference in project.MetadataReferences.OfType<PortableExecutableReference>())
            {
                if (string.IsNullOrWhiteSpace(reference.FilePath)) continue;
                if (reference.FilePath.StartsWith(DirectoryPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                AddWatcher(Path.GetDirectoryName(reference.FilePath), recursive: false);
            }

            foreach (var document in project.Documents.Cast<TextDocument>().Concat(project.AdditionalDocuments))
            {
                var path = document.FilePath;
                if (string.IsNullOrWhiteSpace(path) || path.StartsWith(DirectoryPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                AddWatcher(Path.GetDirectoryName(path), recursive: false);
            }
        }

        public string ProjectPath { get; }
        public string DirectoryPrefix { get; }
        public MSBuildWorkspace Workspace { get; }
        public Project Project { get; }
        public long Generation { get; set; }
        public string? CachedFingerprint { get; set; }
        public ProjectTemplateContext? CachedContext { get; set; }
        public bool Dirty { get; set; }

        public bool MatchesPath(string fullPath)
        {
            if (fullPath.StartsWith(DirectoryPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            if (_watchedFiles.Contains(fullPath)) return true;
            foreach (var directory in _watchedDirectories)
            {
                if (string.Equals(Path.GetDirectoryName(fullPath), directory, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void AddWatchedFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            _watchedFiles.Add(Path.GetFullPath(path));
            AddWatcher(Path.GetDirectoryName(Path.GetFullPath(path)), recursive: false);
        }

        private void AddWatcher(string? directory, bool recursive)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            directory = Path.GetFullPath(directory);
            if (_watchedDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase)) return;
            _watchedDirectories.Add(directory);
            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = recursive,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
                };
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        private static IEnumerable<string> EnumerateAncestorBuildFiles(string projectDirectory)
        {
            var names = new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" };
            var directory = projectDirectory;
            var levels = 0;
            while (!string.IsNullOrEmpty(directory) && levels++ < 64)
            {
                foreach (var name in names)
                {
                    var candidate = Path.Combine(directory, name);
                    if (File.Exists(candidate)) yield return candidate;
                }
                var parent = Path.GetDirectoryName(directory);
                if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)) break;
                directory = parent;
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs args)
        {
            if (!MatchesPath(Path.GetFullPath(args.FullPath))) return;
            _invalidated(args.FullPath);
        }

        private void OnRenamed(object sender, RenamedEventArgs args)
        {
            if (!MatchesPath(Path.GetFullPath(args.FullPath)) && !MatchesPath(Path.GetFullPath(args.OldFullPath))) return;
            _invalidated(args.FullPath);
            _invalidated(args.OldFullPath);
        }

        private void OnError(object sender, ErrorEventArgs args)
        {
            _invalidated(ProjectPath);
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
            Workspace.Dispose();
        }
    }
}

internal sealed class ProjectTemplateContext
{
    public ProjectTemplateContext(
        TemplateProjectAnalysis? analysis,
        string currentNamespace,
        long generation,
        bool isComplete,
        string? warning,
        string? projectPath,
        IReadOnlyList<TemplateComponentDescriptor> localComponents)
    {
        Analysis = analysis;
        CurrentNamespace = currentNamespace;
        Generation = generation;
        IsComplete = isComplete;
        Warning = warning;
        ProjectPath = projectPath;
        LocalComponents = localComponents ?? Array.Empty<TemplateComponentDescriptor>();
    }

    public ProjectTemplateContext WithWarning(string warning) => new(
        Analysis,
        CurrentNamespace,
        Generation,
        IsComplete,
        string.IsNullOrWhiteSpace(Warning) ? warning : Warning + Environment.NewLine + warning,
        ProjectPath,
        LocalComponents);

    public TemplateProjectAnalysis? Analysis { get; }
    public TemplateCatalog? Catalog => Analysis?.Catalog;
    public string CurrentNamespace { get; }
    public long Generation { get; }
    public bool IsComplete { get; }
    public string? Warning { get; }
    public string? ProjectPath { get; }
    public IReadOnlyList<TemplateComponentDescriptor> LocalComponents { get; }

    public static ProjectTemplateContext Incomplete(string warning) => new(
        null,
        string.Empty,
        0,
        false,
        warning,
        null,
        Array.Empty<TemplateComponentDescriptor>());
}
