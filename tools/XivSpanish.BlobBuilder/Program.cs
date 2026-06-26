using System.IO.Compression;
using System.Text;
using System.Text.Json;
using XivSpanish.Translation;

namespace XivSpanish.BlobBuilder;

/// <summary>
/// Regenerates the embedded translation blob. Two subcommands, mirroring the old Python scripts:
/// <list type="bullet">
/// <item><c>sync   [--upstream DIR] [--build]</c> — copy the raw JSONL corpus from the upstream
/// FFXIV-Spanish repo into <c>data/translations/jsonl</c> (git-ignored); <c>--build</c> also runs
/// build.</item>
/// <item><c>build  [--source DIR] [--output FILE]</c> — filter to packageable rows, project them to
/// the fields the runtime reads, and Brotli-compress into <c>data/translations.dat</c>.</item>
/// </list>
/// Cross-platform and dependency-free: it reuses the trimmed <see cref="TranslationEntry"/> model
/// (so re-serializing IS the field projection) and the built-in <see cref="BrotliStream"/>.
/// </summary>
internal static class Program
{
    private const string RecommendedGameVersionPath = "data/recommended-game-version.txt";

    // Mirror of PatchPipeline.Packageable's status set (PackageableStatus.Default).
    private static readonly HashSet<string> PackageableStatuses =
        new(StringComparer.OrdinalIgnoreCase) { TranslationEntryStatus.Approved, TranslationEntryStatus.Gold };

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    // The trimmed TranslationEntry only carries source/target/status/sourceKey, so the default
    // serializer output IS the projection. No nulls, to drop "exdPath":null on rows without a path.
    private static readonly JsonSerializerOptions WriteOptions =
        new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static int Main(string[] args)
    {
        try
        {
            return (args.FirstOrDefault()) switch
            {
                "sync" => Sync(args[1..]),
                "build" => Build(args[1..]),
                _ => Usage(),
            };
        }
        catch (BlobBuilderError error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "usage: blob-builder <command>\n" +
            "  sync   [--upstream DIR] [--build]   copy raw corpus from upstream FFXIV-Spanish\n" +
            "  build  [--source DIR] [--output FILE] [--game-version FILE]  regenerate data/translations.dat (Brotli)");
        return 2;
    }

    private static int Sync(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var upstream = new DirectoryInfo(OptionValue(args, "--upstream")
            ?? Path.Combine(repoRoot.Parent!.FullName, "FFXIV-Spanish"));
        var runBuild = args.Contains("--build");

        if (!upstream.Exists)
        {
            throw new BlobBuilderError($"Upstream repo not found: {upstream.FullName}");
        }

        var src = new DirectoryInfo(Path.Combine(upstream.FullName, "data", "translations", "jsonl"));
        if (!src.Exists)
        {
            throw new BlobBuilderError($"Upstream corpus not found: {src.FullName}");
        }

        var dst = new DirectoryInfo(Path.Combine(repoRoot.FullName, "data", "translations", "jsonl"));
        if (dst.Exists)
        {
            dst.Delete(recursive: true);
        }

        dst.Create();

        var count = 0;
        foreach (var file in src.EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src.FullName, file.FullName);
            var target = Path.Combine(dst.FullName, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            file.CopyTo(target, overwrite: true);
            count++;
        }

        Console.WriteLine($"Synced {count} .jsonl files from upstream into data/translations/jsonl");
        var versionFile = new FileInfo(Path.Combine(upstream.FullName, "data", "snapshots", "GAME_VERSION"));
        WriteRecommendedGameVersion(repoRoot, ReadRequiredGameVersion(versionFile));
        return runBuild ? Build(["--game-version", versionFile.FullName]) : 0;
    }

