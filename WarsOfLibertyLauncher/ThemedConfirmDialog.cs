using System.Windows;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher;

/// <summary>How a <see cref="ThemedConfirmDialog"/> introduces itself.</summary>
public enum ConfirmTone
{
    /// <summary>A decision with a cost (a large download), or plain news.</summary>
    Info,
    /// <summary>Something may go wrong if you carry on (low disk space).</summary>
    Caution,
    /// <summary>It cannot be undone: the button is red and Enter answers no.</summary>
    Destructive,
    /// <summary>It went well.</summary>
    Done,
    Question = Info,
    Warning = Caution,
}

/// <summary>Which button of a three-way question was pressed.</summary>
public enum ConfirmAnswer
{
    Cancel,
    Confirm,
    Left,
}

/// <summary>
/// The launcher's own message box (designs 103a to 103c): a question, a notice, or a three-way
/// choice, with the technical detail in Consolas when there is one. Nothing in the launcher opens
/// a Windows <see cref="MessageBox"/> any more, except the two questions in
/// <c>MultiplayerTab</c> that run inside <c>MainWindow.OnClosing</c>.
///
/// <para>Every choice other than the confirm button is a NO: Cancel, ✕ and Escape all leave
/// <c>DialogResult</c> false or null, and <see cref="Ask"/> only returns true for <c>true</c>.
/// That is the MessageBox contract the callers were written against.</para>
/// </summary>
public sealed class ThemedConfirmDialog : BaseDialog
{
    private ConfirmAnswer _answer = ConfirmAnswer.Cancel;

    internal System.Windows.Controls.Button ConfirmButton => PrimaryAction;
    internal System.Windows.Controls.Button CancelButton => CancelAction;

    public ThemedConfirmDialog(string title, string body, string confirmLabel, string cancelLabel,
        ConfirmTone tone = ConfirmTone.Question, string? barTitle = null)
    {
        SetBarTitle(barTitle);
        Title = title;
        SetHeadline(title);
        SetLead(body);
        ConfirmButton.Content = confirmLabel;
        CancelButton.Content = cancelLabel;
        Tone = tone switch
        {
            ConfirmTone.Caution => DialogTone.Caution,
            ConfirmTone.Destructive => DialogTone.Destructive,
            ConfirmTone.Done => DialogTone.Done,
            _ => DialogTone.Info,
        };
    }

    /// <summary>
    /// Shows the question modally and returns true only when the player confirmed. With no usable
    /// owner (none given, or one that is not on screen) it centres on the screen instead.
    /// </summary>
    /// <param name="defaultIsCancel">
    /// True for a question that ARRIVED from outside — a <c>wol-launcher://</c> link any web page
    /// can fire. Enter then answers no, so a stray keypress can never accept what a stranger asked.
    /// </param>
    /// <param name="mod">The mod the question is about: its tint on the bar and its colour on the
    /// button (unless the button is red).</param>
    public static bool Ask(Window? owner, string title, string body, string confirmLabel,
        string cancelLabel, ConfirmTone tone = ConfirmTone.Question, bool defaultIsCancel = false,
        string? detailLabel = null, string? detail = null, ModProfile? mod = null, string? barTitle = null)
    {
        var dialog = new ThemedConfirmDialog(title, body, confirmLabel, cancelLabel, tone, barTitle);
        dialog.WithDetail(detailLabel, detail);
        dialog.UseMod(mod);
        if (defaultIsCancel)
        {
            dialog.ConfirmButton.IsDefault = false;
            dialog.CancelButton.IsDefault = true;
            dialog.Loaded += (_, _) => dialog.CancelButton.Focus();
        }
        return dialog.ShowOver(owner) == true;
    }

    /// <summary>
    /// Three answers: the third button sits on the left, apart from the pair. Dismissing the
    /// window is <see cref="ConfirmAnswer.Cancel"/>.
    /// </summary>
    public static ConfirmAnswer AskThreeWay(Window? owner, string title, string body, string confirmLabel,
        string cancelLabel, string leftLabel, ConfirmTone tone = ConfirmTone.Question, string? barTitle = null)
    {
        var dialog = new ThemedConfirmDialog(title, body, confirmLabel, cancelLabel, tone, barTitle)
        {
            Width = 520,
        };
        dialog.SetLeftAction(leftLabel);
        dialog.ShowOver(owner);
        return dialog._answer;
    }

    /// <summary>Something to read, with one button. There is nothing to answer.</summary>
    public static void Notice(Window? owner, string title, string body,
        ConfirmTone tone = ConfirmTone.Info, string? detailLabel = null, string? detail = null,
        string? okLabel = null, string? barTitle = null)
    {
        var dialog = new ThemedConfirmDialog(title, body, okLabel ?? Strings.Get("DlgUnderstood"), "", tone, barTitle);
        dialog.WithDetail(detailLabel, detail);
        dialog.HideCancel();
        dialog.ShowOver(owner);
    }

    private void WithDetail(string? label, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return;
        Width = 480;
        SetDetail(label ?? Strings.Get("DlgWhatFailed"), detail);
    }

    protected override void OnPrimary()
    {
        _answer = ConfirmAnswer.Confirm;
        Answer(true);
    }

    protected override void OnLeft()
    {
        _answer = ConfirmAnswer.Left;
        Answer(false);
    }
}
