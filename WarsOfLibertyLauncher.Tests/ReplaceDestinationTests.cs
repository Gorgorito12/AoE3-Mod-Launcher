using System;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="DownloadService.ReplaceDestination"/> — the finalize step that moves a completed
/// <c>.part</c> onto its destination.
///
/// <para>Both finalize paths used to delete the destination unguarded, while the two
/// <c>.part</c> deletes a few lines above were wrapped — and that asymmetry cost a real user
/// every launcher update for days. These tests touch the real filesystem on purpose: the
/// read-only case is the only one that proves the fix does anything, and it cannot be
/// expressed against a pure function.</para>
///
/// <para>Note what is deliberately NOT tested here: replacing a RUNNING image. Windows refuses
/// that whatever we do, and the reason the reported lockout ended is
/// <see cref="LauncherUpdateService.GetPendingUpdatePath(string)"/> no longer being able to
/// name the running exe — not anything in this method.</para>
/// </summary>
public class ReplaceDestinationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "wol-replace-dest-" + Guid.NewGuid().ToString("N"));

    public ReplaceDestinationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(_dir, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }

    private (string temp, string dest) Staged(string newBytes, string? existing)
    {
        var dest = Path.Combine(_dir, "payload.bin");
        var temp = dest + ".part";
        File.WriteAllText(temp, newBytes);
        if (existing != null) File.WriteAllText(dest, existing);
        return (temp, dest);
    }

    /// <summary>The ordinary path: nothing there yet.</summary>
    [Fact]
    public void AMissingDestinationIsReplaced()
    {
        var (temp, dest) = Staged("new", existing: null);

        DownloadService.ReplaceDestination(temp, dest);

        Assert.Equal("new", File.ReadAllText(dest));
        Assert.False(File.Exists(temp));
    }

    /// <summary>An ordinary stale destination is overwritten.</summary>
    [Fact]
    public void AnExistingDestinationIsOverwritten()
    {
        var (temp, dest) = Staged("new", existing: "old");

        DownloadService.ReplaceDestination(temp, dest);

        Assert.Equal("new", File.ReadAllText(dest));
        Assert.False(File.Exists(temp));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A read-only destination used to make the download fail
    /// permanently, because <c>File.Delete</c> throws <see cref="UnauthorizedAccessException"/>
    /// on one — and note that an overwriting move does NOT clear the attribute either, so the
    /// retry that does is a genuinely separate case rather than the same attempt twice.
    /// </summary>
    [Fact]
    public void AReadOnlyDestinationIsReplaced()
    {
        var (temp, dest) = Staged("new", existing: "old");
        File.SetAttributes(dest, FileAttributes.ReadOnly);

        DownloadService.ReplaceDestination(temp, dest);

        Assert.Equal("new", File.ReadAllText(dest));
        Assert.False(File.Exists(temp));
    }

    /// <summary>
    /// A destination nothing can replace surfaces the typed exception carrying the path —
    /// not a bare <see cref="UnauthorizedAccessException"/> whose English .NET message the
    /// dialog would print verbatim. The inner exception is part of the contract; see
    /// <c>DownloadRetryTests.ADestinationThatCannotBeReplaced_IsNotRetried</c>.
    /// </summary>
    [Fact]
    public void ADestinationHeldOpenSurfacesTheTypedFailure()
    {
        var (temp, dest) = Staged("new", existing: "old");

        using var hold = new FileStream(
            dest, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<DownloadDestinationBlockedException>(
            () => DownloadService.ReplaceDestination(temp, dest));

        Assert.Equal(dest, ex.DestinationPath);
        Assert.NotNull(ex.InnerException);
    }
}
