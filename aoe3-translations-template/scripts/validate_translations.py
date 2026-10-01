#!/usr/bin/env python3
"""Checks a translations repository the way the AoE3 Mod Launcher will read it.

Run by .github/workflows/validate.yml on every pull request and push, and runnable
locally (Python 3.9+, standard library only; `jsonschema` is used when installed):

    python scripts/validate_translations.py                     # check the tree
    python scripts/validate_translations.py --base origin/main  # ...and that published files weren't changed

What it enforces, and why:

* Layout `translations/<id>/<version>/translation.json` plus that manifest's zip, and
  nothing else. The launcher ignores (and logs) a manifest at any other depth, so a
  misplaced pack is invisible to players rather than broken — CI is where it gets noticed.
* Folder names are the manifest's id and version. The launcher reads `translations\\<id>`
  by the manifest's id, and the version folder is what players see in the picker.
* The zip is what the launcher accepts: flat, `translation.json` plus the files the
  manifest lists, within the size limits, each file's MD5 equal to its `translatedHash`,
  and the content hash recomputed with the launcher's exact recipe. A pack failing any of
  these is refused on the player's machine with a message about a damaged pack.
* Within one id, no two versions share a label or a content hash.
* Published folders are immutable (with --base): a version is an address players already
  follow, so a fix ships as a NEW version. A maintainer can override with --allow-modify
  (the workflow maps the `allow-modify-published` pull-request label to it).
* An optional `translations-index.json` at the root is checked against the rules the
  launcher applies to index sources (required sha256 and targetMod, matching zips).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path, PurePosixPath

ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\Z")
VERSION_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}\Z")
MD5_RE = re.compile(r"^[0-9a-fA-F]{32}\Z")
SHA256_RE = re.compile(r"^[0-9a-fA-F]{64}\Z")
CONTENT_HASH_RE = re.compile(r"^[0-9a-fA-F]{16}\Z")
RECOMMENDED_VERSION_RE = re.compile(r"-r\d{1,4}\Z", re.IGNORECASE)
DEVICE_NAMES = {"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"} \
    | {f"COM{i}" for i in range(1, 10)} | {f"LPT{i}" for i in range(1, 10)}

MANIFEST = "translation.json"
INDEX = "translations-index.json"
MAX_ZIP_BYTES = 64 * 1024 * 1024
MAX_ENTRY_BYTES = 64 * 1024 * 1024
MAX_TOTAL_BYTES = 128 * 1024 * 1024
MAX_MANIFEST_BYTES = 256 * 1024
MAX_INDEX_ITEMS = 200
REFUSED_HOSTS = ("mega.nz", "mega.co.nz", "mega.io", "mediafire.com", "onedrive.live.com", "1drv.ms",
                 "sharepoint.com", "docs.google.com")


class Report:
    def __init__(self) -> None:
        self.errors = 0
        self.warnings = 0
        self.annotate = os.environ.get("GITHUB_ACTIONS") == "true"

    def _emit(self, level: str, where: str, message: str) -> None:
        if self.annotate:
            print(f"::{level} file={where}::{message}")
        else:
            print(f"{level.upper():7} {where}: {message}")

    def error(self, where: str, message: str) -> None:
        self.errors += 1
        self._emit("error", where, message)

    def warning(self, where: str, message: str) -> None:
        self.warnings += 1
        self._emit("warning", where, message)


def is_safe_segment(value: object, rx: re.Pattern) -> bool:
    """The launcher's TranslationPathPolicy.IsSafePackId / IsSafeVersionSegment."""
    if not isinstance(value, str) or not value or not rx.match(value) or value.endswith("."):
        return False
    stem = value.split(".", 1)[0].rstrip(" ")
    return stem.upper() not in DEVICE_NAMES


