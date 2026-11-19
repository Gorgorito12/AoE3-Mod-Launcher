using System.Collections.Generic;
using System.Windows;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// An operation the launcher runs by itself (an automatic mod update) never opens a window over
/// the player: a question owned by the window running it answers "no", a notice becomes a toast.
/// The REFUSALS to suppress are the point — a question from any other window must still open.
/// </summary>
[Collection("wpf-and-language")]
public class UnattendedTests
{
    [Fact]
    public void OnlyTheOperationsOwnWindowIsAnsweredForThePlayer()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var main = new Window();
            var other = new Window();
            Assert.True(Unattended.Answers(main, main));
            Assert.False(Unattended.Answers(main, other));
            Assert.False(Unattended.Answers(main, null));
            Assert.False(Unattended.Answers(null, main));
        });
        Assert.Null(error);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AQuestionDuringAnAutomaticUpdateIsNoAndOpensNothing()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var main = new Window();
            var notices = new List<string>();
            var sink = Unattended.NoticeSink;
            Unattended.NoticeSink = (title, _, warning, reopen) => notices.Add($"{title}|{warning}|{reopen != null}");
            try
            {
                using (Unattended.Begin(main))
                {
                    // Ask returns without ShowDialog: a modal on this thread would never return.
                    Assert.False(ThemedConfirmDialog.Ask(main, "Retry?", "body", "Yes", "No"));
                    Assert.Equal(ConfirmAnswer.Cancel,
                        ThemedConfirmDialog.AskThreeWay(main, "Which?", "body", "A", "B", "C"));
                    ThemedConfirmDialog.Notice(main, "Reverted", "body", ConfirmTone.Caution);
                    AntivirusExclusionDialog.ShowBlocked(main, @"AI3\wolai.upl", @"C:\Games\WoL");
                    Assert.False(AntivirusExclusionDialog.ShowNotice(main, "WoL", "x", @"C:\x", out var never));
                    Assert.False(never);
                }
                Assert.False(Unattended.Active);
            }
            finally
            {
                Unattended.NoticeSink = sink;
            }
            Assert.Equal(2, notices.Count);
            Assert.Equal("Reverted|True|False", notices[0]);
            // The antivirus toast carries a button that reopens its window later.
            Assert.EndsWith("|True|True", notices[1]);
        });
        Assert.Null(error);
    }

    [Fact]
    public void AScopeRestoresWhatWasThereBefore()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var a = new Window();
            var b = new Window();
            using (Unattended.Begin(a))
            {
                using (Unattended.Begin(b)) Assert.True(Unattended.Answers(b, b));
                Assert.True(Unattended.Skip(a, "question", "t"));
                Assert.False(Unattended.Skip(b, "question", "t"));
            }
            Assert.False(Unattended.Active);
        });
        Assert.Null(error);
    }
}
