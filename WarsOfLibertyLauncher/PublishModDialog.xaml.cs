using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// v0.9 "Publish my mod" wizard. Six steps guide a modder through every
/// field in the catalog schema (<c>mod.schema.json</c>) and, on the final
/// step, render a ready-to-paste <c>mod.json</c> and a one-click link to
/// the catalog repo's "New file" editor pre-populated with that JSON.
///
/// The form is intentionally simple: a TextBox per field, two ComboBoxes
/// for the enums, and inline error labels that surface only when the
/// schema's regex / length constraints are violated. The dialog never
/// touches the network or the file system on its own — the user copies
/// the JSON or opens GitHub themselves, which keeps the publish flow
/// auditable and lets the catalog repo's CI remain the single source of
/// truth for schema validation.
/// </summary>
public partial class PublishModDialog : Window
{
    public const int StepCount = 6;

    /// <summary>
    /// What a mod gets when nothing is selected. GitHubReleases, which is what
    /// <c>docs/MODDING.md</c> recommends for a new mod; WolPatcher is the legacy pipeline
    /// of one built-in that never passes through this wizard.
    /// </summary>
    private const string DefaultMechanism = "GitHubReleases";

    /// <summary>Catalog repo target for the "Open PR" button.</summary>
    public string CatalogRepo { get; set; } = "Gorgorito12/aoe3-mods-catalog";

    /// <summary>Branch the PR template targets.</summary>
    public string CatalogBranch { get; set; } = "main";

    private int _currentStep = 1;

    /// <summary>
    /// The furthest step reached, which is what the rail paints as done. Going BACK must
    /// not un-tick what you already filled in, so this only ever grows.
    /// </summary>
    private int _furthestStep = 1;

    private readonly StackPanel[] _stepPanels;
    private readonly TextBlock[] _stepTitles;
    private readonly TextBlock[] _stepHints;
    private readonly Border[] _railDiscs;
    private readonly TextBlock[] _railGlyphs;
    private readonly TextBlock[] _railLabels;
    private readonly Border[] _railLines;

    /// <summary>
    /// Every "optional" mark. They are set together because they all say the same word:
    /// it used to live inside each label STRING ("Author (optional)"), which made the
    /// parenthesis a translator's problem and the label longer than the value it names.
    /// </summary>
    private readonly TextBlock[] _optionalMarks;

    /// <summary>Stands in for the id in the path preview and the PR url until one is typed.</summary>
    private const string IdPlaceholder = "your-mod-id";

    /// <summary>Where "Full guide" goes. Ours, so it needs no SafeUrl gate — but it uses one anyway.</summary>
    private const string ModdingGuideUrl =
        "https://github.com/Gorgorito12/AoE3-Mod-Launcher/blob/main/docs/MODDING.md";

    // Schema regexes — kept in sync with mod.schema.json. Compiled once
    // because every validation pass hits them twice (Next-button click).
    private static readonly Regex IdRegex = new("^[a-z][a-z0-9-]{1,30}$", RegexOptions.Compiled);
    private static readonly Regex AccentRegex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex IconRegex = new(@"^[a-zA-Z0-9_-]+\.png$", RegexOptions.Compiled);
    private static readonly Regex BannerRegex = new(@"^[a-zA-Z0-9_-]+\.(png|jpg|jpeg)$", RegexOptions.Compiled);
    private static readonly Regex ExeRegex = new(@"^[a-zA-Z0-9_.-]+\.exe$", RegexOptions.Compiled);
    private static readonly Regex WebsiteRegex = new(@"^https?://", RegexOptions.Compiled);
    private static readonly Regex SourceRepoRegex = new(@"^[a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+$", RegexOptions.Compiled);
    private static readonly Regex Sha256Regex = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    public PublishModDialog()
    {
        InitializeComponent();

        _stepPanels = new[] { Step1Panel, Step2Panel, Step3Panel, Step4Panel, Step5Panel, Step6Panel };
        _stepTitles = new[] { Step1Title, Step2Title, Step3Title, Step4Title, Step5Title, Step6Title };
        _stepHints  = new[] { Step1Hint,  Step2Hint,  Step3Hint,  Step4Hint,  Step5Hint,  Step6Hint  };
        _railDiscs  = new[] { RailDisc1,  RailDisc2,  RailDisc3,  RailDisc4,  RailDisc5,  RailDisc6  };
        _railGlyphs = new[] { RailGlyph1, RailGlyph2, RailGlyph3, RailGlyph4, RailGlyph5, RailGlyph6 };
        _railLabels = new[] { RailLabel1, RailLabel2, RailLabel3, RailLabel4, RailLabel5, RailLabel6 };
        _railLines  = new[] { RailLine1,  RailLine2,  RailLine3,  RailLine4,  RailLine5  };
        _optionalMarks = new[]
        {
            OptAuthor, OptSubtitle, OptAccent, OptIcon, OptBanner, OptArguments,
            OptUserDataFolder, OptInstallProductGuid, OptPayloadUrls, OptWolUpdateInfoUrlAlt,
            OptTranslationsRepo, OptDescriptionEs, OptWebsite, OptLinks,
        };

        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        BackButton.Click += (_, _) => GoTo(_currentStep - 1);
        NextButton.Click += OnNextClicked;

        FieldMechanism.SelectionChanged += (_, _) => RefreshMechanismSubforms();
        FieldId.TextChanged += (_, _) => UpdateIdPath();
        CopyJsonButton.Click += (_, _) => CopyJson();
        NextStepsLink.Click += (_, _) => SafeUrl.TryOpen(ModdingGuideUrl);
        HowItWorksButton.Click += (_, _) => ToggleFold(IntroPanel);
        Step3AdvancedToggle.Click += (_, _) => ToggleFold(Step3AdvancedBlock);
        GhAdvancedToggle.Click += (_, _) => ToggleFold(GhAdvancedBlock);
        // The marker is the answer to the question above it, so the field follows the tick.
        MarkerNeededCheck.Click += (_, _) => MarkerBlock.Visibility =
            MarkerNeededCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        ApplyDefaultLabels();
        FieldInstallType.SelectedIndex = 0;
        // GitHubReleases, which is FIRST in the list now. The legacy WoL patcher used to be
        // both first and selected while the hint underneath called GitHubReleases the
        // recommendation for a new mod.
        FieldMechanism.SelectedIndex = 0;
        UpdateIdPath();
        GoTo(1);
    }

    // ------------------------------------------------------------------------
    // Public labels — overridable so MainWindow can push the launcher's
    // current language into every visible string before showing the dialog.
    // ------------------------------------------------------------------------

