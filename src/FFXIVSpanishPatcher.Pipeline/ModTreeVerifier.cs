using System.Text.Json;
using XivSpanish.GameData;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>Mandatory structural verification for an unpacked categorized Penumbra v4 mod tree.</summary>
public sealed class ModTreeVerifier
{
    public IReadOnlyList<string> Verify(string treePath, IReadOnlyDictionary<string, string> declaredFiles)
    {
        var problems = new List<string>();
        if (!Directory.Exists(treePath))
        {
            return [$"mod tree not found: {treePath}"];
        }

        var root = Path.GetFullPath(treePath);
        var files = EnumerateSafeFiles(root, problems);

        if (!files.TryGetValue("meta.json", out var metaPath))
        {
            problems.Add("meta.json missing at mod tree root");
            return problems;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(metaPath));
            problems.AddRange(PenumbraV4ManifestVerifier.Verify(document.RootElement, declaredFiles, files.ContainsKey));
        }
        catch (JsonException exception)
        {
            problems.Add($"invalid meta.json: {exception.Message}");
        }

        var allowedFiles = new HashSet<string>(declaredFiles.Values, StringComparer.Ordinal) { "meta.json" };
        foreach (var relative in files.Keys)
        {
            if (!allowedFiles.Contains(relative))
            {
                problems.Add($"orphan payload file: {relative}");
            }
        }

        foreach (var modRelative in declaredFiles.Values)
        {
            if (files.TryGetValue(modRelative, out var path)
                && modRelative.EndsWith(".exd", StringComparison.OrdinalIgnoreCase)
                && !BeginsWithExdfMagic(path))
            {
                problems.Add($"patched EXD is not a valid EXDF page: {modRelative}");
            }
        }

        return problems;
    }

    private static bool BeginsWithExdfMagic(string path)
    {
        var header = new byte[ExdPage.HeaderSize];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        return read >= 4 && ExdPage.HasExdfMagic(header);
    }

    private static Dictionary<string, string> EnumerateSafeFiles(string root, ICollection<string> problems)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var relative = Path.GetRelativePath(root, child).Replace(Path.DirectorySeparatorChar, '/');
                if (new DirectoryInfo(child).LinkTarget is not null)
                {
                    problems.Add($"symlink directory is not allowed: {relative}");
                }
                else if (!PenumbraV4ManifestVerifier.IsSafeRelativePath(relative))
                {
                    problems.Add($"unsafe tree path: {relative}");
                }
                else
                {
                    pending.Enqueue(child);
                }
            }

            foreach (var child in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var relative = Path.GetRelativePath(root, child).Replace(Path.DirectorySeparatorChar, '/');
                if (new FileInfo(child).LinkTarget is not null)
                {
                    problems.Add($"symlink payload is not allowed: {relative}");
                }
                else if (!PenumbraV4ManifestVerifier.IsSafeRelativePath(relative))
                {
                    problems.Add($"unsafe tree path: {relative}");
                }
                else if (!files.TryAdd(relative, child))
                {
                    problems.Add($"duplicate tree path: {relative}");
                }
            }
        }

        return files;
    }
}

internal static class PenumbraV4ManifestVerifier
{
    public static IReadOnlyList<string> Verify(
        JsonElement root,
        IReadOnlyDictionary<string, string> declaredFiles,
        Func<string, bool> payloadExists)
    {
        var problems = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ["meta.json root must be an object"];
        }

        if (!root.TryGetProperty("FileVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var fileVersion)
            || fileVersion != 4)
        {
            problems.Add("meta.json FileVersion must be 4");
        }

        if (!root.TryGetProperty("Name", out var name)
            || name.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(name.GetString()))
        {
            problems.Add("meta.json Name must not be empty");
        }

        if (!root.TryGetProperty("Groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            problems.Add("meta.json Groups missing or invalid");
            return problems;
        }

        var groupList = groups.EnumerateArray().ToArray();
        if (groupList.Length != 1)
        {
            problems.Add("meta.json must contain exactly one category group");
            return problems;
        }

        var group = groupList[0];
        if (!StringPropertyEquals(group, "Name", TranslationCategoryCatalog.GroupName))
        {
            problems.Add("category group name is not stable");
        }

        if (!StringPropertyEquals(group, "Type", "Multi"))
        {
            problems.Add("category group must be Multi");
        }

        if (!group.TryGetProperty("Options", out var options) || options.ValueKind != JsonValueKind.Array)
        {
            problems.Add("category group options missing or invalid");
            return problems;
        }

        var optionList = options.EnumerateArray().ToArray();
        if (optionList.Length != TranslationCategoryCatalog.All.Count)
        {
            problems.Add($"category group must contain {TranslationCategoryCatalog.All.Count} options");
        }

        if (!group.TryGetProperty("DefaultSettings", out var settings)
            || settings.ValueKind != JsonValueKind.Number
            || !settings.TryGetInt32(out var defaultSettings))
        {
            problems.Add("category group DefaultSettings missing or invalid");
        }
        else if ((defaultSettings & ~((1 << TranslationCategoryCatalog.All.Count) - 1)) != 0)
        {
            problems.Add("category group DefaultSettings contains bits outside its options");
        }

        var manifestFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < optionList.Length; index++)
        {
            var option = optionList[index];
            if (index < TranslationCategoryCatalog.All.Count
                && !StringPropertyEquals(option, "Name", TranslationCategoryCatalog.All[index].DisplayName))
            {
                problems.Add($"category option {index} does not have its stable name");
            }

            if (!option.TryGetProperty("Files", out var files))
            {
                continue;
            }

            if (files.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"category option {index} Files must be an object");
                continue;
            }

            foreach (var redirect in files.EnumerateObject())
            {
                var gamePath = redirect.Name;
                var modRelative = redirect.Value.GetString();
                if (!IsSafeRelativePath(gamePath))
                {
                    problems.Add($"unsafe game path in manifest: {gamePath}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(modRelative) || !IsSafeRelativePath(modRelative))
                {
                    problems.Add($"unsafe payload path in manifest: {modRelative}");
                    continue;
                }

                if (!manifestFiles.TryAdd(gamePath, modRelative))
                {
                    problems.Add($"game path declared by more than one option: {gamePath}");
                }
                else if (!payloadExists(modRelative))
                {
                    problems.Add($"declared file not found: {modRelative}");
                }
            }
        }

        if (manifestFiles.Count != declaredFiles.Count)
        {
            problems.Add("manifest redirect count differs from generated payload");
        }

        foreach (var (gamePath, modRelative) in declaredFiles)
        {
            if (!manifestFiles.TryGetValue(gamePath, out var manifestRelative)
                || !string.Equals(manifestRelative, modRelative, StringComparison.Ordinal))
            {
                problems.Add($"manifest redirect differs from generated payload: {gamePath}");
            }
        }

        return problems;
    }

    public static bool IsSafeRelativePath(string path)
        => !string.IsNullOrWhiteSpace(path)
           && !path.StartsWith("/", StringComparison.Ordinal)
           && !path.Contains(':', StringComparison.Ordinal)
           && !path.Contains('\\', StringComparison.Ordinal)
           && path.Split('/').All(segment => segment is not ("" or "." or ".."));

    private static bool StringPropertyEquals(JsonElement element, string propertyName, string expected)
        => element.TryGetProperty(propertyName, out var property)
           && property.ValueKind == JsonValueKind.String
           && string.Equals(property.GetString(), expected, StringComparison.Ordinal);
}
