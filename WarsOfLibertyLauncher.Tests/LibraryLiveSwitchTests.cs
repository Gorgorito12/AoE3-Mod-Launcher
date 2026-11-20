using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Two Library faults found by using the running launcher (2026-11-19 audit).</summary>
[Collection("wpf-and-language")]
public class LibraryLiveSwitchTests
{
    /// <summary>Opened on another tab, the Library's list stayed EMPTY when you went to it: the page
    /// came back at the same size it had before it was hidden, which raises no SizeChanged, and the
    /// redraw hung off SizeChanged alone. Switching to the Library now draws a list never drawn.
    /// MainWindow is built by no test, so this reads the source.</summary>
    [Fact]
    public void TheListIsDrawnWhenTheLibraryBecomesVisible()
    {
        var src = File.ReadAllText(RepoFile("MainWindow.xaml.cs"));
        Assert.Matches(@"private void SwitchTopTab\([\s\S]{0,3000}?tab == TopTab\.Play && IsLoaded && !_libraryRailEverDrawn[\s\S]{0,200}?ApplyLibraryLayout", src);
    }

    /// <summary>The status line keeps the text it was set with; after a language switch a Spanish
    /// "Juego cerrado." no longer matched the English template and appeared under the buttons.</summary>
    [Fact]
    public void ARoutineStatusIsRecognisedInEitherLanguage()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage(Strings.LangEn);
                var method = typeof(MainWindow).GetMethod("RoutineStatusTemplates", BindingFlags.NonPublic | BindingFlags.Static)!;
                var templates = (IReadOnlyList<string>)method.Invoke(null, null)!;
                Assert.True(LibraryStatus.IsRoutine(Strings.GetIn(Strings.LangEs, "StatusGameClosed"), templates));
                Assert.True(LibraryStatus.IsRoutine(Strings.GetIn(Strings.LangEn, "StatusGameClosed"), templates));
                Assert.False(LibraryStatus.IsRoutine("Error: something real", templates));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "WarsOfLibertyLauncher", relative)))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new FileNotFoundException(relative), "WarsOfLibertyLauncher", relative);
    }
}
