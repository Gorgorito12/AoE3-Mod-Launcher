r"""Runs the REAL launcher beside the player's own, on the CORSAIR XENEON, for an agent to look at.

Adapted from tools/testbed.py in Unharcoded-AOE3- NEW: a CLI of small commands, run one at a time.
A run passes on logs, UI Automation answers and frames somebody looked at, against the list in
README.md. Nothing here decides "looks right".

Usage: python -I tools/testbed/testbed.py <command>
  build                                     Release build of the launcher
  monitors                                  every monitor, marking the test one
  launch [--size WxH|--max] [--lang es|en] [--glass LEVEL] [--software] [--keep] [-- args...]
  close                                     closes only the copy `launch` started
  windows                                   this run's visible windows
  place [--title TEXT]                      moves a window onto the test monitor, not activating it
  shot FILE [--title TEXT] [--screen]       PNG of a window (--screen for real antialiasing)
  record FILE [--seconds N] [--fps N] [--title TEXT] [--master]
  frames VIDEO [--count N]                  stills from the video, starting from the middle
  sidebyside REFERENCE SHOT OUT             reference, launcher and difference at one scale
  log [--grep REGEX] [--tail N]             this run's launcher log
  perf SCENARIO [--software] [--label L]    measures a scenario (see perf_scenarios)
  compare CURRENT BASELINE                  fails when a budget is broken
  safety record|check                       proves the player's machine was left alone
  uia ACTION [args...]                      uia.ps1 for this run's process

Environment: AOE3ML_TEST_MONITOR (default XENEON) names the monitor, by model and never by number.
"""
import sys as _sys
_sys.stdout.reconfigure(errors="replace")
import argparse
import ctypes
import ctypes.wintypes as wt
import datetime
import json
import os
import pathlib
import re
import shutil
import statistics
import struct
import subprocess
import sys
import threading
import time
import zlib

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
EXE = REPO / "WarsOfLibertyLauncher" / "bin" / "Release" / "net8.0-windows" / "Aoe3ModLauncher.exe"
OUT = HERE / "out"
RUN = OUT / "run"            # AOE3ML_DATA_DIR of the launcher under test
DOCUMENTS = OUT / "documents"  # AOE3ML_DOCUMENTS: copies of the player's files, never the originals
SANDBOX = OUT / "sandbox"    # fake mod installs: anything a button writes lands here
STATE = OUT / "state.json"
LOG = RUN / "launcher-debug.log"
REAL_DATA = pathlib.Path(os.environ["LOCALAPPDATA"]) / "AoE3ModLauncher"
FFMPEG = r"C:\Tools\ffmpeg\ffmpeg.exe"
NO_ACTIVATE_MOVE = 0x0001 | 0x0004 | 0x0010  # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32")
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
shcore = ctypes.WinDLL("shcore")
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))  # per-monitor v2: physical pixels
user32.GetDC.restype = wt.HDC
user32.GetDC.argtypes = [wt.HWND]
user32.ReleaseDC.argtypes = [wt.HWND, wt.HDC]
user32.PrintWindow.argtypes = [wt.HWND, wt.HDC, wt.UINT]
user32.GetForegroundWindow.restype = wt.HWND
user32.WindowFromPoint.restype = wt.HWND
user32.WindowFromPoint.argtypes = [wt.POINT]
user32.GetAncestor.restype = wt.HWND
user32.GetAncestor.argtypes = [wt.HWND, wt.UINT]
user32.SetWindowPos.argtypes = [wt.HWND, wt.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.UINT]
user32.PostMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
gdi32.CreateCompatibleDC.restype = wt.HDC
gdi32.CreateCompatibleDC.argtypes = [wt.HDC]
gdi32.CreateCompatibleBitmap.restype = wt.HBITMAP
gdi32.CreateCompatibleBitmap.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int]
gdi32.SelectObject.restype = wt.HGDIOBJ
gdi32.SelectObject.argtypes = [wt.HDC, wt.HGDIOBJ]
gdi32.BitBlt.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.HDC,
                         ctypes.c_int, ctypes.c_int, wt.DWORD]
gdi32.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, wt.UINT, wt.UINT, ctypes.c_void_p, ctypes.c_void_p, wt.UINT]
gdi32.DeleteObject.argtypes = [wt.HGDIOBJ]
gdi32.DeleteDC.argtypes = [wt.HDC]
k32.OpenProcess.restype = wt.HANDLE
k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
k32.CloseHandle.argtypes = [wt.HANDLE]
k32.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
WNDENUMPROC = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
MONITORENUMPROC = ctypes.WINFUNCTYPE(wt.BOOL, wt.HMONITOR, wt.HDC, ctypes.POINTER(wt.RECT), wt.LPARAM)


class MONITORINFOEXW(ctypes.Structure):
    _fields_ = [("cbSize", wt.DWORD), ("rcMonitor", wt.RECT), ("rcWork", wt.RECT),
                ("dwFlags", wt.DWORD), ("szDevice", wt.WCHAR * 32)]


class DISPLAY_DEVICEW(ctypes.Structure):
    _fields_ = [("cb", wt.DWORD), ("DeviceName", wt.WCHAR * 32), ("DeviceString", wt.WCHAR * 128),
                ("StateFlags", wt.DWORD), ("DeviceID", wt.WCHAR * 128), ("DeviceKey", wt.WCHAR * 128)]


# --- monitors --------------------------------------------------------------------------------------

def _adapter_monitors():
    """adapter device -> names of the monitors ACTIVE on it. Windows' DISPLAYn numbers do not match
    Settings and can move, so the model name is the only thing worth matching."""
    out, i = {}, 0
    while True:
        adapter = DISPLAY_DEVICEW()
        adapter.cb = ctypes.sizeof(adapter)
        if not user32.EnumDisplayDevicesW(None, i, ctypes.byref(adapter), 0):
            return out
        j = 0
        while True:
            monitor = DISPLAY_DEVICEW()
            monitor.cb = ctypes.sizeof(monitor)
            if not user32.EnumDisplayDevicesW(adapter.DeviceName, j, ctypes.byref(monitor), 0):
                break
            if monitor.StateFlags & 1:  # DISPLAY_DEVICE_ACTIVE
                out.setdefault(adapter.DeviceName, []).append(monitor.DeviceString)
            j += 1
        i += 1


