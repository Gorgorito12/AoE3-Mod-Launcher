"""Per-thread CPU use of a running process, observed from outside: no suspension, no memory read,
no write. Written to see how AoE3 DE spreads its work over threads (docs/de-architecture.md); it
works on any process, 32- or 64-bit.

Usage: python -I tools/threadprobe.py <process name> <out.csv> [seconds (default 120)]
                                      [interval (default 1.0)] [--wait] [--pdata EXE]
                                      [--sample-hz N] [--sample-top K]

--wait waits for the process to start and records until it exits, ignoring `seconds`.
--pdata EXE also names, for each start address inside EXE, the function that contains it
(tools/re/pe64.py, from the exe file on disk).

Every interval it writes one CSV row per thread: time, thread ID, CPU in % of one core
(GetThreadTimes), the name the program gave the thread (GetThreadDescription; empty when the
program never called SetThreadDescription), and its start address as module+RVA. The module comes
from VirtualQueryEx and GetMappedFileName, which read the memory map's metadata, not memory.
At the end it prints a summary: each thread's mean and peak CPU, threads grouped by start
address (worker pools share one), and the process total.

--sample-hz N also profiles the K busiest threads (--sample-top, default 4), N times a second
each: it suspends the thread, reads only its instruction pointer (GetThreadContext, or
Wow64GetThreadContext for a 32-bit process) and resumes it, a few microseconds per sample. Still no
memory is read. The profile goes to <out.csv>.samples.txt: per thread, the functions it spends its
time in (module+RVA, and the exe function with --pdata).

Access rights: PROCESS_QUERY_INFORMATION on the process, THREAD_QUERY_INFORMATION on its threads,
plus THREAD_SUSPEND_RESUME and THREAD_GET_CONTEXT on the sampled ones.
Stop early with Ctrl+C.
"""
import argparse
import csv
import ctypes
import threading
from collections import Counter
import ctypes.wintypes as wt
import os
import sys
import time

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
ntdll = ctypes.WinDLL("ntdll")
psapi = ctypes.WinDLL("psapi", use_last_error=True)

TH32CS_SNAPPROCESS, TH32CS_SNAPTHREAD = 0x2, 0x4
PROCESS_QUERY_INFORMATION = 0x0400
THREAD_QUERY_INFORMATION = 0x0040
STILL_ACTIVE = 259


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", wt.DWORD), ("cntUsage", wt.DWORD), ("th32ProcessID", wt.DWORD),
                ("th32DefaultHeapID", ctypes.c_size_t), ("th32ModuleID", wt.DWORD), ("cntThreads", wt.DWORD),
                ("th32ParentProcessID", wt.DWORD), ("pcPriClassBase", ctypes.c_long), ("dwFlags", wt.DWORD),
                ("szExeFile", ctypes.c_wchar * 260)]


class THREADENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", wt.DWORD), ("cntUsage", wt.DWORD), ("th32ThreadID", wt.DWORD),
                ("th32OwnerProcessID", wt.DWORD), ("tpBasePri", ctypes.c_long), ("tpDeltaPri", ctypes.c_long),
                ("dwFlags", wt.DWORD)]


class MEMORY_BASIC_INFORMATION(ctypes.Structure):
    _fields_ = [("BaseAddress", ctypes.c_void_p), ("AllocationBase", ctypes.c_void_p),
                ("AllocationProtect", wt.DWORD), ("PartitionId", wt.WORD), ("RegionSize", ctypes.c_size_t),
                ("State", wt.DWORD), ("Protect", wt.DWORD), ("Type", wt.DWORD)]


