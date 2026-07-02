using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the offline-mode core: the pure local-state CheckResult builder
/// (<see cref="UpdateService.BuildOfflineResultData"/>) returned when the network is
/// unreachable, and the network-error classifier
/// (<see cref="ConnectivityState.IsNetworkError"/>) that drives the observed offline
/// signal. Both are pure (no I/O), so they're unit-testable; the thin try/catch
/// wiring in CheckAsync that calls them is not (its UpdateInfoService isn't
/// injectable) and is covered by the manual offline smoke test.
/// </summary>
public class OfflineFallbackTests
{
    // ---- BuildOfflineResultData ------------------------------------------------

    [Fact]
    public void Offline_ValidInstall_UsesCachedVersion_NoPending_Degraded()
    {
        var state = new ModState { LastKnownVersion = "1.2.0c2" };

        var result = UpdateService.BuildOfflineResultData(state, manifest: null, valid: true);

        Assert.True(result.IsValidInstall);
        Assert.True(result.Degraded);
        Assert.NotNull(result.CurrentVersion);
        Assert.Equal("1.2.0c2", result.CurrentVersion!.Ver);
        // Latest mirrors current offline (we can't know a newer one) → clean
        // "up to date" rendering, never the misleading "reinstall" status.
        Assert.NotNull(result.LatestVersion);
        Assert.Equal("1.2.0c2", result.LatestVersion!.Ver);
        // No manifest → no computable/verifiable update → don't nag.
        Assert.Empty(result.PendingDownloads);
    }

    [Fact]
    public void Offline_ValidInstall_NoCache_FallsBackToManifestVersion()
    {
        var state = new ModState { LastKnownVersion = "" };
        var manifest = new InstallManifest { Version = "1.0.0" };

        var result = UpdateService.BuildOfflineResultData(state, manifest, valid: true);

        Assert.NotNull(result.CurrentVersion);
        Assert.Equal("1.0.0", result.CurrentVersion!.Ver);
        Assert.True(result.Degraded);
    }

    [Fact]
    public void Offline_ValidInstall_NoCacheNoManifest_CurrentNonNullEmpty_RendersPlay()
    {
        // The degenerate case (installed, but never version-checked and no manifest):
        // CurrentVersion must still be NON-null so versionKnown==true → PLAY, not
        // Install, for a mod that's actually on disk.
        var state = new ModState { LastKnownVersion = "" };

        var result = UpdateService.BuildOfflineResultData(state, manifest: null, valid: true);

        Assert.NotNull(result.CurrentVersion);
        Assert.Equal("", result.CurrentVersion!.Ver);
        Assert.True(result.IsValidInstall);
    }

    [Fact]
    public void Offline_NotInstalled_CurrentNull_RendersInstall()
    {
        var state = new ModState { LastKnownVersion = "1.2.0c2" };

        var result = UpdateService.BuildOfflineResultData(state, manifest: null, valid: false);

        Assert.Null(result.CurrentVersion);
        Assert.Null(result.LatestVersion);
        Assert.False(result.IsValidInstall);
        Assert.True(result.Degraded);
        Assert.Empty(result.PendingDownloads);
    }

    // ---- ConnectivityState.IsNetworkError -------------------------------------

    [Fact]
    public void IsNetworkError_HttpRequestException_True()
        => Assert.True(ConnectivityState.IsNetworkError(new HttpRequestException("no route")));

    [Fact]
    public void IsNetworkError_SocketException_True()
        => Assert.True(ConnectivityState.IsNetworkError(new SocketException()));

    [Fact]
    public void IsNetworkError_TimeoutLikeTaskCanceled_True()
        => Assert.True(ConnectivityState.IsNetworkError(new TaskCanceledException()));

    [Fact]
    public void IsNetworkError_WrappedTransportError_WalksInnerChain_True()
    {
        // UpdateInfoService wraps the real transport error as InvalidOperationException
        // ("ErrManifestUnreachable") — the wrapper type isn't a signal but its inner is.
        var wrapped = new InvalidOperationException(
            "ErrManifestUnreachable", new HttpRequestException("offline"));
        Assert.True(ConnectivityState.IsNetworkError(wrapped));
    }

    [Fact]
    public void IsNetworkError_LogicBug_False()
        => Assert.False(ConnectivityState.IsNetworkError(new ArgumentException("bad arg")));

    [Fact]
    public void IsNetworkError_Null_False()
        => Assert.False(ConnectivityState.IsNetworkError(null));
}
