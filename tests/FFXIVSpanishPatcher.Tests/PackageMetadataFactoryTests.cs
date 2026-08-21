using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.Tests;

public sealed class PackageMetadataFactoryTests
{
    [Fact]
    public void Create_PreservesClassicPatcherMetadataContract()
    {
        var metadata = PackageMetadataFactory.Create(
            "0.6.0",
            "2026.08.05.0000.0000",
            ["Misiones", "Interfaz"],
            ["Interfaz"]);

        Assert.Equal("v0.6.0-2026.08.05.0000.0000", metadata.Version);
        Assert.Equal(PackageMetadataFactory.LandingPageUrl, metadata.Website);
        Assert.Equal(
            "Traducción al castellano de FFXIV mediante redirección de archivos EXD para Penumbra."
            + "\n\nVersión del patcher: v0.6.0"
            + "\nVersión de FFXIV: 2026.08.05.0000.0000"
            + "\n\nCategorías incluidas:\n* Misiones\n* Interfaz"
            + "\n\nCategorías activadas en primera importación de Penumbra:\n* Interfaz",
            metadata.Description);
    }

    [Fact]
    public void PackageVersionFromAssembly_UsesThreePartReleaseNumber()
    {
        var version = PackageMetadataFactory.PackageVersionFromAssembly(typeof(PackageMetadataFactoryTests).Assembly);

        Assert.Matches("^[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}$", version);
    }
}