for name, res, args in [
    ("CreateToolhelp32Snapshot", wt.HANDLE, [wt.DWORD, wt.DWORD]),
    ("Process32FirstW", wt.BOOL, [wt.HANDLE, ctypes.c_void_p]),
    ("Process32NextW", wt.BOOL, [wt.HANDLE, ctypes.c_void_p]),
    ("Thread32First", wt.BOOL, [wt.HANDLE, ctypes.c_void_p]),
    ("Thread32Next", wt.BOOL, [wt.HANDLE, ctypes.c_void_p]),
    ("OpenProcess", wt.HANDLE, [wt.DWORD, wt.BOOL, wt.DWORD]),
    ("OpenThread", wt.HANDLE, [wt.DWORD, wt.BOOL, wt.DWORD]),
    ("CloseHandle", wt.BOOL, [wt.HANDLE]),
    ("GetThreadTimes", wt.BOOL, [wt.HANDLE] + [ctypes.POINTER(ctypes.c_ulonglong)] * 4),
    ("GetProcessTimes", wt.BOOL, [wt.HANDLE] + [ctypes.POINTER(ctypes.c_ulonglong)] * 4),
    ("GetExitCodeProcess", wt.BOOL, [wt.HANDLE, ctypes.POINTER(wt.DWORD)]),
    ("VirtualQueryEx", ctypes.c_size_t, [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t]),
    ("GetThreadDescription", ctypes.c_long, [wt.HANDLE, ctypes.POINTER(ctypes.c_wchar_p)]),
    ("LocalFree", ctypes.c_void_p, [ctypes.c_void_p]),
    ("SuspendThread", wt.DWORD, [wt.HANDLE]),
    ("ResumeThread", wt.DWORD, [wt.HANDLE]),
    ("GetThreadContext", wt.BOOL, [wt.HANDLE, ctypes.c_void_p]),
    ("Wow64GetThreadContext", wt.BOOL, [wt.HANDLE, ctypes.c_void_p]),
    ("IsWow64Process", wt.BOOL, [wt.HANDLE, ctypes.POINTER(wt.BOOL)]),
    ("QueryDosDeviceW", wt.DWORD, [wt.LPCWSTR, wt.LPWSTR, wt.DWORD]),
]:
    f = getattr(k32, name)
    f.restype, f.argtypes = res, args
ntdll.NtQueryInformationThread.restype = ctypes.c_long
ntdll.NtQueryInformationThread.argtypes = [wt.HANDLE, ctypes.c_int, ctypes.c_void_p, wt.ULONG,
                                           ctypes.POINTER(wt.ULONG)]
psapi.GetMappedFileNameW.restype = wt.DWORD
psapi.GetMappedFileNameW.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.LPWSTR, wt.DWORD]


def find_pid(exe):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    entry = PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(entry)
    pid = None
    ok = k32.Process32FirstW(snap, ctypes.byref(entry))
    while ok:
        if entry.szExeFile.lower() == exe.lower():
            pid = entry.th32ProcessID
        ok = k32.Process32NextW(snap, ctypes.byref(entry))
    k32.CloseHandle(snap)
    return pid


def thread_ids(pid):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    entry = THREADENTRY32()
    entry.dwSize = ctypes.sizeof(entry)
    out = []
    ok = k32.Thread32First(snap, ctypes.byref(entry))
    while ok:
        if entry.th32OwnerProcessID == pid:
            out.append(entry.th32ThreadID)
        ok = k32.Thread32Next(snap, ctypes.byref(entry))
    k32.CloseHandle(snap)
    return out


def times(handle, process=False):
    creation, exit_, kernel, user = (ctypes.c_ulonglong() for _ in range(4))
    fn = k32.GetProcessTimes if process else k32.GetThreadTimes
    if not fn(handle, ctypes.byref(creation), ctypes.byref(exit_), ctypes.byref(kernel), ctypes.byref(user)):
        return None
    return creation.value, kernel.value + user.value


def description(handle):
    text = ctypes.c_wchar_p()
    if k32.GetThreadDescription(handle, ctypes.byref(text)) < 0 or not text.value:
        return ""
    value = text.value
    k32.LocalFree(ctypes.cast(text, ctypes.c_void_p))
    return value


def start_address(handle):
    address = ctypes.c_void_p()
    if ntdll.NtQueryInformationThread(handle, 9, ctypes.byref(address), ctypes.sizeof(address), None) != 0:
        return 0
    return address.value or 0


