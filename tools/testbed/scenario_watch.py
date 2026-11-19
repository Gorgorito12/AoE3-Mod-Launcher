"""Phase 3's "See match" on the rig: a and b play a ranked 1v1, c watches it from outside.

    python -I tools/testbed/scenario_watch.py

Needs the local server running (testbed.py server start). Leaves the three launchers open and the
captures in out/: watch-playing.png (during the match) and watch-result.png (once stored).
"""
import re
import sys
import time

sys.path.insert(0, __import__("os").path.dirname(__file__))
import scenario_two_accounts as s  # noqa: E402


def main():
    for account, where in (("a", "first"), ("b", "second"), ("c", "third")):
        s.testbed("launch", "--mp", "--half", where, "--software", "--lang", "es", account=account)
        s.uia(account, "invoke", "-Id", "TopTabMultiplayer")
    time.sleep(6)
    s.testbed("fakegame", s.SECOND, "--slot", "1", account="a")
    s.testbed("fakegame", s.SECOND, "--slot", "2", account="b")
    before_a = len(s.log("a"))
    s.uia("a", "invoke", "-Id", "CreateRoomButton")
    time.sleep(2)
    s.uia("a", "invoke", "-Id", "Segment:OneVOne")
    s.uia("a", "invoke", "-Id", "CreateButton")
    lobby = s.wait_for("room", lambda: (re.search(r"CreateRoom: lobby id (\w+) created", s.log("a")[before_a:]) or [None, None])[1], 20)
    print("lobby", lobby)
    s.uia("b", "invoke", "-Id", f"RoomAction:{lobby}")
    time.sleep(3)
    s.uia("a", "invoke", "-Id", "ReadyButton")
    s.uia("b", "invoke", "-Id", "ReadyButton")
    started = s.wait_for("start", lambda: "HARNESS fake game" in s.log("a")[before_a:], 40)
    print("started", bool(started))
    # c's Rooms row now says "See match"; the list polls every few seconds.
    time.sleep(12)
    s.uia("c", "invoke", "-Id", f"RoomAction:{lobby}")
    time.sleep(4)
    print(s.testbed("shot", "out/watch-playing.png", "--title", "Partida", account="c"))
    print(re.findall(r"Match watch opened.*", s.log("c")))
    # The simulated game lasts 190 s; the report follows.
    s.wait_for("stored", lambda: "TryReportMatchAsync: reported match" in s.log("a")[before_a:], 260)
    time.sleep(25)
    print(s.testbed("shot", "out/watch-result.png", "--title", "Partida", account="c"))


if __name__ == "__main__":
    main()