def monitors():
    names, out = _adapter_monitors(), []

    def visit(handle, _dc, _rect, _data):
        info = MONITORINFOEXW()
        info.cbSize = ctypes.sizeof(info)
        if user32.GetMonitorInfoW(handle, ctypes.byref(info)):
            dx, dy = wt.UINT(), wt.UINT()
            shcore.GetDpiForMonitor(handle, 0, ctypes.byref(dx), ctypes.byref(dy))
            w = info.rcWork
            out.append({"device": info.szDevice, "name": ", ".join(names.get(info.szDevice, ["?"])),
                        "primary": bool(info.dwFlags & 1), "scale": dx.value / 96,
                        "work": (w.left, w.top, w.right, w.bottom)})
        return True

    callback = MONITORENUMPROC(visit)
    user32.EnumDisplayMonitors(None, None, callback, 0)
    return out


def test_monitor():
    wanted = os.environ.get("AOE3ML_TEST_MONITOR", "XENEON").strip().upper()
    for monitor in monitors():
        if wanted in monitor["name"].upper():
            return monitor
    sys.exit(f"testbed: no active monitor named {wanted!r}; run `monitors` and check it is on")


# --- process and windows ---------------------------------------------------------------------------

def exe_of(pid):
    handle = k32.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
    if not handle:
        return ""
    buf = ctypes.create_unicode_buffer(1024)
    size = wt.DWORD(len(buf))
    ok = k32.QueryFullProcessImageNameW(handle, 0, buf, ctypes.byref(size))
    k32.CloseHandle(handle)
    return buf.value if ok else ""


def run_pid():
    """This run's process, proved by its exe path: the player's launcher has the same file name."""
    if not STATE.exists():
        return None
    pid = json.loads(STATE.read_text())["pid"]
    return pid if exe_of(pid).lower() == str(EXE).lower() else None


def windows(pid=None):
    """Visible top-level windows of the run, topmost first (EnumWindows walks the z-order)."""
    pid = pid or run_pid()
    found = []

    def visit(hwnd, _):
        owner = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value == pid and user32.IsWindowVisible(hwnd):
            title = ctypes.create_unicode_buffer(256)
            user32.GetWindowTextW(hwnd, title, 256)
            rc = wt.RECT()
            user32.GetWindowRect(hwnd, ctypes.byref(rc))
            if title.value and rc.right - rc.left > 1:
                found.append({"hwnd": hwnd, "title": title.value,
                              "rect": (rc.left, rc.top, rc.right, rc.bottom)})
        return True

    callback = WNDENUMPROC(visit)
    user32.EnumWindows(callback, 0)
    return found


def pick_window(title=None):
    if not run_pid():
        sys.exit("testbed: no launcher from this run; use `launch`")
    found = windows()
    if title:
        found = [w for w in found if title.lower() in w["title"].lower()]
    if not found:
        sys.exit(f"testbed: no window{f' with {title!r} in its title' if title else ''}")
    return found[0]