def locate(process, address, cache):
    """(module file name, RVA) of an address, from the memory map's metadata. `cache` also keeps
    each module's device path under the key ('path', name)."""
    if not address:
        return "?", 0
    info = MEMORY_BASIC_INFORMATION()
    if not k32.VirtualQueryEx(process, ctypes.c_void_p(address), ctypes.byref(info), ctypes.sizeof(info)):
        return "?", address
    base = info.AllocationBase or 0
    if base not in cache:
        buf = ctypes.create_unicode_buffer(1024)
        n = psapi.GetMappedFileNameW(process, ctypes.c_void_p(address), buf, len(buf))
        cache[base] = os.path.basename(buf.value) if n else "?"
        if n:
            cache[("path", cache[base])] = buf.value
    return cache[base], address - base


def dos_path(device_path):
    """A kernel device path (as GetMappedFileName returns it) as a drive-letter path, or None."""
    buf = ctypes.create_unicode_buffer(1024)
    for letter in "CDEFGHIJKLMNOPQRSTUVWXYZ":
        if k32.QueryDosDeviceW(f"{letter}:", buf, len(buf)) and device_path.lower().startswith(buf.value.lower() + "\\"):
            return f"{letter}:" + device_path[len(buf.value):]
    return None


def exports(path):
    """Sorted [(rva, name)] of a DLL's named exports (PE32 or PE32+), read from the file."""
    import struct
    try:
        data = open(path, "rb").read()
    except OSError:
        return []
    nt = struct.unpack_from("<I", data, 0x3C)[0]
    opt = nt + 24
    directory = opt + (0x60 if struct.unpack_from("<H", data, opt)[0] == 0x10B else 0x70)
    export_rva = struct.unpack_from("<I", data, directory)[0]
    count, optional_size = struct.unpack_from("<H", data, nt + 6)[0], struct.unpack_from("<H", data, nt + 20)[0]
    sections = [struct.unpack_from("<IIII", data, opt + optional_size + 40 * i + 8) for i in range(count)]

    def off(rva):
        return next((rva - va + raw for vsize, va, size, raw in sections if va <= rva < va + max(vsize, size)), None)

    base = off(export_rva) if export_rva else None
    if base is None:
        return []
    names, functions, address_rva, name_rva, ordinal_rva = struct.unpack_from("<IIIII", data, base + 0x14)
    out = []
    for i in range(names):
        name_at = off(struct.unpack_from("<I", data, off(name_rva) + 4 * i)[0])
        ordinal = struct.unpack_from("<H", data, off(ordinal_rva) + 2 * i)[0]
        name = data[name_at:data.index(b"\0", name_at)].decode(errors="replace")
        out.append((struct.unpack_from("<I", data, off(address_rva) + 4 * ordinal)[0], name))
    return sorted(out)


class Sampler(threading.Thread):
    """Instruction-pointer profile of chosen threads: suspend, read the context's IP, resume."""

    def __init__(self, process, hz):
        super().__init__(daemon=True)
        wow64 = wt.BOOL()
        k32.IsWow64Process(process, ctypes.byref(wow64))
        self.wow64 = bool(wow64.value)
        self.period = 1.0 / hz
        self.handles, self.counts, self.lock, self.stop = {}, {}, threading.Lock(), threading.Event()

    def watch(self, tids):
        with self.lock:
            for tid in tids:
                if tid not in self.handles:
                    handle = k32.OpenThread(0x0002 | 0x0008 | THREAD_QUERY_INFORMATION, False, tid)
                    if handle:
                        self.handles[tid] = handle
                        self.counts[tid] = Counter()

    def run(self):
        if self.wow64:  # WOW64_CONTEXT: 0x2CC bytes, CONTEXT_CONTROL 0x10001, Eip at 0xB8
            raw = (ctypes.c_ubyte * 0x2CC)()
            ctx, flags, ip_at, get = ctypes.addressof(raw), 0x00010001, 0xB8, k32.Wow64GetThreadContext
        else:  # x64 CONTEXT: 0x4D0 bytes, 16-byte aligned, CONTEXT_CONTROL 0x100001, Rip at 0xF8
            raw = (ctypes.c_ubyte * (0x4D0 + 16))()
            ctx = (ctypes.addressof(raw) + 15) & ~15
            flags, ip_at, get = 0x00100001, 0xF8, k32.GetThreadContext
        while not self.stop.is_set():
            with self.lock:
                targets = list(self.handles.items())
            for tid, handle in targets:
                ctypes.memmove(ctx + (0 if self.wow64 else 0x30), flags.to_bytes(4, "little"), 4)
                if k32.SuspendThread(handle) == 0xFFFFFFFF:
                    continue
                try:
                    ok = get(handle, ctypes.c_void_p(ctx))
                finally:
                    k32.ResumeThread(handle)
                if ok:
                    size = 4 if self.wow64 else 8
                    ip = int.from_bytes(ctypes.string_at(ctx + ip_at, size), "little")
                    self.counts[tid][ip] += 1
            time.sleep(self.period)