    public string HeaderTitleText { get => TitleBarControl.Title; set => TitleBarControl.Title = value; }
    public string CancelLabel { get => (string)(CancelButton.Content ?? ""); set => CancelButton.Content = value; }
    public string BackLabel { get => (string)(BackButton.Content ?? ""); set => BackButton.Content = value; }
    public string NextLabel { get; set; } = "Next";
    public string StepIndicatorFormat { get; set; } = "Step {0} of {1}";

    // Per-field label setters. Keeping each one explicit (instead of a
    // dictionary) makes the call site at MainWindow read like a checklist
    // of every visible string — easier to audit when adding a translation.
    public string LblIdText { get => LblId.Text; set => LblId.Text = value; }
    public string HintIdText { get => HintId.Text; set => HintId.Text = value; }
    public string LblDisplayNameText { get => LblDisplayName.Text; set => LblDisplayName.Text = value; }
    public string LblAuthorText { get => LblAuthor.Text; set => LblAuthor.Text = value; }
    public string LblSubtitleText { get => LblSubtitle.Text; set => LblSubtitle.Text = value; }
    public string LblAccentText { get => LblAccent.Text; set => LblAccent.Text = value; }
    public string HintAccentText { get => HintAccent.Text; set => HintAccent.Text = value; }
    public string LblIconText { get => LblIcon.Text; set => LblIcon.Text = value; }
    public string HintIconText { get => HintIcon.Text; set => HintIcon.Text = value; }
    public string LblBannerText { get => LblBanner.Text; set => LblBanner.Text = value; }
    public string HintBannerText { get => HintBanner.Text; set => HintBanner.Text = value; }
    public string HintInstallTypeText { get => HintInstallType.Text; set => HintInstallType.Text = value; }
    // Plain-language install options (Content localized by the opener). The Tag on
    // each item (set in XAML) is what maps to install.type / privateSetupPath.
    public string InstallOptUhcText { set => InstallOptUhc.Content = value; }
    public string InstallOptAdditiveText { set => InstallOptAdditive.Content = value; }
    public string InstallOptReplaceText { set => InstallOptReplace.Content = value; }
    public string LblDefaultFolderText { get => LblDefaultFolder.Text; set => LblDefaultFolder.Text = value; }
    public string LblProbeFileText { get => LblProbeFile.Text; set => LblProbeFile.Text = value; }
    public string LblExecutableText { get => LblExecutable.Text; set => LblExecutable.Text = value; }
    public string LblArgumentsText { get => LblArguments.Text; set => LblArguments.Text = value; }
    public string LblMechanismText { get => LblMechanism.Text; set => LblMechanism.Text = value; }
    public string LblWolUpdateInfoUrlText { get => LblWolUpdateInfoUrl.Text; set => LblWolUpdateInfoUrl.Text = value; }
    public string LblSourceRepoText { get => LblSourceRepo.Text; set => LblSourceRepo.Text = value; }
    public string HintSourceRepoText { get => HintSourceRepo.Text; set => HintSourceRepo.Text = value; }
    public string LblApprovedTagText { get => LblApprovedTag.Text; set => LblApprovedTag.Text = value; }
    public string LblDescriptionEnText { get => LblDescriptionEn.Text; set => LblDescriptionEn.Text = value; }
    public string LblDescriptionEsText { get => LblDescriptionEs.Text; set => LblDescriptionEs.Text = value; }
    public string LblWebsiteText { get => LblWebsite.Text; set => LblWebsite.Text = value; }
    public string CopyJsonLabel { get => (string)(CopyJsonButton.Content ?? ""); set => CopyJsonButton.Content = value; }

    /// <summary>
    /// The footer's caption on the review step. There is no separate Finish button any
    /// more: opening the PR IS finishing, and a second solid button beside it was the
    /// fourth competing action on that screen.
    /// </summary>
    public string OpenPrLabel { get; set; } = "Open PR on GitHub";

    // Per-field example hints added in the guidance pass. Each one sits
    // under its field and shows a concrete example value so the modder
    // never has to guess the expected format.
    public string HintDisplayNameText { get => HintDisplayName.Text; set => HintDisplayName.Text = value; }
    public string HintAuthorText { get => HintAuthor.Text; set => HintAuthor.Text = value; }
    public string HintSubtitleText { get => HintSubtitle.Text; set => HintSubtitle.Text = value; }
    public string HintDefaultFolderText { get => HintDefaultFolder.Text; set => HintDefaultFolder.Text = value; }
    public string HintProbeFileText { get => HintProbeFile.Text; set => HintProbeFile.Text = value; }
    public string HintExecutableText { get => HintExecutable.Text; set => HintExecutable.Text = value; }
    public string HintArgumentsText { get => HintArguments.Text; set => HintArguments.Text = value; }
    public string HintMechanismText { get => HintMechanism.Text; set => HintMechanism.Text = value; }
    public string HintWolUpdateInfoUrlText { get => HintWolUpdateInfoUrl.Text; set => HintWolUpdateInfoUrl.Text = value; }
    public string HintApprovedTagText { get => HintApprovedTag.Text; set => HintApprovedTag.Text = value; }
    public string HintDescriptionText { get => HintDescription.Text; set => HintDescription.Text = value; }
    public string HintWebsiteText { get => HintWebsite.Text; set => HintWebsite.Text = value; }
    public string LblLinksText { get => LblLinks.Text; set => LblLinks.Text = value; }
    public string HintLinksText { get => HintLinks.Text; set => HintLinks.Text = value; }

    // Guidance copy — the wizard intro, the "filenames aren't files" reminder
    // on the look & feel step, and the post-publish flow on the review step.
    public string IntroBodyText { get => IntroText.Text; set => IntroText.Text = value; }
    public string ImagesUploadNoteText { get => ImagesUploadNote.Text; set => ImagesUploadNote.Text = value; }
    public string NextStepsTitleText { get => NextStepsTitle.Text; set => NextStepsTitle.Text = value; }

    /// <summary>Localised error strings — overridable per language.</summary>
    public string ErrorIdInvalid { get; set; } = "Invalid id. Use lowercase letters, digits and dashes (max 31 chars, starts with a letter).";
    public string ErrorDisplayNameRequired { get; set; } = "Display name is required (1–50 characters).";
    public string ErrorAccentInvalid { get; set; } = "Accent colour must be a six-digit hex string like #c8102e.";
    public string ErrorIconInvalid { get; set; } = "Icon filename must end with .png and contain only letters, digits, dashes or underscores.";
    public string ErrorBannerInvalid { get; set; } = "Banner filename must end with .png/.jpg/.jpeg and contain only safe characters.";
    public string ErrorExecutableInvalid { get; set; } = "Executable must be a filename ending in .exe (e.g. age3y.exe).";
    public string ErrorWebsiteInvalid { get; set; } = "Website must start with http:// or https://.";