def place(window):
    """Moves a window onto the test monitor. Dialogs centre on the primary one, which may be another."""
    left, top, right, bottom = test_monitor()["work"]
    x0, y0, x1, y1 = window["rect"]
    if left <= x0 and y0 >= top and x1 <= right and y1 <= bottom:
        return False
    x = left + max(0, (right - left - (x1 - x0)) // 2)
    y = top + max(0, (bottom - top - (y1 - y0)) // 2)
    user32.SetWindowPos(window["hwnd"], None, x, y, 0, 0, NO_ACTIVATE_MOVE)
    time.sleep(0.3)
    return True


def fit(pid, width, height, title=None):
    """The window opens on the primary monitor, at that monitor's scale, and when it does not fit the
    test monitor as such Windows moves it there without rescaling: asking for 1920 left it wider
    than the screen. So the size asked for is set again, in the test monitor's own pixels."""
    monitor = test_monitor()
    left, top, right, bottom = monitor["work"]
    found = windows(pid)
    if not found:
        return
    named = [w for w in found if title and title in w["title"]]
    main = max(named or found, key=lambda w: (w["rect"][2] - w["rect"][0]) * (w["rect"][3] - w["rect"][1]))
    w, h = round(width * monitor["scale"]), round(height * monitor["scale"])
    x, y = left + max(0, (right - left - w) // 2), top + max(0, (bottom - top - h) // 2)
    user32.SetWindowPos(main["hwnd"], None, x, y, w, h, 0x0004 | 0x0010)  # SWP_NOZORDER | SWP_NOACTIVATE
    time.sleep(0.3)


# --- fixtures --------------------------------------------------------------------------------------

def fake_install(folder, mod_id, version, files):
    for rel in files:
        path = folder / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        if rel.endswith(".xml"):
            path.write_text("<root/>", encoding="utf-8")
        else:
            path.touch()
    (folder / "install-manifest.json").write_text(json.dumps({
        "modId": mod_id, "version": version, "installPath": str(folder),
        "installedAt": "2026-11-01T12:00:00Z", "clonedAoe3": True}), encoding="utf-8")


REAL_DOCUMENTS = pathlib.Path(os.environ["USERPROFILE"]) / "Documents"


def seed_documents():
    """Copies of a few of the player's game files, so backups and imports run on real shapes.
    Read once, here, and never written: everything the launcher does lands in DOCUMENTS."""
    for mod in ("Wars of Liberty", "AoE3 Improvement Mod"):
        source = REAL_DOCUMENTS / "My Games" / mod
        target = DOCUMENTS / "My Games" / mod
        (target / "Users3").mkdir(parents=True, exist_ok=True)
        (target / "Savegame").mkdir(parents=True, exist_ok=True)
        if (source / "Users3").is_dir():
            for item in (source / "Users3").iterdir():
                if item.is_file() and item.stat().st_size < 2_000_000:
                    shutil.copy2(item, target / "Users3" / item.name)
        if (source / "Savegame").is_dir():
            for item in (source / "Savegame").glob("*homecity*.xml"):
                shutil.copy2(item, target / "Savegame" / item.name)
            records = sorted((source / "Savegame").glob("*.age3Yrec"), key=lambda f: f.stat().st_mtime)
            for item in records[-3:]:
                shutil.copy2(item, target / "Savegame" / item.name)


def seed(args):
    """A data folder with every first-run question already answered, and no account in it: the
    player's config holds a Discord token and is never read here, only the public catalog cache."""
    shutil.rmtree(RUN, ignore_errors=True)
    RUN.mkdir(parents=True)
    for name in ("catalog-cache.json", "news-cache.json"):
        if (REAL_DATA / name).exists():
            shutil.copy2(REAL_DATA / name, RUN / name)
    if (REAL_DATA / "mod-assets").exists():
        shutil.copytree(REAL_DATA / "mod-assets", RUN / "mod-assets")

    engine = ["RockallDLL.dll", "binkw32.dll", "granny2.dll", "deformerdlly.dll"]
    wol = SANDBOX / "Wars of Liberty"
    fake_install(wol, "wol", "1.2.0e", engine + [
        "age3y.exe", "data/stringtabley.xml", "data/protoy.xml", "data/techtreey.xml", "data/unithelpstringsy.xml", "art/zulushield/x.ddt"])
    other = SANDBOX / "Improvement Mod"
    fake_install(other, "improvement-mod", "24.07.2026", engine + ["age3m.exe", "data/protom.xml"])
    copy = SANDBOX / "WoL Torneo"
    fake_install(copy, "wol", "1.2.0e", engine + ["age3y.exe", "art/zulushield/x.ddt", "data/stringtabley.xml"])
    old = SANDBOX / "Copia antigua"
    fake_install(old, "wol", "1.2.0d", engine + ["age3y.exe", "art/zulushield/x.ddt", "data/stringtabley.xml"])

    scale, (left, top, _right, _bottom) = test_monitor()["scale"], test_monitor()["work"]
    width, height = (1100, 700) if not args.size else map(int, args.size.lower().split("x"))
    config = {
        "language": args.lang, "languageExplicitlyChosen": True,
        "textScale": "100", "textScaleExplicitlyChosen": True,
        "windowWidth": width, "windowHeight": height,
        "windowLeft": left / scale + 8, "windowTop": top / scale + 8, "windowMaximized": bool(args.max),
        "backgroundDefaultSeeded": True, "startWithWindows": False, "minimizeToTray": False,
        "startMinimized": False, "closeToTray": False, "enableJoinLinks": False,
        "selfInstallPromptShown": True, "crossUserAccountNoticeShown": True,
        "gameRecordingNoticePending": False, "antivirusNoticeAcknowledged": True,
        "catalogBaselineSeeded": True, "enableSounds": False, "checkUpdatesOnStartup": False,
        "activeModId": "wol", "userModIds": ["improvement-mod"],
        "favoriteModIds": ["wol"],
        "notifications": sample_notifications(),
        "mods": {
            "wol": {"installPath": str(wol), "lastKnownVersion": "1.2.0e",
                    "otherInstalls": [{"id": "torneo", "installPath": str(copy), "lastKnownVersion": "1.2.0e"},
                                      {"id": "antigua", "installPath": str(old), "lastKnownVersion": "1.2.0d"}]},
            "improvement-mod": {"installPath": str(other), "lastKnownVersion": "24.07.2026"},
        },
    }
    if args.glass:
        config["glassLevel"] = args.glass
    (RUN / "launcher-config.json").write_text(json.dumps(config, indent=2), encoding="utf-8")


# --- launch and close ------------------------------------------------------------------------------

def sample_notifications():
    now = datetime.datetime.now(datetime.timezone.utc)
    def item(kind, mod, title, body, hours, read=False, target=None):
        when = (now - datetime.timedelta(hours=hours)).isoformat()
        return {"id": f"s{hours}", "kind": kind, "modId": mod, "title": title, "body": body,
                "createdAtUtc": when, "read": read, "targetId": target}
    return [
        item(0, "improvement-mod", "Actualización de Improvement Mod", "v16.10.2026 → v19.07.2026 · 186 MB", 1),
        item(1, "hundred-days", "Hundred Days se actualizó", "Ya tienes la vbeta-0.10.", 26),
        item(2, "hundred-days", "Nueva traducción al español", "Hundred Days · publicada por la comunidad", 27, True),
        item(4, "", "Conexión recuperada", "Vuelves a estar conectado al servidor.", 28, True, "online"),
        item(3, "", "Hay una versión nueva del launcher: v1.0.17", "Se instala al reiniciar el launcher.", 80, True),
    ]


def launch(args):
    if run_pid():
        sys.exit("testbed: this run's launcher is still open; use `close`")
    if not EXE.exists():
        sys.exit(f"testbed: {EXE} is missing; use `build`")
    if args.keep and (RUN / "launcher-config.json").exists():
        # A kept sandbox keeps its data (a signed-in session), not its look.
        kept = json.loads((RUN / "launcher-config.json").read_text(encoding="utf-8-sig"))
        kept["language"] = args.lang
        if args.glass:
            kept["glassLevel"] = args.glass
        if args.size or args.max:
            width, height = (1100, 700) if not args.size else map(int, args.size.lower().split("x"))
            kept.update(windowWidth=width, windowHeight=height, windowMaximized=bool(args.max))
        (RUN / "launcher-config.json").write_text(json.dumps(kept, indent=2), encoding="utf-8")
    if not args.keep:
        seed(args)
    if not DOCUMENTS.exists():
        seed_documents()
    env = dict(os.environ, AOE3ML_HARNESS="1", AOE3ML_DATA_DIR=str(RUN), AOE3ML_DOCUMENTS=str(DOCUMENTS),
               AOE3ML_SANDBOX=str(SANDBOX))
    if args.software:
        env["AOE3ML_SOFTWARE_RENDER"] = "1"
    foreground = user32.GetForegroundWindow()
    started = time.time()
    process = subprocess.Popen([str(EXE), "--no-update-gate", *args.rest], cwd=EXE.parent, env=env)
    STATE.write_text(json.dumps({"pid": process.pid, "started": started}))
    while time.time() - started < 60:
        if process.poll() is not None:
            sys.exit(f"testbed: the launcher exited with {process.returncode}; see `log`")
        if _log_has("window shown"):
            break
        time.sleep(0.2)
    else:
        sys.exit("testbed: no `window shown` in 60 s; see `log`")
    if "HARNESS active" not in LOG.read_text(encoding="utf-8", errors="replace"):
        process.terminate()
        sys.exit("testbed: the launcher did not enter harness mode; it was closed")
    time.sleep(0.5)
    for window in windows(process.pid):
        place(window)
    if args.size and not args.max:
        fit(process.pid, *map(int, args.size.lower().split("x")))
    stolen = user32.GetForegroundWindow() != foreground
    monitor = test_monitor()
    print(f"testbed: launcher ready, pid {process.pid}, on {monitor['name']} ({monitor['device']})"
          f"{'; the foreground window changed' if stolen else ''}")


def _log_has(text):
    return LOG.exists() and text in LOG.read_text(encoding="utf-8", errors="replace")


def close():
    pid = run_pid()
    if not pid:
        print("testbed: nothing to close")
        return
    for window in windows(pid):
        user32.PostMessageW(window["hwnd"], 0x0010, 0, 0)  # WM_CLOSE
    deadline = time.time() + 8
    while time.time() < deadline and run_pid():
        time.sleep(0.2)
    if run_pid():  # the path was proved by run_pid: this cannot be the player's launcher
        subprocess.run(["taskkill", "/F", "/PID", str(pid)], check=False, capture_output=True)
    STATE.unlink(missing_ok=True)
    print(f"testbed: closed {pid}")


# --- pictures --------------------------------------------------------------------------------------

def grab(hwnd, rect, screen_copy):
    x0, y0, x1, y1 = rect
    w, h = x1 - x0, y1 - y0
    desktop = user32.GetDC(None)
    mem = gdi32.CreateCompatibleDC(desktop)
    bmp = gdi32.CreateCompatibleBitmap(desktop, w, h)
    gdi32.SelectObject(mem, bmp)
    if screen_copy:
        gdi32.BitBlt(mem, 0, 0, w, h, desktop, x0, y0, 0x00CC0020)  # SRCCOPY
    else:
        user32.PrintWindow(hwnd, mem, 2)  # PW_RENDERFULLCONTENT
    bmi = ctypes.create_string_buffer(struct.pack("<IiiHHIIiiII", 40, w, -h, 1, 32, 0, 0, 0, 0, 0, 0) + b"\0" * 16)
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mem, bmp, 0, h, buf, bmi, 0)
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mem)
    user32.ReleaseDC(None, desktop)
    return w, h, buf.raw


def write_png(path, w, h, bgra):
    def chunk(kind, data):
        c = struct.pack(">I", len(data)) + kind + data
        return c + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    rows = bytearray()
    for y in range(h):
        line = bgra[y * w * 4:(y + 1) * w * 4]
        rgb = bytearray(w * 3)
        rgb[0::3], rgb[1::3], rgb[2::3] = line[2::4], line[1::4], line[0::4]
        rows += b"\0" + rgb
    pathlib.Path(path).parent.mkdir(parents=True, exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) +
                chunk(b"IDAT", zlib.compress(bytes(rows), 6)) + chunk(b"IEND", b""))