def normalize_relative(path: object) -> str | None:
    """The launcher's TranslationPathPolicy.TryNormalizeRelative: forward slashes, or None."""
    if not isinstance(path, str) or not path or path != path.strip() or len(path) > 260:
        return None
    if any(ord(c) < 32 or ord(c) == 127 for c in path) or ":" in path:
        return None
    p = path.replace("\\", "/")
    if p.startswith("/"):
        return None
    segments = p.split("/")
    for seg in segments:
        if seg in ("", ".", "..") or seg.endswith(".") or seg.endswith(" "):
            return None
        if seg.split(".", 1)[0].rstrip(" ").upper() in DEVICE_NAMES:
            return None
    return "/".join(segments)


def content_hash(files: list) -> str:
    """TranslationCompat.ComputeContentHash, byte for byte: files sorted by path (ordinal,
    i.e. UTF-16 code units, stable), "path\\ntranslatedHash" joined with "\\n", SHA-256 of
    the UTF-8 bytes, first 16 lower-case hex characters. Hashes are used exactly as written."""
    entries = [f for f in files if isinstance(f, dict)]
    entries.sort(key=lambda f: str(f.get("path") or "").encode("utf-16-be"))
    payload = "\n".join(str(f.get("path") or "") + "\n" + str(f.get("translatedHash") or "") for f in entries)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]


def effective_content_hash(manifest: dict) -> str:
    """TranslationCompat.EffectiveContentHash: the declared value, else the computed one."""
    declared = manifest.get("contentHash")
    if isinstance(declared, str) and declared.strip():
        return declared.strip()
    return content_hash(manifest.get("files") or [])


def load_json_bytes(data: bytes):
    return json.loads(data.decode("utf-8-sig"))


def load_schema(root: Path, name: str):
    try:
        import jsonschema  # type: ignore
    except ImportError:
        return None
    schema_path = root / "schema" / name
    if not schema_path.is_file():
        return None
    return jsonschema.Draft7Validator(json.loads(schema_path.read_text(encoding="utf-8")))


def schema_errors(validator, document) -> list[str]:
    if validator is None:
        return []
    return [f"{'/'.join(str(p) for p in e.absolute_path) or '(root)'}: {e.message}"
            for e in sorted(validator.iter_errors(document), key=lambda e: list(e.absolute_path))]


def load_covered(root: Path, report: Report) -> dict[str, set[str]]:
    """covered-files.json: { "<mod id>": ["data/stringtabley.xml", ...] } — what each mod's
    `translations.coveredFiles` lets a pack replace. A mod that isn't listed is only warned about."""
    path = root / "covered-files.json"
    if not path.is_file():
        return {}
    try:
        raw = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as ex:
        report.error("covered-files.json", f"not valid JSON: {ex}")
        return {}
    covered: dict[str, set[str]] = {}
    for mod, files in (raw.items() if isinstance(raw, dict) else []):
        if isinstance(files, list):
            covered[mod.lower()] = {(normalize_relative(f) or "").lower() for f in files if isinstance(f, str)}
    return covered


def git_lines(root: Path, *args: str) -> list[str] | None:
    try:
        out = subprocess.run(["git", *args], cwd=root, check=True, capture_output=True, text=True)
    except (OSError, subprocess.CalledProcessError) as ex:
        detail = getattr(ex, "stderr", "") or str(ex)
        print(f"git {' '.join(args)} failed: {detail.strip()}", file=sys.stderr)
        return None
    return [line for line in out.stdout.splitlines() if line.strip()]


def published_at_base(root: Path, base: str | None) -> set[str] | None:
    if not base:
        return None
    lines = git_lines(root, "ls-tree", "-r", "--name-only", base, "--", "translations")
    return set(lines) if lines is not None else None


