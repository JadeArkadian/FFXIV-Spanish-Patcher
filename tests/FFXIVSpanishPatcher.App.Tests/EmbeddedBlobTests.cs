using FFXIVSpanishPatcher.App.ViewModels;
using FFXIVSpanishPatcher.Pipeline;
using Xunit;

namespace FFXIVSpanishPatcher.App.Tests;

/// <summary>
/// Guards the build → consume contract for the embedded <c>translations.dat</c>: the compact blob
/// emitted by <c>build/build-translations.py</c> (field-projected, filtered to approved+gold) must
/// still deserialize into packageable <c>TranslationEntry</c> rows the pipeline can apply. Loading
/// the real resource catches a projection that drops a field the model needs.
/// </summary>
public class EmbeddedBlobTests
{
    private const string ResourceName = "FFXIVSpanishPatcher.App.translations.dat";

    [Fact]
    public void EmbeddedBlob_LoadsPackageableEntries()
    {
        var source = EmbeddedTranslationSource.FromAssemblyResource(typeof(MainViewModel).Assembly, ResourceName);

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