def mostly_black(w, h, data):
    step = max(1, (w * h) // 2000)
    dark = sum(1 for i in range(0, w * h, step) if max(data[4 * i:4 * i + 3]) < 8)
    return dark > 0.97 * ((w * h + step - 1) // step)


def uncovered(window):
    """A screen copy shows whatever is on top, so it only counts when that is this window."""
    x0, y0, x1, y1 = window["rect"]
    for fx, fy in ((.5, .5), (.1, .1), (.9, .1), (.1, .9), (.9, .9)):
        point = wt.POINT(int(x0 + (x1 - x0) * fx), int(y0 + (y1 - y0) * fy))
        top = user32.GetAncestor(user32.WindowFromPoint(point), 2)  # GA_ROOT
        owner = wt.DWORD()
        user32.GetWindowThreadProcessId(top, ctypes.byref(owner))
        if top != window["hwnd"] and owner.value != run_pid():  # its own popups count as the window
            return False
    return True


def popup_window(pid):
    """A WPF Popup is its own untitled top-level window, and PrintWindow of the parent omits it."""
    found = []

    def visit(hwnd, _):
        owner = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value == pid and user32.IsWindowVisible(hwnd):
            title = ctypes.create_unicode_buffer(8)
            user32.GetWindowTextW(hwnd, title, 8)
            rc = wt.RECT()
            user32.GetWindowRect(hwnd, ctypes.byref(rc))
            if not title.value and rc.right - rc.left > 80 and rc.bottom - rc.top > 40:
                found.append({"hwnd": hwnd, "title": "(popup)", "rect": (rc.left, rc.top, rc.right, rc.bottom)})
        return True

    user32.EnumWindows(ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)(visit), 0)
    return found[0] if found else None


def raise_windows(pid):
    """A screen copy needs the window on top. Without activating it: the player's own windows keep
    the focus, and the launcher closes right after, so nothing stays always-on-top."""
    above = [popup_window(pid)]  # untitled, so windows() leaves it out, and it has to end up on top
    for window in [*reversed(windows(pid)), *filter(None, above)]:
        user32.SetWindowPos(window["hwnd"], wt.HWND(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010)
    time.sleep(0.5)


def shot(args):
    if args.popup:
        window = popup_window(run_pid())
        if not window:
            sys.exit("testbed: no popup is open")
        w, h, data = grab(window["hwnd"], window["rect"], False)
        write_png(args.file, w, h, data)
        print(f"{args.file}: popup {w}x{h}")
        return
    window = pick_window(args.title)
    place(window)
    if args.top:
        raise_windows(run_pid())
        args.screen = True
    window = pick_window(args.title)
    if args.screen and not uncovered(window):
        sys.exit("testbed: another window covers it, and a screen copy would show that one")
    w, h, data = grab(window["hwnd"], window["rect"], args.screen)
    write_png(args.file, w, h, data)
    method = "screen copy" if args.screen else "PrintWindow (do not judge antialiasing)"
    black = "; MOSTLY BLACK, do not trust it" if mostly_black(w, h, data) else ""
    print(f"{args.file}: {window['title']!r} {w}x{h}, {method}{black}")


def record(args):
    window = pick_window(args.title)
    place(window)
    window = pick_window(args.title)
    x0, y0, x1, y1 = window["rect"]
    w, h = (x1 - x0) // 2 * 2, (y1 - y0) // 2 * 2
    rect = (x0, y0, x0 + w, y0 + h)
    path = pathlib.Path(args.file)
    path.parent.mkdir(parents=True, exist_ok=True)
    command = [FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgra",
               "-s", f"{w}x{h}", "-framerate", str(args.fps), "-i", "-", "-c:v", "libx264", "-preset", "medium",
               "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(path)]
    if args.master:
        command += ["-c:v", "libx264rgb", "-preset", "ultrafast", "-qp", "0", str(path.with_suffix(".mkv"))]
    encoder = subprocess.Popen(command, stdin=subprocess.PIPE)
    total, written, captured = int(args.seconds * args.fps), 0, 0
    start = time.perf_counter()
    try:
        while written < total:
            delay = start + written / args.fps - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            frame = grab(window["hwnd"], rect, False)[2]
            captured += 1
            due = min(total, max(written + 1, int((time.perf_counter() - start) * args.fps) + 1))
            encoder.stdin.write(frame * (due - written))  # a slow grab repeats: the video keeps real speed
            written = due
    finally:
        encoder.stdin.close()
        encoder.wait()
    size = path.stat().st_size / 1e6 if path.exists() else 0
    print(f"{path}: {args.seconds:g} s, {captured} frames grabbed, {written - captured} repeated, {size:.1f} MB")


def frames(args):
    """Stills from a video, the middle one first: that is the one to check before trusting it."""
    video = pathlib.Path(args.video)
    probe = subprocess.run([FFMPEG, "-hide_banner", "-i", str(video)], capture_output=True, text=True)
    found = re.search(r"Duration: (\d+):(\d+):([\d.]+)", probe.stderr)
    if not found:
        sys.exit(f"testbed: cannot read {video}")
    length = int(found[1]) * 3600 + int(found[2]) * 60 + float(found[3])
    stops = [length / 2] + [length * i / (args.count + 1) for i in range(1, args.count)]
    for i, second in enumerate(stops):
        out = video.with_name(f"{video.stem}-f{i}.png")
        subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-ss", f"{second:.2f}", "-i", str(video),
                        "-frames:v", "1", str(out)], check=True)
        print(out)


def sidebyside(args):
    from PIL import Image, ImageChops, ImageDraw
    reference, shot_ = Image.open(args.reference).convert("RGB"), Image.open(args.shot).convert("RGB")
    # A window at 125 % has 1.25 pixels per CSS pixel; compare at the reference's own size.
    launcher = shot_.resize(reference.size, Image.LANCZOS)
    difference = ImageChops.difference(reference, launcher)
    w, h = reference.size
    sheet = Image.new("RGB", (w * 3 + 20, h + 26), (24, 24, 24))
    for i, (label, image) in enumerate((("prototype", reference), ("launcher", launcher), ("difference", difference))):
        sheet.paste(image, (i * (w + 10), 26))
        ImageDraw.Draw(sheet).text((i * (w + 10) + 6, 7), label, fill=(255, 255, 255))
    pathlib.Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.out)
    pixels = list(difference.convert("L").getdata())
    changed = sum(1 for p in pixels if p > 24) / len(pixels)
    print(f"{args.out}: {w}x{h}, {changed:.1%} of pixels differ visibly (a guide, not a verdict)")


