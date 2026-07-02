using System.Text.Json;

namespace XivSpanish.Translation;

/// <summary>
/// Loads <see cref="TranslationEntry"/> records from a manifest file that is either a JSON
/// array, one JSON object per line (JSONL), or a directory / index file that references
/// multiple fragment files. Shared by the maintained tools (Packager, Coverage) so the on-disk
/// contract stays in one place.
/// </summary>
public static class ManifestLoader
{
    private const string IndexFormat = "xivspanish-translation-index-v1";

    /// <summary>
    /// Loads every entry from a manifest path. Supports:
    /// <list type="bullet">
    /// <item>A single JSONL file or JSON array (legacy).</item>
    /// <item>A directory containing an <c>_index.json</c>.</item>
    /// <item>An <c>_index.json</c> file directly.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<TranslationEntry> Load(string path)
    {
        if (Directory.Exists(path))
        {
            var indexPath = Path.Combine(path, "_index.json");
            if (File.Exists(indexPath))
            {
                return LoadFromIndex(indexPath);
            }

            throw new DirectoryNotFoundException($"No _index.json found in directory: {path}");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Manifest not found: {path}");
        }

        if (IsIndexFile(path))
        {
            return LoadFromIndex(path);
        }

        // Legacy: single JSONL or JSON array file
        var text = File.ReadAllText(path).TrimStart();
        if (text.StartsWith('['))
        {
            return JsonlSerialization.DeserializeList(text) ?? [];
        }

        var entries = new List<TranslationEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonlSerialization.Deserialize(line);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Loads and concatenates every entry across several manifest paths.</summary>
    public static IReadOnlyList<TranslationEntry> LoadMany(IEnumerable<string> paths)
    {
        var entries = new List<TranslationEntry>();
        foreach (var path in paths)
        {
            entries.AddRange(Load(path));
        }

        return entries;
    }

    /// <summary>
    /// Returns <c>true</c> if the file at <paramref name="path"/> is a valid translation index.
    /// </summary>
    public static bool IsIndexFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(path).TrimStart();
            if (!text.StartsWith('{'))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("format", out var formatElement))
            {
                return false;
            }

            return formatElement.GetString() == IndexFormat
                && doc.RootElement.TryGetProperty("fragments", out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Loads every fragment listed in a translation index file.
    /// </summary>
    public static IReadOnlyList<TranslationEntry> LoadFromIndex(string indexPath)
    {
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException($"Index not found: {indexPath}");
        }

        var indexDir = Path.GetDirectoryName(indexPath)!;
        var text = File.ReadAllText(indexPath);
        using var doc = JsonDocument.Parse(text);

        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var formatElement) || formatElement.GetString() != IndexFormat)
        {
            throw new InvalidDataException($"Invalid index format: {indexPath}");
        }

        if (!root.TryGetProperty("fragments", out var fragmentsElement))
        {
            throw new InvalidDataException($"Index missing 'fragments': {indexPath}");
        }

        var entries = new List<TranslationEntry>();
        foreach (var fragment in fragmentsElement.EnumerateArray())
        {
            var fragmentPath = Path.Combine(indexDir, fragment.GetString()!);
            entries.AddRange(LoadSingleFile(fragmentPath));
        }

        return entries;
    }

    private static IReadOnlyList<TranslationEntry> LoadSingleFile(string path)
    {
        var text = File.ReadAllText(path).TrimStart();
        if (text.StartsWith('['))
        {
            return JsonlSerialization.DeserializeList(text) ?? [];
        }

        var entries = new List<TranslationEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonlSerialization.Deserialize(line);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }
}
