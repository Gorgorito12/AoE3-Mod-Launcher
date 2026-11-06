"""A real mod in the sandbox, to press Repair for real: downloads the published payload of Age of
Empires 1: HD Edition once, lays it out as an install with a true manifest, and damages one file.

Run it between `launch` (which seeds out/run and out/sandbox) and `launch --keep`. It writes only
under out/. This mod was chosen because it needs no HKLM key, so Repair asks for no elevation.
"""
import hashlib
import json
import pathlib
import urllib.request
import zipfile

HERE = pathlib.Path(__file__).parent
OUT = HERE / "out"
CACHE = OUT / "cache"
SANDBOX = OUT / "sandbox"
INSTALL = SANDBOX / "AoE1 HD"
MOD = "aoe1-hd-edition"
URL = "https://github.com/papillo12/AgeofEmpires1-HDEdition/releases/download/2.0/AoE1HDEdition-2.0-win11-payload.zip"
ZIP = CACHE / "aoe1-hd-payload.zip"
SHA = "4e79fa222d4c9f27512a6fb19b63c8e786060e52ce8dc62fb5d8950d7488295c"
DAMAGED = "AoE1ModVersion.txt"


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    CACHE.mkdir(parents=True, exist_ok=True)
    if not ZIP.exists() or sha256(ZIP) != SHA:
        print("downloading the payload")
        request = urllib.request.Request(URL, headers={"User-Agent": "aoe3-launcher-testbed"})
        with urllib.request.urlopen(request) as answer, open(ZIP, "wb") as target:
            while chunk := answer.read(1 << 20):
                target.write(chunk)
        assert sha256(ZIP) == SHA, "the payload does not match its published hash"
    INSTALL.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(ZIP) as archive:
        archive.extractall(INSTALL)
    for name in ("RockallDLL.dll", "binkw32.dll", "granny2.dll", "deformerdlly.dll", "age3y.exe"):
        (INSTALL / name).touch()

    files = [p for p in INSTALL.rglob("*") if p.is_file() and p.name != "install-manifest.json"]
    hashes = {}
    with zipfile.ZipFile(ZIP) as archive:
        shipped = {name.replace("\\", "/") for name in archive.namelist() if not name.endswith("/")}
    for path in files:
        rel = path.relative_to(INSTALL).as_posix()
        if rel in shipped:
            hashes[rel] = {"size": path.stat().st_size, "sha256": sha256(path)}
    (INSTALL / "install-manifest.json").write_text(json.dumps({
        "modId": MOD, "version": "2.0", "installPath": str(INSTALL), "appName": "Age of Empires 1: HD Edition",
        "installedAt": "2026-11-01T12:00:00Z", "clonedAoe3": True,
        "files": [], "overlayFiles": sorted(shipped), "overlayNetNew": sorted(shipped), "fileHashes": hashes,
    }), encoding="utf-8")

    damaged = INSTALL / DAMAGED
    before = damaged.read_bytes()
    damaged.write_bytes(b"damaged by the testbed" + before)

    config_path = OUT / "run" / "launcher-config.json"
    config = json.loads(config_path.read_text(encoding="utf-8"))
    config["activeModId"] = MOD
    state = config["mods"].setdefault(MOD, {})
    state["installPath"] = str(INSTALL)
    state["lastKnownVersion"] = "2.0"
    ids = config.setdefault("userModIds", [])
    if MOD not in ids:
        ids.append(MOD)
    config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")
    print(f"ready: {len(hashes)} files recorded, {DAMAGED} damaged, was {hashlib.sha256(before).hexdigest()}")


main()
