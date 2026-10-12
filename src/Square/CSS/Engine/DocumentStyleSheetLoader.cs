using Square.CSS.Ast;
using Square.CSS.Tokenizer;

namespace Square.CSS.Engine;

internal sealed class DocumentStyleSheetLoader(CssEngine engine)
{
    private const int MaxImportDepth = 64;

    public DocumentStyleSheet LoadFile(string path)
    {
        var resolvedPath = ResolveLocalPath(path);
        var styleSheet = LoadFileCore(resolvedPath, [], 0);
        LoadIntoEngine(styleSheet);
        return styleSheet;
    }

    public DocumentStyleSheet LoadText(string css) => LoadText(css, sourcePath: null);

    public DocumentStyleSheet LoadText(string css, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(css);
        var styleSheet = LoadTextCore(css, sourcePath == null ? null : ResolveLocalPath(sourcePath), [], 0);
        LoadIntoEngine(styleSheet);
        return styleSheet;
    }

    private static string ResolveLocalPath(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar)));

    private DocumentStyleSheet LoadFileCore(string path, HashSet<string> importStack, int depth)
    {
        if (depth > MaxImportDepth)
            throw new InvalidOperationException($"CSS @import exceeded the maximum depth of {MaxImportDepth}.");

        path = Path.GetFullPath(path);
        if (!importStack.Add(path))
            throw new InvalidOperationException($"Circular CSS @import detected for '{path}'.");

        try
        {
            var css = File.ReadAllText(path);
            return LoadTextCore(css, path, importStack, depth);
        }
        finally
        {
            importStack.Remove(path);
        }
    }

    private DocumentStyleSheet LoadTextCore(string css, string? href, HashSet<string> importStack, int depth)
    {
        var sheet = new CssParser(new CssTokenizer(css).Tokenize()).Parse();
        var imports = new List<DocumentStyleSheet>(sheet.Imports.Count);
        foreach (var import in sheet.Imports)
        {
            if (!string.IsNullOrWhiteSpace(import.Conditions))
                throw new NotSupportedException(
                    $"Conditional CSS @import is not supported: '{import.Href} {import.Conditions}'.");

            var importPath = ResolveImportPath(import.Href, href);
            imports.Add(LoadFileCore(importPath, importStack, depth + 1));
        }

        return new DocumentStyleSheet(href, css, sheet, imports);
    }

    private void LoadIntoEngine(DocumentStyleSheet styleSheet)
    {
        foreach (var import in styleSheet.Imports)
            LoadIntoEngine(import);
        engine.LoadStyleSheet(styleSheet.ParsedSheet);
    }

    private static string ResolveImportPath(string importHref, string? ownerHref)
    {
        if (Path.IsPathRooted(importHref)) return importHref;
        if (Uri.TryCreate(importHref, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                throw new NotSupportedException($"Only local file CSS @import URLs are supported: '{importHref}'.");
            return uri.LocalPath;
        }

        if (ownerHref == null)
            throw new InvalidOperationException(
                $"Relative CSS @import '{importHref}' requires a stylesheet loaded from a file.");

        return Path.Combine(Path.GetDirectoryName(ownerHref)!,
            importHref.Replace('/', Path.DirectorySeparatorChar));
    }
}
