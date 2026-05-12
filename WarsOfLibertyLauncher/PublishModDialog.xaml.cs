using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher;

/// <summary>
/// v0.9 "Publish my mod" wizard — guides a modder through the six-step
/// flow that produces a <c>mod.json</c> conforming to the catalog repo's
/// JSON Schema. This commit lays down the navigation shell: a Header
/// breadcrumb ("Step N of 6"), six Step panels (only the active one is
/// visible) and Back / Next / Cancel buttons.
///
/// The actual form fields, validation, JSON serialisation, and the
/// "Open PR on GitHub" entry point arrive in commit 8 — keeping this
/// commit focused on a reviewable navigation skeleton.
/// </summary>
public partial class PublishModDialog : Window
{
    public const int StepCount = 6;

    private int _currentStep = 1;
    private readonly StackPanel[] _stepPanels;
    private readonly TextBlock[] _stepTitles;
    private readonly TextBlock[] _stepHints;

    public PublishModDialog()
    {
        InitializeComponent();

        _stepPanels = new[]
        {
            Step1Panel, Step2Panel, Step3Panel,
            Step4Panel, Step5Panel, Step6Panel,
        };
        _stepTitles = new[]
        {
            Step1Title, Step2Title, Step3Title,
            Step4Title, Step5Title, Step6Title,
        };
        _stepHints = new[]
        {
            Step1Hint, Step2Hint, Step3Hint,
            Step4Hint, Step5Hint, Step6Hint,
        };

        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        BackButton.Click += (_, _) => GoTo(_currentStep - 1);
        NextButton.Click += (_, _) => GoTo(_currentStep + 1);

        ApplyDefaultLabels();
        GoTo(1);
    }

    // ------------------------------------------------------------------------
    // Public labels — MainWindow can replace these with localised strings
    // before showing the dialog so the wizard text follows the launcher's
    // current language.
    // ------------------------------------------------------------------------

    public string HeaderTitleText
    {
        get => HeaderTitle.Text;
        set => HeaderTitle.Text = value;
    }

    public string CancelLabel
    {
        get => (string)(CancelButton.Content ?? "");
        set => CancelButton.Content = value;
    }

    public string BackLabel
    {
        get => (string)(BackButton.Content ?? "");
        set => BackButton.Content = value;
    }

    public string NextLabel { get; set; } = "Next";
    public string FinishLabel { get; set; } = "Finish";
    public string StepIndicatorFormat { get; set; } = "Step {0} of {1}";

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

    /// <summary>Currently visible step (1..6).</summary>
    public int CurrentStep => _currentStep;

    /// <summary>
    /// Switches to <paramref name="step"/>. Clamps to [1, StepCount]; the
    /// "Next" button on the last step closes the dialog with DialogResult=true
    /// so the caller can pick up the generated manifest from the
    /// public properties exposed in commit 8.
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
    }

    // ------------------------------------------------------------------------
    // Defaults — applied in the constructor so the dialog still reads
    // sensibly if MainWindow forgets to override the labels.
    // ------------------------------------------------------------------------
    private void ApplyDefaultLabels()
    {
        HeaderTitle.Text = "Publish my mod";
        CancelButton.Content = "Cancel";
        BackButton.Content = "Back";
        // Default step titles — overridable from the host (MainWindow) so
        // the wizard reads in the user's launcher language. Mirrors the
        // 6 catalog-schema groupings in the order modders walk through them.
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
    }
}
