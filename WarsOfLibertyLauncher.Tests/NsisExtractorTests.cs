using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins NSIS's command-line rules, which are unusual enough that breaking them
/// fails SILENTLY: the installer still runs and still exits 0, it just ignores
/// the destination and writes wherever it defaults to. Nothing downstream would
/// say why the addon didn't appear.
/// </summary>
public class NsisExtractorTests
{
    /// <summary>
    /// <c>/D=</c> must be last. Anything after it is read as part of the path.
    /// </summary>
    [Fact]
    public void DestinationSwitchComesLast()
    {
        var args = NsisExtractor.BuildArguments(@"C:\temp\addon");

        Assert.EndsWith(@"/D=C:\temp\addon", args);
        Assert.StartsWith("/S", args);
    }

    /// <summary>
    /// Unquoted is what lets the path contain spaces at all: NSIS reads to the
    /// end of the command line, so quoting it makes the quotes part of the path.
    /// A Windows user name with a space is enough to hit this.
    /// </summary>
    [Fact]
    public void DestinationIsNeverQuoted_EvenWithSpaces()
    {
        var args = NsisExtractor.BuildArguments(@"C:\Users\Ana María\AppData\Local\x");

        Assert.DoesNotContain("\"", args);
        Assert.EndsWith(@"/D=C:\Users\Ana María\AppData\Local\x", args);
    }

    /// <summary>A trailing separator makes NSIS reject the path.</summary>
    [Theory]
    [InlineData(@"C:\temp\addon\")]
    [InlineData("C:/temp/addon/")]
    [InlineData(@"C:\temp\addon")]
    public void TrailingSeparatorIsRemoved(string input)
        => Assert.EndsWith("addon", NsisExtractor.BuildArguments(input));

    [Fact]
    public void SilentSwitchIsAlwaysPresent()
        => Assert.Contains("/S", NsisExtractor.BuildArguments(@"C:\x"));

    [Fact]
    public void SurroundingWhitespaceIsIgnored()
        => Assert.Equal(@"/S /D=C:\temp\addon", NsisExtractor.BuildArguments(@"  C:\temp\addon  "));

    /// <summary>
    /// The safety argument for running a third-party installer at all rests on it
    /// writing somewhere disposable, so a destination outside the launcher's own
    /// data folder is refused rather than trusted.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task RefusesToRunOutsideTheLauncherDataFolder()
    {
        var ex = await Assert.ThrowsAsync<NsisExtractionException>(() =>
            NsisExtractor.ExtractAsync(@"C:\Windows\System32\notepad.exe", @"C:\Games\Wars of Liberty"));

        Assert.Contains("outside", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingInstallerIsReportedClearly()
    {
        var dest = System.IO.Path.Combine(AppPaths.DataDir, "addons", "unpacked", "test-missing");

        var ex = await Assert.ThrowsAsync<NsisExtractionException>(() =>
            NsisExtractor.ExtractAsync(@"C:\nope\does-not-exist.exe", dest));

        Assert.Contains("not found", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}
