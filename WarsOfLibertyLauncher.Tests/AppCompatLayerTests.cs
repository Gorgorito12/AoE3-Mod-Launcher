using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="AppCompatLayerService.Parse"/> — the pure half of the compatibility
/// layer probe, split out of the registry precisely so it can be tested.
///
/// <para>The case that matters is the <c>~</c> marker. It is the only signal separating
/// "Windows decided this on its own" from "the user ticked the Compatibility box", and
/// the launcher offers to remove ONLY the former. Read it wrong in the permissive
/// direction and the launcher undoes a deliberate choice; read it wrong in the strict
/// direction and the fix never appears for the case it was built for — the observed
/// <c>"~ WINXPSP3"</c> that Windows pinned on <c>age3y.exe</c> by itself.</para>
/// </summary>
public class AppCompatLayerTests
{
    [Fact]
    public void TildeMarksALayerWindowsAppliedItself()
    {
        // The exact value observed on the reporting machine.
        var info = AppCompatLayerService.Parse("~ WINXPSP3", inCurrentUserHive: true);

        Assert.True(info.AppliedByWindows);
        Assert.True(info.HasCompatibilityMode);
        Assert.False(info.HasRunAsAdmin);       // no explicit RUNASADMIN, yet it still elevates
        Assert.True(info.InCurrentUserHive);
    }

    [Fact]
    public void TildeGluedToTheFirstTokenCountsToo()
    {
        // Windows writes "~ WINXPSP3", but the marker is not always space-separated.
        var info = AppCompatLayerService.Parse("~WINXPSP3", inCurrentUserHive: true);

        Assert.True(info.AppliedByWindows);
        Assert.True(info.HasCompatibilityMode);
    }

    [Fact]
    public void NoTildeMeansTheUserSetItDeliberately()
    {
        // What age3.exe / age3x.exe carry on the same machine — set by hand years ago.
        // Offering to undo one of these would be undoing someone's own decision.
        var info = AppCompatLayerService.Parse("WINXPSP3 RUNASADMIN", inCurrentUserHive: true);

        Assert.False(info.AppliedByWindows);
        Assert.True(info.HasRunAsAdmin);
        Assert.True(info.HasCompatibilityMode);
    }

    [Fact]
    public void BehaviourFixesAreNotCompatibilityModes()
    {
        // HIGHDPIAWARE and friends are not an OS-version mode and must not read as one.
        var info = AppCompatLayerService.Parse("~ HIGHDPIAWARE", inCurrentUserHive: true);

        Assert.True(info.AppliedByWindows);
        Assert.False(info.HasCompatibilityMode);
        Assert.False(info.HasRunAsAdmin);
    }

    [Fact]
    public void UnknownFutureModesStillClassifyAsCompatibilityModes()
    {
        // Matched by prefix rather than a closed list: a mode Microsoft adds later must
        // not silently read as "no compatibility mode set".
        Assert.True(AppCompatLayerService.Parse("~ WIN11RTM", true).HasCompatibilityMode);
        Assert.True(AppCompatLayerService.Parse("VISTASP2", true).HasCompatibilityMode);
    }
}
