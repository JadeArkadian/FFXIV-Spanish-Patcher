using System.IO.Compression;
using System.Text.Json;
using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class ModTreeVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ffxivsp-v4-" + Guid.NewGuid().ToString("N"));

    public ModTreeVerifierTests()
        => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Verify_RejectsLegacyManifest()
    {
        File.WriteAllText(Path.Combine(_root, "meta.json"), """{"FileVersion":3,"Name":"Prueba"}""");

        var problems = new ModTreeVerifier().Verify(_root, new Dictionary<string, string>());

        Assert.Contains(problems, problem => problem.Contains("FileVersion must be 4", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Groups missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_RejectsDefaultSettingsBitsOutsideTenOptions()
    {
        WriteMetadata(defaultSettings: 1 << TranslationCategoryCatalog.All.Count);

        var problems = new ModTreeVerifier().Verify(_root, new Dictionary<string, string>());

        Assert.Contains(problems, problem => problem.Contains("DefaultSettings contains bits", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_RejectsPayloadNotDeclaredByManifest()
    {
        WriteMetadata(defaultSettings: 1);
        var payload = Path.Combine(_root, "files", "categories", "09-interfaz", "exd");
        Directory.CreateDirectory(payload);
        File.WriteAllBytes(Path.Combine(payload, "orphan.exd"), "EXDF"u8.ToArray());

        var problems = new ModTreeVerifier().Verify(_root, new Dictionary<string, string>());

        Assert.Contains(problems, problem => problem.Contains("orphan payload file", StringComparison.Ordinal));
    }

    [Fact]
    public void VerifyArchive_RejectsLegacyManifest()
    {
        var archivePath = Path.Combine(_root, "legacy.pmp");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("meta.json").Open());
            writer.Write("""{"FileVersion":3,"Name":"Prueba"}""");
        }

        var problems = new IntegrityVerifier().Verify(archivePath, new Dictionary<string, string>());

        Assert.Contains(problems, problem => problem.Contains("FileVersion must be 4", StringComparison.Ordinal));
    }

    private void WriteMetadata(int defaultSettings)
    {
        var metadata = new
        {
            FileVersion = 4,
            Name = "Prueba",
            DefaultData = new { },
            Groups = new[]
            {
                new
                {
                    Name = TranslationCategoryCatalog.GroupName,
                    Type = "Multi",
                    DefaultSettings = defaultSettings,
                    Options = TranslationCategoryCatalog.All.Select(category => new { Name = category.DisplayName }).ToArray(),
                },
            },
        };
        File.WriteAllText(Path.Combine(_root, "meta.json"), JsonSerializer.Serialize(metadata));
    }
}
