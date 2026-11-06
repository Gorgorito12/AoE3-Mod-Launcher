"""Samples what one process costs, from outside it. Adapted from perfprobe.py in Unharcoded-AOE3- NEW,
without the game's addresses, its stack sampler and its shared-memory statistics.

Usage: python -I tools/testbed/perfprobe.py <pid> <seconds> <out.json> [--interval 1.0]

Read-only: the process is opened with PROCESS_QUERY_LIMITED_INFORMATION and nothing else.
cpu_percent is of one core; the first quarter of the run is left out of cpu_percent_settled,
because a launcher that has just started is still doing start-up work.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import json
import statistics
import sys
import time

PDH_FMT_DOUBLE = 0x200
PDH_MORE_DATA = 0x800007D2

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi")
pdh = ctypes.WinDLL("pdh")
k32.OpenProcess.restype = wt.HANDLE
k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
k32.GetProcessTimes.argtypes = [wt.HANDLE] + [ctypes.POINTER(wt.FILETIME)] * 4
k32.GetExitCodeProcess.argtypes = [wt.HANDLE, ctypes.POINTER(wt.DWORD)]
pdh.PdhOpenQueryW.restype = wt.DWORD
pdh.PdhOpenQueryW.argtypes = [wt.LPCWSTR, ctypes.c_size_t, ctypes.POINTER(wt.HANDLE)]
pdh.PdhAddEnglishCounterW.restype = wt.DWORD
pdh.PdhAddEnglishCounterW.argtypes = [wt.HANDLE, wt.LPCWSTR, ctypes.c_size_t, ctypes.POINTER(wt.HANDLE)]
pdh.PdhCollectQueryData.restype = wt.DWORD
pdh.PdhCollectQueryData.argtypes = [wt.HANDLE]
pdh.PdhGetFormattedCounterArrayW.restype = wt.DWORD
pdh.PdhGetFormattedCounterArrayW.argtypes = [wt.HANDLE, wt.DWORD, ctypes.POINTER(wt.DWORD),
                                             ctypes.POINTER(wt.DWORD), ctypes.c_void_p]


class PROCESS_MEMORY_COUNTERS_EX(ctypes.Structure):
    _fields_ = [("cb", wt.DWORD), ("PageFaultCount", wt.DWORD), ("PeakWorkingSetSize", ctypes.c_size_t),
                ("WorkingSetSize", ctypes.c_size_t), ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPagedPoolUsage", ctypes.c_size_t), ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                ("QuotaNonPagedPoolUsage", ctypes.c_size_t), ("PagefileUsage", ctypes.c_size_t),
                ("PeakPagefileUsage", ctypes.c_size_t), ("PrivateUsage", ctypes.c_size_t)]


class PDH_FMT_COUNTERVALUE(ctypes.Structure):
    _fields_ = [("CStatus", wt.DWORD), ("doubleValue", ctypes.c_double)]


class PDH_FMT_COUNTERVALUE_ITEM_W(ctypes.Structure):
    _fields_ = [("szName", ctypes.c_wchar_p), ("FmtValue", PDH_FMT_COUNTERVALUE)]


class Gpu:
    """Sum of the process's 3D-engine utilisation over all its GPU engines."""

    def __init__(self, pid):
        self.query, self.counter = wt.HANDLE(), wt.HANDLE()
        path = f"\\GPU Engine(pid_{pid}_*engtype_3D)\\Utilization Percentage"
        self.ok = (pdh.PdhOpenQueryW(None, 0, ctypes.byref(self.query)) == 0 and
                   pdh.PdhAddEnglishCounterW(self.query, path, 0, ctypes.byref(self.counter)) == 0)
        if self.ok:
            pdh.PdhCollectQueryData(self.query)

    def sample(self):
        if not self.ok or pdh.PdhCollectQueryData(self.query) != 0:
            return 0.0
        size, count = wt.DWORD(0), wt.DWORD(0)
        status = pdh.PdhGetFormattedCounterArrayW(self.counter, PDH_FMT_DOUBLE, ctypes.byref(size),
                                                  ctypes.byref(count), None)
        if status != PDH_MORE_DATA:
            return 0.0
        buf = ctypes.create_string_buffer(size.value)
        if pdh.PdhGetFormattedCounterArrayW(self.counter, PDH_FMT_DOUBLE, ctypes.byref(size),
                                            ctypes.byref(count), buf) != 0:
            return 0.0
        items = ctypes.cast(buf, ctypes.POINTER(PDH_FMT_COUNTERVALUE_ITEM_W))
        return sum(items[i].FmtValue.doubleValue for i in range(count.value) if items[i].FmtValue.CStatus in (0, 1))


def cpu_seconds(handle):
    times = [wt.FILETIME() for _ in range(4)]
    k32.GetProcessTimes(handle, *[ctypes.byref(t) for t in times])
    return sum((t.dwHighDateTime << 32 | t.dwLowDateTime) / 1e7 for t in times[2:])


def alive(handle):
    code = wt.DWORD()
    return k32.GetExitCodeProcess(handle, ctypes.byref(code)) and code.value == 259  # STILL_ACTIVE


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pid", type=int)
    parser.add_argument("seconds", type=float)
    parser.add_argument("out")
    parser.add_argument("--interval", type=float, default=1.0)
    args = parser.parse_args()

    handle = k32.OpenProcess(0x1000, False, args.pid)  # PROCESS_QUERY_LIMITED_INFORMATION
    if not handle:
        sys.exit(f"perfprobe: cannot open process {args.pid}")
    gpu = Gpu(args.pid)
    rows, end = [], time.time() + args.seconds
    last_cpu, last_time = cpu_seconds(handle), time.perf_counter()
    while time.time() < end and alive(handle):
        time.sleep(args.interval)
        cpu, now = cpu_seconds(handle), time.perf_counter()
        memory = PROCESS_MEMORY_COUNTERS_EX()
        memory.cb = ctypes.sizeof(memory)
        psapi.GetProcessMemoryInfo(wt.HANDLE(handle), ctypes.byref(memory), memory.cb)
        rows.append({"cpu": round((cpu - last_cpu) / (now - last_time) * 100, 2), "gpu": round(gpu.sample(), 2),
                     "working_set_mb": round(memory.WorkingSetSize / 1048576, 1),
                     "private_mb": round(memory.PrivateUsage / 1048576, 1)})
        last_cpu, last_time = cpu, now
    if not rows:
        sys.exit("perfprobe: the process ended before the first sample")
    settled = rows[len(rows) // 4:]
    summary = {
        "samples": len(rows),
        "cpu_percent_settled": round(statistics.fmean(r["cpu"] for r in settled), 2),
        "cpu_percent_max": max(r["cpu"] for r in rows),
        "gpu_percent": round(statistics.fmean(r["gpu"] for r in settled), 2),
        "working_set_mb_max": max(r["working_set_mb"] for r in rows),
        "private_mb_max": max(r["private_mb"] for r in rows),
        "rows": rows,
    }
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(summary, f)
    print(f"perfprobe: pid {args.pid}: cpu {summary['cpu_percent_settled']} %, private {summary['private_mb_max']} MB")


if __name__ == "__main__":
    main()
