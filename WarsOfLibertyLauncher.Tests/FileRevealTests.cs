using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="FileReveal"/> — the button on the end-of-match card that shows the
/// recording in Explorer.
///
/// <para><b>Only the refusals are testable, and that is fine, because they are the risky
/// half.</b> A successful reveal launches Explorer, which a test suite must not do; what a
/// test CAN prove is that the paths which cannot be revealed return false instead of
/// throwing. That matters because every one of them is reachable in normal use: AoE3
/// renumbers recordings after each match and <see cref="GameRecordingPurge"/> deletes the
/// ones past the newest ten, so a path captured minutes ago routinely names nothing — and
/// an exception there would take down the card it is drawn on.</para>
/// </summary>
public class FileRevealTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToReveal_IsRefusedRatherThanThrown(string? path)
        => Assert.False(FileReveal.Reveal(path));

    [Fact]
    public void APathWhoseFolderIsAlsoGone_IsRefused()
    {
        // No folder to fall back to either, so there is nothing to open — and crucially no
        // throw. A directory that DOES exist is deliberately not tested: that case opens
        // Explorer, which is exactly what a test must not do.
        var missing = Path.Combine(
            Path.GetTempPath(), "aoe3-launcher-tests-no-such-dir", "Record Game 1.age3Yrec");
        Assert.False(Directory.Exists(Path.GetDirectoryName(missing)!));

        Assert.False(FileReveal.Reveal(missing));
    }
}
