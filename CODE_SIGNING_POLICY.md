# Code Signing Policy

The release binary of the **AoE3 Mod Launcher** (`Aoe3ModLauncher.exe`,
distributed through [GitHub Releases](https://github.com/Gorgorito12/Updater/releases))
is Authenticode-signed.

> Free code signing provided by [SignPath.io](https://about.signpath.io),
> certificate by [SignPath Foundation](https://signpath.org).

## Project integrity

- **License:** Apache-2.0 (OSI-approved), with no proprietary or commercially
  dual-licensed components — see [`LICENSE`](LICENSE).
- **Source of truth:** the public repository
  <https://github.com/Gorgorito12/Updater>. The team that maintains the source
  owns the repository and authorizes every signing request.
- **Actively maintained**, released through GitHub Releases.
- Contains no malware, and no features designed to identify or exploit security
  vulnerabilities.

## Team roles

This project follows the role model required by SignPath Foundation. For this
maintainer-led project the roles are held as follows:

- **Authors / committers** — trusted developers who write and commit code:
  [@Gorgorito12](https://github.com/Gorgorito12) and the contributors listed at
  <https://github.com/Gorgorito12/Updater/graphs/contributors>.
- **Reviewers** — review every external contribution before merge:
  [@Gorgorito12](https://github.com/Gorgorito12). All third-party contributions
  arrive as pull requests under a [DCO](CONTRIBUTING.md) sign-off and are
  reviewed before they are merged.
- **Approvers** — manually authorize each code-signing request in SignPath:
  [@Gorgorito12](https://github.com/Gorgorito12).

All team members use multi-factor authentication for both their source-code
repository (GitHub) and SignPath accounts.

## Build and origin verification

- Release binaries are **built in CI on GitHub-hosted runners** (GitHub Actions,
  `windows-latest`) — see
  [`.github/workflows/release.yml`](.github/workflows/release.yml). Locally built
  binaries are never submitted for signing.
- The build is fully determined by files under source control. The CI job
  produces an **unsigned** artifact that is submitted to SignPath for signing
  with **origin verification** enabled, so SignPath confirms the artifact was
  produced by this repository's CI.
- **Every release is manually approved** by an Approver before the binary is
  signed.
- After signing, the SHA-256 of the released `.exe` is published in the GitHub
  release notes so users can verify their download.

## Privacy

The launcher's data practices are described in its privacy policy,
[`PRIVACY.md`](PRIVACY.md). In short: no analytics and no third-party trackers;
network access is limited to update checks (which the user can disable) and —
once the user opts in by signing in with Discord — multiplayer lobbies and chat.
Third-party services and their own privacy policies (Discord, GitHub, Famatech
Radmin VPN, and the mod distribution servers) are listed in `PRIVACY.md`.

## Reporting

Security or code-signing concerns: please
[open an issue](https://github.com/Gorgorito12/Updater/issues), or see
[`CONTRIBUTING.md`](CONTRIBUTING.md).
