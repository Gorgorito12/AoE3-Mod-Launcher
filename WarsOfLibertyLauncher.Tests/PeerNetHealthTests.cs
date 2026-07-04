using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the pure per-peer connection-health classifier. Order of precedence
/// is load-bearing: no Radmin IP → WaitingVpn regardless of counters; an answered
/// probe → Online; sustained failure past the threshold → Lost; anything below the
/// threshold → Unstable (transient, not a false "lost" alarm).
/// </summary>
public class PeerNetHealthTests
{
    [Fact]
    public void NoRadminIp_IsWaitingVpn_RegardlessOfHistory()
    {
        Assert.Equal(PeerLinkState.WaitingVpn, PeerNetHealth.Classify(false, -1, 0));
        Assert.Equal(PeerLinkState.WaitingVpn, PeerNetHealth.Classify(false, 42, 99));
    }

    [Fact]
    public void AnsweredProbe_IsOnline()
    {
        Assert.Equal(PeerLinkState.Online, PeerNetHealth.Classify(true, 0, 0));
        Assert.Equal(PeerLinkState.Online, PeerNetHealth.Classify(true, 250, 0));
    }

    [Fact]
    public void TransientFailure_BelowThreshold_IsUnstable()
    {
        Assert.Equal(PeerLinkState.Unstable, PeerNetHealth.Classify(true, -1, 0));
        Assert.Equal(PeerLinkState.Unstable,
            PeerNetHealth.Classify(true, -1, PeerNetHealth.LostThreshold - 1));
    }

    [Fact]
    public void SustainedFailure_AtOrAboveThreshold_IsLost()
    {
        Assert.Equal(PeerLinkState.Lost,
            PeerNetHealth.Classify(true, -1, PeerNetHealth.LostThreshold));
        Assert.Equal(PeerLinkState.Lost,
            PeerNetHealth.Classify(true, -1, PeerNetHealth.LostThreshold + 10));
    }
}
