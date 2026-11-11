using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// A menu opened from the keyboard opens under its element; WPF centred it on the element, which
/// on the folded Library list covered the tile and ran out of the window. A right click must keep
/// opening at the pointer, so everything is put back when the menu closes.
/// </summary>
public class KeyboardContextMenuTests
{
    [Theory]
    [InlineData(-1, -1, true)]
    [InlineData(0, 0, false)]
    [InlineData(120, 40, false)]
    public void OnlyAnOpeningWithNoPointerIsTheKeyboard(double left, double top, bool keyboard)
        => Assert.Equal(keyboard, KeyboardContextMenu.IsFromKeyboard(left, top));

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheMenuGoesUnderItsElementAndComesBackForTheMouse()
    {
        StaTestThread.Run(() =>
        {
            var owner = new Button();
            var menu = new ContextMenu();
            KeyboardContextMenu.Place(menu, owner);
            Assert.Equal(PlacementMode.Bottom, menu.Placement);
            Assert.Same(owner, menu.PlacementTarget);

            KeyboardContextMenu.Restore(menu);
            Assert.Equal(PlacementMode.MousePoint, menu.Placement);
            Assert.Null(menu.PlacementTarget);
            Assert.Equal(0, menu.VerticalOffset);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AMenuWithAPlacementOfItsOwnIsLeftAlone()
    {
        StaTestThread.Run(() =>
        {
            var other = new Button();
            var menu = new ContextMenu { Placement = PlacementMode.Top, PlacementTarget = other, VerticalOffset = -6 };
            KeyboardContextMenu.Place(menu, new Button());
            Assert.Equal(PlacementMode.Top, menu.Placement);
            Assert.Same(other, menu.PlacementTarget);

            KeyboardContextMenu.Restore(menu);
            Assert.Equal(PlacementMode.Top, menu.Placement);
            Assert.Equal(-6, menu.VerticalOffset);
        }, TimeSpan.FromSeconds(30));
    }
}
