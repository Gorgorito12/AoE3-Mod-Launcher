"""Opens every screen and dialog the launcher can paint on its own, and for each one keeps a
capture of every window and the layout probe's report (Services/LayoutAudit.cs).

    python -I tools/testbed/sweep_screens.py OUTDIR [--size 1366x768] [--lang es] [--only NAME...]
                                                    [--glass solid|mid|max] [--software]

The list below is every `--demo-*`, `--preview-*` and `--open-*` the launcher answers in harness
mode. A screen whose probe finds a problem is printed; the captures are for looking, the probe
lines are leads. Nothing outside the harness is touched.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

sys.stdout.reconfigure(errors="replace")
HERE = Path(__file__).resolve().parent
TESTBED = [sys.executable, "-I", str(HERE / "testbed.py")]

SCREENS = [
    ("library", []),
    ("library-updating", ["--preview-library=updating"]),
    ("library-installing", ["--preview-library=installing"]),
    ("library-stopped", ["--preview-library=stopped"]),
    ("workshop", ["--open-tab=workshop"]),
    ("multiplayer", ["--open-tab=multiplayer"]),
] + [(f"rooms-{s}", [f"--demo-rooms={s}"]) for s in ("86a", "86b", "86c", "86d", "86e")] \
  + [(f"tournaments-{s}", [f"--demo-tournaments={s}"]) for s in ("big32", "teams", "myroom", "waiting", "registration", "finished", "dialog")] \
  + [(f"elo-{s}", [f"--demo-elo={s}"]) for s in ("93a", "93b", "93c", "93d", "93e", "93f", "93g", "profile", "refund", "room1v1",
                                                  "roomteams", "countdown", "result", "history", "highlights", "rankingmatches",
                                                  "profile:nogames", "profile:placing", "profile:inactive",
                                                  "profile:achievements", "profile:achievements-tad",
                                                  "profile:achievements-more")] \
  + [(f"stats-{s}", [f"--demo-stats={s}"]) for s in ("full", "empty", "other")] \
  + [(f"room-{s}", [f"--demo-room={s}"]) for s in ("87a", "87b", "87c", "87d", "87e", "87f", "87g", "87h", "87i", "87j", "87k",
                                                    "87l", "87live", "87b-farm", "87b-hidden", "87b-private", "87nocity",
                                                    "85a", "85b", "85c", "85d", "85e", "85f", "85g", "85h", "85i")] \
  + [(f"settings-{s}", [f"--open-settings={s}"]) for s in ("general", "interface", "games", "mods", "advanced")] \
  + [(f"modsettings-{s}", [f"--open-mod-settings={s}"]) for s in ("general", "local-files", "user-data", "language", "addons", "decks")] \
  + [(f"small-{s}", [f"--preview-dialog=small:{s}"]) for s in "abcdefghijklmno"] \
  + [(f"dialog-{s.replace(':', '-')}", [f"--preview-dialog={s}"]) for s in (
      "signin", "createroom", "lanhelper", "lanhelper:tad", "lanhelper:manual", "lanhelper:manual-admin", "lanhelper:manual-ok",
      "newtournament:a", "newtournament:b", "newtournament:c", "newtournament:soi", "newtournament:pick",
      "enroll:a", "enroll:c", "enroll:f", "enroll:j", "teams:a", "teams:d", "teams:h",
      "matchfound", "matchfound:guest", "matchfound:placing", "matchfound:casual",
      "matchwatch", "matchwatch:result", "packager", "patchgen", "selfinstall", "selfinstall:lowdisk", "selfuninstall",
      "launcherupdate", "launcherupdate:link", "launcherupdate:busy", "settingsupdate",
      "radmin:offline", "radmin:loggedin", "radmin:connected", "radmin:steps",
      "publish:1", "publish:3", "publish:6", "installfolder", "updatepill:available", "updatepill:ready",
      "bar:offline", "bar:outnet", "bar:account-room",
      "achievement:card", "achievement:notice", "achievement:several")] \
  + [(f"toasts-{s}", [f"--preview-toasts={s}"]) for s in ("1", "2", "3")] \
  + [(f"antivirus-{s}", [f"--preview-antivirus={s}"]) for s in ("notice", "blocked", "notice-one")] \
  + [(f"uninstall-{s}", [f"--preview-uninstall={s}"]) for s in ("valid", "copy", "userdata", "overlay", "invalid", "base", "nothing")]


def testbed(*args, timeout=180):
    # The titles carry accents and a middle dot: the child has to print UTF-8 for them to survive.
    return subprocess.run([*TESTBED, *args], capture_output=True, text=True, encoding="utf-8",
                          errors="replace", timeout=timeout, env={**os.environ, "PYTHONIOENCODING": "utf-8"})


def windows():
    rows = []
    for line in testbed("windows").stdout.strip().splitlines():
        m = re.match(r"(\d+) \([^)]*\) ?(.*)", line)
        if m:
            rows.append((int(m.group(1)), m.group(2)))
    return rows


def title_key(title, others):
    """A piece of the title that no other window's title contains. The testbed prints titles in
    the console's code page, so a "·" or an accent arrives mangled and a whole title never
    matches; a plain-ASCII run of it does."""
    runs = sorted((r.strip() for r in re.findall(r"[ -~]{4,}", title)), key=len, reverse=True)
    for run in runs:
        if run and not any(run in o for o in others if o != title):
            return run
    return runs[0] if runs else title


def main():
    p = argparse.ArgumentParser()
    p.add_argument("outdir")
    p.add_argument("--size", default="1366x768")
    p.add_argument("--lang", default="es")
    p.add_argument("--glass")
    p.add_argument("--text")
    p.add_argument("--effects")
    p.add_argument("--max", action="store_true")
    p.add_argument("--software", action="store_true")
    p.add_argument("--settle", type=float, default=4.0)
    p.add_argument("--only", nargs="*")
    args = p.parse_args()
    out = Path(args.outdir)
    out.mkdir(parents=True, exist_ok=True)
    summary = {}
    for name, rest in SCREENS:
        if args.only and not any(name.startswith(o) for o in args.only):
            continue
        launch = ["launch", "--size", args.size, "--lang", args.lang]
        if args.glass:
            launch += ["--glass", args.glass]
        if args.text:
            launch += ["--text", args.text]
        if args.effects:
            launch += ["--effects", args.effects]
        if args.max:
            launch += ["--max"]
        if args.software:
            launch += ["--software"]
        testbed("close")  # a launcher left open by an interrupted run would refuse the next start
        started = testbed(*launch, "--", *rest)
        if "ready" not in started.stdout:
            print(f"{name:34} DID NOT START {started.stdout.strip()[-120:]} {started.stderr.strip()[-200:]}")
            summary[name] = {"error": "did not start"}
            testbed("close")
            continue
        time.sleep(args.settle)
        report = out / f"{name.replace(':', '-')}.audit.txt"
        audited = testbed("audit", str(report), "--all")
        text = report.read_text(encoding="utf-8-sig", errors="replace") if report.exists() else audited.stdout + audited.stderr
        problems = [l.strip() for l in text.splitlines() if l.startswith("  ") and not l.strip().startswith(("TRIM", "NEAR"))]
        shots = []
        listed = windows()
        titles = [t for _, t in listed]
        for i, (_, title) in enumerate(listed):
            # No ":" in a file name: on NTFS it names a hidden stream of another file.
            shot = out / f"{name.replace(':', '-')}.{i}.png"
            key = title_key(title, titles)
            testbed("shot", str(shot), "--title", key, "--screen")
            if not shot.exists():
                testbed("shot", str(shot), "--title", key)
            if shot.exists():
                shots.append(shot.name)
        unhandled = [l for l in testbed("log", "--grep", "UNHANDLED|Unhandled exception|CRASH").stdout.splitlines() if l.strip()]
        testbed("close")
        summary[name] = {"problems": problems, "shots": shots, "unhandled": unhandled}
        flag = f"{len(problems)} problems" + (f", {len(unhandled)} UNHANDLED" if unhandled else "")
        print(f"{name:34} {flag:22} {', '.join(shots)}", flush=True)
        for line in problems[:12]:
            print("    " + line[:200])
    (out / "summary.json").write_text(json.dumps(summary, indent=1, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    main()
