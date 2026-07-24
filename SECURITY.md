# Security Policy

## "Is this a virus?"

Short answer: **no** — it's an unsigned open-source `.exe`, which is why Windows
or some antivirus tools may warn on first run. That's a false positive, not
malware. For the full explanation and how to verify the binary yourself
(VirusTotal, SHA-256, building from source), see
**[docs/IS-IT-A-VIRUS.md](docs/IS-IT-A-VIRUS.md)**.

## How this project earns trust

For a deep, code-cited walkthrough — every network endpoint, every system change,
a no-malware checklist, build/update integrity, and the AI models used to build it —
see the **[Transparency & Security Audit](docs/AUDIT.md)**.

- **Open source.** The complete source is public at
  <https://github.com/Gorgorito12/AoE3-Mod-Launcher> under Apache-2.0. Anyone can
  read exactly what the launcher does.
- **Reproducible, CI-built releases.** Official binaries are built in **GitHub
  Actions** on GitHub-hosted runners (see
  [`.github/workflows/release.yml`](.github/workflows/release.yml)), not on a
  developer's machine, and the build is fully determined by files under source
  control.
- **Published hashes.** Every release publishes the `.exe`'s **SHA-256** in the
  release notes, so you can confirm your download matches what was built.
- **Code signing (in progress).** Release binaries are Authenticode-signed; free
  signing is provided by [SignPath Foundation](https://signpath.org) once the
  application is approved. See
  [`CODE_SIGNING_POLICY.md`](CODE_SIGNING_POLICY.md) for the team roles and the
  origin-verification model. Until then, verify by SHA-256.
- **Privacy by default.** No analytics or third-party trackers; local telemetry
  is off by default. See [`PRIVACY.md`](PRIVACY.md).

## Reporting a vulnerability

If you believe you've found a security issue — malicious behavior in the code, a
tampered release, a credential/token exposure, or anything that could harm
users — please report it:

1. **Preferred:** open a
   [GitHub Security Advisory](https://github.com/Gorgorito12/AoE3-Mod-Launcher/security/advisories/new)
   (private, coordinated disclosure).
2. **Or** [open a regular issue](https://github.com/Gorgorito12/AoE3-Mod-Launcher/issues)
   if the matter is not sensitive.

Please include the launcher version, your OS version, and clear reproduction
steps or the relevant code reference. We aim to acknowledge reports within a few
days. This is a volunteer-maintained project, so timelines are best-effort — but
security reports are taken seriously and reviewed in the open.

## Supported versions

Only the **latest release** on the
[Releases page](https://github.com/Gorgorito12/AoE3-Mod-Launcher/releases)
receives fixes. The launcher's built-in self-update keeps you on the latest
version automatically. If you're on an older build, please update before
reporting.

## What to be cautious about

- **Download only from the official
  [Releases page](https://github.com/Gorgorito12/AoE3-Mod-Launcher/releases).**
  A copy of `Aoe3ModLauncher.exe` from anywhere else (a re-upload, a random
  Discord attachment, a mirror site) is **not** covered by this policy — verify
  its SHA-256 against the official release notes before trusting it.
- The launcher stores a multiplayer session token in
  `%LocalAppData%\AoE3ModLauncher\launcher-config.json`. Treat that file like a
  password; the diagnostics bundle (`Share diagnostics`) deliberately excludes it.