def sheet(reference, shot_, out):
    """Prototype, launcher and difference at the launcher's 1.25, so small text can be read."""
    from PIL import Image, ImageChops, ImageDraw
    prototype = Image.open(reference).convert("RGB")
    prototype = prototype.resize((round(prototype.width * 1.25), round(prototype.height * 1.25)), Image.LANCZOS)
    launcher = Image.open(shot_).convert("RGB")
    w, h = max(prototype.width, launcher.width), max(prototype.height, launcher.height)
    a, b = Image.new("RGB", (w, h), (60, 60, 66)), Image.new("RGB", (w, h), (60, 60, 66))
    a.paste(prototype)
    b.paste(launcher)
    difference = ImageChops.difference(a, b)
    page = Image.new("RGB", (w * 3 + 40, h + 26), (60, 60, 66))
    draw = ImageDraw.Draw(page)
    for i, (label, image) in enumerate((("prototype", a), ("launcher", b), ("difference", difference))):
        page.paste(image, (10 + i * (w + 10), 26))
        draw.text((14 + i * (w + 10), 7), f"{label} {image.width}x{image.height}", fill=(255, 220, 120))
    pathlib.Path(out).parent.mkdir(parents=True, exist_ok=True)
    page.save(out)
    print(f"{out}: prototype {prototype.size}, launcher {launcher.size}")