def sample_report(sampler, threads, process, modules, pe, exe_name, path):
    import bisect
    tables = {}

    def export_name(module, rva):
        if module not in tables:
            device = modules.get(("path", module))
            file = dos_path(device) if device else None
            table = exports(file) if file else []
            tables[module] = ([r for r, _ in table], [n for _, n in table])
        rvas, names = tables[module]
        k = bisect.bisect_right(rvas, rva) - 1
        return names[k] if k >= 0 and rva - rvas[k] < 0x1000 else None

    lines = []
    for tid, counts in sorted(sampler.counts.items(), key=lambda kv: -sum(kv[1].values())):
        total = sum(counts.values())
        if total == 0:
            continue
        th = threads.get(tid, {})
        by_function = Counter()
        for ip, n in counts.items():
            module, rva = locate(process, ip, modules)
            if pe is not None and module.lower() == exe_name:
                entry = pe.function_of(rva)
                key = f"{module} fn {entry[2]:#x}" if entry else f"{module} {pe.where(rva)}"
            else:
                name = export_name(module, rva)
                key = f"{module}!{name}" if name else f"{module}+{rva & ~0xF:#x}"
            by_function[key] += n
        lines.append(f"tid {tid} {th.get('name') or ''} start {th.get('start', '?')}: {total} samples")
        lines += [f"  {n / total * 100:5.1f}%  {key}" for key, n in by_function.most_common(25)]
        lines.append("")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines[:60]))
    print(f"... profile: {path}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("process", help="exe name, or a process ID")
    parser.add_argument("out_csv")
    parser.add_argument("seconds", nargs="?", type=float, default=120.0)
    parser.add_argument("interval", nargs="?", type=float, default=1.0)
    parser.add_argument("--wait", action="store_true")
    parser.add_argument("--pdata", help="the process's exe file, to name functions (tools/re/pe64.py)")
    parser.add_argument("--sample-hz", type=float, default=0.0, help="profile the busiest threads (e.g. 100)")
    parser.add_argument("--sample-top", type=int, default=4, help="how many busy threads to profile")
    args = parser.parse_args()
    exe_name = None

    pe = None
    if args.pdata:
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "re"))
        import pe64
        pe = pe64.PE64(args.pdata)
        exe_name = os.path.basename(args.pdata).lower()

    pid = int(args.process) if args.process.isdigit() else find_pid(args.process)
    if pid is None and args.wait:
        print(f"waiting for {args.process} ...", flush=True)
        while (pid := find_pid(args.process)) is None:
            time.sleep(2)
    if pid is None:
        sys.exit(f"{args.process} is not running")
    process = k32.OpenProcess(PROCESS_QUERY_INFORMATION, False, pid)
    if not process:
        sys.exit(f"OpenProcess failed: {ctypes.get_last_error()}")
    print(f"pid {pid}; recording per-thread CPU every {args.interval} s", flush=True)
    sampler = Sampler(process, args.sample_hz) if args.sample_hz > 0 else None
    if sampler:
        sampler.start()

    modules, threads, stats = {}, {}, {}
    last_t, t0 = time.perf_counter(), time.perf_counter()
    last_process = times(process, process=True)[1]
    deadline = time.monotonic() + args.seconds
    with open(args.out_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["t", "tid", "cpu", "name", "start", "function"])
        try:
            while args.wait or time.monotonic() < deadline:
                code = wt.DWORD()
                if k32.GetExitCodeProcess(process, ctypes.byref(code)) and code.value != STILL_ACTIVE:
                    print("process exited")
                    break
                for tid in thread_ids(pid):
                    if tid not in threads:
                        handle = k32.OpenThread(THREAD_QUERY_INFORMATION, False, tid)
                        first = times(handle) if handle else None
                        if first is None:
                            continue
                        module, rva = locate(process, start_address(handle), modules)
                        function = ""
                        if pe is not None and module.lower() == exe_name:
                            entry = pe.function_of(rva)
                            function = f"fn {entry[2]:#x}" if entry else pe.where(rva)
                        threads[tid] = {"handle": handle, "created": first[0], "last": first[1],
                                        "start": f"{module}+{rva:#x}", "function": function, "name": ""}
                time.sleep(args.interval)
                now = time.perf_counter()
                dt, last_t = now - last_t, now
                total = times(process, process=True)
                process_cpu = (total[1] - last_process) / 1e7 / dt * 100 if total else 0
                last_process = total[1] if total else last_process
                rows = []
                for tid, th in threads.items():
                    current = times(th["handle"])
                    if current is None:
                        continue
                    cpu = (current[1] - th["last"]) / 1e7 / dt * 100
                    th["last"] = current[1]
                    th["name"] = description(th["handle"]) or th["name"]
                    s = stats.setdefault(tid, {"sum": 0.0, "max": 0.0, "n": 0})
                    s["sum"] += cpu
                    s["max"] = max(s["max"], cpu)
                    s["n"] += 1
                    rows.append((cpu, tid, th))
                for cpu, tid, th in rows:
                    writer.writerow([f"{now - t0:.1f}", tid, f"{cpu:.1f}", th["name"], th["start"], th["function"]])
                f.flush()
                busy = sorted(rows, key=lambda r: r[0], reverse=True)[:max(4, args.sample_top)]
                if sampler:
                    sampler.watch([tid for cpu, tid, _ in busy[:args.sample_top] if cpu >= 5])
                busy = busy[:4]
                print(f"t={now - t0:6.1f} process={process_cpu:6.1f}% threads={len(rows):3d} | " + " | ".join(
                    f"{th['name'] or th['start']}:{cpu:.0f}" for cpu, _, th in busy), flush=True)
        except KeyboardInterrupt:
            pass
    summarize(threads, stats)
    if sampler:
        sampler.stop.set()
        sampler.join(2)
        sample_report(sampler, threads, process, modules, pe, exe_name, args.out_csv + ".samples.txt")


