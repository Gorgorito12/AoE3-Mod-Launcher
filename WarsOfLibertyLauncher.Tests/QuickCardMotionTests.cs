using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Animation;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The quick match card's spinner and pulse (design 86) follow the same rule as the rank badges:
/// they move only while animations are on, and "Effects and animations: Reduced" in Settings turns
/// them off as surely as the slow-PC governor does.
/// </summary>
[Collection("wpf-and-language")]
public class QuickCardMotionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static int ClocksAfterMark(MultiplayerTab tab, QuickCardMark mark)
    {
        typeof(MultiplayerTab).GetMethod("BuildQuickCardMark", Private)!.Invoke(tab, new object[] { mark });
        var clocks = (List<AnimationClock>)typeof(MultiplayerTab).GetField("_quickCardClocks", Private)!.GetValue(tab)!;
        var n = clocks.Count;
        typeof(MultiplayerTab).GetMethod("StopQuickCardClocks", Private)!.Invoke(tab, null);
        return n;
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ReducedEffectsBuildTheMarksStill()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var tab = new MultiplayerTab();
                Assert.Equal(1, ClocksAfterMark(tab, QuickCardMark.Spinner));
                Assert.Equal(1, ClocksAfterMark(tab, QuickCardMark.Pulse));
                Assert.Equal(0, ClocksAfterMark(tab, QuickCardMark.Found));

                RankBadge.SetUserReducedEffects(true);
                Assert.Equal(0, ClocksAfterMark(tab, QuickCardMark.Spinner));
                Assert.Equal(0, ClocksAfterMark(tab, QuickCardMark.Pulse));
                RankBadge.SetUserReducedEffects(false);

                RankBadge.AnimationsOverride = false;   // Windows' own "animations off"
                Assert.Equal(0, ClocksAfterMark(tab, QuickCardMark.Pulse));
            }
            finally
            {
                RankBadge.AnimationsOverride = null;
                RankBadge.SetUserReducedEffects(false);
            }
        });
        Assert.Null(error);
    }

    /// <summary>Turning the setting back off never re-lights a PC the governor found slow.</summary>
    [Fact]
    public void TheSettingAndTheGovernorAreKeptApart()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            try
            {
                RankBadge.SetReducedEffects(true);       // the governor
                RankBadge.SetUserReducedEffects(true);   // the setting
                RankBadge.SetUserReducedEffects(false);
                Assert.True(RankBadge.ReducedEffects);

                RankBadge.SetReducedEffects(false);
                Assert.False(RankBadge.ReducedEffects);

                RankBadge.SetUserReducedEffects(true);
                Assert.True(RankBadge.ReducedEffects);
            }
            finally
            {
                RankBadge.SetReducedEffects(false);
                RankBadge.SetUserReducedEffects(false);
            }
        });
        Assert.Null(error);
    }
}