def check(args):
    """One step of the loop: open the launcher, capture it, set it beside the prototype."""
    folder = OUT / "check"
    folder.mkdir(parents=True, exist_ok=True)
    ref, picture, out = folder / f"{args.name}-ref.png", folder / f"{args.name}-shot.png", folder / f"{args.name}.png"
    command = ["node", str(HERE / "reference.mjs"), args.folder, args.ref, args.block, str(ref),
               "--props", json.dumps({"transparencia": args.glass_name, **json.loads(args.props)}, ensure_ascii=False)]
    for flag, value in (("--frame", args.frame), ("--click", args.click)):
        if value is not None:
            command += [flag, str(value)]
    done = subprocess.run(command, capture_output=True, text=True, encoding="utf-8")
    if done.returncode:
        sys.exit(done.stdout + done.stderr)
    if args.build:
        subprocess.run([sys.executable, "-I", __file__, "build"], check=True)
    launch(argparse.Namespace(keep=args.keep, size=args.size, max=False, lang=args.lang, glass=args.glass, software=False,
                              rest=args.rest))
    try:
        time.sleep(args.wait)
        if args.size and args.title:
            fit(run_pid(), *map(int, args.size.lower().split("x")), title=args.title)
            time.sleep(1)
        shot(argparse.Namespace(popup=False, file=str(picture), title=args.title, screen=True, top=True))
    finally:
        close()
    sheet(ref, picture, out)


# --- performance -----------------------------------------------------------------------------------

def parse_log(path=LOG):
    text = path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""
    timing = {}
    for name, ms in re.findall(r"TIMING  (.+?) \u2014 \+(\d+) ms since launch", text):
        timing.setdefault(name, int(ms))
    perf = [tuple(map(int, m)) for m in
            re.findall(r"PERF  (\d+) passes, (\d+) redraws, (\d+) animated, (\d+) ms drawing", text)]
    return {
        "timing": timing,
        "perf": perf,
        "stalls": [int(ms) for ms in re.findall(r"UI STALL  (\d+) ms", text)],
        "ops": [int(ms) for ms in re.findall(r"UI OP  (\d+) ms", text)],
        "switches": [(to, int(ms), kind == "first time") for to, ms, kind in
                     re.findall(r"SWITCH  .+? -> (.+?)  (\d+) ms \((first time|repeat)\)", text)],
        "storms": len(re.findall(r"LAYOUT STORM  ", text)),
        # The lines themselves: the log is overwritten by the next run, and a count alone
        # cannot say which page stormed.
        "storm_lines": [line[:1200] for line in text.splitlines() if "LAYOUT STORM  " in line],
        "effects_reduced": "EFFECTS REDUCED" in text,
    }


def percentile(values, share):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(len(ordered) * share))] if ordered else 0


def frame_ms(perf):
    """Average cost of a redraw in each second that had any."""
    return [ms / redraws for _p, redraws, _a, ms in perf if redraws]


def perf_scenarios():
    return {
        "coldstart": {"runs": 5, "seconds": 8, "args": []},
        "idle": {"runs": 1, "seconds": 60, "args": []},
        "tray": {"runs": 1, "seconds": 60, "args": ["--minimized"]},
        "workshop": {"runs": 1, "seconds": 30, "args": ["--open-tab=workshop"]},
        "multiplayer": {"runs": 1, "seconds": 30, "args": ["--open-tab=multiplayer"]},
        "settings": {"runs": 1, "seconds": 15, "args": ["--open-settings=interface"]},
        "modsettings": {"runs": 1, "seconds": 15, "args": ["--open-mod-settings=general"]},
        "tabs": {"runs": 1, "seconds": 30, "args": [], "drive": "tabs"},
        "switches": {"runs": 3, "seconds": 45, "args": ["--demo-tournaments=teams", "--demo-elo"], "drive": "switches"},
        "switchesfirst": {"runs": 3, "seconds": 15, "args": ["--demo-stats"], "drive": "switches"},
        "scroll": {"runs": 1, "seconds": 20, "args": ["--open-tab=workshop"], "drive": "scroll"},
        "resize": {"runs": 1, "seconds": 20, "args": [], "drive": "resize"},
        "mods": {"runs": 1, "seconds": 30, "args": [], "drive": "mods"},
    }


SWITCH_ROUTE = ("TopTabMultiplayer", "SubtabRooms", "SubtabTournaments", "SubtabRanking", "SubtabStats", "TopTabPlay")


def drive(kind, pid, stop):
    """What the launcher is made to do while perfprobe watches it. The PowerShell clicks cost the
    PowerShell process, which perfprobe does not count."""
    def uia_call(*rest):
        subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                        str(HERE / "uia.ps1"), *rest, "-ProcessId", str(pid)], capture_output=True)
    step = 0
    while not stop.is_set():
        if kind == "tabs":
            uia_call("invoke", "-Id", ("TopTabMultiplayer", "TopTabPlay")[step % 2])
            stop.wait(1.0)
        elif kind == "switches":
            uia_call("invoke", "-Id", SWITCH_ROUTE[step % len(SWITCH_ROUTE)])
            stop.wait(0.8)
        elif kind == "mods":
            uia_call("invoke", "-Id", ("ModRow_improvement-mod", "ModRow_aoe3-tad", "ModRow_wol")[step % 3])
            stop.wait(1.0)
        elif kind == "scroll":
            uia_call("scroll", "-Value", str(step * 13 % 101))
            stop.wait(0.2)
        elif kind == "resize":
            window = windows(pid)[0]

            # SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE
            user32.SetWindowPos(window["hwnd"], None, 0, 0, 1200 + step % 5 * 100, 880 + step % 3 * 60, 0x2 | 0x4 | 0x10)
            stop.wait(0.25)
        step += 1