def check_immutability(root: Path, base: str, allow_modify: bool, report: Report) -> None:
    lines = git_lines(root, "diff", "--name-status", "--no-renames", f"{base}...HEAD", "--", "translations")
    if lines is None:
        report.error("translations", f"could not compare with {base} — fetch it (actions/checkout fetch-depth: 0)")
        return
    for line in lines:
        status, _, path = line.partition("\t")
        if status.startswith("A"):
            continue
        what = {"M": "changed", "D": "deleted", "T": "changed"}.get(status[:1], status)
        message = (f"a published file was {what}. Published versions are immutable — players already "
                   f"follow them — so ship the fix as a NEW version folder")
        if allow_modify:
            report.warning(path, message + " (allowed by the maintainer override)")
        else:
            report.error(path, message + " (a maintainer can allow it with the 'allow-modify-published' label)")


def check_zip(where: str, zip_path: Path, outer: dict, report: Report) -> None:
    size = zip_path.stat().st_size
    if size > MAX_ZIP_BYTES:
        report.error(where, f"the zip is {size} bytes; the launcher's limit is {MAX_ZIP_BYTES}")
        return
    with zip_path.open("rb") as fh:
        if fh.read(4) != b"PK\x03\x04":
            report.error(where, "not a zip file (it doesn't start with PK\\x03\\x04)")
            return
    try:
        archive = zipfile.ZipFile(zip_path)
    except zipfile.BadZipFile as ex:
        report.error(where, f"unreadable zip: {ex}")
        return

    with archive:
        listed = {}
        for f in outer.get("files") or []:
            if isinstance(f, dict) and isinstance(f.get("path"), str):
                listed[PurePosixPath(f["path"].replace("\\", "/")).name.lower()] = f

        names_seen: set[str] = set()
        manifest_entries = []
        file_entries = {}
        for info in archive.infolist():
            name = info.filename
            if name.endswith("/"):
                report.error(where, f"the zip contains a folder ('{name}'); packs must be flat")
                continue
            if "/" in name or "\\" in name:
                report.error(where, f"'{name}' is inside a folder; the launcher only reads files at the zip's root")
                continue
            key = name.lower()
            if key in names_seen:
                report.error(where, f"the zip contains '{name}' more than once")
                continue
            names_seen.add(key)
            if key == MANIFEST:
                manifest_entries.append(info)
            elif key in listed:
                file_entries[key] = info
            else:
                report.error(where, f"'{name}' isn't listed in translation.json — the launcher ignores it; remove it")

        if len(manifest_entries) != 1:
            report.error(where, "the zip must contain exactly one translation.json at its root")
            return
        if manifest_entries[0].file_size > MAX_MANIFEST_BYTES:
            report.error(where, "the zip's translation.json is larger than 256 KiB")
            return
        try:
            inner = load_json_bytes(archive.read(manifest_entries[0]))
        except ValueError as ex:
            report.error(where, f"the zip's translation.json is not valid JSON: {ex}")
            return
        if not isinstance(inner, dict):
            report.error(where, "the zip's translation.json is not an object")
            return

        # The outer manifest is the LISTING; the launcher verifies the zip against it.
        if inner.get("id") != outer.get("id"):
            report.error(where, f"the zip's translation.json says id '{inner.get('id')}', the folder's says '{outer.get('id')}'")
        if (inner.get("targetMod") or "") != (outer.get("targetMod") or ""):
            report.error(where, "the zip's translation.json names another targetMod than the folder's")
        if effective_content_hash(inner) != effective_content_hash(outer):
            report.error(where, "the zip's translation.json has another content hash than the folder's — "
                                "the launcher would refuse the download as 'not the version that was listed'")
        if (inner.get("version") or "") != (outer.get("version") or ""):
            report.warning(where, "the zip's translation.json has another version than the folder's (tolerated)")

        inner_files = {}
        for f in inner.get("files") or []:
            if isinstance(f, dict) and isinstance(f.get("path"), str):
                inner_files[PurePosixPath(f["path"].replace("\\", "/")).name.lower()] = f

        total = 0
        for key, f in listed.items():
            info = file_entries.get(key)
            if info is None:
                report.error(where, f"translation.json lists '{f['path']}' but the zip doesn't contain it")
                continue
            digest = hashlib.md5()
            read = 0
            with archive.open(info) as src:
                while True:
                    chunk = src.read(1 << 16)
                    if not chunk:
                        break
                    read += len(chunk)
                    total += len(chunk)
                    if read > MAX_ENTRY_BYTES or total > MAX_TOTAL_BYTES:
                        break
                    digest.update(chunk)
            if read > MAX_ENTRY_BYTES:
                report.error(where, f"'{info.filename}' inflates past {MAX_ENTRY_BYTES} bytes")
                continue
            if total > MAX_TOTAL_BYTES:
                report.error(where, f"the pack inflates past {MAX_TOTAL_BYTES} bytes")
                return
            expected = (inner_files.get(key) or {}).get("translatedHash") or ""
            if digest.hexdigest().lower() != str(expected).strip().lower():
                report.error(where, f"'{info.filename}' doesn't match the translatedHash its translation.json "
                                    f"records — the launcher refuses it as damaged")


