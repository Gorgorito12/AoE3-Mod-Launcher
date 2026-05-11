namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// Settings for the "Pin to GitHub Release Tag" update mechanism. Used
/// when a mod's <see cref="ModProfile.UpdateMechanism"/> is
/// <see cref="ModUpdateMechanism.GitHubReleases"/>.
///
/// Mental model: the catalog entry (in <c>aoe3-mods-catalog</c>) is a
/// tiny pointer file that says "for this mod, go to the modder's own repo
/// and download the asset from this specific release tag". The modder
/// owns versioning in their repo; updates flow into the launcher via
/// micro-PRs to the catalog that bump <see cref="ApprovedReleaseTag"/>
/// (Tier 2 — auto-merge after CI verifies the tag exists and validates).
///
/// Why "approved" tag: the catalog tracks a tag the maintainer has
/// signed off on, not <c>latest</c>. If the modder publishes a buggy
/// release, the catalog still points at the previously-blessed tag until
/// a new micro-PR is merged.
/// </summary>
public class GitHubReleasesSettings
{
    /// <summary>
    /// owner/repo of the modder's GitHub repository. Format validated by
    /// the catalog schema; treat as untrusted input defensively in code
    /// paths that take it.
    /// </summary>
    public string SourceRepo { get; set; } = "";

    /// <summary>
    /// The specific release tag (e.g. <c>v1.2.3</c>) the launcher should
    /// load. Updated by Tier 2 catalog PRs when the modder publishes a
    /// new release.
    /// </summary>
    public string ApprovedReleaseTag { get; set; } = "";

    /// <summary>
    /// Optional. When the release has multiple assets, pick the one whose
    /// filename matches this glob-like pattern. Currently supports
    /// <c>*</c> wildcards (turned into <c>.*</c> regex internally). Empty
    /// means "first .zip asset wins". A modder shipping <c>napoleonic-mod.zip</c>
    /// can leave this empty; one shipping both <c>napoleonic-mod.zip</c>
    /// and <c>source-only.zip</c> can set this to <c>napoleonic-mod-*.zip</c>
    /// to disambiguate.
    /// </summary>
    public string AssetNamePattern { get; set; } = "";
}
