"""A whole multiplayer match between two accounts on one machine, checked step by step.

    python -I tools/testbed/scenario_two_accounts.py [--keep-server]

A local lobby server, three launchers (a hosts, b joins, c watches the global chat), and two
simulated games that leave real recordings behind, so everything after the game runs the
production code: reading the recording, the host's report, the guest's confirmation, both
civilizations, the result screen, the chat's room card going Join -> Full -> See match -> Closed,
and the rematch. About ten minutes: a match shorter than three minutes is never reported.

Each step says ok or FAIL with what it looked for. Nothing leaves this machine (see the README's
"Two accounts" section), and `safety check` runs at the end.
"""
import json
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(errors="replace")
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE / "out"
TESTBED = [sys.executable, "-I", str(HERE / "testbed.py")]
FIRST = "2026-10-10_El_Taita-vs-SKIbidi_silkRoad.age3Yrec"
SECOND = "2026-10-20_El_Taita-vs-JosephMan_WOLblackforest.age3Yrec"
failures = []


def testbed(*args, account=None, check_ok=True):
    command = [*TESTBED, *(["--as", account] if account else []), *args]
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if check_ok and result.returncode != 0:
        sys.exit(f"scenario: `{' '.join(args)}` failed: {(result.stdout + result.stderr).strip()[-400:]}")
    return result.stdout


def uia(account, *args):
    out = testbed("uia", *args, account=account, check_ok=False).strip()
    try:
        return json.loads(out)
    except ValueError:
        return {"ok": False, "raw": out[:200]}


def log(account):
    path = OUT / f"run-{account}" / "launcher-debug.log"
    return path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""


def check(name, ok, detail=""):
    print(("ok    " if ok else "FAIL  ") + name + (f"  ({detail})" if detail else ""))
    if not ok:
        failures.append(name)
    return ok


def wait_for(what, predicate, seconds):
    deadline = time.time() + seconds
    while time.time() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(1)
    return None


def card_state(lobby):
    """What c's chat card for LOBBY says now, from its own log line: "Full" and "Closed" are text,
    not buttons, so UI Automation cannot read them."""
    states = re.findall(rf"Chat room card {lobby}: (\w+), (\w+(?: \w+)?)", log("c"))
    return " ".join(states[-1]) if states else ""


def play_match(recording, title):
    """a creates a ranked 1v1, b joins, both ready; returns the lobby id once both games ran."""
    testbed("fakegame", recording, "--slot", "1", account="a")
    testbed("fakegame", recording, "--slot", "2", account="b")
    before_a = len(log("a"))
    uia("a", "invoke", "-Id", "CreateRoomButton")
    time.sleep(2)
    uia("a", "invoke", "-Id", "Segment:OneVOne")
    uia("a", "invoke", "-Id", "CreateButton")
    lobby = wait_for("room", lambda: (re.search(r"CreateRoom: lobby id (\w+) created", log("a")[before_a:]) or [None, None])[1], 20)
    if not check(f"{title}: a created a room", bool(lobby)):
        return None
    check(f"{title}: c's chat card offers Join", wait_for("join", lambda: card_state(lobby).endswith("Join"), 15), card_state(lobby))
    uia("b", "invoke", "-Id", f"RoomAction:{lobby}")
    check(f"{title}: c's chat card says Full once b is in", wait_for("full", lambda: card_state(lobby).endswith("Full"), 15), card_state(lobby))

    before_a, before_b = len(log("a")), len(log("b"))
    uia("a", "invoke", "-Id", "ReadyButton")
    uia("b", "invoke", "-Id", "ReadyButton")
    started = wait_for("start", lambda: "HARNESS fake game: 'wol' runs" in log("a")[before_a:]
                       and "HARNESS fake game: 'wol' runs" in log("b")[before_b:], 30)
    check(f"{title}: the countdown started by itself and both games launched", started)
    host_args = re.search(r"HARNESS BLOCKED launch game wol (.*)", log("a")[before_a:])
    check(f"{title}: the host of a ranked room passes Standard rules",
          bool(host_args) and "+uhcLanHost uhcLanRules=0" in host_args[1], host_args[1] if host_args else "")
    check(f"{title}: c's chat card says See match", wait_for("ingame", lambda: card_state(lobby).endswith("SeeMatch"), 20),
          card_state(lobby))

    print(f"...   {title}: waiting for the simulated games (190 s)")
    reported = wait_for("report", lambda: "TryReportMatchAsync: reported match" in log("a")[before_a:], 260)
    check(f"{title}: the host reported the match", reported)
    confirmed = wait_for("confirm", lambda: "TryConfirmMatchAsync: sent own reading" in log("b")[before_b:], 30)
    check(f"{title}: the guest sent its own reading", confirmed)
    check(f"{title}: c's chat card says Closed", wait_for("closed", lambda: card_state(lobby).startswith("Closed"), 60),
          card_state(lobby))
    # The points at stake (2026-11-18): each launcher logs the figures it showed at Start, and the
    # result card must show exactly the one that applied. Two new accounts have no rated game yet,
    # so nobody is being placed and the figures are shown.
    for who, since in (("a", before_a), ("b", before_b)):
        promised = re.search(r"Room stake: final (gain=\S+ loss=\S+|\w+)", log(who)[since:])
        check(f"{title}: {who} was shown the points at stake", bool(promised) and "gain=" in promised[1],
              promised[1] if promised else "no line")
        result = wait_for("result card", lambda: re.search(r"Result card: (.*)", log(who)[since:]), 30)
        check(f"{title}: {who}'s result card shows the figure it promised",
              bool(result) and "STAKE MISMATCH" not in result[1], result[1] if result else "no line")
    match = re.search(r"match_reported: match=([0-9a-f-]+)", log("a")[before_a:])
    return match[1] if match else None