    public void SetStepTitle(int step, string title)
    {
        if (step < 1 || step > StepCount) return;
        _stepTitles[step - 1].Text = title;
    }
    public void SetStepHint(int step, string hint)
    {
        if (step < 1 || step > StepCount) return;
        _stepHints[step - 1].Text = hint;
    }

    // ------------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------------

    public int CurrentStep => _currentStep;

    /// <summary>
    /// Jumps to <paramref name="step"/>. Skips validation — that runs in
    /// <see cref="OnNextClicked"/> so the user can move backwards even
    /// when the current step has half-filled fields. The last step is the
    /// review page; landing on it (re)generates the JSON preview.
    /// </summary>
    public void GoTo(int step)
    {
        if (step < 1) step = 1;
        if (step > StepCount)
        {
            DialogResult = true;
            Close();
            return;
        }

        _currentStep = step;
        if (step > _furthestStep) _furthestStep = step;
        for (int i = 0; i < _stepPanels.Length; i++)
            _stepPanels[i].Visibility = (i == step - 1) ? Visibility.Visible : Visibility.Collapsed;

        HeaderStep.Text = string.Format(StepIndicatorFormat, step, StepCount);
        RefreshRail();

        // Step 1 has nothing behind it, so Back is not DRAWN. It used to be painted and
        // merely disabled, which is a button announcing a previous step and then refusing.
        BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = step == StepCount ? OpenPrLabel : NextLabel;

        if (step == 4) RefreshMechanismSubforms();
        if (step == 6)
        {
            JsonPreview.Text = GenerateJson();
            JsonPathText.Text = $"mods/{IdOrPlaceholder()}/mod.json";
            RefreshMissing();
        }

        // Each step starts at its own top. The reported symptom of the overflowing step 3
        // was its first label "cut off at the top", which is a leftover scroll offset
        // carried in from the step before.
        ContentScroller.ScrollToTop();
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        if (!ValidateCurrentStep()) return;
        if (_currentStep == StepCount)
        {
            OpenCatalogPr();
            DialogResult = true;
            Close();
            return;
        }
        GoTo(_currentStep + 1);
    }

    /// <summary>
    /// Paints the six-node rail: blue for where you are, a green tick for what is behind
    /// you, an empty ring for what is left. "Step 1 of 6" in the caption said where you
    /// were and nothing about what was still to come.
    /// </summary>
    private void RefreshRail()
    {
        bool Done(int step) => step < _furthestStep && step != _currentStep;

        var action = BrushOf("MpAction");
        var okFill = BrushOf("MpOkBg");
        var ok = BrushOf("MpOk");
        var ring = BrushOf("MpRimMedium");
        var bright = BrushOf("FgBright");
        var ghost = BrushOf("UiTextGhost");
        var heading = BrushOf("MpTextHeading");
        var muted = BrushOf("MpTextMuted");
        var seam = BrushOf("UiRimSeam");
        var seamLit = BrushOf("MpRimStrong");

        for (int i = 0; i < _railDiscs.Length; i++)
        {
            int step = i + 1;
            bool current = step == _currentStep;
            bool done = Done(step);

            _railDiscs[i].Background = current ? action : done ? okFill : Brushes.Transparent;
            _railDiscs[i].BorderBrush = current ? action : done ? okFill : ring;
            _railGlyphs[i].Text = done ? "\u2713" : step.ToString(CultureInfo.InvariantCulture);
            _railGlyphs[i].Foreground = current ? bright : done ? ok : ghost;
            _railLabels[i].Foreground = current ? heading : done ? muted : ghost;
            _railLabels[i].FontWeight = current ? FontWeights.SemiBold : FontWeights.Medium;
        }

        // A gap lights up only once BOTH of its ends are behind you, which is what makes
        // the finished run read as one line instead of six unrelated ticks.
        for (int i = 0; i < _railLines.Length; i++)
            _railLines[i].Background = Done(i + 1) && Done(i + 2) ? seamLit : seam;
    }

    private Brush BrushOf(string key) => (Brush)FindResource(key);

    /// <summary>Opens or closes one of the quiet fold-outs (the intro, the two "advanced" blocks).</summary>
    private static void ToggleFold(FrameworkElement block) =>
        block.Visibility = block.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private string IdOrPlaceholder()
    {
        var id = FieldId.Text.Trim();
        return string.IsNullOrEmpty(id) ? IdPlaceholder : id;
    }

    /// <summary>
    /// The live "Will create mods/&lt;id&gt;/mod.json" under the id field. It answers "is
    /// this the folder name?" without spending a sentence on it, and shows where the PR
    /// is going to land.
    /// </summary>
    private void UpdateIdPath() => IdPathValue.Text = $"mods/{IdOrPlaceholder()}/mod.json";

    /// <summary>
    /// What manual review will ask for, listed BEFORE the JSON instead of arriving as a
    /// rejected PR days later. It never blocks — publishing a minimal mod is legitimate.
    ///
    /// <para>Deliberately NOT derived from <c>mod.schema.json</c>, which the handoff asked
    /// for: that schema requires exactly <c>id</c>, <c>displayName</c>, <c>install.type</c>
    /// and <c>update.mechanism</c> and carries no conditionals at all, so a derived list
    /// would be EMPTY for precisely the four-field manifest this exists to catch. These are
    /// what make a mod installable and recognisable, which is a review question rather than
    /// a schema one.</para>
    /// </summary>
    private void RefreshMissing()
    {
        var missing = new List<(int Step, string Label, bool Required)>();
        if (string.IsNullOrWhiteSpace(FieldProbeFile.Text))
            missing.Add((3, Strings.Get("PublishMissingProbe"), true));
        if (string.IsNullOrWhiteSpace(FieldExecutable.Text))
            missing.Add((3, Strings.Get("PublishMissingExecutable"), true));
        if (string.IsNullOrWhiteSpace(FieldDescriptionEn.Text)
            && string.IsNullOrWhiteSpace(FieldDescriptionEs.Text))
            missing.Add((5, Strings.Get("PublishMissingDescription"), true));
        if (string.IsNullOrWhiteSpace(FieldIcon.Text))
            missing.Add((2, Strings.Get("PublishMissingIcon"), false));

        MissingPills.Children.Clear();
        if (missing.Count == 0)
        {
            MissingCard.Visibility = Visibility.Collapsed;
            return;
        }

        MissingCard.Visibility = Visibility.Visible;
        MissingTitle.Text = missing.Count == 1
            ? Strings.Get("PublishMissingTitleOne")
            : Strings.Format("PublishMissingTitleMany", missing.Count);
        MissingFootnote.Text = Strings.Get("PublishMissingFootnote");
        foreach (var m in missing)
            MissingPills.Children.Add(BuildMissingPill(m.Step, m.Label, m.Required));
    }

