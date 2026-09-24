using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Fills a TextBlock from a localized string in which **this** is drawn stronger and
/// {0} is replaced by a monospace value (a file name, a path). The string table stays
/// the single source of the wording; only the emphasis is marked in it, so a translator
/// moves the bold part with the sentence instead of the code splitting it in three.
/// </summary>
internal static class MarkedText
{
    public static void Set(TextBlock block, string template, string? mono = null)
    {
        block.Inlines.Clear();
        var pieces = template.Split("{0}");
        for (int p = 0; p < pieces.Length; p++)
        {
            AppendBold(block, pieces[p]);
            if (p < pieces.Length - 1 && mono != null)
            {
                block.Inlines.Add(new Run(mono)
                {
                    FontFamily = (FontFamily)block.FindResource("MonoFont"),
                    Foreground = (Brush)block.FindResource("MpCautionTextAlt"),
                });
            }
        }
    }

    private static void AppendBold(TextBlock block, string s)
    {
        var parts = s.Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var run = new Run(parts[i]);
            if (i % 2 == 1)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.Foreground = (Brush)block.FindResource("UiTextStrong");
            }
            block.Inlines.Add(run);
        }
    }
}
