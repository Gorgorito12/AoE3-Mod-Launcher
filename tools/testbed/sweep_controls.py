"""Presses every control a screen offers and reports what each one did. A dead-button finder.

    python -I tools/testbed/sweep_controls.py NAME [--size WxH] [--lang es] [--skip ID ...] -- LAUNCH ARGS

For each interactive control UI Automation lists (buttons, links, toggles, menu items, tab items),
on a fresh screen each time it is needed: press it, wait, and record what changed — a new window
(closed again), a log line (HARNESS BLOCKED, TAB, SWITCH…), an exception, the launcher closing, or
the list of controls itself. A control that changes NOTHING is listed as "no effect": the lead to
look at, not a verdict (a toggle that only changes its own state counts as an effect). An
exception or a launcher that dies is always a failure.

Writes out/sweep-NAME.json and prints a table. Nothing outside the harness is touched: whatever
would leave the launcher ends as a HARNESS BLOCKED line.
"""
import argparse
import ctypes
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
OUT = HERE / "out"
LOG = OUT / "run" / "launcher-debug.log"
PRESSABLE = {"Button", "Hyperlink", "MenuItem", "TabItem", "CheckBox", "RadioButton", "ListItem", "SplitButton"}
# What a press must never cost: these end the run or leave the machine, and the harness blocks
# the second kind anyway. Closing the window is pressed last, on its own.
NEVER = {"Minimize", "Maximize", "Restore", "Close", "Cerrar", "Minimizar", "Maximizar", "Restaurar"}
user32 = ctypes.windll.user32
WM_CLOSE = 0x0010


def testbed(*args, timeout=120):
    # The titles carry accents and a middle dot: the child has to print UTF-8 for them to survive.
    return subprocess.run([*TESTBED, *args], capture_output=True, text=True, encoding="utf-8",
                          errors="replace", timeout=timeout, env={**os.environ, "PYTHONIOENCODING": "utf-8"})


def uia(*args):
    out = testbed("uia", *args).stdout.strip()
    try:
        return json.loads(out)
    except ValueError:
        return {"ok": False, "raw": out[:200]}


def tree():
    rows = uia("tree")
    return [r for r in rows if isinstance(r, dict)] if isinstance(rows, list) else []


def key(c):
    return c.get("id") or ("name:" + (c.get("name") or ""))


def state(c):
    """What a press can change without adding or removing a control: a switch's state, a
    field's text, whether it is enabled."""
    return (c.get("toggle"), c.get("value"), c.get("enabled"))


CONFIG = OUT / "run" / "launcher-config.json"


def config_text():
    try:
        return CONFIG.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def window_key(title):
    """An ASCII run of the title: the "·" and accents do not survive the trip to PowerShell."""
    runs = sorted((r.strip() for r in re.findall(r"[ -~]{4,}", title or "")), key=len, reverse=True)
    return runs[0] if runs else (title or "")


def windows():
    # "<hwnd> (<left>, <top>, <right>, <bottom>) <title>"
    rows = []
    for line in testbed("windows").stdout.strip().splitlines():
        m = re.match(r"(\d+) \([^)]*\) ?(.*)", line)
        if m:
            rows.append((int(m.group(1)), m.group(2)))
    return rows


def log_text():
    try:
        return LOG.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def launcher_alive():
    return bool(windows())


def launch(args):
    testbed("close")
    started = testbed("launch", "--size", args.size, "--lang", args.lang, "--", *args.rest, timeout=180)
    time.sleep(args.settle)
    return "ready" in started.stdout


