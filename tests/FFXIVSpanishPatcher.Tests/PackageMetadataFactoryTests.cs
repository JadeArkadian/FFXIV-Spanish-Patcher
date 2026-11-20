using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class PackageMetadataFactoryTests
{
    [Fact]
    public void Create_DescribesVersionsAndGenerationTime()
    {
        var metadata = PackageMetadataFactory.Create(
            "0.6.0",
            "2026.08.05.0000.0000",
            new DateTimeOffset(2026, 11, 3, 9, 5, 42, TimeSpan.FromHours(1)));

        Assert.Equal("v0.6.0-2026.08.05.0000.0000", metadata.Version);
        Assert.Equal(PackageMetadataFactory.LandingPageUrl, metadata.Website);
        Assert.Equal(
            "Traducción al castellano de FFXIV mediante redirección de archivos EXD para Penumbra."
            + "\n\nVersión del patcher: v0.6.0"
            + "\nVersión de FFXIV: 2026.08.05.0000.0000"
            + "\nFecha de generación: 03/11/2026 09:05",
            metadata.Description);
    }

    [Fact]
    public void Create_DoesNotListCategoriesAndToleratesAnUnknownGameVersion()
    {
        var metadata = PackageMetadataFactory.Create("0.6.0", null, DateTimeOffset.UnixEpoch);

        Assert.Equal("v0.6.0", metadata.Version);
        Assert.Contains("Versión de FFXIV: desconocida", metadata.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Categorías", metadata.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageVersionFromAssembly_UsesThreePartReleaseNumber()
    {
        var version = PackageMetadataFactory.PackageVersionFromAssembly(typeof(PackageMetadataFactoryTests).Assembly);

        Assert.Matches("^[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}$", version);
    }
}
