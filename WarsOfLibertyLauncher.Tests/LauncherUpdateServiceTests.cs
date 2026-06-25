using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests for <see cref="LauncherUpdateService.EvaluateUpdate"/> —
/// the pure self-update decision. The canonical bug: a binary obtained outside
/// the in-app self-updater (a manual download from GitHub Releases, or a build
/// run straight from <c>publish\</c>) has no saved <c>LastInstalledLauncherTag</c>,
/// so the launcher couldn't recognise its own version and offered an "update" to
/// the version it was already running, shown as "current: —". The fix falls back
/// to the binary's stamped AssemblyVersion as the effective current version.
/// These tests pin that behaviour without touching the network.
/// </summary>
public class LauncherUpdateServiceTests
{
    [Theory]
    // The exact bug: a freshly-downloaded v0.9.9 with no saved tag must NOT be
    // offered an "update" to v0.9.9 — its own AssemblyVersion is the fallback.
    [InlineData("",       "0.9.9.0", null,     "v0.9.9", false)]
    // A genuinely old binary (asm 0.6.0) with no saved tag still gets the real update.
    [InlineData("",       "0.6.0.0", null,     "v0.9.9", true)]
    // Saved tag present and equal to remote → no update (unchanged behaviour).
    [InlineData("v0.9.9", "0.9.9.0", null,     "v0.9.9", false)]
    // Saved tag older than remote → update (unchanged behaviour).
    [InlineData("v0.9.8", "0.9.9.0", null,     "v0.9.9", true)]
    // A saved tag wins over a stale AssemblyVersion — it's the authoritative record.
    [InlineData("v0.9.9", "0.6.0.0", null,     "v0.9.9", false)]
    // Dismissed tag suppresses the prompt even with no saved tag and an old asm.
    [InlineData("",       "0.6.0.0", "v0.9.9", "v0.9.9", false)]
    // Remote rolled back below the effective current → no backwards "update".
    [InlineData("",       "0.9.9.0", null,     "v0.9.8", false)]
    // A non-SemVer saved tag keeps prompt-on-difference (never silently miss one).
    [InlineData("weird",  "0.9.9.0", null,     "v1.0.0", true)]
    [InlineData("weird",  "0.9.9.0", null,     "weird",  false)]
    // --- WoL-style LETTER suffix support ---
    // Letter release is newer than the plain patch → offer.
    [InlineData("v1.0.5",  "1.0.5.0", null,    "v1.0.5a", true)]
    // Same letter tag installed → no offer (equality).
    [InlineData("v1.0.5a", "1.0.5.0", null,    "v1.0.5a", false)]
    // Next patch is newer than a letter release → offer.
    [InlineData("v1.0.5a", "1.0.5.0", null,    "v1.0.6",  true)]
    // Letter-vs-letter ordering: b > a installed → offer.
    [InlineData("v1.0.5a", "1.0.5.0", null,    "v1.0.5b", true)]
    // Downgrade guard now WORKS for letters: remote a < installed b → no offer.
    [InlineData("v1.0.5b", "1.0.5.0", null,    "v1.0.5a", false)]
    // Downgrade guard: plain patch is OLDER than the letter release → no offer.
    [InlineData("v1.0.5a", "1.0.5.0", null,    "v1.0.5",  false)]
    public void EvaluateUpdate_OfferDecision(
        string lastInstalledTag, string asmVersion, string? skippedTag,
        string remoteTag, bool expectedOffer)
    {
        var (offer, _) = LauncherUpdateService.EvaluateUpdate(
            lastInstalledTag, Version.Parse(asmVersion), skippedTag, remoteTag);

        Assert.Equal(expectedOffer, offer);
    }

    [Fact]
    public void EvaluateUpdate_EmptyTag_UsesAssemblyVersionAsCurrentLabel()
    {
        // No saved tag + running 0.9.9 + remote v0.9.9 → no offer, and the
        // "current" label is the real version, NOT the "—" placeholder.
        var (offer, currentLabel) = LauncherUpdateService.EvaluateUpdate(
            "", new Version(0, 9, 9, 0), null, "v0.9.9");

        Assert.False(offer);
        Assert.Equal("v0.9.9", currentLabel);
    }

    [Fact]
    public void EvaluateUpdate_OldBinary_OffersUpdateWithHonestCurrentLabel()
    {
        var (offer, currentLabel) = LauncherUpdateService.EvaluateUpdate(
            "", new Version(0, 6, 0, 0), null, "v0.9.9");

        Assert.True(offer);
        Assert.Equal("v0.6.0", currentLabel); // honest current version, not "—"
    }

    [Fact]
    public void EvaluateUpdate_LetterBinaryNoSavedTag_RecognisedViaInformationalTag()
    {
        // A manually-downloaded "1.0.5a" binary: AssemblyVersion is numeric 1.0.5
        // (the letter can't live there), but InformationalVersion carries "v1.0.5a".
        // With the informational tag as the effective-current fallback, the remote
        // "v1.0.5a" must NOT be offered as an update to itself.
        var (offer, currentLabel) = LauncherUpdateService.EvaluateUpdate(
            lastInstalledTag: "", assemblyVersion: new Version(1, 0, 5, 0),
            skippedTag: null, remoteTag: "v1.0.5a", currentInformationalTag: "v1.0.5a");

        Assert.False(offer);
        Assert.Equal("v1.0.5a", currentLabel);
    }

    [Fact]
    public void EvaluateUpdate_LetterBinaryNoSavedTag_WithoutInformational_WouldOfferItself()
    {
        // Without the informational fallback, the numeric AssemblyVersion (1.0.5)
        // can't represent the letter, so the same-version "v1.0.5a" reads as newer
        // and gets offered — this is exactly why the informational tag exists.
        var (offer, _) = LauncherUpdateService.EvaluateUpdate(
            lastInstalledTag: "", assemblyVersion: new Version(1, 0, 5, 0),
            skippedTag: null, remoteTag: "v1.0.5a");

        Assert.True(offer);
    }

    [Theory]
    [InlineData("0.9.9.0", "v0.9.9")]
    [InlineData("1.0.0.0", "v1.0.0")]
    [InlineData("0.0.0",   "v0.0.0")]
    public void FormatVersionTag_FormatsAssemblyVersionAsGitHubTag(string asmVersion, string expected)
    {
        Assert.Equal(expected, LauncherUpdateService.FormatVersionTag(Version.Parse(asmVersion)));
    }

    [Fact]
    public void FormatVersionTag_TwoPartVersion_FloorsBuildToZero()
    {
        // new Version(1, 0) has Build = -1; must not produce "v1.0.-1".
        Assert.Equal("v1.0.0", LauncherUpdateService.FormatVersionTag(new Version(1, 0)));
    }
}
