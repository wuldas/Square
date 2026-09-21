using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Square.Compiler.LanguageServices;

public static class TemplateNamespaceResolver
{
    public static bool IsTemplateFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return (path.EndsWith(".sqx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)) &&
               !IsResourceDirectoryPath(path);
    }

    public static bool IsResourceDirectoryPath(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        return normalized.StartsWith("Public/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
               normalized.IndexOf("/Public/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string GetDefaultNamespace(
        string rootNamespace,
        string filePath,
        string projectDirectory,
        string logicalPath)
    {
        var relativePath = !string.IsNullOrWhiteSpace(logicalPath)
            ? logicalPath.Replace('\\', '/')
            : GetProjectRelativePath(filePath, projectDirectory).Replace('\\', '/');
        var directory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(directory)) return rootNamespace;

        var segments = directory
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeNamespaceSegment)
            .Where(segment => segment.Length > 0);
        var suffix = string.Join(".", segments);
        if (suffix.Length == 0) return rootNamespace;
        return string.IsNullOrWhiteSpace(rootNamespace) ? suffix : rootNamespace + "." + suffix;
    }

    private static string GetProjectRelativePath(string filePath, string projectDirectory)
    {
        if (!Path.IsPathRooted(filePath)) return filePath;
        if (string.IsNullOrWhiteSpace(projectDirectory)) return Path.GetFileName(filePath);

        var projectPath = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(filePath);
        return fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(projectPath.Length)
            : Path.GetFileName(filePath);
    }

    private static string SanitizeNamespaceSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return string.Empty;
        var builder = new StringBuilder(segment.Length + 1);
        for (var i = 0; i < segment.Length; i++)
        {
            var character = segment[i];
            var valid = i == 0
                ? character == '_' || char.IsLetter(character)
                : character == '_' || char.IsLetterOrDigit(character);
            builder.Append(valid ? character : '_');
        }

        var value = builder.ToString();
        if (value.Length == 0 || !(value[0] == '_' || char.IsLetter(value[0]))) value = "_" + value;
        return SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ? "_" + value : value;
    }
}
