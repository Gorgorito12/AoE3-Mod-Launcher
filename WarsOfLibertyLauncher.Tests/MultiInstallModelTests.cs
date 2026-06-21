using System.Linq;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Phase 0 of the multi-install feature: the data model on <see cref="ModState"/>.
/// The flat fields ARE the active install; inactive copies live in
/// <see cref="ModState.OtherInstalls"/>. These pin the invariants the rest of the
/// feature relies on:
///   * a single-install config is UNCHANGED by normalization (zero migration);
///   * snapshot/adopt round-trips every per-install field;
///   * the stock game never carries multi-install state;
///   * the JSON shape round-trips (so configs survive a save/load).
/// </summary>
public class MultiInstallModelTests
{
    private static ModState SampleActive() => new()
    {
        InstallPath = @"C:\Games\AoE3\Wars of Liberty",
        ActiveInstallId = "id-active",
        ActiveInstallLabel = "Principal",
        LastKnownVersion = "1.2.0d",
        PinnedVersion = "1.2.0d",
        ActiveTranslationId = "es",
        ActiveTranslationVersion = "3",
        // per-MOD fields (must NOT travel with a slot):
        LastKnownLatestVersion = "1.2.0d",
        NotifiedUpdateVersion = "1.2.0d",
    };

    [Fact]
    public void NormalizeInstalls_SingleInstall_IsNoOp()
    {
        var st = new ModState { InstallPath = @"C:\X", LastKnownVersion = "1.0" };

        st.NormalizeInstalls(isStock: false);

        Assert.Empty(st.OtherInstalls);
        Assert.Equal("", st.ActiveInstallId);          // legacy shape preserved
        Assert.Equal(@"C:\X", st.InstallPath);
        Assert.False(st.HasMultipleInstalls);
    }

    [Fact]
    public void NormalizeInstalls_WithOtherInstalls_AssignsStableId_AndIsIdempotent()
    {
        var st = new ModState { InstallPath = @"C:\A" };
        st.OtherInstalls.Add(new ModInstall { InstallPath = @"C:\B" });

        st.NormalizeInstalls(isStock: false);
        var assigned = st.ActiveInstallId;

        Assert.False(string.IsNullOrEmpty(assigned));   // active gained a stable id
        Assert.True(st.HasMultipleInstalls);

        st.NormalizeInstalls(isStock: false);           // idempotent — id unchanged
        Assert.Equal(assigned, st.ActiveInstallId);
    }

    [Fact]
    public void NormalizeInstalls_StockGame_StripsMultiInstallState()
    {
        var st = new ModState { InstallPath = @"C:\AoE3", ActiveInstallId = "x" };
        st.OtherInstalls.Add(new ModInstall { InstallPath = @"C:\AoE3-copy" });

        st.NormalizeInstalls(isStock: true);

        Assert.Empty(st.OtherInstalls);
        Assert.Equal("", st.ActiveInstallId);
        Assert.Equal("", st.ActiveInstallLabel);
        Assert.Equal(@"C:\AoE3", st.InstallPath);       // the detected base path stays
    }

    [Fact]
    public void SnapshotActive_ThenAdopt_RoundTripsEveryPerInstallField()
    {
        var st = SampleActive();

        ModInstall snap = st.SnapshotActive();

        Assert.Equal("id-active", snap.Id);
        Assert.Equal("Principal", snap.Label);
        Assert.Equal(st.InstallPath, snap.InstallPath);
        Assert.Equal(st.LastKnownVersion, snap.LastKnownVersion);
        Assert.Equal(st.PinnedVersion, snap.PinnedVersion);
        Assert.Equal(st.ActiveTranslationId, snap.ActiveTranslationId);
        Assert.Equal(st.ActiveTranslationVersion, snap.ActiveTranslationVersion);

        // Adopt a DIFFERENT slot — the flat fields become that slot.
        var other = new ModInstall
        {
            Id = "id-test",
            Label = "Prueba",
            InstallPath = @"D:\Copy",
            LastKnownVersion = "1.1.0",
            PinnedVersion = "",
            ActiveTranslationId = "",
            ActiveTranslationVersion = "",
        };
        st.AdoptInstall(other);

        Assert.Equal("id-test", st.ActiveInstallId);
        Assert.Equal("Prueba", st.ActiveInstallLabel);
        Assert.Equal(@"D:\Copy", st.InstallPath);
        Assert.Equal("1.1.0", st.LastKnownVersion);
        Assert.Equal("", st.PinnedVersion);
        Assert.Equal("", st.ActiveTranslationId);
        // per-MOD field is NOT overwritten by adopting a slot:
        Assert.Equal("1.2.0d", st.LastKnownLatestVersion);
    }

    [Fact]
    public void SnapshotActive_MintsIdWhenActiveHasNone()
    {
        var st = new ModState { InstallPath = @"C:\X" }; // ActiveInstallId == ""

        var snap = st.SnapshotActive();

        Assert.False(string.IsNullOrEmpty(snap.Id));
    }

    [Fact]
    public void AllInstallPaths_EnumeratesActivePlusOthers_SkippingEmpty()
    {
        var st = new ModState { InstallPath = @"C:\A" };
        st.OtherInstalls.Add(new ModInstall { InstallPath = @"C:\B" });
        st.OtherInstalls.Add(new ModInstall { InstallPath = "" });  // skipped
        st.OtherInstalls.Add(new ModInstall { InstallPath = @"C:\C" });

        var paths = st.AllInstallPaths().ToList();

        Assert.Equal(new[] { @"C:\A", @"C:\B", @"C:\C" }, paths);
    }

    [Fact]
    public void Json_RoundTrips_OtherInstalls()
    {
        var st = SampleActive();
        st.OtherInstalls.Add(new ModInstall
        {
            Id = "id-test", Label = "Prueba", InstallPath = @"D:\Copy", LastKnownVersion = "1.1.0",
        });

        var json = JsonSerializer.Serialize(st);
        var back = JsonSerializer.Deserialize<ModState>(json)!;

        Assert.Equal(st.InstallPath, back.InstallPath);
        Assert.Equal(st.ActiveInstallId, back.ActiveInstallId);
        Assert.Single(back.OtherInstalls);
        Assert.Equal("id-test", back.OtherInstalls[0].Id);
        Assert.Equal(@"D:\Copy", back.OtherInstalls[0].InstallPath);
        Assert.Equal("1.1.0", back.OtherInstalls[0].LastKnownVersion);
    }

    [Fact]
    public void Json_LegacyConfigWithoutOtherInstalls_DeserializesToEmptyList()
    {
        // A config written by an OLD build has no "otherInstalls"/"activeInstallId".
        const string legacy = """
        { "installPath": "C:\\Legacy", "lastKnownVersion": "1.0" }
        """;

        var st = JsonSerializer.Deserialize<ModState>(legacy)!;

        Assert.Equal(@"C:\Legacy", st.InstallPath);
        Assert.NotNull(st.OtherInstalls);
        Assert.Empty(st.OtherInstalls);
        Assert.Equal("", st.ActiveInstallId);
    }
}
