"""
Validate that icon.png and banner.png in every mod folder meet the spec
documented in CONTRIBUTING.md.

Specs (kept here as the single source of truth — keep CONTRIBUTING.md in sync):

  icon.png   (REQUIRED if the mod.json declares "icon")
    - PNG with alpha channel
    - exactly 256x256 px
    - <= 100 KB on disk

  banner.png/banner.jpg   (OPTIONAL)
    - PNG or JPEG
    - exactly 1200x300 px
    - <= 500 KB on disk

Image specs are enforced strictly — the workflow fails the PR if anything is
out of spec, with a list of every violation across every changed asset.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image


# Dimension/weight specs. (width, height, max_bytes, allowed_formats)
ICON_SPEC = (256, 256, 100 * 1024, {"PNG"})
BANNER_SPEC = (1200, 300, 500 * 1024, {"PNG", "JPEG"})


def check_icon(path: Path) -> list[str]:
    """Return a list of human-readable error strings (empty list = pass)."""
    width, height, max_bytes, formats = ICON_SPEC
    errors: list[str] = []

    size = path.stat().st_size
    if size > max_bytes:
        errors.append(
            f"{path}: file size {size:,} bytes exceeds limit of {max_bytes:,}"
        )

    try:
        img = Image.open(path)
    except Exception as e:
        errors.append(f"{path}: cannot open image — {e}")
        return errors

    if img.format not in formats:
        errors.append(
            f"{path}: format {img.format!r} not in allowed {sorted(formats)}"
        )

    if img.size != (width, height):
        errors.append(
            f"{path}: dimensions {img.size} != required ({width}, {height})"
        )

    # Icons are rendered against a dark background — the mod looks broken
    # without transparency. Catch this up front rather than at runtime.
    if "A" not in img.getbands():
        errors.append(f"{path}: PNG must have an alpha channel for transparency")

    return errors


def check_banner(path: Path) -> list[str]:
    width, height, max_bytes, formats = BANNER_SPEC
    errors: list[str] = []

    size = path.stat().st_size
    if size > max_bytes:
        errors.append(
            f"{path}: file size {size:,} bytes exceeds limit of {max_bytes:,}"
        )

    try:
        img = Image.open(path)
    except Exception as e:
        errors.append(f"{path}: cannot open image — {e}")
        return errors

    if img.format not in formats:
        errors.append(
            f"{path}: format {img.format!r} not in allowed {sorted(formats)}"
        )

    if img.size != (width, height):
        errors.append(
            f"{path}: dimensions {img.size} != required ({width}, {height})"
        )

    return errors


def main() -> int:
    mods_root = Path("mods")
    if not mods_root.is_dir():
        print("No mods/ folder yet — nothing to validate.")
        return 0

    all_errors: list[str] = []

    for mod_dir in sorted(p for p in mods_root.iterdir() if p.is_dir()):
        manifest_path = mod_dir / "mod.json"
        if not manifest_path.exists():
            # Schema validation in the same workflow run catches this; here
            # we just skip so the report is focused on image issues.
            continue

        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            # Likewise — leave the JSON parse error to the schema step.
            continue

        # Icon: required if declared.
        icon_name = manifest.get("icon")
        if icon_name:
            icon_path = mod_dir / icon_name
            if not icon_path.exists():
                all_errors.append(
                    f"{manifest_path}: declares icon {icon_name!r} but file is missing"
                )
            else:
                all_errors.extend(check_icon(icon_path))

        # Banner: optional.
        banner_name = manifest.get("banner")
        if banner_name:
            banner_path = mod_dir / banner_name
            if not banner_path.exists():
                all_errors.append(
                    f"{manifest_path}: declares banner {banner_name!r} but file is missing"
                )
            else:
                all_errors.extend(check_banner(banner_path))

    if all_errors:
        print("Image validation FAILED — fix these issues:")
        for err in all_errors:
            print(f"  - {err}")
        return 1

    print("All images pass spec validation.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