def check_manifest_fields(where: str, manifest: dict, covered_map: dict[str, set[str]], report: Report) -> None:
    files = manifest.get("files")
    if not isinstance(files, list) or not files:
        report.error(where, "files must be a non-empty list")
        return
    target = manifest.get("targetMod") or ""
    covered = covered_map.get(target.lower()) if target else None
    if not target:
        report.warning(where, "no targetMod — accepted only from the mod's OWN translations repo; "
                              "any other source must name the mod")
    elif target.lower() not in covered_map and covered_map:
        report.warning(where, f"targetMod '{target}' isn't in covered-files.json, so its paths can't be checked")

    paths = set()
    for i, f in enumerate(files):
        if not isinstance(f, dict):
            report.error(where, f"files[{i}] is not an object")
            continue
        norm = normalize_relative(f.get("path"))
        if norm is None:
            report.error(where, f"files[{i}].path '{f.get('path')}' is not a plain install-relative path")
            continue
        if norm.lower() in paths:
            report.error(where, f"files[{i}].path '{norm}' is listed twice")
        paths.add(norm.lower())
        if covered is not None and norm.lower() not in covered:
            report.error(where, f"files[{i}].path '{norm}' is not one of {target}'s covered files "
                                f"({', '.join(sorted(covered))}) — the launcher refuses it")
        if not MD5_RE.match(str(f.get("translatedHash") or "")):
            report.error(where, f"files[{i}].translatedHash must be the file's MD5 (32 hex) — the launcher "
                                f"refuses a pack without it")

    names = [PurePosixPath(p).name for p in paths]
    if len(names) != len(set(names)):
        report.error(where, "two listed files share a file name; a flat zip can't hold both")

    declared = manifest.get("contentHash")
    if declared not in (None, ""):
        if not isinstance(declared, str) or not CONTENT_HASH_RE.match(declared):
            report.error(where, "contentHash must be 16 hex characters")
        elif declared.lower() != content_hash(files):
            report.error(where, f"contentHash is {declared} but the files say {content_hash(files)} — "
                                f"let the Packager write it")


