#!/usr/bin/env python3
"""Check that every GitHubReleases mod in the catalog can still be installed.

A catalog entry can go stale with nothing in the catalog changing: a modder re-tags a release,
deletes one, or uploads the payload under another name, and every install of that mod fails for
every player while CI stays green. (King's Return sat on a tag that no longer existed.) This
script asks GitHub what the LAUNCHER will ask and applies the launcher's own selection rules
(``GitHubReleaseDownloader.PickAssetIndex`` / ``PickPayloadPartIndices``, the patch-asset naming
of ``DeltaPatchService``, the baseline rule of ``DeltaChainPlanner``) - the two implementations
are held together by ``check_releases_cases.json``, read by the tests here AND by the launcher's
``CatalogCheckerParityTests``.

Standard library only. Two API calls per mod (the repo, its releases page).

A 5xx, a timeout or a rate limit is INCONCLUSIVE, never a finding: no issue is opened or closed
on a verdict GitHub did not actually give.

Usage:
  python check_releases.py [--mods-dir mods] [--only mod-id ...] [--json report.json]
Exit code: 1 when any mod has an ERROR, 0 otherwise (warnings and inconclusive do not fail).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

PATCH_PREFIX = "patch-"
PART_SUFFIX = re.compile(r"^(?P<base>.+\.zip)\.(?P<n>\d{3})$", re.IGNORECASE)


# ----------------------------------------------------------------- the launcher's selection rules

def is_patch_asset(name: str) -> bool:
    n = (name or "").lower()
    return n.startswith(PATCH_PREFIX) and (n.endswith(".zip") or n.endswith(".json"))


def is_descriptor(name: str) -> bool:
    n = (name or "").lower()
    return n.startswith(PATCH_PREFIX) and n.endswith(".json")


def _is_non_payload(name: str, non_payload: Optional[str]) -> bool:
    return bool(non_payload) and (name or "").lower() == non_payload.lower()


def pick_asset_index(names: list[str], non_payload: Optional[str] = None) -> Optional[int]:
    """Mirror of PickAssetIndex with no pattern (the catalog cannot set one)."""
    if not names:
        return None
    any_descriptor = any(is_descriptor(n) for n in names)
    kept = [i for i, n in enumerate(names) if not is_patch_asset(n) and not _is_non_payload(n, non_payload)]

    def select(pool: list[int]) -> Optional[int]:
        for i in pool:
            if (names[i] or "").lower().endswith(".zip"):
                return i
        return None

    selected = select(kept)
    if selected is not None or any_descriptor:
        return selected
    return select([i for i, n in enumerate(names) if not _is_non_payload(n, non_payload)])


def pick_payload_parts(names: list[str], non_payload: Optional[str] = None) -> list[int]:
    """Mirror of PickPayloadPartIndices with no pattern."""
    groups: dict[str, dict[int, int]] = {}
    keys: dict[str, str] = {}
    for i, name in enumerate(names or []):
        m = PART_SUFFIX.match(name or "")
        if not m:
            continue
        base = m.group("base")
        if is_patch_asset(base) or _is_non_payload(base, non_payload):
            continue
        part = int(m.group("n"))
        k = base.lower()
        keys.setdefault(k, base)
        parts = groups.setdefault(k, {})
        parts[part] = -1 if part in parts else i
    for k in sorted(groups, key=lambda k: (-len(groups[k]), keys[k])):
        parts = groups[k]
        if len(parts) < 2:
            continue
        ordered = sorted(parts)
        if ordered != list(range(1, len(ordered) + 1)) or any(parts[p] < 0 for p in ordered):
            continue
        return [parts[p] for p in ordered]
    return []


def unusable_part_set(names: list[str]) -> Optional[str]:
    """Mirror of DescribeUnusablePartSet: a release that MEANS to ship a split payload but cannot.
    Only meaningful when pick_payload_parts found nothing (that is the only way the launcher asks)."""
    groups: dict[str, set[int]] = {}
    keys: dict[str, str] = {}
    for name in names or []:
        m = PART_SUFFIX.match(name or "")
        if not m or is_patch_asset(m.group("base")):
            continue
        k = m.group("base").lower()
        keys.setdefault(k, m.group("base"))
        groups.setdefault(k, set()).add(int(m.group("n")))
    if not groups:
        return None
    k = sorted(groups, key=lambda k: (-len(groups[k]), keys[k]))[0]
    expected = 1
    for n in sorted(groups[k]):
        if n != expected:
            return f"'{keys[k]}' is published in parts but part {expected:03d} is missing"
        expected += 1
    return f"'{keys[k]}' is published in parts but only part 001 is present"


def has_full_payload(names: list[str], non_payload: Optional[str] = None) -> bool:
    """A baseline: a full payload, single or split (DeltaChainPlanner.FullZipOf)."""
    return bool(pick_payload_parts(names, non_payload)) or pick_asset_index(names, non_payload) is not None


# ----------------------------------------------------------------- checking one mod

@dataclass
class Verdict:
    mod_id: str
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    inconclusive: Optional[str] = None

    @property
    def clean(self) -> bool:
        return not self.errors and not self.warnings and self.inconclusive is None


class Inconclusive(Exception):
    pass


def _get(url: str, token: Optional[str]):
    req = urllib.request.Request(url, headers={
        "Accept": "application/vnd.github+json",
        "User-Agent": "aoe3-mods-catalog-release-check",
        **({"Authorization": f"Bearer {token}"} if token else {}),
    })
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return 200, json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return 404, None
        if e.code in (403, 429) or e.code >= 500:
            raise Inconclusive(f"GitHub answered {e.code} for {url}")
        raise
    except (urllib.error.URLError, TimeoutError) as e:
        raise Inconclusive(f"no answer from GitHub for {url}: {e}")


def check_mod(manifest: dict, releases: Optional[list], repo_exists: bool) -> Verdict:
    """The pure half: what the manifest promises against what GitHub holds."""
    v = Verdict(manifest.get("id", "?"))
    update = manifest.get("update") or {}
    gh = update.get("github") or {}
    repo = manifest.get("sourceRepo") or ""
    approved = manifest.get("approvedReleaseTag") or ""
    follow = bool(gh.get("followLatest"))
    delta = bool(gh.get("deltaPatches"))
    external = bool(gh.get("externalAssetUrlTemplate"))
    seed = (manifest.get("install") or {}).get("userDataPayload")

    if not repo_exists:
        v.errors.append(f"the repository {repo} does not exist (or is private)")
        return v
    published = [r for r in (releases or []) if not r.get("draft")]
    by_tag = {r.get("tag_name"): r for r in published}

    def names(r):
        return [a.get("name", "") for a in r.get("assets") or []]

    def payload_problem(r) -> Optional[str]:
        if external:
            return None
        n = names(r)
        if pick_payload_parts(n, seed):
            return None
        broken = unusable_part_set(n)
        if broken:
            return broken
        if pick_asset_index(n, seed) is not None:
            return None
        if delta and any(is_descriptor(x) for x in n):
            return None  # a patch-only release: reached by patches from a baseline
        return "carries no payload (.zip, .zip.001...) and no patch"

    if approved not in by_tag:
        (v.warnings if follow else v.errors).append(
            f"approvedReleaseTag '{approved}' is not a release of {repo}"
            + (" (only the offline seed: followLatest is on)" if follow else ""))
    else:
        problem = payload_problem(by_tag[approved])
        if problem:
            v.errors.append(f"release '{approved}' {problem}")

    if delta and not external and not any(has_full_payload(names(r), seed) for r in published):
        v.errors.append("deltaPatches is on but no release carries a full payload (a baseline)")

    if follow and not external:
        stable = [r for r in published if not r.get("prerelease")]
        if not stable:
            v.errors.append("followLatest is on but the repository has no stable release")
        else:
            latest = stable[0]
            problem = payload_problem(latest)
            if problem:
                v.errors.append(f"latest release '{latest.get('tag_name')}' {problem}")

    if seed:
        carrying = [r for r in published if any((x or "").lower() == seed.lower() for x in names(r))]
        if not carrying:
            v.errors.append(f"userDataPayload '{seed}' is on no release")
        else:
            target = by_tag.get(approved)
            if target is not None and target not in carrying:
                v.warnings.append(f"userDataPayload '{seed}' is not on '{approved}' (a later release carries it)")
    return v


def check_one(path: Path, token: Optional[str]) -> Optional[Verdict]:
    manifest = json.loads(path.read_text(encoding="utf-8"))
    if (manifest.get("update") or {}).get("mechanism") != "GitHubReleases":
        return None
    repo = manifest.get("sourceRepo") or ""
    v = Verdict(manifest.get("id", path.parent.name))
    try:
        status, _ = _get(f"https://api.github.com/repos/{repo}", token)
        if status == 404:
            return check_mod(manifest, None, repo_exists=False)
        status, releases = _get(f"https://api.github.com/repos/{repo}/releases?per_page=100", token)
        return check_mod(manifest, releases or [], repo_exists=True)
    except Inconclusive as e:
        v.inconclusive = str(e)
        return v


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mods-dir", default="mods")
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--json", default=None)
    args = ap.parse_args(argv)
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")

    verdicts: list[Verdict] = []
    for path in sorted(Path(args.mods_dir).glob("*/mod.json")):
        if args.only is not None and path.parent.name not in args.only:
            continue
        v = check_one(path, token)
        if v is not None:
            verdicts.append(v)

    for v in verdicts:
        state = "INCONCLUSIVE" if v.inconclusive else "ERROR" if v.errors else "WARN" if v.warnings else "ok"
        print(f"{state:12} {v.mod_id}")
        for e in v.errors:
            print(f"    error: {e}")
        for w in v.warnings:
            print(f"    warning: {w}")
        if v.inconclusive:
            print(f"    {v.inconclusive}")

    if args.json:
        Path(args.json).write_text(json.dumps([
            {"id": v.mod_id, "errors": v.errors, "warnings": v.warnings, "inconclusive": v.inconclusive}
            for v in verdicts], indent=2), encoding="utf-8")
    return 1 if any(v.errors for v in verdicts) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
