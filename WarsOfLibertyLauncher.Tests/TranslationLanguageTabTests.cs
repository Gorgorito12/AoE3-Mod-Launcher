using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The mod window's Language tab once the same language can come from several translators: one
/// card per translator, and the "Translation sources" block under them. The cards and the source
/// rows are built in code from names the XAML must still carry (<c>TxSourceRows</c>,
/// <c>TxAddInput</c>…), so a rename throws only when the window opens — which nothing else in the
/// run reaches with two cards of the same id.
/// </summary>
[Collection("wpf-and-language")]
public class TranslationLanguageTabTests
{
    private static TranslationVersion Ver(string source, bool official, string version, string hash, string date) => new()
    {
        Version = version, ContentHash = hash, Date = date, TargetMod = "wol",
        CompatibleWith = new List<string> { "1.2.0e" },
        SourceKey = source, SourceLabel = source, IsOfficial = official,
        SourceKind = official ? TranslationSourceKind.GitHubFolder : TranslationSourceKind.Index,
    };

    private static TranslationIndexEntry Card(string source, string label, bool official, params TranslationVersion[] versions) => new()
    {
        Id = "ES-LA", Name = "Español", Author = official ? "Gorgorito" : "Juan",
        Version = versions[0].Version, ContentHash = versions[0].ContentHash, CompatibleWith = versions[0].CompatibleWith,
        TargetMod = "wol", SourceKey = source, SourceLabel = label, IsOfficial = official,
        SourceKind = versions[0].SourceKind, Versions = versions.ToList(),
    };

    private static ModPropertiesDialog Build(LauncherConfig config, TranslationIndex index, IReadOnlyList<TranslationSourceRow> rows)
    {
        var profile = ModRegistry.Default;
        return new ModPropertiesDialog(
            profile,
            new UpdateService(config, profile),
            config,
            translationIndex: index,
            applyTranslation: _ => { },
            revertToEnglish: () => { },
            openVerify: () => { },
            openRepair: () => { },
            checkForUpdates: () => Task.FromResult<UpdateService.CheckResult?>(null),
            openAoE3Folder: () => { },
            changeModFolder: () => { },
            changeAoE3Folder: () => { },
            openUserDataFolder: () => { },
            createBackup: () => null,
            restoreBackup: () => null,
            viewLogs: () => { },
            shareDiagnostics: () => { },
            uninstall: () => { },
            listTranslationSources: () => rows,
            addTranslationSource: _ => Task.FromResult((false, "")),
            removeTranslationSource: _ => Task.CompletedTask);
    }

    private static IEnumerable<string> TextsUnder(DependencyObject root)
    {
        if (root is TextBlock tb)
        {
            yield return tb.Text;
            foreach (var run in tb.Inlines.OfType<System.Windows.Documents.Run>()) yield return run.Text;
        }
        if (root is ContentControl { Content: string s }) yield return s;
        int n = root is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
        if (n == 0)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var t in TextsUnder(child)) yield return t;
            yield break;
        }
        for (int i = 0; i < n; i++)
            foreach (var t in TextsUnder(VisualTreeHelper.GetChild(root, i))) yield return t;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void TwoTranslatorsOfTheSameLanguage_AreTwoCards_AndTheSourcesAreListed(string lang)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage(lang);
                var config = new LauncherConfig();
                // Juan's pack is applied; the official card must NOT read "in use".
                config.GetState(ModRegistry.Default.Id)
                    .SetActiveTranslation("ES-LA", "1.2.0e-r5", "bbbb000000000005", "url:juan");

                var index = new TranslationIndex
                {
                    Translations =
                    {
                        Card("gh:gorgorito12/translations", "Gorgorito12/translations", true,
                            Ver("gh:gorgorito12/translations", true, "1.2.0e-r2", "aaaa000000000002", "2026-09-09"),
                            Ver("gh:gorgorito12/translations", true, "1.2.0e-r1", "aaaa000000000001", "2026-07-04")),
                        Card("url:juan", "Traducciones de Juan", false,
                            Ver("url:juan", false, "1.2.0e-r5", "bbbb000000000005", "2026-09-20")),
                    },
                };
                var rows = new List<TranslationSourceRow>
                {
                    new("gh:gorgorito12/translations", "Gorgorito12/translations", "Gorgorito12/translations",
                        TranslationSourceKind.GitHubFolder, true, true, null, 1),
                    new("url:juan", "Traducciones de Juan", "https://example.com/translations-index.json",
                        TranslationSourceKind.Index, false, true, null, 1),
                    new("url:gone", "example.org", "https://example.org/translations-index.json",
                        TranslationSourceKind.Index, false, false, "TxSrcErrNotFound", -1),
                };

                var dlg = Build(config, index, rows);

                // English + one card per translator.
                Assert.Equal(3, dlg.LanguageCardList.Children.Count);
                Assert.Equal(Visibility.Collapsed, dlg.LanguageEmptyHint.Visibility);

                // The active card (Juan's) is first after English, and it names its source.
                var texts = TextsUnder(dlg.LanguageCardList.Children[1]).ToList();
                Assert.Contains(texts, t => t.Contains("Traducciones de Juan"));
                Assert.Contains(texts, t => t.Contains(Strings.Get("LangCardUnofficial")));

                Assert.Equal(Visibility.Visible, dlg.TxSourcesSection.Visibility);
                Assert.Equal(3, dlg.TxSourceRows.Children.Count);
                Assert.True(dlg.TxAddInput.IsEnabled);
                Assert.False(string.IsNullOrWhiteSpace(dlg.TxAddPlaceholder.Text));
                dlg.Close();
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>With translations switched off the box can't add anything, and says why.</summary>
    [Fact]
    public void WithTranslationsDisabled_TheAddBoxIsOff()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var config = new LauncherConfig { CommunityTranslationsDisabled = true };
            var dlg = Build(config, new TranslationIndex(), new List<TranslationSourceRow>());

            Assert.False(dlg.TxAddInput.IsEnabled);
            Assert.False(dlg.TxAddButton.IsEnabled);
            Assert.Single(dlg.TxSourceRows.Children);   // the explanation row
            dlg.Close();
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The add-source confirmation is the only defence against a hostile link, and it works only
    /// if the player SEES the full address and the host — in both languages.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void TheAddSourceConfirmation_ShowsTheFullAddressAndTheHost(string lang)
    {
        var previous = Strings.Language;
        try
        {
            Strings.SetLanguage(lang);
            var body = Strings.Format("DlgAddSourceBody", "https://example.com/aoe3/translations-index.json", "xn--exmple-cua.com");
            Assert.Contains("https://example.com/aoe3/translations-index.json", body);
            Assert.Contains("xn--exmple-cua.com", body);
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get("DlgAddSourceTitle")));
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get("DlgAddSourceConfirm")));
        }
        finally { Strings.SetLanguage(previous); }
    }
}
