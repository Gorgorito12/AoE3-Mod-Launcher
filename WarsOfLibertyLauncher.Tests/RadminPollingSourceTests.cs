using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What the Radmin polling may cost the UI thread, and where. The probes it makes — the uninstall
/// registry, the process list, the network adapters, ~11 MB of Radmin logs — are each a few
/// milliseconds to hundreds; these pin which of them run, how often, and on which thread.
/// </summary>
public class RadminPollingSourceTests
{
    private static string Read(string relative) => File.ReadAllText(UiThreadAttributionTests.LauncherFile(relative));

    private static string Body(string src, string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, $"'{signature}' has moved or been renamed.");
        var next = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        return next > start ? src[start..next] : src[start..];
    }

    // ---------------------------------------------------------------- the install lookup

    [Fact]
    public void AnInstallIsRememberedAndStillCheckedAgainstTheDisk()
    {
        var cache = new RadminInstallCache();
        var exists = new HashSet<string> { @"C:\R\RvRvpnGui.exe" };
        cache.Store(@"C:\R\RvRvpnGui.exe", "2.0", nowMs: 0);
        Assert.Equal((@"C:\R\RvRvpnGui.exe", "2.0"), cache.Get(1000, exists.Contains));

        // Uninstalled: the file is gone, so the next read goes back to the registry.
        exists.Clear();
        Assert.Null(cache.Get(2000, exists.Contains));
    }

    [Fact]
    public void AMissIsNeverRemembered_SoAnInstallIsSeenOnTheNextPoll()
    {
        var cache = new RadminInstallCache();
        cache.Store(null, null, nowMs: 0);
        Assert.Null(cache.Get(1, _ => true));
    }

    [Fact]
    public void AnInstallIsRescannedAfterFiveMinutes()
    {
        var cache = new RadminInstallCache();
        cache.Store(@"C:\R\RvRvpnGui.exe", "2.0", nowMs: 0);
        var limit = (long)RadminInstallCache.Rescan.TotalMilliseconds;
        Assert.NotNull(cache.Get(limit - 1, _ => true));
        Assert.Null(cache.Get(limit, _ => true));
    }

    [Fact]
    public void ForgetDropsIt()
    {
        var cache = new RadminInstallCache();
        cache.Store(@"C:\R\RvRvpnGui.exe", "2.0", nowMs: 0);
        cache.Forget();
        Assert.Null(cache.Get(1, _ => true));
    }

    // ---------------------------------------------------------------- the banner poll

    /// <summary>
    /// A minimized window keeps IsVisible, so the visibility gate alone never stopped the poll.
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void TheBannerPollsOnlyWhileSomebodyCanSeeIt(bool visible, bool minimized, bool expected)
        => Assert.Equal(expected, MultiplayerTab.ShouldPollBanner(visible, minimized));

    [Fact]
    public void TheStartupProbesArePostedNotRunInline()
    {
        var tab = Read("Controls/MultiplayerTab.xaml.cs");
        var polling = Body(tab, "private void StartRadminPolling()");
        Assert.DoesNotContain("RefreshRadminBanner();", polling);
        Assert.Contains("PostRadminBannerRefresh();", polling);

        // The constructor's own render, likewise.
        var ctorRender = tab.IndexOf("PostRadminBannerRefresh();\n        // Initial state is the signed-out gate", StringComparison.Ordinal);
        if (ctorRender < 0)
            ctorRender = tab.IndexOf("PostRadminBannerRefresh();\r\n        // Initial state is the signed-out gate", StringComparison.Ordinal);
        Assert.True(ctorRender > 0, "The constructor no longer posts its first banner refresh.");

        var xaml = Read("Controls/MultiplayerTab.xaml");
        var banner = xaml.IndexOf("x:Name=\"RadminBanner\"", StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml.Substring(banner, 200));
    }

    // ---------------------------------------------------------------- the adapter walks

    [Fact]
    public void TheConnectionChipNeverWalksTheAdapters_TheDropdownDoesWhenItOpens()
    {
        Assert.DoesNotContain("TryGetAdapterIp(", Body(Read("Controls/MultiplayerTab.xaml.cs"), "private void PushConnectionChip()"));
        Assert.Contains("TryGetAdapterIp(", Body(Read("MainWindow.Compact.cs"), "private void ConnectionChip_Click("));
    }

    /// <summary>One walk of the interfaces per traffic reading, not two.</summary>
    [Fact]
    public void TheTrafficMeterWalksTheAdaptersOnce()
    {
        var src = Read("Services/RadminVpnService.cs");
        var start = src.IndexOf("public static (long sent, long received)? GetAdapterBytes()", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = src.IndexOf("\n    }", start, StringComparison.Ordinal);
        var body = src[start..end];
        Assert.DoesNotContain("GetAllNetworkInterfaces", body);
        Assert.Contains("ReadAdapterCandidates(nics)", body);
    }

    // ---------------------------------------------------------------- the assistant

    /// <summary>
    /// The probe runs on the pool, a tick never starts a second one, and the auto-open decides
    /// from the base stage without reading the logs at all.
    /// </summary>
    [Fact]
    public void TheAssistantProbesOffTheUiThread()
    {
        var service = Read("Services/RadminAssistantService.cs");
        Assert.Contains("=> Task.Run(() => ProbeCoreAsync(ct));", service);

        var window = Read("RadminAssistantWindow.xaml.cs");
        Assert.Contains("if (_refreshing || _closed) return;", window);

        var autoOpen = Body(Read("Controls/MultiplayerTab.xaml.cs"), "private async void MaybeAutoOpenAssistant()");
        Assert.DoesNotContain("ProbeAsync(", autoOpen);
        Assert.Contains("RadminAssistantService.StageOf(", autoOpen);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the auto-open: deciding from the base stage is the SAME decision
    /// the full probe made. The probe only ever promotes LoggedIn to InAoE3Network, so "the probe
    /// says at least LoggedIn" is exactly "the base stage is LoggedIn" — exactly "the service is
    /// running" on an installed Radmin.
    /// </summary>
    [Theory]
    [InlineData(RadminInstallState.NotInstalled, false, RadminStage.NotInstalled)]
    [InlineData(RadminInstallState.NotInstalled, true, RadminStage.NotInstalled)]
    [InlineData(RadminInstallState.Installed, false, RadminStage.InstalledNotRunning)]
    [InlineData(RadminInstallState.Installed, true, RadminStage.LoggedIn)]
    public void THE_ONE_THAT_MATTERS_TheAutoOpenDecisionIsTheBaseStage(
        RadminInstallState install, bool running, RadminStage expected)
    {
        var status = new RadminStatus(install, null, null, running, running ? "26.1.2.3" : null);
        var stage = RadminAssistantService.StageOf(status);
        Assert.Equal(expected, stage);
        Assert.Equal(install == RadminInstallState.Installed && running, stage >= RadminStage.LoggedIn);
    }
}
