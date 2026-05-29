using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

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

    /// <summary>Catalog repo target for the "Open PR" button.</summary>
    public string CatalogRepo { get; set; } = "Gorgorito12/aoe3-mods-catalog";

    /// <summary>Branch the PR template targets.</summary>
    public string CatalogBranch { get; set; } = "main";

    private int _currentStep = 1;
    private readonly StackPanel[] _stepPanels;
    private readonly TextBlock[] _stepTitles;
    private readonly TextBlock[] _stepHints;

    // Schema regexes — kept in sync with mod.schema.json. Compiled once
    // because every validation pass hits them twice (Next-button click).
    private static readonly Regex IdRegex = new("^[a-z][a-z0-9-]{1,30}$", RegexOptions.Compiled);
    private static readonly Regex AccentRegex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex IconRegex = new(@"^[a-zA-Z0-9_-]+\.png$", RegexOptions.Compiled);
    private static readonly Regex BannerRegex = new(@"^[a-zA-Z0-9_-]+\.(png|jpg|jpeg)$", RegexOptions.Compiled);
    private static readonly Regex ExeRegex = new(@"^[a-zA-Z0-9_.-]+\.exe$", RegexOptions.Compiled);
    private static readonly Regex WebsiteRegex = new(@"^https?://", RegexOptions.Compiled);
    private static readonly Regex SourceRepoRegex = new(@"^[a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    public PublishModDialog()
    {
        InitializeComponent();

        _stepPanels = new[] { Step1Panel, Step2Panel, Step3Panel, Step4Panel, Step5Panel, Step6Panel };
        _stepTitles = new[] { Step1Title, Step2Title, Step3Title, Step4Title, Step5Title, Step6Title };
        _stepHints  = new[] { Step1Hint,  Step2Hint,  Step3Hint,  Step4Hint,  Step5Hint,  Step6Hint  };

        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        CloseHeaderButton.Click += (_, _) => { DialogResult = false; Close(); };
        BackButton.Click += (_, _) => GoTo(_currentStep - 1);
        NextButton.Click += OnNextClicked;

        FieldMechanism.SelectionChanged += (_, _) => RefreshMechanismSubforms();
        CopyJsonButton.Click += (_, _) => CopyJson();
        OpenPrButton.Click += (_, _) => OpenCatalogPr();

        ApplyDefaultLabels();
        FieldInstallType.SelectedIndex = 0;
        FieldMechanism.SelectedIndex = 0;
        GoTo(1);
    }

    // ------------------------------------------------------------------------
    // Public labels — overridable so MainWindow can push the launcher's
    // current language into every visible string before showing the dialog.
    // ------------------------------------------------------------------------

    public string HeaderTitleText { get => HeaderTitle.Text; set => HeaderTitle.Text = value; }
    public string CancelLabel { get => (string)(CancelButton.Content ?? ""); set => CancelButton.Content = value; }
    public string BackLabel { get => (string)(BackButton.Content ?? ""); set => BackButton.Content = value; }
    public string NextLabel { get; set; } = "Next";
    public string FinishLabel { get; set; } = "Finish";
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
    public string LblInstallTypeText { get => LblInstallType.Text; set => LblInstallType.Text = value; }
    public string HintInstallTypeText { get => HintInstallType.Text; set => HintInstallType.Text = value; }
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
    public string OpenPrLabel { get => (string)(OpenPrButton.Content ?? ""); set => OpenPrButton.Content = value; }

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

    // Guidance copy — the wizard intro, the "filenames aren't files" reminder
    // on the look & feel step, and the post-publish flow on the review step.
    public string IntroBodyText { get => IntroText.Text; set => IntroText.Text = value; }
    public string ImagesUploadNoteText { get => ImagesUploadNote.Text; set => ImagesUploadNote.Text = value; }
    public string NextStepsTitleText { get => NextStepsTitle.Text; set => NextStepsTitle.Text = value; }
    public string NextStepsBodyText { get => NextStepsBody.Text; set => NextStepsBody.Text = value; }

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
        for (int i = 0; i < _stepPanels.Length; i++)
            _stepPanels[i].Visibility = (i == step - 1) ? Visibility.Visible : Visibility.Collapsed;

        HeaderStep.Text = string.Format(StepIndicatorFormat, step, StepCount);
        BackButton.IsEnabled = step > 1;
        NextButton.Content = step == StepCount ? FinishLabel : NextLabel;

        if (step == 4) RefreshMechanismSubforms();
        if (step == 6) JsonPreview.Text = GenerateJson();
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        if (!ValidateCurrentStep()) return;
        GoTo(_currentStep + 1);
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
        4 => true, // Step 4 only constrains enum values + optional URLs we don't pre-validate.
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
        return ok;
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
        string mech = SelectedTag(FieldMechanism) ?? "WolPatcher";
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
    public string GenerateJson()
    {
        var doc = new Dictionary<string, object?>
        {
            ["$schema"] = $"https://raw.githubusercontent.com/{CatalogRepo}/{CatalogBranch}/schema/mod.schema.json",
            ["id"] = FieldId.Text.Trim(),
            ["displayName"] = FieldDisplayName.Text.Trim(),
        };

        AddIfPresent(doc, "subtitle", FieldSubtitle.Text);
        AddIfPresent(doc, "author", FieldAuthor.Text);
        AddIfPresent(doc, "accentColor", FieldAccent.Text);
        AddIfPresent(doc, "icon", FieldIcon.Text);
        AddIfPresent(doc, "banner", FieldBanner.Text);
        AddIfPresent(doc, "officialWebsite", FieldWebsite.Text);

        var descriptions = new Dictionary<string, string>();
        AddDescription(descriptions, "en", FieldDescriptionEn.Text);
        AddDescription(descriptions, "es", FieldDescriptionEs.Text);
        if (descriptions.Count > 0) doc["description"] = descriptions;

        // install.* is required by the schema; always emit it even when
        // every nested field is blank — the schema will tell the modder
        // exactly which sub-field is missing if they try to merge.
        var install = new Dictionary<string, object?>
        {
            ["type"] = SelectedTag(FieldInstallType) ?? "IsolatedFolder",
        };
        AddIfPresent(install, "defaultFolder", FieldDefaultFolder.Text);
        AddIfPresent(install, "probeFile", FieldProbeFile.Text);
        AddIfPresent(install, "executable", FieldExecutable.Text);
        AddIfPresent(install, "arguments", FieldArguments.Text);
        doc["install"] = install;

        string mech = SelectedTag(FieldMechanism) ?? "WolPatcher";
        var update = new Dictionary<string, object?> { ["mechanism"] = mech };
        if (mech == "WolPatcher")
        {
            var wol = new Dictionary<string, object?>();
            AddIfPresent(wol, "updateInfoUrl", FieldWolUpdateInfoUrl.Text);
            if (wol.Count > 0) update["wol"] = wol;
        }
        doc["update"] = update;

        // Source repo + approved tag live at the top level, not under update.
        if (mech == "GitHubReleases")
        {
            AddIfPresent(doc, "sourceRepo", FieldSourceRepo.Text);
            AddIfPresent(doc, "approvedReleaseTag", FieldApprovedTag.Text);
        }

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static void AddIfPresent(IDictionary<string, object?> doc, string key, string value)
    {
        var trimmed = value?.Trim() ?? "";
        if (!string.IsNullOrEmpty(trimmed)) doc[key] = trimmed;
    }

    private static void AddDescription(IDictionary<string, string> doc, string lang, string value)
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
        HeaderTitle.Text = "Publish my mod";
        CancelButton.Content = "Cancel";
        BackButton.Content = "Back";

        SetStepTitle(1, "Identity");
        SetStepHint(1, "Pick a stable id and a display name. These two fields anchor the catalog entry.");
        SetStepTitle(2, "Look & feel");
        SetStepHint(2, "Accent colour, icon and banner. Optional but recommended.");
        SetStepTitle(3, "Install");
        SetStepHint(3, "How the mod's files live on disk and which executable launches it.");
        SetStepTitle(4, "Updates");
        SetStepHint(4, "How the launcher pulls new versions: WoL patcher, GitHub Releases, external updater, or manual.");
        SetStepTitle(5, "Description & website");
        SetStepHint(5, "Per-language description and the mod's homepage URL.");
        SetStepTitle(6, "Review & publish");
        SetStepHint(6, "Inspect the generated mod.json, copy it to the clipboard, and open the catalog PR template on GitHub.");

        LblId.Text = "Id"; HintId.Text = "Lowercase letters, digits, dashes. Used as the folder name under /mods/. Example: napoleonic-era";
        LblDisplayName.Text = "Display name"; HintDisplayName.Text = "The name shown in the catalog. Example: Napoleonic Era";
        LblAuthor.Text = "Author (optional)"; HintAuthor.Text = "Your name or your team's. Example: Napoleonic Team";
        LblSubtitle.Text = "Subtitle (optional)"; HintSubtitle.Text = "Short tagline under the title. Example: Napoleonic Wars, 1789–1815";
        LblAccent.Text = "Accent colour (optional)"; HintAccent.Text = "Hex format, e.g. #c8102e. It's the mod's brand colour in the launcher.";
        LblIcon.Text = "Icon filename (optional)"; HintIcon.Text = "icon.png — 256x256, PNG with alpha, ≤100 KB.";
        LblBanner.Text = "Banner filename (optional)"; HintBanner.Text = "banner.png/.jpg — 1200x300, ≤500 KB.";
        LblInstallType.Text = "Install type"; HintInstallType.Text = "IsolatedFolder = own folder (recommended for most mods). InPlaceOverlay = on top of AoE3.";
        LblDefaultFolder.Text = "Default install folder"; HintDefaultFolder.Text = "Folder name suggested when installing. Example: Napoleonic Era";
        LblProbeFile.Text = "Probe file"; HintProbeFile.Text = "A file that confirms the mod is installed. Example: data\\napoleonic.xml";
        LblExecutable.Text = "Executable"; HintExecutable.Text = "The .exe that launches the game. Example: age3y.exe";
        LblArguments.Text = "Arguments (optional)"; HintArguments.Text = "Command-line flags on launch. Example: +nointromovie";
        LblMechanism.Text = "Update mechanism"; HintMechanism.Text = "GitHubReleases is recommended for new mods. WolPatcher is the legacy UpdateInfo.xml flow; Manual = no auto-updates.";
        LblWolUpdateInfoUrl.Text = "UpdateInfo.xml URL"; HintWolUpdateInfoUrl.Text = "URL to a WoL-style UpdateInfo.xml. Example: https://yoursite.com/UpdateInfo.xml";
        LblSourceRepo.Text = "Source repo (owner/repo)"; HintSourceRepo.Text = "Your mod's GitHub repository, e.g. yourname/your-mod.";
        LblApprovedTag.Text = "Approved release tag"; HintApprovedTag.Text = "The release tag the launcher downloads. Example: v1.0.0";
        LblDescriptionEn.Text = "Description (English)"; HintDescription.Text = "1–2 sentences on what your mod does. Example: A total conversion set during the Napoleonic Wars.";
        LblDescriptionEs.Text = "Descripción (Español)";
        LblWebsite.Text = "Official website (optional)"; HintWebsite.Text = "Your mod's page, Discord or ModDB. Example: https://discord.gg/your-mod";
        CopyJsonButton.Content = "Copy JSON";
        OpenPrButton.Content = "Open PR on GitHub";

        IntroText.Text =
            "This wizard builds a mod.json for the public catalog. Fill in each step, then on the " +
            "last step copy the file or open a ready-made GitHub pull request. You don't install " +
            "anything here — your mod is added by a PR to the catalog repo (Gorgorito12/aoe3-mods-catalog), " +
            "which the launcher reads to list every mod.";
        ImagesUploadNote.Text =
            "These are just filenames. After you open the pull request, drop the real image files " +
            "(icon.png, banner.png) into the same mods/<id>/ folder of the PR — otherwise the catalog " +
            "has nothing to show.";
        NextStepsTitle.Text = "What happens after you publish";
        NextStepsBody.Text =
            "1. Click \"Open PR on GitHub\" — it opens the catalog's new-file editor with this mod.json " +
            "pre-filled at mods/<id>/mod.json.\n" +
            "2. Commit it and create the pull request (GitHub forks the repo for you).\n" +
            "3. Add your icon.png / banner.png to the same folder in the PR.\n" +
            "4. Automated checks validate the schema and images. Cosmetic edits and version bumps merge " +
            "automatically; first-time mods and changes to install/update fields get a manual review.\n" +
            "5. Once merged, your mod appears in the Catalog after pressing \"Refresh catalog\".";
    }
}