def perf(args):
    """Each run is a new process: EFFECTS REDUCED lasts a session and would colour what follows."""
    scenario = perf_scenarios()[args.scenario]
    probe = HERE / "perfprobe.py"
    results = []
    for i in range(scenario["runs"]):
        ns = argparse.Namespace(keep=False, size=args.size, max=args.max, lang="es", glass=None, software=args.software,
                                rest=scenario["args"])
        launch(ns)
        pid = run_pid()
        sample = OUT / "perf" / f"{args.label}-{args.scenario}-{i}.json"
        sample.parent.mkdir(parents=True, exist_ok=True)
        stop = threading.Event()
        if "drive" in scenario:
            threading.Thread(target=drive, args=(scenario["drive"], pid, stop), daemon=True).start()
        subprocess.run([sys.executable, "-I", str(probe), str(pid), str(scenario["seconds"]), str(sample)], check=True)
        stop.set()
        log = parse_log()
        close()
        results.append({"probe": json.loads(sample.read_text()), "log": log})
    timing = [r["log"]["timing"].get("window shown") for r in results]
    timing = [t for t in timing if t]
    frame = [v for r in results for v in frame_ms(r["log"]["perf"])]
    summary = {
        "scenario": args.scenario, "software": args.software, "runs": scenario["runs"],
        "window_shown_ms": statistics.median(timing) if timing else None,
        "cpu_percent": statistics.median(r["probe"]["cpu_percent_settled"] for r in results),
        "private_mb": max(r["probe"]["private_mb_max"] for r in results),
        "working_set_mb": max(r["probe"]["working_set_mb_max"] for r in results),
        "gpu_percent": statistics.median(r["probe"]["gpu_percent"] for r in results),
        "redraws_per_second": statistics.median(
            statistics.fmean(p[1] for p in r["log"]["perf"][-max(1, scenario["seconds"] // 2):] or [(0, 0)])
            for r in results),
        "frame_ms_p95": round(percentile(frame, .95), 1),
        "stalls_over_250ms": sum(len(r["log"]["stalls"]) for r in results),
        "slow_ops": sum(len(r["log"]["ops"]) for r in results),
        "layout_storms": sum(r["log"]["storms"] for r in results),
        "layout_storm_lines": [line for r in results for line in r["log"].get("storm_lines", [])],
        "effects_reduced": any(r["log"]["effects_reduced"] for r in results),
    }
    switches = [s for r in results for s in r["log"]["switches"]]
    if switches:
        first = [s for s in switches if s[2]]
        repeat = [s for s in switches if not s[2]]
        summary.update({
            "switch_first_worst_ms": max((s[1] for s in first), default=0),
            "switch_repeat_median_ms": statistics.median(s[1] for s in repeat) if repeat else 0,
            "switch_repeat_worst_ms": max((s[1] for s in repeat), default=0),
            "switch_by_page": {page: {
                "first": max((s[1] for s in first if s[0] == page), default=None),
                "repeat_median": statistics.median([s[1] for s in repeat if s[0] == page] or [0]),
                "repeat_worst": max((s[1] for s in repeat if s[0] == page), default=None),
            } for page in sorted({s[0] for s in switches})},
        })
    name = f"{args.label}-{args.scenario}{'-sw' if args.software else ''}.json"
    (OUT / "perf" / name).write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary, indent=2))


BUDGETS = (
    ("window_shown_ms", "ratio", 1.10), ("cpu_percent", "add", 1.0), ("private_mb", "ratio", 1.15),
    ("stalls_over_250ms", "add", 0), ("layout_storms", "add", 0),
)


def compare(args):
    current, baseline = (json.loads(pathlib.Path(p).read_text()) for p in (args.current, args.baseline))
    failed = False
    for key, kind, value in BUDGETS:
        now, base = current.get(key), baseline.get(key)
        if now is None or base is None:
            continue
        limit = base * value if kind == "ratio" else base + value
        verdict = "ok" if now <= limit else "OVER"
        failed |= verdict != "ok"
        print(f"{key:<22} {base:>9} -> {now:>9}  limit {limit:.1f}  {verdict}")
    for key, goal in (("switch_repeat_worst_ms", 100), ("switch_first_worst_ms", 250)):
        if key in current:
            allowed = goal * 3 if current.get("software") else goal
            over = current[key] > allowed
            failed |= over
            print(f"{key:<22} {current[key]} (goal {allowed})  {'OVER' if over else 'ok'}")
    limit = 33 if current.get("software") else 16
    over = current.get("frame_ms_p95", 0) > limit
    failed |= over
    print(f"{'frame_ms_p95':<22} {current.get('frame_ms_p95')} (limit {limit})  {'OVER' if over else 'ok'}")
    if current.get("effects_reduced") and not baseline.get("effects_reduced"):
        failed = True
        print("effects_reduced        the governor turned the lights off: too slow")
    sys.exit(1 if failed else 0)


# --- the player's machine --------------------------------------------------------------------------

def machine_state():
    def query(key, value=None):
        command = ["reg", "query", key] + (["/v", value] if value else ["/ve"])
        return subprocess.run(command, capture_output=True, text=True).stdout.strip()
    config = {}
    path = REAL_DATA / "launcher-config.json"
    if path.exists():
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        # The player's launcher saves this file all day; these are what a stray test run would move.
        config = {"startWithWindows": data.get("startWithWindows"), "activeModId": data.get("activeModId"),
                  "installs": {k: v.get("installPath") for k, v in data.get("mods", {}).items()}}
    listing = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Aoe3ModLauncher.exe", "/FO", "CSV", "/NH"],
                             capture_output=True, text=True).stdout
    own = run_pid()
    pids = sorted(int(p) for p in re.findall(r'^"[^"]+","(\d+)"', listing, re.M) if int(p) != own)
    def names(folder, pattern="*.lnk"):
        return sorted(item.name for item in folder.glob(pattern)) if folder.is_dir() else []
    def uninstall(root):
        text = subprocess.run(["reg", "query", root + r"\Software\Microsoft\Windows\CurrentVersion\Uninstall"],
                              capture_output=True, text=True).stdout
        return sorted(line.rsplit("\\", 1)[-1] for line in text.splitlines() if "\\" in line)
    games = REAL_DOCUMENTS / "My Games"
    programs = pathlib.Path(os.environ["APPDATA"]) / "Microsoft" / "Windows" / "Start Menu" / "Programs"
    return {"run_key": query(r"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Aoe3ModLauncher"),
            "deep_link": query(r"HKCU\Software\Classes\wol-launcher\shell\open\command"),
            "config": config, "player_launcher_pids": pids,
            "desktop": names(pathlib.Path(os.environ["USERPROFILE"]) / "Desktop"),
            "start_menu": names(programs) + names(programs, "*/*.lnk"),
            "uninstall_hkcu": uninstall("HKCU"), "uninstall_hklm": uninstall("HKLM"),
            "my_games": sorted(item.name for item in games.iterdir()) if games.is_dir() else [],
            "my_games_profiles": {str(f.relative_to(games)): f.stat().st_mtime_ns
                                  for mod in ("Wars of Liberty", "AoE3 Improvement Mod", "Age of Empires 1 HD Edition")
                                  for f in (games / mod / "Users3").glob("*")}}


