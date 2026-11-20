"""Opens and closes one window many times and reads the launcher's memory as it goes: a window
that is never freed shows as memory and handles that climb by the same step every round.

    python -I tools/testbed/leak_loop.py NAME --open ID [ID ...] [--rounds 30] [--every 5] -- LAUNCH ARGS

Each round presses the controls of --open in order (an AutomationId, or name:<caption>), waits,
then closes every window that was not there before (WM_CLOSE, the same as its X). Every --every
rounds it asks the launcher for `testbed.py memory`, which reads AFTER a full collection, so a
figure that grows is memory something still holds. Writes out/leak-NAME.json.
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
user32 = ctypes.windll.user32
WM_CLOSE = 0x0010


def testbed(*args, timeout=120):
    return subprocess.run([*TESTBED, *args], capture_output=True, text=True, encoding="utf-8",
                          errors="replace", timeout=timeout, env={**os.environ, "PYTHONIOENCODING": "utf-8"})


def windows():
    rows = []
    for line in testbed("windows").stdout.strip().splitlines():
        m = re.match(r"(\d+) \([^)]*\) ?(.*)", line)
        if m:
            rows.append((int(m.group(1)), m.group(2)))
    return rows


def press(step):
    args = ["-Id", step[5:], "-ByName"] if step.startswith("name:") else ["-Id", step]
    out = testbed("uia", "invoke", *args, "-Timeout", "4000").stdout.strip()
    return '"ok":true' in out.replace(" ", "")


def reading():
    out = testbed("memory").stdout.strip()
    try:
        return json.loads(out)
    except ValueError:
        return {"error": out[:200]}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("name")
    p.add_argument("--open", nargs="+", required=True)
    p.add_argument("--rounds", type=int, default=30)
    p.add_argument("--every", type=int, default=5)
    p.add_argument("--wait", type=float, default=1.5)
    p.add_argument("--size", default="1366x768")
    p.add_argument("rest", nargs="*")
    args = p.parse_args()

    testbed("close")
    if "ready" not in testbed("launch", "--size", args.size, "--", *args.rest, timeout=180).stdout:
        sys.exit("leak: the launcher did not start")
    time.sleep(4)
    base = {h for h, _ in windows()}
    # One warm-up round: the first open loads code, fonts and images that are kept for good.
    rows = []
    for round_ in range(args.rounds + 1):
        ok = all(press(step) or time.sleep(0.5) for step in args.open)
        time.sleep(args.wait)
        opened = [(h, t) for h, t in windows() if h not in base]
        for h, _ in opened:
            user32.PostMessageW(h, WM_CLOSE, 0, 0)
        time.sleep(0.8)
        if round_ == 0 or round_ % args.every == 0:
            r = reading()
            r.update(round=round_, opened=len(opened), pressed=ok)
            rows.append(r)
            print(json.dumps(r))
    testbed("close")
    first, last = rows[0], rows[-1]
    keys = ("managed_mb", "private_mb", "handles", "gdi", "user", "threads", "windows")
    delta = {k: round(last.get(k, 0) - first.get(k, 0), 2) for k in keys if k in first and k in last}
    per_round = {k: round(v / max(1, args.rounds), 3) for k, v in delta.items()}
    print(f"\nafter {args.rounds} rounds (warm-up excluded): {delta}\nper round: {per_round}")
    (HERE / "out" / f"leak-{args.name}.json").write_text(
        json.dumps({"rows": rows, "delta": delta, "per_round": per_round}, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
