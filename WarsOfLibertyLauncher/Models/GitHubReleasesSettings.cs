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

    /// <summary>
    /// Optional. URL template the launcher uses to fetch the mod payload
    /// from a host OUTSIDE of GitHub Releases (e.g. the modder's own CDN,
    /// S3, archive.org). The GitHub release at
    /// <see cref="ApprovedReleaseTag"/> still acts as the canonical
    /// version marker — the tag is what the StatusCard shows and what
    /// drives "is there an update available". The release itself can be
    /// empty (no attached assets); only the tag matters.
    ///
    /// The <c>{tag}</c> placeholder is substituted with
    /// <see cref="ApprovedReleaseTag"/> at resolve time. Example:
    /// <c>"https://my-cdn.com/napoleonic-{tag}.zip"</c> with tag
    /// <c>"v1.5"</c> resolves to
    /// <c>"https://my-cdn.com/napoleonic-v1.5.zip"</c>.
    ///
    /// When empty (the common case), the launcher falls back to the
    /// regular GitHub Release asset resolution via the GitHub API.
    ///
    /// Security: an external URL bypasses GitHub's authenticity. Pair
    /// this with <see cref="ExternalAssetSha256"/> so a compromised host
    /// cannot silently swap the payload — the launcher rejects downloads
    /// whose hash doesn't match the catalog-approved value.
    /// </summary>
    public string ExternalAssetUrlTemplate { get; set; } = "";

    /// <summary>
    /// Expected SHA-256 (lowercase hex) of the payload at
    /// <see cref="ExternalAssetUrlTemplate"/>. The launcher refuses to
    /// install a download whose computed SHA-256 doesn't match this
    /// value. Catalog PRs that change the SHA-256 are Tier 3 by design
    /// (see <c>classify_pr.py</c>: any change inside <c>update</c> is
    /// tier 3), so a CDN compromise can't slip a new SHA past review.
    ///
    /// Required whenever <see cref="ExternalAssetUrlTemplate"/> is set;
    /// the launcher rejects external URLs without a hash to prevent
    /// silent regressions. Ignored when <see cref="ExternalAssetUrlTemplate"/>
    /// is empty.
    /// </summary>
    public string ExternalAssetSha256 { get; set; } = "";
}
