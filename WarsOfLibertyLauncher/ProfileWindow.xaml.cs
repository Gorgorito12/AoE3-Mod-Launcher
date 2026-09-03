using System.Windows;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The player's own profile as a window: standing, record, civilizations, decks and
/// the match history.
///
/// <para>It used to be the PERFIL subtab of MULTIPLAYER. It left because that subtab
/// strip is width-starved by construction — it shares a <c>*</c> + <c>Auto</c> row
/// with the room-tool cluster and neither side trims, so the loser is painted over
/// rather than ellipsised — and because this is the one page there that is about the
/// viewer alone. The door is the account block in the launcher's nav bar, which was
/// already opening a two-item menu whose first entry jumped here.</para>
///
/// <para>Shape and ownership are LobbyWindow's, deliberately: opened with
/// <see cref="Window.Show()"/> from <see cref="Controls.MultiplayerTab"/>, which owns
/// it in a single-instance field, renders into <c>ProfileBody</c> through its own
/// <c>RenderProfileTab</c>, and clears the field on <see cref="Window.Closed"/>. The
/// page is built entirely in that class's code-behind — it is bound to the session,
/// the standing cache, the history rows and the deck caches — so nothing moved across
/// except the eight lines of markup it draws into.</para>
///
/// <para>This class therefore owns exactly two things: its own static strings, and
/// the sign-out forwarder.</para>
/// </summary>
public partial class ProfileWindow : Window
{
    public ProfileWindow()
    {
        InitializeComponent();
        ApplyStrings();
    }

    /// <summary>
    /// The window's own static text. Called from the constructor and again from
    /// MultiplayerTab's <c>ApplyStrings</c> while the window is open, which is the
    /// same arrangement <c>ApplyLobbyStaticLabels</c> uses — the window does not
    /// subscribe to <see cref="Strings.LanguageChanged"/> itself, so there is no
    /// handler to leak when it closes.
    ///
    /// <para>The body is not touched here: every string in it is read fresh by
    /// <c>RenderProfileTab</c>, which MultiplayerTab re-runs on a language change.</para>
    /// </summary>
    internal void ApplyStrings()
    {
        // MpSubtabProfile, freed by the subtab this window replaced.
        var title = Strings.Get("MpSubtabProfile");
        Title = title;
        TitleBarControl.Title = title;
    }
}