def safety(action):
    path = OUT / "safety.json"
    now = machine_state()
    if action == "record":
        OUT.mkdir(exist_ok=True)
        path.write_text(json.dumps(now, indent=2))
        print(f"testbed: recorded; the player's launcher is {now['player_launcher_pids']}")
        return
    before = json.loads(path.read_text())
    changed = [key for key in before if before[key] != now[key]]
    for key in changed:
        print(f"CHANGED {key}: {before[key]!r} -> {now[key]!r}")
    if changed:
        sys.exit(1)
    print("testbed: the player's machine is as it was")


def uia(rest):
    pid = run_pid()
    if not pid:
        sys.exit("testbed: no launcher from this run")
    command = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(HERE / "uia.ps1"),
               "-ProcessId", str(pid), *rest]
    sys.exit(subprocess.run(command).returncode)


# --- command line ----------------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("build")
    sub.add_parser("monitors")
    p = sub.add_parser("launch")
    p.add_argument("--size")
    p.add_argument("--max", action="store_true")
    p.add_argument("--lang", default="es")
    p.add_argument("--glass")
    p.add_argument("--software", action="store_true")
    p.add_argument("--keep", action="store_true", help="keep the data folder of the last run")
    p.add_argument("rest", nargs="*")
    sub.add_parser("close")
    sub.add_parser("windows")
    p = sub.add_parser("place")
    p.add_argument("--title")
    p = sub.add_parser("shot")
    p.add_argument("--popup", action="store_true")
    p.add_argument("file")
    p.add_argument("--title")
    p.add_argument("--screen", action="store_true")
    p.add_argument("--top", action="store_true", help="raise it first, then copy the screen")
    p = sub.add_parser("record")
    p.add_argument("file")
    p.add_argument("--seconds", type=float, default=10)
    p.add_argument("--fps", type=int, default=15)
    p.add_argument("--title")
    p.add_argument("--master", action="store_true")
    p = sub.add_parser("frames")
    p.add_argument("video")
    p.add_argument("--count", type=int, default=3)
    p = sub.add_parser("sidebyside")
    p.add_argument("reference")
    p.add_argument("shot")
    p.add_argument("out")
    p = sub.add_parser("sheet")
    p.add_argument("reference")
    p.add_argument("shot")
    p.add_argument("out")
    p = sub.add_parser("check")
    p.add_argument("name")
    p.add_argument("--ref", required=True, help="the prototype file")
    p.add_argument("--block", required=True)
    p.add_argument("--folder", default="design_base_launcher_2")
    p.add_argument("--props", default="{}")
    p.add_argument("--frame")
    p.add_argument("--click")
    p.add_argument("--size", default="1366x768")
    p.add_argument("--lang", default="es")
    p.add_argument("--glass", default="mid", choices=("solid", "mid", "max"))
    p.add_argument("--title", help="capture this window instead of the main one")
    p.add_argument("--wait", type=float, default=5)
    p.add_argument("--keep", action="store_true")
    p.add_argument("--build", action="store_true")
    p.add_argument("rest", nargs="*")
    p = sub.add_parser("log")
    p.add_argument("--grep")
    p.add_argument("--tail", type=int)
    p = sub.add_parser("perf")
    p.add_argument("scenario", choices=sorted(perf_scenarios()))
    p.add_argument("--software", action="store_true")
    p.add_argument("--label", default="current")
    p.add_argument("--size")
    p.add_argument("--max", action="store_true")
    p = sub.add_parser("compare")
    p.add_argument("current")
    p.add_argument("baseline")
    p = sub.add_parser("safety")
    p.add_argument("action", choices=("record", "check"))
    p = sub.add_parser("uia")
    p.add_argument("rest", nargs=argparse.REMAINDER)
    args = parser.parse_args()

    if args.command == "build":
        sys.exit(subprocess.run(["dotnet", "build", str(REPO / "WarsOfLibertyLauncher"), "-c", "Release", "-v", "q",
                                 "--nologo"]).returncode)
    elif args.command == "monitors":
        chosen = test_monitor()["device"]
        for m in monitors():
            print(f"{'*' if m['device'] == chosen else ' '} {m['device']} {m['name']} scale {m['scale']:g} "
                  f"work {m['work']}{' primary' if m['primary'] else ''}")
    elif args.command == "launch":
        launch(args)
    elif args.command == "close":
        close()
    elif args.command == "windows":
        for w in windows():
            print(f"{w['hwnd']} {w['rect']} {w['title']}")
    elif args.command == "place":
        print("moved" if place(pick_window(args.title)) else "already there")
    elif args.command == "shot":
        shot(args)
    elif args.command == "record":
        record(args)
    elif args.command == "frames":
        frames(args)
    elif args.command == "sidebyside":
        sidebyside(args)
    elif args.command == "sheet":
        sheet(args.reference, args.shot, args.out)
    elif args.command == "check":
        args.glass_name = {"solid": "S\u00f3lida", "mid": "Media", "max": "M\u00e1xima"}[args.glass]
        check(args)
    elif args.command == "log":
        lines = LOG.read_text(encoding="utf-8", errors="replace").splitlines() if LOG.exists() else []
        if args.grep:
            lines = [line for line in lines if re.search(args.grep, line)]
        print("\n".join(lines[-args.tail:] if args.tail else lines))
    elif args.command == "perf":
        perf(args)
    elif args.command == "compare":
        compare(args)
    elif args.command == "safety":
        safety(args.action)
    elif args.command == "uia":
        uia(args.rest)


if __name__ == "__main__":
    main()