def check_tree(root: Path, base_files: set[str] | None, covered_map: dict[str, set[str]], report: Report) -> None:
    tdir = root / "translations"
    if not tdir.is_dir():
        report.warning("translations", "no translations/ folder yet")
        return
    validator = load_schema(root, "translation.schema.json")

    for path in sorted(p for p in tdir.rglob("*") if p.is_file()):
        rel = path.relative_to(root).as_posix()
        parts = rel.split("/")
        if parts[-1] == ".gitkeep":
            continue
        if len(parts) != 4:
            if parts[-1] == MANIFEST:
                report.error(rel, "translation.json must be at translations/<id>/<version>/translation.json — "
                                  "the launcher ignores it here")
            else:
                report.error(rel, "unexpected file; translations/ holds only <id>/<version>/ folders")

    def has_files(d: Path) -> bool:
        # git keeps no empty folders, so one left behind by a local move is not part of the repo.
        return any(p.is_file() for p in d.rglob("*"))

    for id_dir in sorted(p for p in tdir.iterdir() if p.is_dir() and has_files(p)):
        pack_id = id_dir.name
        if not is_safe_segment(pack_id, ID_RE):
            report.error(id_dir.relative_to(root).as_posix(),
                         "folder name is not a valid pack id (letters, digits, '.', '_', '-'; starts with a letter or digit)")
            continue
        versions_seen: dict[str, str] = {}
        hashes_seen: dict[str, str] = {}
        for ver_dir in sorted(p for p in id_dir.iterdir() if p.is_dir() and has_files(p)):
            rel_dir = ver_dir.relative_to(root).as_posix()
            version = ver_dir.name
            if not is_safe_segment(version, VERSION_RE):
                report.error(rel_dir, "folder name is not a valid version (letters, digits, '.', '_', '-', '+')")
                continue
            if any(p.is_dir() for p in ver_dir.iterdir()):
                report.error(rel_dir, "a version folder holds only translation.json and its zip — no subfolders "
                                      "(the layout is translations/<id>/<version>/)")
                continue

            manifest_path = ver_dir / MANIFEST
            rel_manifest = manifest_path.relative_to(root).as_posix()
            is_new = base_files is not None and rel_manifest not in base_files
            if not manifest_path.is_file():
                report.error(rel_dir, "no translation.json")
                continue
            if manifest_path.stat().st_size > MAX_MANIFEST_BYTES:
                report.error(rel_manifest, "larger than 256 KiB")
                continue
            try:
                manifest = load_json_bytes(manifest_path.read_bytes())
            except ValueError as ex:
                report.error(rel_manifest, f"not valid JSON: {ex}")
                continue
            if not isinstance(manifest, dict):
                report.error(rel_manifest, "not a JSON object")
                continue
            for problem in schema_errors(validator, manifest):
                report.error(rel_manifest, f"schema: {problem}")

            if manifest.get("id") != pack_id:
                report.error(rel_manifest, f"id is '{manifest.get('id')}' but the folder is '{pack_id}' — "
                                           f"the launcher skips a folder whose name isn't its id")
            if manifest.get("version") != version:
                text = f"version is '{manifest.get('version')}' but the folder is '{version}'"
                if is_new:
                    report.error(rel_manifest, text + " — a new version's folder must be its version")
                else:
                    report.warning(rel_manifest, text)
            if is_new and not RECOMMENDED_VERSION_RE.search(version):
                report.warning(rel_manifest, f"recommended version shape is <mod version>-r<N>, e.g. 1.2.0e-r1")

            check_manifest_fields(rel_manifest, manifest, covered_map, report)

            key = version.lower()
            if key in versions_seen:
                report.error(rel_dir, f"version '{version}' already exists as '{versions_seen[key]}' (case-insensitive)")
            versions_seen[key] = version
            effective = effective_content_hash(manifest).lower()
            if effective in hashes_seen:
                report.error(rel_dir, f"same content as version '{hashes_seen[effective]}' — "
                                      f"a new version must contain a change")
            hashes_seen[effective] = version

            zip_name = manifest.get("zip") or f"{pack_id}.zip"
            if not isinstance(zip_name, str) or "/" in zip_name or "\\" in zip_name or not zip_name.lower().endswith(".zip"):
                report.error(rel_manifest, f"zip '{zip_name}' must be a plain .zip file name in the same folder")
                continue
            others = sorted(p.name for p in ver_dir.iterdir() if p.is_file() and p.name not in (MANIFEST, zip_name))
            for other in others:
                report.error(f"{rel_dir}/{other}", f"unexpected file; this folder holds translation.json and {zip_name}")
            zip_path = ver_dir / zip_name
            if not zip_path.is_file():
                report.error(rel_manifest, f"its zip '{zip_name}' is not in the folder")
                continue
            check_zip(zip_path.relative_to(root).as_posix(), zip_path, manifest, report)


