using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXIVSpanishPatcher.Pipeline;

/// <summary>
/// Writes the Penumbra package tree (patched EXD files + <c>default_mod.json</c> + <c>meta.json</c>)
/// and zips it to a <c>.pmp</c>. Ported from the upstream Packager's Program.cs glue: the reusable
/// game-data primitives stay vendored, this orchestration belongs to the app.
/// </summary>
internal sealed class PackageWriter
{
    /// <summary>Game sub-path -> mod-relative path mapping declared in <c>default_mod.json</c>.</summary>
    private readonly SortedDictionary<string, string> _declared = new(StringComparer.Ordinal);

    private readonly string _staging;

    public PackageWriter(string stagingPath)
    {
        if (Directory.Exists(stagingPath))
        {
            Directory.Delete(stagingPath, recursive: true);
        }

        Directory.CreateDirectory(stagingPath);
        _staging = stagingPath;
    }

    public int FileCount => _declared.Count;

    public IReadOnlyDictionary<string, string> DeclaredFiles => _declared;

    /// <summary>Writes one patched EXD page into the staging tree and records its redirect.</summary>
    public void AddPatchedExd(string exdGamePath, byte[] bytes)
    {
        var modRelative = $"files/{exdGamePath}";
        var destination = Path.Combine(_staging, modRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, bytes);
        _declared[exdGamePath] = modRelative;
    }

    /// <summary>Writes the manifests and zips the tree to <paramref name="outputPath"/>.</summary>
    public string Package(PackageMeta meta, string outputPath)
    {
        WriteJson(Path.Combine(_staging, "default_mod.json"), new PackageDefaultMod(_declared));
        WriteJson(Path.Combine(_staging, "meta.json"), PackageModMeta.From(meta));

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(_staging, "*", SearchOption.AllDirectories))
        {
            var entryName = Path.GetRelativePath(_staging, file).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }

        return outputPath;
    }

    private static void WriteJson(string path, PackageDefaultMod value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, PipelineJsonContext.Default.PackageDefaultMod));

    private static void WriteJson(string path, PackageModMeta value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, PipelineJsonContext.Default.PackageModMeta));
}

internal sealed class PackageDefaultMod(SortedDictionary<string, string> files)
{
    [JsonPropertyName("Version")] public int Version => 0;
    [JsonPropertyName("Files")] public SortedDictionary<string, string> Files { get; } = files;
    [JsonPropertyName("FileSwaps")] public Dictionary<string, string> FileSwaps { get; } = new(StringComparer.Ordinal);
    [JsonPropertyName("Manipulations")] public string[] Manipulations => [];
}

internal sealed class PackageModMeta
{
    [JsonPropertyName("FileVersion")] public int FileVersion => 3;
    [JsonPropertyName("Name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("Author")] public string Author { get; init; } = string.Empty;
    [JsonPropertyName("Description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("Image")] public string Image => string.Empty;
    [JsonPropertyName("Version")] public string Version { get; init; } = "0.0.0";
    [JsonPropertyName("Website")] public string Website { get; init; } = string.Empty;
    [JsonPropertyName("ModTags")] public string[] ModTags => ["translation", "spanish", "castellano", "español", "text", "UI"];

    public static PackageModMeta From(PackageMeta meta) => new()
    {
        Name = meta.Name,
        Author = meta.Author,
        Description = meta.Description,
        Version = meta.Version,
        Website = meta.Website,
    };
}