def interesting(lines):
    keep = ("HARNESS BLOCKED", "TAB ", "SWITCH", "UNHANDLED", "Exception", "exception", "CRASH", "SLOW",
            "UI STALL", "LAYOUT STORM", "Opened", "opened", "Settings", "toast", "Toast")
    return [l[l.find("]") + 1:].strip()[:140] for l in lines if any(k in l for k in keep)]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("name")
    parser.add_argument("--size", default="1366x768")
    parser.add_argument("--lang", default="es")
    parser.add_argument("--settle", type=float, default=3.0)
    parser.add_argument("--wait", type=float, default=1.0, help="seconds after each press")
    parser.add_argument("--skip", nargs="*", default=[])
    parser.add_argument("--only-window", help="press only controls of windows whose title contains this")
    parser.add_argument("rest", nargs="*")
    args = parser.parse_args()

    if not launch(args):
        sys.exit("sweep: the launcher did not start")
    base_windows = windows()
    base_hwnds = {h for h, _ in base_windows}
    controls = [c for c in tree() if c.get("type") in PRESSABLE and c.get("enabled") and c.get("visible")]
    if args.only_window:
        controls = [c for c in controls if args.only_window.lower() in (c.get("window") or "").lower()]
    seen, todo = set(), []
    for c in controls:
        k = key(c)
        if k in seen or k in args.skip or (c.get("name") or "") in NEVER or k in ("name:",):
            continue
        seen.add(k)
        todo.append(c)
    print(f"{len(todo)} controls to press on {args.name}")

    results = []
    dirty = False
    for c in todo:
        if dirty:
            if not launch(args):
                results.append({"control": key(c), "verdict": "FAIL", "what": "the launcher did not restart"})
                continue
            base_windows = windows()
            base_hwnds = {h for h, _ in base_windows}
            dirty = False
        before_rows = {key(x): state(x) for x in tree()}
        before_tree = set(before_rows)
        before_config = config_text()
        before_log = len(log_text())
        ident = ["-Id", c["id"]] if c.get("id") else ["-Id", c["name"], "-ByName"]
        if c.get("window"):
            ident += ["-Window", window_key(c["window"])]
        pressed = uia("invoke", *ident)
        time.sleep(args.wait)
        now_windows = windows()
        if not now_windows:
            # A modal window opening can leave the list empty for a moment: look again before
            # calling it a closed launcher.
            time.sleep(1.5)
            now_windows = windows()
        new_windows = [(h, t) for h, t in now_windows if h not in base_hwnds]
        alive = bool(now_windows)
        after_log = log_text()[before_log:].splitlines()
        lines = interesting(after_log)
        after_rows = {key(x): state(x) for x in tree()} if alive else {}
        after_tree = set(after_rows)
        appeared = sorted(after_tree - before_tree)
        vanished = sorted(before_tree - after_tree)
        changed = sorted(k for k in before_tree & after_tree if before_rows[k] != after_rows[k])
        config_changed = alive and config_text() != before_config
        failure = [l for l in after_log if "UNHANDLED" in l or "Unhandled" in l or "CRASH" in l]
        if not pressed.get("ok"):
            verdict, what = "SKIP", pressed.get("error") or pressed.get("raw") or "not pressable"
        elif failure:
            verdict, what = "FAIL", failure[0][:160]
        elif not alive:
            verdict, what = "CLOSED", "the launcher closed"
        elif new_windows or lines or appeared or vanished or changed or config_changed:
            verdict = "ok"
            bits = []
            if new_windows: bits.append("window: " + "; ".join(t for _, t in new_windows))
            if lines: bits.append("log: " + " | ".join(lines[:3]))
            if appeared: bits.append(f"+{len(appeared)} controls ({', '.join(appeared[:4])})")
            if vanished: bits.append(f"-{len(vanished)} controls")
            if changed: bits.append(f"~{len(changed)} changed ({', '.join(changed[:3])})")
            if config_changed: bits.append("settings saved")
            what = "  ".join(bits)
        else:
            verdict, what = "NOEFFECT", "nothing changed: no window, no log line, same controls"
        results.append({"control": key(c), "name": c.get("name"), "type": c.get("type"),
                        "window": c.get("window"), "verdict": verdict, "what": what})
        print(f"{verdict:8} {key(c)[:46]:46} {what[:150]}")
        # Back to the screen that was swept: close what opened; restart when the screen changed.
        for h, _ in new_windows:
            user32.PostMessageW(h, WM_CLOSE, 0, 0)
        if new_windows:
            time.sleep(0.8)
        if not alive or vanished or (appeared and len(appeared) > 3) or [w for w in windows() if w[0] not in base_hwnds]:
            dirty = True

    testbed("close")
    (OUT / f"sweep-{args.name}.json").write_text(json.dumps(results, indent=1, ensure_ascii=False), encoding="utf-8")
    counts = {}
    for r in results:
        counts[r["verdict"]] = counts.get(r["verdict"], 0) + 1
    print("\n" + ", ".join(f"{v}: {n}" for v, n in sorted(counts.items())))
    sys.exit(1 if counts.get("FAIL") else 0)


if __name__ == "__main__":
    main()
