using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="DownloadService.IsTransientDownloadFailure"/> — the policy that
/// decides whether a failed download attempt is retried.
///
/// It matters because of what sits behind it: the WoL payload is ~4 GB over three
/// parts with no mirror, so "retry and resume from the .part file" is the difference
/// between a momentary network blip and losing the whole install. The REJECTION cases
/// are the point of this file. Retrying a user's own Cancel would make the Cancel
/// button take four attempts and a backoff to be obeyed, and retrying a permission
/// error just rewrites the same failure — both would be worse than not retrying at all.
///
/// The awkward case, and the reason this takes a <c>userCancelled</c> flag instead of
/// only an exception: an HttpClient timeout and a user cancellation are BOTH
/// <see cref="TaskCanceledException"/>. The token state is the only thing that tells
/// them apart, which is the same trap <see cref="ConnectivityState.IsNetworkError"/>
/// documents.
/// </summary>
public class DownloadRetryTests
{
    // ---- Must NOT be retried ---------------------------------------------------

    [Fact]
    public void UserCancel_IsNeverRetried()
    {
        Assert.False(DownloadService.IsTransientDownloadFailure(
            new OperationCanceledException(), userCancelled: true));
    }

    [Fact]
    public void UserCancel_OutranksAWrappedNetworkError()
    {
        // A cancellation that happened while a socket error was already in flight
        // still has the socket error in its chain. The user's intent wins.
        var ex = new IOException("connection reset", new SocketException(10054));

        Assert.False(DownloadService.IsTransientDownloadFailure(ex, userCancelled: true));
    }

    [Fact]
    public void PermissionError_IsNotRetried()
    {
        Assert.False(DownloadService.IsTransientDownloadFailure(
            new UnauthorizedAccessException("access to the path is denied"),
            userCancelled: false));
    }

    [Fact]
    public void PermissionError_WrappedInAnIOException_IsStillNotRetried()
    {
        // UnauthorizedAccessException is checked before IOException while walking the
        // chain, so the more specific "this will never work" answer wins over the
        // generic "I/O went wrong" one.
        var ex = new IOException("could not write", new UnauthorizedAccessException());

        Assert.False(DownloadService.IsTransientDownloadFailure(ex, userCancelled: false));
    }

    [Fact]
    public void AProgrammingError_IsNotRetried()
    {
        Assert.False(DownloadService.IsTransientDownloadFailure(
            new InvalidOperationException("bug"), userCancelled: false));
    }

    [Fact]
    public void NoException_IsNotRetried()
    {
        Assert.False(DownloadService.IsTransientDownloadFailure(null, userCancelled: false));
    }

    // ---- Must be retried -------------------------------------------------------

    [Fact]
    public void HttpTimeout_IsRetried_BecauseTheTokenWasNotCancelled()
    {
        // This is what an HttpClient timeout looks like. Indistinguishable by type
        // from a user cancel — only userCancelled:false separates them.
        Assert.True(DownloadService.IsTransientDownloadFailure(
            new TaskCanceledException("The request was canceled due to timeout."),
            userCancelled: false));
    }

    [Fact]
    public void DroppedConnection_IsRetried()
    {
        Assert.True(DownloadService.IsTransientDownloadFailure(
            new IOException("The response ended prematurely."), userCancelled: false));
    }

    [Fact]
    public void SocketError_IsRetried()
    {
        Assert.True(DownloadService.IsTransientDownloadFailure(
            new SocketException(10054), userCancelled: false));
    }

    [Fact]
    public void HttpRequestException_IsRetried()
    {
        Assert.True(DownloadService.IsTransientDownloadFailure(
            new HttpRequestException("name resolution failed"), userCancelled: false));
    }

    [Fact]
    public void NetworkErrorBuriedInAWrapper_IsFound()
    {
        // The wrapper type is not the signal; its inner exception is. Same reason
        // ConnectivityState.IsNetworkError walks the chain.
        var ex = new InvalidOperationException(
            "manifest unreachable", new HttpRequestException("connection refused"));

        Assert.True(DownloadService.IsTransientDownloadFailure(ex, userCancelled: false));
    }
}
