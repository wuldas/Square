using System.Security.Cryptography;
using System.Text.Json;
using Square.Graphics;
using Square.Text.Fonts;

namespace Square.FontComparison;

internal static class ComparisonAssets
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string AssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets");
    public static string FontsDirectory => Path.Combine(AssetsDirectory, "Fonts");
    public static string CasesPath => Path.Combine(AppContext.BaseDirectory, "Cases", "FontComparisonCases.json");

    public static async Task<FontManifest> LoadAndRegisterFontsAsync()
    {
        var manifestPath = Path.Combine(FontsDirectory, "fonts.json");
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<FontManifest>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Font manifest is empty.");

        foreach (var definition in manifest.Fonts)
        {
            var path = Path.Combine(FontsDirectory, definition.File);
            await using var fontStream = File.OpenRead(path);
            var actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(fontStream));
            if (!string.Equals(actualHash, definition.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Font hash mismatch for '{definition.File}'.");

            var face = new FontFace(
                definition.Family,
                path,
                (FontWeight)definition.Weight,
                ParseStyle(definition.Style));
            await face.LoadAsync();
        }

        return manifest;
    }

    public static async Task<FontComparisonManifest> LoadCasesAsync()
    {
        await using var stream = File.OpenRead(CasesPath);
        var manifest = await JsonSerializer.DeserializeAsync<FontComparisonManifest>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Font comparison case manifest is empty.");
        ValidateCases(manifest.Cases);
        return manifest;
    }

    private static void ValidateCases(IReadOnlyList<FontComparisonCase> cases)
    {
        var duplicate = cases.GroupBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate font comparison case id '{duplicate.Key}'.");
        if (cases.Count == 0)
            throw new InvalidOperationException("Font comparison case manifest has no cases.");
    }

    private static FontStyle ParseStyle(string value) => value.ToLowerInvariant() switch
    {
        "italic" => FontStyle.Italic,
        "oblique" => FontStyle.Oblique,
        _ => FontStyle.Normal
    };
}