    private static int Build(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var source = new DirectoryInfo(OptionValue(args, "--source")
            ?? Path.Combine(repoRoot.FullName, "data", "translations", "jsonl"));
        var output = OptionValue(args, "--output")
            ?? Path.Combine(repoRoot.FullName, "data", "translations.dat");
        var gameVersionFile = ResolveGameVersionFile(args, repoRoot);

        if (!source.Exists)
        {
            throw new BlobBuilderError($"Translation source not found: {source.FullName}. Run `sync` first.");
        }

        var files = source.EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderBy(f => f.FullName, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            throw new BlobBuilderError($"No .jsonl files found under {source.FullName}.");
        }

        var lines = new List<string>();
        var byStatus = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int droppedStatus = 0, droppedIncomplete = 0;

        foreach (var file in files)
        {
            foreach (var raw in File.ReadLines(file.FullName))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                TranslationEntry? entry;
                try
                {
                    entry = JsonSerializer.Deserialize<TranslationEntry>(line, ReadOptions);
                }
                catch (JsonException exception)
                {
                    throw new BlobBuilderError($"Invalid JSON in {file.FullName}: {exception.Message}");
                }

                if (entry is null)
                {
                    continue;
                }

                var status = entry.Status?.ToLowerInvariant() ?? "(none)";
                byStatus[status] = byStatus.GetValueOrDefault(status) + 1;

                if (entry.Status is null || !PackageableStatuses.Contains(entry.Status))
                {
                    droppedStatus++;
                    continue;
                }

                if (!IsPackageable(entry))
                {
                    droppedIncomplete++;
                    continue;
                }

                lines.Add(JsonSerializer.Serialize(entry, WriteOptions));
            }
        }

        // One newline-delimited JSONL stream (UTF-8, no BOM), Brotli-compressed (max ratio) in one shot.
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using (var fs = File.Create(output))
        using (var brotli = new BrotliStream(fs, CompressionLevel.SmallestSize))
        {
            brotli.Write(payload);
        }

        var recommendedVersion = ReadOptionalGameVersion(gameVersionFile);
        if (recommendedVersion is not null)
        {
            WriteRecommendedGameVersion(repoRoot, recommendedVersion);
        }

        var sizeMb = new FileInfo(output).Length / (1024.0 * 1024.0);
        var detail = string.Join(", ", byStatus.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"));
        Console.WriteLine(
            $"Wrote {output}: {lines.Count} packageable entries ({sizeMb:0.00} MB compressed) from " +
            $"{files.Count} files; dropped {droppedStatus} by status + {droppedIncomplete} " +
            $"empty-target/incomplete-key. Corpus by status: {detail}.");
        return 0;
    }

    /// <summary>Mirror of <c>PatchPipeline.Packageable</c> (the status is checked by the caller):
    /// a non-empty target and a complete source key (sheet + rowId).</summary>
    private static bool IsPackageable(TranslationEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Target)
           && entry.SourceKey is { } key
           && !string.IsNullOrWhiteSpace(key.Sheet)
           && key.RowId.HasValue;

    private static string? OptionValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static FileInfo ResolveGameVersionFile(string[] args, DirectoryInfo repoRoot)
    {
        var explicitPath = OptionValue(args, "--game-version");
        return new FileInfo(explicitPath
            ?? Path.Combine(repoRoot.Parent!.FullName, "FFXIV-Spanish", "data", "snapshots", "GAME_VERSION"));
    }

    private static string ReadRequiredGameVersion(FileInfo file)
    {
        if (!file.Exists)
        {
            throw new BlobBuilderError($"Game version file not found: {file.FullName}");
        }

        var version = File.ReadAllText(file.FullName).Trim();
        if (version.Length == 0)
        {
            throw new BlobBuilderError($"Game version file is empty: {file.FullName}");
        }

        return version;
    }

    private static string? ReadOptionalGameVersion(FileInfo file)
    {
        if (!file.Exists)
        {
            Console.Error.WriteLine($"Game version file not found; keeping existing embedded recommendation if any: {file.FullName}");
            return null;
        }

        return ReadRequiredGameVersion(file);
    }

    private static void WriteRecommendedGameVersion(DirectoryInfo repoRoot, string version)
    {
        var output = Path.Combine(repoRoot.FullName, RecommendedGameVersionPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, version + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Wrote {RecommendedGameVersionPath}: {version}");
    }

    /// <summary>Walks up from the running assembly to the repo root (the dir with the .slnx).</summary>
    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FFXIVSpanishPatcher.slnx")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new BlobBuilderError("Could not locate repo root (FFXIVSpanishPatcher.slnx).");
    }
}

internal sealed class BlobBuilderError(string message) : Exception(message);
