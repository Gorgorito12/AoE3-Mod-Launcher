using System.Windows.Controls;
using System.Windows.Documents;
using WarsOfLibertyLauncher;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Clicking on text while the notification bell is open must not take the launcher down.
///
/// <para><b>This is a real crash, off a real crash log.</b>
/// <c>crash-20260905-133330.log</c>, from a shipped 1.0.13m:</para>
/// <code>
/// System.InvalidOperationException: 'System.Windows.Documents.Run' is not a Visual or Visual3D.
///    at MainWindow.IsWithin(DependencyObject node, DependencyObject ancestor)
///    at MainWindow.CloseNotifOnOutsideClick(Object sender, MouseButtonEventArgs e)
/// </code>
///
/// <para>Opening the bell attaches <c>CloseNotifOnOutsideClick</c> to the window's
/// <c>PreviewMouseDown</c>, and that handler asks whether the click landed inside the bell by
/// walking up from <c>e.OriginalSource</c>. For a click on text the OriginalSource is a
/// <c>Run</c> — a <c>ContentElement</c>, which is not in the visual tree at all — and
/// <c>VisualTreeHelper.GetParent</c> <b>throws</b> for it rather than returning null. From a
/// preview handler the exception reaches <c>DispatcherUnhandledException</c> and the process
/// goes. So: open the bell, click any label, launcher gone.</para>
///
/// <para>Same trap as the test walker that stepped into <c>TextBlock.Inlines</c> this week.
/// There the answer was to stop at the TextBlock; here it cannot be, because the question is
/// whether the click was inside the bell and the answer is above the text.</para>
/// </summary>
[Collection("wpf-and-language")]
public class OutsideClickTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. A click on text, anywhere outside the bell, answers "no" instead
    /// of throwing.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ClickingTextWithThePopupOpenDoesNotThrow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var bell = new Button();

            // Somewhere else entirely — a label in the window, which is what the reporter
            // clicked. The Run is the mouse event's OriginalSource.
            var run = new Run("Wars of Liberty");
            var elsewhere = new TextBlock(run);

            // The crash, in one line: this used to throw before it could return anything.
            Assert.False(MainWindow.IsWithin(run, bell));

            // And the containing TextBlock, which is a Visual and always worked, still does.
            Assert.False(MainWindow.IsWithin(elsewhere, bell));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// And a click on the bell's OWN text still counts as inside it.
    ///
    /// <para>The cheap way to stop the throw is to give up at the first thing that is not a
    /// Visual and answer "no" — which would make every click on the bell's own caption read as
    /// a click outside, so the popup would close and the button's Click handler would reopen
    /// it, or not, depending on ordering. The walk has to CONTINUE through the text's host.</para>
    /// </summary>
    [Fact]
    public void AClickInsideTheBellIsRecognisedThroughItsText()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var caption = new Run("3");
            var bell = new Button { Content = new TextBlock(caption) };

            // Give the button a visual tree: Content is only realised on measure.
            bell.Measure(new System.Windows.Size(100, 40));
            bell.Arrange(new System.Windows.Rect(0, 0, 100, 40));
            bell.UpdateLayout();

            Assert.True(MainWindow.IsWithin(caption, bell),
                "a click on the bell's own caption reads as a click outside it, so the popup "
                + "fights its own toggle.");
        });
        Assert.Null(error);
    }
}