def summarize(threads, stats):
    if not stats:
        print("no samples")
        return
    main_tid = min(threads, key=lambda t: threads[t]["created"])
    print("\nper thread (mean / peak % of one core), busiest first:")
    order = sorted(stats, key=lambda t: stats[t]["sum"] / stats[t]["n"], reverse=True)
    for tid in order[:40]:
        th, s = threads[tid], stats[tid]
        print(f"  {s['sum'] / s['n']:6.1f} {s['max']:6.1f}  tid {tid:6d}{' (main)' if tid == main_tid else ''}  "
              f"{th['name'] or '-':28s} {th['start']} {th['function']}")
    groups = {}
    for tid, s in stats.items():
        key = threads[tid]["start"]
        g = groups.setdefault(key, {"threads": 0, "sum": 0.0, "names": set()})
        g["threads"] += 1
        g["sum"] += s["sum"] / s["n"]
        if threads[tid]["name"]:
            g["names"].add(threads[tid]["name"])
    print("\nby start address (mean % of one core, summed over the group):")
    for key, g in sorted(groups.items(), key=lambda kv: kv[1]["sum"], reverse=True)[:25]:
        names = ", ".join(sorted(g["names"])[:4]) + (" ..." if len(g["names"]) > 4 else "")
        print(f"  {g['sum']:6.1f}  {g['threads']:3d} thread(s)  {key}  {names}")
    total = sum(s["sum"] / s["n"] for s in stats.values())
    print(f"\nall threads: {total:.1f} % of one core on average; main thread "
          f"{stats[main_tid]['sum'] / stats[main_tid]['n']:.1f} %")


if __name__ == "__main__":
    main()
