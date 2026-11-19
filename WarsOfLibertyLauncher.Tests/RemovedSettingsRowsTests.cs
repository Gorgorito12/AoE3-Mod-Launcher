using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Beta channel and "Verify the signature" rows were removed (2026-11-18): there is no beta
/// channel, and the digest check is now always on. The settings search reveals a collapsed row
/// that matches, so a row merely hidden would still come up for "beta" — it has to be gone.
/// </summary>
[Collection("wpf-and-language")]
public class RemovedSettingsRowsTests
{
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }

    [Fact]
    public void SearchingBetaOrSignatureFindsNothing()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            foreach (var lang in new[] { Strings.LangEn, Strings.LangEs })
            {
                var previous = Strings.Language;
                Strings.SetLanguage(lang);
                try
                {
                    var dlg = new LauncherSettingsDialog(new LauncherConfig());
                    Assert.Null(dlg.FindName("ChannelBetaRadio"));
                    Assert.Null(dlg.FindName("VerifyDownloadsCheck"));
                    var texts = Walk(dlg).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                    Assert.DoesNotContain(texts, t => t.Contains("Beta", System.StringComparison.OrdinalIgnoreCase));
                    dlg.Close();
                }
                finally { Strings.SetLanguage(previous); }
            }
        });
        Assert.Null(error);
    }
}
