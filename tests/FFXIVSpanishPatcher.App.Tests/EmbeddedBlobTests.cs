using System.Reflection;
using FFXIVSpanishPatcher.App.ViewModels;
using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.App.Tests;

/// <summary>
/// Guards the build → consume contract for <c>translations.dat</c>, whether the selected build
/// embeds it or ships it beside the executable. The compact blob emitted by
/// <c>tools/XivSpanish.BlobBuilder</c> must still deserialize into packageable rows.
/// </summary>
public class EmbeddedBlobTests
{
    private const string ResourceName = "FFXIVSpanishPatcher.App.translations.dat";

    [Fact]
    public void PackagedBlob_LoadsPackageableEntries()
    {
        var assembly = typeof(MainViewModel).Assembly;
        var external = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute =>
                attribute.Key.Equals("ExternalTranslations", StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(attribute.Value, out var enabled)
                && enabled);
        var source = external
            ? EmbeddedTranslationSource.FromFile(Path.Combine(AppContext.BaseDirectory, "translations.dat"))
            : EmbeddedTranslationSource.FromAssemblyResource(assembly, ResourceName);

        var entries = source.Load();

        Assert.NotEmpty(entries);
        // Every shipped row must be packageable: the build script already filtered to approved+gold.
        Assert.All(entries, e => Assert.True(PackageableStatus.IsPackageable(e, PackageableStatus.Default)));
        // The projection kept the fields the pipeline reads.
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrEmpty(e.Target));
            Assert.NotNull(e.SourceKey);
            Assert.False(string.IsNullOrWhiteSpace(e.SourceKey!.Sheet));
            Assert.True(e.SourceKey.RowId.HasValue);
        });
        // Sanity: the big post-Sprint-2 sheet survived the round-trip.
        Assert.Contains(entries, e => string.Equals(e.SourceKey!.Sheet, "Item", System.StringComparison.OrdinalIgnoreCase));
    }
}