def check_index(root: Path, report: Report) -> None:
    path = root / INDEX
    if not path.is_file():
        return
    try:
        index = load_json_bytes(path.read_bytes())
    except ValueError as ex:
        report.error(INDEX, f"not valid JSON: {ex}")
        return
    for problem in schema_errors(load_schema(root, "translations-index.schema.json"), index):
        report.error(INDEX, f"schema: {problem}")
    items = index.get("translations") if isinstance(index, dict) else None
    if not isinstance(items, list):
        report.error(INDEX, "needs a \"translations\" list")
        return
    if len(items) > MAX_INDEX_ITEMS:
        report.error(INDEX, f"more than {MAX_INDEX_ITEMS} items — the launcher reads only the first {MAX_INDEX_ITEMS}")
    seen = set()
    for i, item in enumerate(items):
        where = f"{INDEX} (item {i + 1})"
        if not isinstance(item, dict):
            report.error(where, "not an object")
            continue
        if not is_safe_segment(item.get("id"), ID_RE):
            report.error(where, "id is not a valid pack id")
        if not is_safe_segment(item.get("version"), VERSION_RE):
            report.error(where, "version is not valid")
        if not is_safe_segment(item.get("targetMod"), ID_RE):
            report.error(where, "targetMod is required (the mod id this pack is for)")
        sha = str(item.get("sha256") or "")
        if not SHA256_RE.match(sha):
            report.error(where, "sha256 is required (64 hex) — the launcher skips items without it")
        key = (str(item.get("id")).lower(), str(item.get("version")).lower())
        if key in seen:
            report.error(where, f"'{item.get('id')}' {item.get('version')} is listed twice — the launcher keeps the first")
        seen.add(key)

        zip_ref = item.get("zip")
        if not isinstance(zip_ref, str) or not zip_ref:
            report.error(where, "zip is required")
            continue
        if "://" in zip_ref:
            lowered = zip_ref.lower()
            if not lowered.startswith("https://"):
                report.error(where, "zip must be an https link")
            elif any(h in lowered.split("/")[2] for h in REFUSED_HOSTS):
                report.error(where, "zip is on a host the launcher can't download from (Mega, MediaFire, OneDrive, Docs)")
            continue
        local = normalize_relative(zip_ref)
        target = root / local if local else None
        if target is None or not target.is_file():
            report.error(where, f"zip '{zip_ref}' is not a file in this repository")
            continue
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        if SHA256_RE.match(sha) and digest != sha.lower():
            report.error(where, f"sha256 doesn't match {zip_ref} (it is {digest}) — players would get a mismatch error")
        size = item.get("size")
        if isinstance(size, int) and size and size != target.stat().st_size:
            report.warning(where, f"size is {size} but {zip_ref} is {target.stat().st_size} bytes")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", default=".", help="repository root (default: current folder)")
    parser.add_argument("--base", help="git ref to compare with; published files may not change")
    parser.add_argument("--allow-modify", action="store_true",
                        help="report changes to published files as warnings (maintainer override)")
    args = parser.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    root = Path(args.root).resolve()
    report = Report()
    covered = load_covered(root, report)
    base_files = published_at_base(root, args.base)
    if args.base and base_files is None:
        report.error("translations", f"could not read {args.base} — fetch it (actions/checkout fetch-depth: 0)")

    check_tree(root, base_files, covered, report)
    check_index(root, report)
    if args.base and base_files is not None:
        check_immutability(root, args.base, args.allow_modify, report)

    print(f"\n{report.errors} error(s), {report.warnings} warning(s).")
    return 1 if report.errors else 0


if __name__ == "__main__":
    sys.exit(main())
