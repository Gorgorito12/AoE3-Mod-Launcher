using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the advisory "an operation started and did not finish" record, and the temp names of
/// payload parts. Both close silent failures: an interrupted repair left a mixed install that
/// PLAY launched with no warning, and every mod's parts shared one temp name, so a cancelled
/// download of one mod could be resumed with another mod's bytes.
/// </summary>
public class OperationJournalTests
{
    [Fact]
    public void TheKeyIgnoresCaseAndATrailingSeparator()
    {
        Assert.Equal(OperationJournal.KeyFor(@"C:\Games\Wars of Liberty"),
                     OperationJournal.KeyFor(@"c:\games\WARS OF LIBERTY\"));
        Assert.NotEqual(OperationJournal.KeyFor(@"C:\Games\Wars of Liberty"),
                        OperationJournal.KeyFor(@"C:\Games\Wars of Liberty (2)"));
    }

    [Fact]
    public void BeginThenClear_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "journal-" + Guid.NewGuid().ToString("N"));
        Assert.Null(OperationJournal.TryGetOpen(path));

        OperationJournal.Begin("repair", "wol", path);
        var open = OperationJournal.TryGetOpen(path);
        Assert.NotNull(open);
        Assert.Equal("repair", open!.Operation);
        Assert.Equal("wol", open.ModId);

        OperationJournal.Clear(path);
        Assert.Null(OperationJournal.TryGetOpen(path));
    }

    [Fact]
    public void ACopyNeverSeesAnotherCopysEntry()
    {
        var a = Path.Combine(Path.GetTempPath(), "journal-a-" + Guid.NewGuid().ToString("N"));
        var b = a + " (2)";
        OperationJournal.Begin("repair", "wol", a);
        try { Assert.Null(OperationJournal.TryGetOpen(b)); }
        finally { OperationJournal.Clear(a); }
    }

    [Fact]
    public void PartNamesAreKeyedOnTheUrl_SoTwoModsNeverShareAResumeFile()
    {
        var wol = NativeInstallService.PartFileName("https://example.org/wol/WolPayload.zip.001", 1);
        var other = NativeInstallService.PartFileName("https://example.org/other/payload.zip", 1);
        Assert.NotEqual(wol, other);
        // Stable for the SAME part, which is the one case resuming is right for.
        Assert.Equal(wol, NativeInstallService.PartFileName("https://example.org/wol/WolPayload.zip.001", 1));
        Assert.EndsWith(".001", wol);
    }
}
