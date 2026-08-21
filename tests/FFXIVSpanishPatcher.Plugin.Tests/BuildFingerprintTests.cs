using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class BuildFingerprintTests
{
    [Fact]
    public void RequiresRegenerationComparedTo_NullPreviousFingerprint_IsTrue()
        => Assert.True(Current().RequiresRegenerationComparedTo(null));

    [Fact]
    public void RequiresRegenerationComparedTo_RuntimeOnlyFieldsChange_IsFalse()
    {
        var previous = Current();
        var current = previous with
        {
            SuccessfulPayloadId = "new-payload",
            CompletedAtUtc = previous.CompletedAtUtc.AddMinutes(1),
        };

        Assert.False(current.RequiresRegenerationComparedTo(previous));
    }

    [Theory]
    [MemberData(nameof(ChangedBuildInputs))]
    public void RequiresRegenerationComparedTo_BuildInputChanges_IsTrue(BuildFingerprint current)
        => Assert.True(current.RequiresRegenerationComparedTo(Current()));

    public static IEnumerable<object[]> ChangedBuildInputs()
    {
        var fingerprint = Current();
        yield return [fingerprint with { SchemaVersion = 2 }];
        yield return [fingerprint with { PluginAssemblyVersion = "0.6.1" }];
        yield return [fingerprint with { CorpusSha256 = "other-hash" }];
        yield return [fingerprint with { RecommendedGameVersion = "other-recommended" }];
        yield return [fingerprint with { InstalledGameVersion = "other-installed" }];
        yield return [fingerprint with { CategorySchemaVersion = 2 }];
        yield return [fingerprint with { PackageFormatVersion = 5 }];
        yield return [fingerprint with { ModIdentity = "other-identity" }];
    }

    private static BuildFingerprint Current()
        => new(1, "0.6.0", "hash", "recommended", "installed", 1, 4, "identity", "payload", DateTimeOffset.UnixEpoch);
}