def main():
    keep_server = "--keep-server" in sys.argv
    testbed("safety", "record")
    testbed("server", "start", "--fresh")
    for account, name in (("a", "El Taita"), ("b", "skibidi"), ("c", "Miron")):
        testbed("token", "--name", name, account=account)
    for account, where in (("a", "first"), ("b", "second"), ("c", "third")):
        testbed("launch", "--mp", "--half", where, "--software", account=account)
        check(f"{account}: signed in on the local server",
              "HARNESS lobby url" in log(account) and "Multiplayer session: resumed" in log(account)
              and "Migrated multiplayer.lobbyBaseUrl" not in log(account))
        uia(account, "invoke", "-Id", "TopTabMultiplayer")

    match = play_match(FIRST, "match 1")
    if match:
        show = testbed("admin", "match:show", match, check_ok=False)
        civs = re.findall(r"civ (\S+)", show)
        check("match 1: the server has a civilization for BOTH players",
              len(civs) >= 2 and all(c != "-" for c in civs[:2]), ", ".join(civs[:2]))
        check("match 1: the guest's reading agrees", "agreement agree" in show)
    # A storm of thousands of passes a second is a loop; a few seconds of a window opening or a
    # card fading in, with nobody watching (the test launchers are never in front), is not.
    rates = [(a, int(m)) for a in "abc" for m in re.findall(r"LAYOUT STORM  (\d+)/s layout passes", log(a))]
    loops = [f"{a}:{r}/s" for a, r in rates if r >= 100]
    check("no launcher went into a layout loop", not loops, ", ".join(loops))

    testbed("fakegame", SECOND, "--slot", "1", account="a")
    testbed("fakegame", SECOND, "--slot", "2", account="b")
    before_b = len(log("b"))
    uia("a", "invoke", "-ByName", "-Id", "Revancha")
    check("rematch: b is invited", wait_for("invite", lambda: re.search(r"AppToast .*(evancha|ematch)", log("b")[before_b:]), 20))

    testbed("close", "--all")
    if not keep_server:
        testbed("server", "stop")
    out = testbed("safety", "check", check_ok=False)
    check("the player's machine is as it was", "as it was" in out, out.strip()[-200:])
    print(f"\n{len(failures)} failure(s)" + (": " + ", ".join(failures) if failures else ""))
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
