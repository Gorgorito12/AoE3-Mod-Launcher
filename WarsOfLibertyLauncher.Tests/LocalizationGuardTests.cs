using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Catches user-visible text that can never be translated, because nothing can reach it.
///
/// <para><b>The bug this exists for.</b> The dashboard's progress strip showed
/// "VELOCIDAD / TIEMPO RESTANTE / PROGRESO" to a launcher set to English. It was not a
/// missing translation: the three captions were written straight into
/// <c>MainWindow.xaml</c> as literals on <c>TextBlock</c>s with <b>no
/// <c>x:Name</c></b>. With no name WPF generates no field, so no code — not
/// <c>ApplyLanguage</c>, not anything — could assign them. They were unreachable, and had
/// been since the strip was built. Nothing throws, nothing fails to build, and the only way
/// to see it is to run the launcher in the other language and look.</para>
///
/// <para><b>Why the rule needs no list of Spanish words.</b> A literal on an unnamed
/// element is unreachable BY CONSTRUCTION, whatever language it happens to be in — so this
/// catches the English mirror of the same mistake just as well. It also cannot fire on a
/// legitimate design-time default: a default is only worth writing on an element that
/// something later overwrites, and overwriting it needs a name. That is why the check is
/// "no name" rather than "looks foreign", which would need a word list that leaks.</para>
///
/// <para><b>What it deliberately does NOT cover.</b> An element that HAS a name but that
/// nothing ever assigns — the lobby chat's "Insert emoji" tooltip was one. Detecting that
/// automatically means cross-referencing every <c>x:Name</c> against every <c>.cs</c> file,
/// and the assignments are made in loops, through helpers like <c>TooltipHelper.Wrap</c>,
/// and from other files, so the false positives would arrive faster than the real ones. A
/// narrow guard people believe is worth more than a wide one they learn to skip.
/// <c>Title="…"</c> is out for the same reason: all sixteen are design-time values that a
/// constructor replaces.</para>
/// </summary>
public class LocalizationGuardTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The attributes that put words on the screen.</summary>
    private static readonly string[] VisibleAttributes = { "Text", "Content", "ToolTip", "Header" };

    /// <summary>
    /// The three literals that are SUPPOSED to be hardcoded, each for a reason that would
    /// still hold in any language.
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        // A language is named in its own language. Translating these would be the opposite
        // bug: an English user hunting for "Spanish" in a list that calls it that.
        "English",
        "Español",
        // The product's name.
        "AoE3 Mod Launcher",
    };

    [Fact]
    public void NoVisibleTextIsStrandedOnAnUnnamedElement()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(RepoFile("."), "*.xaml",
                                                      SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            scanned++;
            var doc = XDocument.Load(file);
            foreach (var el in doc.Descendants())
            {
                if (el.Attribute(Xaml + "Name") != null) continue;   // reachable, so fixable

                foreach (var name in VisibleAttributes)
                {
                    var value = el.Attribute(name)?.Value;
                    if (!IsProse(value)) continue;
                    if (Allowed.Contains(value!.Trim())) continue;
                    offenders.Add(
                        $"{Path.GetFileName(file)}: <{el.Name.LocalName} {name}=\"{value.Trim()}\">");
                }
            }
        }

        // A pass because nothing was read is not a pass — the same guard TextScaleTests
        // puts on its own walk.
        Assert.True(scanned > 20, $"Only {scanned} XAML files were scanned; the walk is wrong.");

        Assert.True(offenders.Count == 0,
            "This text has no x:Name, so no code can reach it and it can never be "
            + "translated — put it in Localization/Strings.cs and assign it from the "
            + "window's ApplyLanguage: " + string.Join(" | ", offenders));
    }

    /// <summary>
    /// Words, as opposed to a Segoe MDL2 glyph, a dash, or a number. Kept generous on
    /// purpose: everything it lets through is something a reader would read.
    /// </summary>
    private static bool IsProse(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length < 3) return false;
        if (v.StartsWith("{", StringComparison.Ordinal)) return false;  // a binding
        // XDocument has already turned "&#xE73E;" into the character itself, so an icon
        // arrives here as one glyph in the Private Use Area rather than as its escape.
        if (v.All(c => char.IsWhiteSpace(c) || char.IsDigit(c) || char.IsPunctuation(c)
                       || char.IsSymbol(c) || IsPrivateUse(c)))
            return false;
        return true;
    }

    private static bool IsPrivateUse(char c) => c >= (char)0xE000 && c <= (char)0xF8FF;

    /// <summary>
    /// Walks up to the launcher project by looking for a file that has to be there, so a
    /// layout change fails loudly instead of quietly skipping the checks.
    /// </summary>
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(Path.Combine(project, "App.xaml")))
                return Path.GetFullPath(Path.Combine(project, relative));
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the WarsOfLibertyLauncher project above " + AppContext.BaseDirectory);
    }
}
