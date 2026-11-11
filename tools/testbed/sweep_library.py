"""Presses every control of the Library (design 68a) on the real launcher and checks what each did.

    python -I tools/testbed/sweep_library.py

A control with no case here is a failure of the screen, so this list grows with it. Each case is
(what to press, what must then be true). Nothing outside the sandbox is touched: a button that
would leave the launcher ends in a HARNESS BLOCKED line, which is what proves it.
"""
import json
import subprocess
import sys
import time

sys.stdout.reconfigure(errors="replace")
from pathlib import Path

HERE = Path(__file__).resolve().parent
TESTBED = [sys.executable, "-I", str(HERE / "testbed.py")]
LOG = HERE / "out" / "run" / "launcher-debug.log"
failures = []


def testbed(*args):
    return subprocess.run([*TESTBED, *args], capture_output=True, text=True, encoding="utf-8", errors="replace")


def uia(*args):
    out = testbed("uia", *args).stdout.strip()
    try:
        return json.loads(out)
    except ValueError:
        return {"ok": False, "raw": out[:200]}


def controls():
    return {c["id"]: c for c in uia("tree") if isinstance(c, dict) and c.get("id")}


def log_text():
    return LOG.read_text(encoding="utf-8", errors="replace")


def check(name, ok, detail=""):
    print(("ok    " if ok else "FAIL  ") + name + (f"  ({detail})" if detail else ""))
    if not ok:
        failures.append(name)


def press(control, settle=.7):
    result = uia("invoke", "-Id", control)
    time.sleep(settle)
    return bool(result.get("ok"))


def press_and_log(name, control, needle):
    before = len(log_text())
    pressed = press(control, 1.0)
    check(name, pressed and needle in log_text()[before:], needle)


def windows():
    return testbed("windows").stdout


def main():
    testbed("close")
    started = testbed("launch", "--size", "1920x1080", "--lang", "es")
    check("launch", "ready" in started.stdout, started.stdout.strip()[:80])
    time.sleep(1.5)

    tree = controls()
    required = ["LibrarySearchBox", "LibrarySortButton", "LibraryRefreshButton", "LibraryPublishButton",
                "LibraryRailFold", "ModRow_wol", "ModStar_wol", "DashboardPlayButton", "DashboardSettingsButton",
                "DashboardCopyChip", "LibraryFilter_All", "LibraryFilter_Installed", "LibraryFilter_NotInstalled",
                "LibraryFilter_Updates", "LibraryFilter_Compatible", "LibraryLink_0", "TopTabPlay", "TopTabMultiplayer"]
    for control in required:
        check(f"present {control}", control in tree)
    check("Workshop tab is gone", "TopTabMods" not in tree or not tree["TopTabMods"]["visible"])

    # The filters and the search narrow the list.
    press("LibraryFilter_NotInstalled")
    now = controls()
    check("filter hides installed mods", "ModRow_improvement-mod" not in now and "ModRow_kings-return" in now)
    press("LibraryFilter_Updates")
    now = controls()
    check("no updates shows the empty list", not [i for i in now if i.startswith("ModRow_")])
    press("LibraryFilter_All")
    uia("type", "-Id", "LibrarySearchBox", "-Value", "hundred")
    time.sleep(.8)
    now = controls()
    rows = [i for i in now if i.startswith("ModRow_")]
    check("search narrows to one", rows == ["ModRow_hundred-days"], str(rows))
    uia("type", "-Id", "LibrarySearchBox", "-Value", "")
    time.sleep(.8)

    # The star moves a mod to the favourites and back.
    press("ModStar_improvement-mod")
    config = json.loads((HERE / "out" / "run" / "launcher-config.json").read_text(encoding="utf-8"))
    time.sleep(.5)
    config = json.loads((HERE / "out" / "run" / "launcher-config.json").read_text(encoding="utf-8"))
    check("star saves the favourite", "improvement-mod" in config.get("favoriteModIds", []))
    press("ModStar_improvement-mod")

    # Fold and unfold.
    # Relative, not absolute: the window opens on whichever monitor the testbed picks (the
    # CORSAIR sits far to the right), so a fixed screen x says nothing. Folding narrows the
    # list, so its button moves left.
    unfolded = controls().get("LibraryRailFold")
    press("LibraryRailFold", 1.0)
    rail = controls().get("LibraryRailFold")
    check("fold narrows the list",
          unfolded is not None and rail is not None and rail["rect"][0] < unfolded["rect"][0] - 200,
          f"{unfolded and unfolded['rect'][0]} -> {rail and rail['rect'][0]}")
    press("LibraryRailFold", 1.0)

    # The sort is a menu.
    press("LibrarySortButton", .5)
    check("sort opens", "LibrarySort_Name" in controls())
    press("LibrarySort_Name", .5)
    press("LibrarySortButton", .5)
    press("LibrarySort_Recent", .5)

    # Selecting and what each state offers.
    press("ModRow_kings-return", 2.5)
    now = controls()
    check("not installed offers Install", "DashboardPlayButton" in now)
    check("not installed offers its folder", "DashboardSearchInstallButton" in now)
    check("not installed hides settings", "DashboardSettingsButton" not in now)
    press_and_log("choose folder stays in the harness", "DashboardSearchInstallButton", "HARNESS BLOCKED folder picker")

    # The pictures under the buttons (68a): a mod with a cover and a banner shows both, one with
    # only a cover shows none, and a thumbnail opens the viewer, which steps and closes.
    press("ModRow_hundred-days", 2.5)
    now = controls()
    check("cover and banner make a gallery", "LibraryShot_0" in now and "LibraryShot_1" in now)
    press("LibraryShot_0", 1.0)
    now = controls()
    check("a thumbnail opens the viewer", "LibraryShotClose" in now and "LibraryShotNext" in now)
    press("LibraryShotNext", .6)
    check("the viewer steps", "LibraryShotClose" in controls())
    press("LibraryShotClose", .6)
    check("the viewer closes", "LibraryShotClose" not in controls())
    press("ModRow_wol", 2.0)
    check("a cover alone is no gallery", "LibraryShot_0" not in controls())

    press_and_log("a link leaves through SafeUrl", "LibraryLink_0", "HARNESS BLOCKED")
    press_and_log("Play is the real launch", "DashboardPlayButton", "HARNESS BLOCKED")

    # Copies.
    press("DashboardCopyChip", .6)
    now = controls()
    check("copies list both", "LibraryCopy_0" in now and "LibraryCopy_1" in now)
    press("LibraryCopy_1", 2.0)
    config = json.loads((HERE / "out" / "run" / "launcher-config.json").read_text(encoding="utf-8"))
    check("switched copy", config["mods"]["wol"]["installPath"].endswith("WoL Torneo"), config["mods"]["wol"]["installPath"])
    press("DashboardCopyChip", .6)
    press("LibraryCopy_1", 2.0)

    # Settings opens the mod's window.
    press("DashboardSettingsButton", 1.5)
    check("settings window", len(windows().strip().splitlines()) > 1, windows().strip()[:160])

    # Tabs.
    press("TopTabMultiplayer", 1.0)
    now = controls()
    check("multiplayer hides the Library", "LibrarySearchBox" not in now)
    press("TopTabPlay", 1.0)
    check("back on the Library", "LibrarySearchBox" in controls())

    crash = [l for l in log_text().splitlines() if "UNHANDLED" in l]
    check("no unhandled exception", not crash, crash[0][:120] if crash else "")
    testbed("close")
    print(f"\n{len(failures)} failed" if failures else "\nall passed")
    sys.exit(1 if failures else 0)


main()