    /// <summary>
    /// One gap, saying which step to go back to. Amber for what review will actually ask
    /// for; a neutral rim for what merely helps (the icon), so the two are told apart at a
    /// glance instead of four identical alarms.
    /// </summary>
    private Border BuildMissingPill(int step, string label, bool required)
    {
        var stepText = new TextBlock
        {
            Text = Strings.Format("PublishMissingStepFormat", step),
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = (double)FindResource("SetGroupLabelSize"),
            FontWeight = FontWeights.Medium,
            Foreground = required ? BrushOf("MpCautionTextAlt") : BrushOf("UiTextDim"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = (double)FindResource("SetMonoSize"),
            FontFamily = (FontFamily)FindResource("BodyFont"),
            FontWeight = FontWeights.Medium,
            Foreground = required ? BrushOf("MpCautionText") : BrushOf("MpTextMuted"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(stepText);
        row.Children.Add(labelText);

        var pill = new Border
        {
            Style = (Style)FindResource("WzMissingPill"),
            Child = row,
        };
        if (!required)
        {
            pill.Background = BrushOf("MpActivityOwnRow");
            pill.BorderBrush = BrushOf("MpRimField");
        }
        return pill;
    }

    // ------------------------------------------------------------------------
    // Validation
    // ------------------------------------------------------------------------

    /// <summary>
    /// Validates the fields on the current step against the catalog
    /// schema's regex / length constraints. Inline error labels are
    /// toggled per-field; returning true unblocks GoTo.
    /// </summary>
    public bool ValidateCurrentStep() => _currentStep switch
    {
        1 => ValidateStep1(),
        2 => ValidateStep2(),
        3 => ValidateStep3(),
        4 => ValidateStep4(),
        5 => ValidateStep5(),
        6 => true, // Review step — Finish closes the dialog.
        _ => true,
    };

    private bool ValidateStep1()
    {
        bool ok = true;
        string id = FieldId.Text.Trim();
        if (string.IsNullOrEmpty(id) || !IdRegex.IsMatch(id))
        {
            ErrorId.Text = ErrorIdInvalid;
            ErrorId.Visibility = Visibility.Visible;
            ok = false;
        }
        else
        {
            ErrorId.Visibility = Visibility.Collapsed;
        }

        string name = FieldDisplayName.Text.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 50)
        {
            ErrorDisplayName.Text = ErrorDisplayNameRequired;
            ErrorDisplayName.Visibility = Visibility.Visible;
            ok = false;
        }
        else
        {
            ErrorDisplayName.Visibility = Visibility.Collapsed;
        }

        return ok;
    }

    private bool ValidateStep2()
    {
        bool ok = true;
        string accent = FieldAccent.Text.Trim();
        if (!string.IsNullOrEmpty(accent) && !AccentRegex.IsMatch(accent))
        {
            ErrorAccent.Text = ErrorAccentInvalid;
            ErrorAccent.Visibility = Visibility.Visible;
            ok = false;
        }
        else { ErrorAccent.Visibility = Visibility.Collapsed; }

        string icon = FieldIcon.Text.Trim();
        if (!string.IsNullOrEmpty(icon) && !IconRegex.IsMatch(icon))
        {
            ErrorIcon.Text = ErrorIconInvalid;
            ErrorIcon.Visibility = Visibility.Visible;
            ok = false;
        }
        else { ErrorIcon.Visibility = Visibility.Collapsed; }

        string banner = FieldBanner.Text.Trim();
        if (!string.IsNullOrEmpty(banner) && !BannerRegex.IsMatch(banner))
        {
            ErrorBanner.Text = ErrorBannerInvalid;
            ErrorBanner.Visibility = Visibility.Visible;
            ok = false;
        }
        else { ErrorBanner.Visibility = Visibility.Collapsed; }

        return ok;
    }

    private bool ValidateStep3()
    {
        bool ok = true;
        string exe = FieldExecutable.Text.Trim();
        if (!string.IsNullOrEmpty(exe) && !ExeRegex.IsMatch(exe))
        {
            ErrorExecutable.Text = ErrorExecutableInvalid;
            ErrorExecutable.Visibility = Visibility.Visible;
            ok = false;
        }
        else { ErrorExecutable.Visibility = Visibility.Collapsed; }

        ok &= ValidateSha256Field(FieldPayloadSha256.Text, ErrorPayloadSha256);
        return ok;
    }

    private bool ValidateStep4()
    {
        bool ok = true;
        string mech = SelectedTag(FieldMechanism) ?? DefaultMechanism;

        if (mech == "GitHubReleases")
        {
            string repo = FieldSourceRepo.Text.Trim();
            if (!string.IsNullOrEmpty(repo) && !SourceRepoRegex.IsMatch(repo))
            {
                ErrorSourceRepo.Text = Strings.Get("PublishErrorSourceRepo");
                ErrorSourceRepo.Visibility = Visibility.Visible;
                ok = false;
            }
            else { ErrorSourceRepo.Visibility = Visibility.Collapsed; }

            // dependentRequired: an external URL template needs its SHA-256.
            string tmpl = FieldGhExternalUrl.Text.Trim();
            string sha = FieldGhExternalSha.Text.Trim();
            string? shaError = null;
            if (!string.IsNullOrEmpty(tmpl) && string.IsNullOrEmpty(sha))
                shaError = "PublishErrorGhShaRequired";
            else if (!string.IsNullOrEmpty(sha) && !Sha256Regex.IsMatch(sha))
                shaError = "PublishErrorSha256";
            if (shaError != null)
            {
                ErrorGhExternalSha.Text = Strings.Get(shaError);
                ErrorGhExternalSha.Visibility = Visibility.Visible;
                ok = false;
            }
            else { ErrorGhExternalSha.Visibility = Visibility.Collapsed; }
        }
        else if (mech == "WolPatcher")
        {
            ok &= ValidateSha256Field(FieldWolPayloadSha256.Text, ErrorWolPayloadSha256);
        }

        return ok;
    }

    /// <summary>Every non-empty line of a multi-line SHA field must be 64-hex.</summary>
    private bool ValidateSha256Field(string text, TextBlock error)
    {
        bool allValid = SplitLines(text).TrueForAll(line => Sha256Regex.IsMatch(line));
        if (!allValid)
        {
            error.Text = Strings.Get("PublishErrorSha256");
            error.Visibility = Visibility.Visible;
            return false;
        }
        error.Visibility = Visibility.Collapsed;
        return true;
    }

    private bool ValidateStep5()
    {
        bool ok = true;
        string url = FieldWebsite.Text.Trim();
        if (!string.IsNullOrEmpty(url) && !WebsiteRegex.IsMatch(url))
        {
            ErrorWebsite.Text = ErrorWebsiteInvalid;
            ErrorWebsite.Visibility = Visibility.Visible;
            ok = false;
        }
        else { ErrorWebsite.Visibility = Visibility.Collapsed; }
        return ok;
    }

    private void RefreshMechanismSubforms()
    {
        string mech = SelectedTag(FieldMechanism) ?? DefaultMechanism;
        MechanismWolPanel.Visibility = mech == "WolPatcher" ? Visibility.Visible : Visibility.Collapsed;
        MechanismGitHubPanel.Visibility = mech == "GitHubReleases" ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? SelectedTag(ComboBox cb)
    {
        if (cb.SelectedItem is ComboBoxItem item) return item.Tag as string;
        return null;
    }

    // ------------------------------------------------------------------------
    // JSON generation
    // ------------------------------------------------------------------------

    /// <summary>
    /// Builds the <c>mod.json</c> contents from the current form state.
    /// Empty optional fields are omitted so the resulting JSON contains
    /// only what the modder actually filled in — keeps diffs tidy in
    /// catalog PRs and dodges the schema's <c>additionalProperties:false</c>
    /// branches for empty sub-objects.
    /// </summary>
    /// <summary>
    /// Flat, WPF-free snapshot of the wizard form. Built by
    /// <see cref="ReadFormInput"/> from the controls and consumed by the pure
    /// <see cref="BuildModJson"/> — so the field→JSON mapping (including the
    /// load-bearing nesting) is unit-testable without standing up the UI.
    /// Multi-value fields (payload URLs/hashes, covered files) are lists; the
    /// wizard collects them as one-per-line text and splits before filling this.
    /// </summary>
    public sealed record ModJsonInput
    {
        // Identity
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string? Subtitle { get; init; }
        public string? Author { get; init; }
        // Look & feel
        public string? AccentColor { get; init; }
        public string? Icon { get; init; }
        public string? Banner { get; init; }
        public string? OfficialWebsite { get; init; }
        /// <summary>Raw "type|url" lines from the community-links field.</summary>
        public IReadOnlyList<string>? LinkLines { get; init; }
        public string? DescriptionEn { get; init; }
        public string? DescriptionEs { get; init; }
        // install.*  (nested under "install")
        public string InstallType { get; init; } = "IsolatedFolder";
        /// <summary>
        /// When true, emits <c>install.privateSetupPath: true</c> — for a total
        /// conversion that ships the STOCK <c>age3y.exe</c> (no UHC) and replaces
        /// base game data, so the launcher points the player's own copy of the exe
        /// at a registry key of the mod's own at install time. Only meaningful with
        /// <c>InstallType = "IsolatedFolder"</c>. See MODDING.md §4.
        /// </summary>
        public bool PrivateSetupPath { get; init; }
        public string? DefaultFolder { get; init; }
        public string? ProbeFile { get; init; }
        public string? Marker { get; init; }
        public string? Executable { get; init; }
        public string? Arguments { get; init; }
        public IReadOnlyList<string>? PayloadUrls { get; init; }
        public IReadOnlyList<string>? PayloadSha256 { get; init; }
        // TOP-LEVEL extras (NOT under install)
        public string? UserDataFolder { get; init; }
        public string? InstallProductGuid { get; init; }
        public string? TranslationsRepo { get; init; }
        public IReadOnlyList<string>? TranslationsCoveredFiles { get; init; }
        // update.*
        public string Mechanism { get; init; } = "WolPatcher";
        public string? SourceRepo { get; init; }          // top-level
        public string? ApprovedReleaseTag { get; init; }  // top-level
        public string? GithubExternalAssetUrlTemplate { get; init; }  // update.github
        public string? GithubExternalAssetSha256 { get; init; }       // update.github
        public bool GithubDeltaPatches { get; init; }                 // update.github
        public string? WolUpdateInfoUrl { get; init; }     // update.wol
        public string? WolUpdateInfoUrlAlt { get; init; }  // update.wol
        public IReadOnlyList<string>? WolPayloadZipUrls { get; init; } // update.wol
        public IReadOnlyList<string>? WolPayloadSha256 { get; init; }  // update.wol
        // Catalog target (for the $schema URL)
        public string CatalogRepo { get; init; } = "Gorgorito12/aoe3-mods-catalog";
        public string CatalogBranch { get; init; } = "main";
    }

    /// <summary>
    /// The Step-3 install-type combo carries a compound <c>Tag</c> that encodes
    /// the install type AND, via a <c>+privateSetupPath</c> suffix, whether the
    /// mod needs its own registry key — so a modder answers ONE plain-language
    /// question ("how does your mod run?") instead of two technical fields.
    /// e.g. <c>"IsolatedFolder+privateSetupPath"</c> → IsolatedFolder + private key.
    /// </summary>
    private static string InstallTypeFromTag(string? tag)
        => (string.IsNullOrWhiteSpace(tag) ? "IsolatedFolder" : tag).Split('+')[0];

    private static bool PrivateSetupPathFromTag(string? tag)
        => (tag ?? "").Contains("+privateSetupPath", System.StringComparison.OrdinalIgnoreCase);

    public string GenerateJson() => BuildModJson(ReadFormInput());

    /// <summary>Reads the live WPF controls into a flat <see cref="ModJsonInput"/>.</summary>
    private ModJsonInput ReadFormInput() => new()
    {
        Id = FieldId.Text,
        DisplayName = FieldDisplayName.Text,
        Subtitle = FieldSubtitle.Text,
        Author = FieldAuthor.Text,
        AccentColor = FieldAccent.Text,
        Icon = FieldIcon.Text,
        Banner = FieldBanner.Text,
        OfficialWebsite = FieldWebsite.Text,
        LinkLines = SplitLines(FieldLinks.Text),
        DescriptionEn = FieldDescriptionEn.Text,
        DescriptionEs = FieldDescriptionEs.Text,
        InstallType = InstallTypeFromTag(SelectedTag(FieldInstallType)),
        PrivateSetupPath = PrivateSetupPathFromTag(SelectedTag(FieldInstallType)),
        DefaultFolder = FieldDefaultFolder.Text,
        ProbeFile = FieldProbeFile.Text,
        // Only when the question above the field was answered yes. The field keeps whatever
        // was typed if the box is un-ticked, so an accidental click costs nothing.
        Marker = MarkerNeededCheck.IsChecked == true ? FieldMarker.Text : null,
        Executable = FieldExecutable.Text,
        Arguments = FieldArguments.Text,
        PayloadUrls = SplitLines(FieldPayloadUrls.Text),
        PayloadSha256 = SplitLines(FieldPayloadSha256.Text),
        UserDataFolder = FieldUserDataFolder.Text,
        InstallProductGuid = FieldInstallProductGuid.Text,
        TranslationsRepo = FieldTranslationsRepo.Text,
        TranslationsCoveredFiles = SplitLines(FieldTranslationsCovered.Text),
        Mechanism = SelectedTag(FieldMechanism) ?? DefaultMechanism,
        SourceRepo = FieldSourceRepo.Text,
        ApprovedReleaseTag = FieldApprovedTag.Text,
        GithubExternalAssetUrlTemplate = FieldGhExternalUrl.Text,
        GithubExternalAssetSha256 = FieldGhExternalSha.Text,
        GithubDeltaPatches = FieldDeltaPatches.IsChecked == true,
        WolUpdateInfoUrl = FieldWolUpdateInfoUrl.Text,
        WolUpdateInfoUrlAlt = FieldWolUpdateInfoUrlAlt.Text,
        WolPayloadZipUrls = SplitLines(FieldWolPayloadZipUrls.Text),
        WolPayloadSha256 = SplitLines(FieldWolPayloadSha256.Text),
        CatalogRepo = CatalogRepo,
        CatalogBranch = CatalogBranch,
    };

    /// <summary>
    /// Pure builder: maps a <see cref="ModJsonInput"/> to the catalog JSON.
    /// Empty optional fields are omitted (keeps PR diffs tidy and dodges the
    /// schema's <c>additionalProperties:false</c> on empty sub-objects). The
    /// nesting here is load-bearing — see the schema: userDataFolder /
    /// installProductGuid / translations are TOP-LEVEL; marker / payload* are
    /// under install; github.* / wol.* are under update.
    /// </summary>
    internal static string BuildModJson(ModJsonInput input)
    {
        var doc = new Dictionary<string, object?>
        {
            ["$schema"] = $"https://raw.githubusercontent.com/{input.CatalogRepo}/{input.CatalogBranch}/schema/mod.schema.json",
            ["id"] = input.Id.Trim(),
            ["displayName"] = input.DisplayName.Trim(),
        };

        AddIfPresent(doc, "subtitle", input.Subtitle);
        AddIfPresent(doc, "author", input.Author);
        AddIfPresent(doc, "accentColor", input.AccentColor);
        AddIfPresent(doc, "icon", input.Icon);
        AddIfPresent(doc, "banner", input.Banner);
        AddIfPresent(doc, "officialWebsite", input.OfficialWebsite);

        var links = ParseLinkLines(input.LinkLines);
        if (links.Count > 0) doc["links"] = links;

        var descriptions = new Dictionary<string, string>();
        AddDescription(descriptions, "en", input.DescriptionEn);
        AddDescription(descriptions, "es", input.DescriptionEs);
        if (descriptions.Count > 0) doc["description"] = descriptions;

        // TOP-LEVEL extras (schema places these at the root, NOT under install).
        AddIfPresent(doc, "userDataFolder", input.UserDataFolder);
        AddIfPresent(doc, "installProductGuid", input.InstallProductGuid);

        // install.* — required by the schema; always emitted.
        var install = new Dictionary<string, object?>
        {
            ["type"] = string.IsNullOrWhiteSpace(input.InstallType) ? "IsolatedFolder" : input.InstallType.Trim(),
        };
        AddIfPresent(install, "defaultFolder", input.DefaultFolder);
        AddIfPresent(install, "probeFile", input.ProbeFile);
        AddIfPresent(install, "marker", input.Marker);
        AddIfPresent(install, "executable", input.Executable);
        AddIfPresent(install, "arguments", input.Arguments);
        // Emitted only when true (JSON stays clean, like every other optional flag).
        // Marks a stock-exe replacement TC that needs its own registry key (§4).
        if (input.PrivateSetupPath) install["privateSetupPath"] = true;
        AddArrayIfPresent(install, "payloadUrls", input.PayloadUrls);
        AddArrayIfPresent(install, "payloadSha256", input.PayloadSha256);
        doc["install"] = install;

        // update.* — required by the schema.
        string mech = string.IsNullOrWhiteSpace(input.Mechanism) ? "WolPatcher" : input.Mechanism.Trim();
        var update = new Dictionary<string, object?> { ["mechanism"] = mech };
        if (mech == "WolPatcher")
        {
            var wol = new Dictionary<string, object?>();
            AddIfPresent(wol, "updateInfoUrl", input.WolUpdateInfoUrl);
            AddIfPresent(wol, "updateInfoUrlAlt", input.WolUpdateInfoUrlAlt);
            AddArrayIfPresent(wol, "payloadZipUrls", input.WolPayloadZipUrls);
            AddArrayIfPresent(wol, "payloadSha256", input.WolPayloadSha256);
            if (wol.Count > 0) update["wol"] = wol;
        }
        else if (mech == "GitHubReleases")
        {
            var gh = new Dictionary<string, object?>();
            AddIfPresent(gh, "externalAssetUrlTemplate", input.GithubExternalAssetUrlTemplate);
            AddIfPresent(gh, "externalAssetSha256", input.GithubExternalAssetSha256);
            // Opt-in delta patches. Only meaningful for GitHub-hosted payloads (external-hosted
            // mods always use the full path), so only emit it when NOT external-hosted.
            if (input.GithubDeltaPatches && string.IsNullOrWhiteSpace(input.GithubExternalAssetUrlTemplate))
                gh["deltaPatches"] = true;
            if (gh.Count > 0) update["github"] = gh;
        }
        doc["update"] = update;

        // Source repo + approved tag live at TOP level, not under update.
        if (mech == "GitHubReleases")
        {
            AddIfPresent(doc, "sourceRepo", input.SourceRepo);
            AddIfPresent(doc, "approvedReleaseTag", input.ApprovedReleaseTag);
        }

        // translations — TOP-LEVEL object.
        var translations = new Dictionary<string, object?>();
        AddIfPresent(translations, "repo", input.TranslationsRepo);
        AddArrayIfPresent(translations, "coveredFiles", input.TranslationsCoveredFiles);
        if (translations.Count > 0) doc["translations"] = translations;

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static void AddIfPresent(IDictionary<string, object?> doc, string key, string? value)
    {
        var trimmed = value?.Trim() ?? "";
        if (!string.IsNullOrEmpty(trimmed)) doc[key] = trimmed;
    }

    private static void AddArrayIfPresent(IDictionary<string, object?> doc, string key, IReadOnlyList<string>? values)
    {
        if (values == null) return;
        var cleaned = values.Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).ToArray();
        if (cleaned.Length > 0) doc[key] = cleaned;
    }

    /// <summary>
    /// Turns the community-links field's <c>type|url</c> lines into the schema's
    /// <c>links</c> array. A line with no pipe is read as a bare url and typed
    /// <c>other</c>, so a modder who just pastes links still gets valid JSON.
    ///
    /// Silently drops what the schema would reject anyway (non-HTTPS, empties)
    /// and caps at <see cref="ModLink.MaxLinks"/>: this wizard exists to produce
    /// a manifest that passes catalog CI on the first try, so emitting something
    /// known-invalid would only hand the modder a red PR.
    /// </summary>
    internal static List<Dictionary<string, string>> ParseLinkLines(IReadOnlyList<string>? lines)
    {
        var result = new List<Dictionary<string, string>>();
        if (lines == null) return result;

        foreach (var line in lines)
        {
            if (result.Count >= ModLink.MaxLinks) break;

            var raw = (line ?? "").Trim();
            if (raw.Length == 0) continue;

            string type = "other", url = raw;
            var pipe = raw.IndexOf('|');
            if (pipe >= 0)
            {
                type = raw[..pipe].Trim().ToLowerInvariant();
                url = raw[(pipe + 1)..].Trim();
            }

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            // Normalise an unrecognised type rather than emitting one the schema's
            // enum would reject.
            if (ModLink.ParseType(type) == ModLinkType.Other) type = "other";

            result.Add(new Dictionary<string, string> { ["type"] = type, ["url"] = url });
        }
        return result;
    }

    /// <summary>Splits one-per-line textbox text into a trimmed, non-empty list.</summary>
    internal static List<string> SplitLines(string? text) =>
        (text ?? "")
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private static void AddDescription(IDictionary<string, string> doc, string lang, string? value)
    {
        var trimmed = value?.Trim() ?? "";
        if (!string.IsNullOrEmpty(trimmed)) doc[lang] = trimmed;
    }

    // ------------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------------

    private void CopyJson()
    {
        try
        {
            Clipboard.SetText(JsonPreview.Text);
        }
        catch
        {
            // Clipboard.SetText occasionally throws on a busy clipboard;
            // not load-bearing — the user can select all + Ctrl+C.
        }
    }

    /// <summary>
    /// Opens GitHub's "New file" editor for the catalog repo with the
    /// mod.json pre-populated. The id field drives the target path —
    /// <c>mods/&lt;id&gt;/mod.json</c> — and the generated JSON is shoved
    /// into the <c>value</c> query parameter, which GitHub honours up to
    /// URL-length limits (well above what a mod.json can produce).
    /// </summary>
    private void OpenCatalogPr()
    {
        string id = FieldId.Text.Trim();
        if (string.IsNullOrEmpty(id)) id = "your-mod-id";
        string filename = $"mods/{id}/mod.json";
        string encoded = WebUtility.UrlEncode(JsonPreview.Text ?? "");
        string url =
            $"https://github.com/{CatalogRepo}/new/{CatalogBranch}" +
            $"?filename={WebUtility.UrlEncode(filename)}" +
            $"&value={encoded}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not open browser: {ex.Message}",
                "Publish",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ------------------------------------------------------------------------
    // Defaults — overridable from MainWindow's ConfigurePublishWizardStrings.
    // ------------------------------------------------------------------------
    private void ApplyDefaultLabels()
    {
        TitleBarControl.Title = Strings.Get("PublishWizardTitle");
        CancelButton.Content = Strings.Get("PublishWizardCancel");
        BackButton.Content = Strings.Get("PublishWizardBack");
        NextLabel = Strings.Get("PublishWizardNext");
        OpenPrLabel = Strings.Get("PublishOpenPr");
        StepIndicatorFormat = Strings.Get("PublishWizardStepFormat");

        for (int i = 0; i < _optionalMarks.Length; i++)
            _optionalMarks[i].Text = Strings.Get("PublishOptionalMark");

        for (int i = 0; i < _railLabels.Length; i++)
            _railLabels[i].Text = Strings.Get($"PublishRail{i + 1}");

        for (int step = 1; step <= StepCount; step++)
        {
            SetStepTitle(step, Strings.Get($"PublishWizardStep{step}Title"));
            SetStepHint(step, Strings.Get($"PublishWizardStep{step}Hint"));
        }

        // --- Step 1 ---
        HowItWorksButton.Content = Strings.Get("PublishHowItWorks");
        IntroText.Text = Strings.Get("PublishWizardIntro");
        LblId.Text = Strings.Get("PublishFieldId");
        HintId.Text = Strings.Get("PublishFieldIdHint");
        IdPathPrefix.Text = Strings.Get("PublishIdPathPrefix");
        LblDisplayName.Text = Strings.Get("PublishFieldDisplayName");
        HintDisplayName.Text = Strings.Get("PublishFieldDisplayNameHint");
        LblAuthor.Text = Strings.Get("PublishFieldAuthor");
        HintAuthor.Text = Strings.Get("PublishFieldAuthorHint");
        LblSubtitle.Text = Strings.Get("PublishFieldSubtitle");
        HintSubtitle.Text = Strings.Get("PublishFieldSubtitleHint");

        // --- Step 2 ---
        LblAccent.Text = Strings.Get("PublishFieldAccent");
        HintAccent.Text = Strings.Get("PublishFieldAccentHint");
        LblIcon.Text = Strings.Get("PublishFieldIcon");
        HintIcon.Text = Strings.Get("PublishFieldIconHint");
        LblBanner.Text = Strings.Get("PublishFieldBanner");
        HintBanner.Text = Strings.Get("PublishFieldBannerHint");
        ImagesUploadNote.Text = Strings.Get("PublishImagesUploadNote");

        // --- Step 3, grouped by the question each field answers ---
        Grp3Install.Text = Strings.Get("PublishGrpInstall");
        Grp3Recognise.Text = Strings.Get("PublishGrpRecognise");
        Grp3Launch.Text = Strings.Get("PublishGrpLaunch");
        Grp3PlayerData.Text = Strings.Get("PublishGrpPlayerData");
        HintInstallType.Text = Strings.Get("PublishFieldInstallTypeHint");
        InstallOptUhc.Content = Strings.Get("PublishInstallOptUhc");
        InstallOptAdditive.Content = Strings.Get("PublishInstallOptAdditive");
        InstallOptReplace.Content = Strings.Get("PublishInstallOptReplace");
        LblDefaultFolder.Text = Strings.Get("PublishFieldDefaultFolder");
        HintDefaultFolder.Text = Strings.Get("PublishFieldDefaultFolderHint");
        LblProbeFile.Text = Strings.Get("PublishFieldProbeFile");
        HintProbeFile.Text = Strings.Get("PublishFieldProbeFileHint");
        MarkerNeededTitle.Text = Strings.Get("PublishMarkerQuestion");
        MarkerNeededDesc.Text = Strings.Get("PublishMarkerQuestionHint");
        LblMarker.Text = Strings.Get("PublishFieldMarker");
        HintMarker.Text = Strings.Get("PublishFieldMarkerHint");
        LblExecutable.Text = Strings.Get("PublishFieldExecutable");
        HintExecutable.Text = Strings.Get("PublishFieldExecutableHint");
        LblArguments.Text = Strings.Get("PublishFieldArguments");
        HintArguments.Text = Strings.Get("PublishFieldArgumentsHint");
        LblUserDataFolder.Text = Strings.Get("PublishFieldUserDataFolder");
        HintUserDataFolder.Text = Strings.Get("PublishFieldUserDataFolderHint");
        Step3AdvancedSummary.Text = Strings.Get("PublishAdvancedSummary3");
        Step3AdvancedToggle.Content = Strings.Get("PublishAdvancedToggle");
        LblInstallProductGuid.Text = Strings.Get("PublishFieldProductGuid");
        HintInstallProductGuid.Text = Strings.Get("PublishFieldProductGuidHint");

        // --- Step 4 ---
        LblMechanism.Text = Strings.Get("PublishFieldMechanism");
        HintMechanism.Text = Strings.Get("PublishFieldMechanismHint");
        MechOptGitHub.Content = Strings.Get("PublishMechGitHub");
        MechOptWol.Content = Strings.Get("PublishMechWol");
        MechOptExternal.Content = Strings.Get("PublishMechExternal");
        MechOptManual.Content = Strings.Get("PublishMechManual");
        LblSourceRepo.Text = Strings.Get("PublishFieldSourceRepo");
        HintSourceRepo.Text = Strings.Get("PublishFieldSourceRepoHint");
        LblApprovedTag.Text = Strings.Get("PublishFieldApprovedTag");
        HintApprovedTag.Text = Strings.Get("PublishFieldApprovedTagHint");
        FieldDeltaPatches.Content = Strings.Get("PublishFieldDeltaPatches");
        HintDeltaPatches.Text = Strings.Get("PublishFieldDeltaPatchesHint");
        UpdateDeletionNote.Text = Strings.Get("PublishUpdateDeletionNote");
        GhAdvancedSummary.Text = Strings.Get("PublishAdvancedSummaryGh");
        GhAdvancedToggle.Content = Strings.Get("PublishAdvancedToggle");
        LblGhExternalUrl.Text = Strings.Get("PublishFieldGhExternalUrl");
        HintGhExternalUrl.Text = Strings.Get("PublishFieldGhExternalUrlHint");
        LblGhExternalSha.Text = Strings.Get("PublishFieldGhExternalSha");
        HintGhExternalSha.Text = Strings.Get("PublishFieldGhExternalShaHint");
        LblWolUpdateInfoUrl.Text = Strings.Get("PublishFieldWolUpdateInfoUrl");
        HintWolUpdateInfoUrl.Text = Strings.Get("PublishFieldWolUpdateInfoUrlHint");
        LblWolUpdateInfoUrlAlt.Text = Strings.Get("PublishFieldWolUrlAlt");
        HintWolUpdateInfoUrlAlt.Text = Strings.Get("PublishFieldWolUrlAltHint");
        LblWolPayloadZipUrls.Text = Strings.Get("PublishFieldWolPayloadUrls");
        HintWolPayloadZipUrls.Text = Strings.Get("PublishFieldWolPayloadUrlsHint");
        LblWolPayloadSha256.Text = Strings.Get("PublishFieldWolPayloadSha256");
        // Its OWN hint. This line used to read PublishFieldPayloadSha256Hint -- the INSTALL
        // payload's -- so the two SHA fields said the same words on two different steps,
        // which is half of why three near-identical url+sha pairs were impossible to tell
        // apart.
        HintWolPayloadSha256.Text = Strings.Get("PublishFieldWolPayloadSha256Hint");
        Grp4Payload.Text = Strings.Get("PublishGrpPayload");
        Grp4PayloadNote.Text = Strings.Get("PublishGrpPayloadNote");
        LblPayloadUrls.Text = Strings.Get("PublishFieldPayloadUrls");
        HintPayloadUrls.Text = Strings.Get("PublishFieldPayloadUrlsHint");
        LblPayloadSha256.Text = Strings.Get("PublishFieldPayloadSha256");
        HintPayloadSha256.Text = Strings.Get("PublishFieldPayloadSha256Hint");
        Step4TranslationsHeader.Text = Strings.Get("PublishTranslationsHeader");
        LblTranslationsRepo.Text = Strings.Get("PublishFieldTranslationsRepo");
        HintTranslationsRepo.Text = Strings.Get("PublishFieldTranslationsRepoHint");
        LblTranslationsCovered.Text = Strings.Get("PublishFieldTranslationsCovered");
        HintTranslationsCovered.Text = Strings.Get("PublishFieldTranslationsCoveredHint");

        // --- Step 5 ---
        LblDescriptionEn.Text = Strings.Get("PublishFieldDescriptionEn");
        HintDescription.Text = Strings.Get("PublishFieldDescriptionHint");
        LblDescriptionEs.Text = Strings.Get("PublishFieldDescriptionEs");
        HintDescriptionEs.Text = Strings.Get("PublishFieldDescriptionEsHint");
        LblWebsite.Text = Strings.Get("PublishFieldWebsite");
        HintWebsite.Text = Strings.Get("PublishFieldWebsiteHint");
        LblLinks.Text = Strings.Get("PublishFieldLinks");
        HintLinks.Text = Strings.Get("PublishFieldLinksHint");

        // --- Step 6 ---
        JsonHeaderLabel.Text = Strings.Get("PublishJsonHeader");
        CopyJsonButton.Content = Strings.Get("PublishCopyJson");
        NextStepsTitle.Text = Strings.Get("PublishNextStepsTitle");
        NextStep1.Text = Strings.Get("PublishNextStep1");
        NextStep2.Text = Strings.Get("PublishNextStep2");
        NextStep3.Text = Strings.Get("PublishNextStep3");
        NextStepsLink.Content = Strings.Get("PublishNextStepsLink");

        ErrorIdInvalid = Strings.Get("PublishErrorId");
        ErrorDisplayNameRequired = Strings.Get("PublishErrorDisplayName");
        ErrorAccentInvalid = Strings.Get("PublishErrorAccent");
        ErrorIconInvalid = Strings.Get("PublishErrorIcon");
        ErrorBannerInvalid = Strings.Get("PublishErrorBanner");
        ErrorExecutableInvalid = Strings.Get("PublishErrorExecutable");
        ErrorWebsiteInvalid = Strings.Get("PublishErrorWebsite");
    }
}
