// AoeP2pHook.dll — Fase 1: skeleton that logs every WinSock call.
//
// Lifecycle:
//   1. The .NET launcher does CreateProcess(age3y.exe, CREATE_SUSPENDED).
//   2. Before the main thread runs, the launcher injects this DLL via
//      LoadLibraryW (CreateRemoteThread + kernel32!LoadLibraryW).
//   3. DllMain attaches Detours hooks to ws2_32 sendto/recvfrom/bind/connect.
//   4. Launcher resumes the main thread. AoE3 boots; every socket call
//      now flows through us first and a line lands in
//      %LOCALAPPDATA%\AoeP2pHook.log.
//
// Why we hook ws2_32 instead of DirectPlay: empirically, age3y.exe (Wars
// of Liberty 1.2.0c2) imports ws2_32.dll and contains zero DirectPlay
// strings. AoE3 / WoL implements its lobby protocol directly on top of
// BSD sockets. WinSock hooks are the right layer for this game family.
//
// Why Detours: maintained by Microsoft, MIT-licensed since 2016,
// production-quality, supports x86 (which age3y.exe is) cleanly.

#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <WinSock2.h>
#include <WS2tcpip.h>
#include <detours.h>
#include <cstdarg>
#include <cstdio>
#include <cstdint>
#include <deque>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#pragma comment(lib, "ws2_32.lib")

// ---- bridge IPC wire protocol (must match AoeP2pBridgeService.cs) -------
//
// 16-byte little-endian header followed by 0..N payload bytes:
//   uint8_t  kind         (1=HELLO, 2=PACKET_OUT, 3=PACKET_IN, 4=PEER_SET)
//   uint8_t  version      (must match AoeP2pBridgeService.ProtocolVersion)
//   uint16_t payloadLen
//   uint32_t srcIp        (IPv4, native order on x86 == little-endian)
//   uint32_t dstIp        (IPv4 — HELLO repurposes this as the hook PID)
//   uint16_t srcPort
//   uint16_t dstPort
//
// All fields are exchanged in little-endian. x86 is little-endian
// natively, so a packed struct copies byte-for-byte to the wire.

#pragma pack(push, 1)
struct BridgeFrameHeader
{
    uint8_t  kind;
    uint8_t  version;
    uint16_t payloadLen;
    uint32_t srcIp;
    uint32_t dstIp;
    uint16_t srcPort;
    uint16_t dstPort;
};
#pragma pack(pop)
static_assert(sizeof(BridgeFrameHeader) == 16, "BridgeFrameHeader must be exactly 16 bytes on the wire");

static constexpr uint8_t kFrameKindHello     = 1;
static constexpr uint8_t kFrameKindPacketOut = 2;
static constexpr uint8_t kFrameKindPacketIn  = 3;
static constexpr uint8_t kFrameKindPeerSet   = 4;
static constexpr uint8_t kBridgeProtocolVersion = 1;

// ---- logging --------------------------------------------------------------
//
// We can't use the .NET launcher's DiagnosticLog from here (different
// process), so we write to a per-user file. AoeP2pHook.log lives in
// %LOCALAPPDATA% so it survives across runs of age3y.exe and is easy
// to find for support.

static HANDLE             g_logFile = INVALID_HANDLE_VALUE;
static CRITICAL_SECTION   g_logLock;
static bool               g_logReady = false;
// Path of the file we ultimately ended up writing to (or empty if all
// fallbacks failed). Reported back to the launcher in the HELLO frame
// so operators can find the log even when LOCALAPPDATA resolves to
// something weird on the user's machine (UAC elevation as a different
// user, OneDrive-redirected profile, AV-quarantined target, etc.).
static wchar_t            g_logPath[MAX_PATH] = {};

// Try to open `path` for append. On success, stores the handle in
// g_logFile, records the path in g_logPath, and returns true.
static bool LogTryOpen(const wchar_t* path)
{
    HANDLE h = CreateFileW(
        path,
        FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr,
        OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    g_logFile = h;
    wcsncpy_s(g_logPath, path, MAX_PATH - 1);
    return true;
}

// Open the log file. Tries multiple locations so a misconfigured
// LOCALAPPDATA doesn't silently lose us every diagnostic line:
//   1. The DLL's own folder — which is the launcher's folder too,
//      and where launcher-debug.log already lives. Easiest place for
//      a user to find both logs side-by-side.
//   2. %LOCALAPPDATA%\AoeP2pHook.log — the original location; works
//      on a normal Windows install.
//   3. %TEMP%\AoeP2pHook.log — almost always writable, last resort
//      so we don't end up in pure silent-fail mode.
// If all three fail (unbelievably hostile environment), Log() will
// silently no-op and the bridge HELLO will report an empty g_logPath
// so the launcher can flag the situation.
static void LogInit(HMODULE hSelf)
{
    InitializeCriticalSection(&g_logLock);
    g_logReady = true;

    wchar_t path[MAX_PATH] = {};

    // 1. DLL folder. GetModuleFileNameW returns the absolute path of
    //    AoeP2pHook.dll inside age3y.exe's address space; strip the
    //    last path segment to get the directory and append our name.
    if (hSelf != nullptr &&
        GetModuleFileNameW(hSelf, path, MAX_PATH) > 0 &&
        GetLastError() != ERROR_INSUFFICIENT_BUFFER)
    {
        wchar_t* lastSlash = wcsrchr(path, L'\\');
        if (lastSlash != nullptr)
        {
            // Overwrite the filename portion (AoeP2pHook.dll) with our
            // log filename. There's always room because the .dll name
            // is longer than the .log name.
            wcscpy_s(lastSlash + 1,
                     MAX_PATH - (size_t)(lastSlash - path) - 1,
                     L"AoeP2pHook.log");
            if (LogTryOpen(path)) return;
        }
    }

    // 2. %LOCALAPPDATA% — the original location.
    wchar_t base[MAX_PATH];
    DWORD got = GetEnvironmentVariableW(L"LOCALAPPDATA", base, MAX_PATH);
    if (got > 0 && got < MAX_PATH)
    {
        swprintf_s(path, L"%s\\AoeP2pHook.log", base);
        if (LogTryOpen(path)) return;
    }

    // 3. %TEMP% — almost always writable, even from a locked-down user.
    got = GetEnvironmentVariableW(L"TEMP", base, MAX_PATH);
    if (got > 0 && got < MAX_PATH)
    {
        swprintf_s(path, L"%s\\AoeP2pHook.log", base);
        if (LogTryOpen(path)) return;
    }

    // All fallbacks failed — leave g_logFile=INVALID_HANDLE_VALUE and
    // g_logPath="" so the bridge reports the situation upstream.
}

static void Log(const char* fmt, ...)
{
    if (!g_logReady || g_logFile == INVALID_HANDLE_VALUE) return;

    EnterCriticalSection(&g_logLock);
    char    buf[1024];
    SYSTEMTIME st;
    GetLocalTime(&st);
    int prefix = snprintf(
        buf, sizeof(buf),
        "[%02d:%02d:%02d.%03d] ",
        st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);

    va_list ap;
    va_start(ap, fmt);
    int written = vsnprintf(
        buf + prefix,
        sizeof(buf) - prefix - 2,
        fmt, ap);
    va_end(ap);

    if (written > 0)
    {
        int total = prefix + written;
        buf[total] = '\n';
        DWORD bw = 0;
        WriteFile(g_logFile, buf, static_cast<DWORD>(total + 1), &bw, nullptr);
        // Phase 2.j: force the OS to make the bytes visible to readers
        // immediately. Without this, the file system cache holds the
        // tail of the log until age3y.exe exits — when we're trying to
        // diagnose what's happening RIGHT NOW (joiner stuck, host idle),
        // the log a reader sees is minutes stale.
        FlushFileBuffers(g_logFile);
    }
    LeaveCriticalSection(&g_logLock);
}

// Pretty-print a sockaddr to "1.2.3.4:567" or "(unsupported)".
// IPv6 not handled yet — AoE3 LAN is IPv4-only in practice.
static void DescribeAddr(const sockaddr* addr, int len, char* out, size_t outLen)
{
    if (!addr || len < static_cast<int>(sizeof(sockaddr_in)) || addr->sa_family != AF_INET)
    {
        snprintf(out, outLen, "(non-IPv4)");
        return;
    }
    const sockaddr_in* in = reinterpret_cast<const sockaddr_in*>(addr);
    char ip[INET_ADDRSTRLEN] = {};
    InetNtopA(AF_INET, &in->sin_addr, ip, sizeof(ip));
    snprintf(out, outLen, "%s:%u", ip, ntohs(in->sin_port));
}

// ---- bridge IPC TCP client ----------------------------------------------
//
// Phase 2.d: connect to the launcher's loopback TCP listener (if
// AOE_P2P_HOOK_PORT is set), send a HELLO frame so the launcher knows
// the hook came up, then enter a read loop that drains PEER_SET and
// PACKET_IN frames.
//
// The connect itself happens on a worker thread spawned from DllMain
// — never block the loader lock with synchronous IO. If the launcher
// hasn't set the env var (e.g. Solo launch path or a stale build of
// the .NET side), this code is a no-op and the hook stays in pure
// passthrough/log mode.
//
// We deliberately do NOT call WSAStartup(): age3y.exe imports
// ws2_32.dll and has already initialised WinSock by the time DllMain
// runs, so socket()/connect()/send()/recv() are all live for us out
// of the box. We also don't call WSACleanup() on detach — age3y.exe
// owns the WinSock lifetime.

static SOCKET             g_bridgeSocket = INVALID_SOCKET;
static CRITICAL_SECTION   g_bridgeLock;
static bool               g_bridgeLockReady = false;
// Phase 2.d.1: BridgeConnectThread now calls WSAStartup itself (the
// "age3y.exe owns WinSock init" shortcut was wrong — see comment in
// BridgeConnectThread). This flag tracks whether the WSAStartup
// succeeded so the DLL_PROCESS_DETACH path can balance it with
// WSACleanup.
static bool               g_bridgeWsaStarted = false;

// ---- bridge writer queue (Phase 2.c) ------------------------------------
//
// Phase 2.b shipped a synchronous Hooked_sendto → BridgeWriteLocked path
// that wrote straight to the pipe from the game's network thread. When
// the named-pipe kernel buffer filled (4 KiB default on the launcher
// side back then), WriteFile blocked the game thread and AoE3 hung on
// "Setting Up Network Connection" forever. The hotfix flipped the divert
// off entirely; this rework re-enables it the right way.
//
// New flow: Hooked_sendto builds a full frame (header + payload) in a
// std::vector, pushes it onto a bounded queue under g_writeQueueLock,
// signals an auto-reset event, and returns immediately. A dedicated
// writer thread (BridgeWriterThread) waits on the event, drains the
// queue, and does the blocking WriteFile work off the game thread. If
// the queue grows past kWriteQueueCapacity we drop the oldest frame —
// UDP is best-effort anyway, and dropping is much better than blocking
// AoE3.
static CRITICAL_SECTION                  g_writeQueueLock;
static bool                              g_writeQueueLockReady = false;
static HANDLE                            g_writeQueueEvent = nullptr;   // auto-reset, signals "queue has work"
static HANDLE                            g_writerThread    = nullptr;
static std::deque<std::vector<uint8_t>>  g_writeQueue;                  // each entry = a full frame (header + payload)
static constexpr size_t                  kWriteQueueCapacity = 256;     // drop-oldest after this many pending frames
static volatile LONG                     g_writerStop = 0;
static volatile LONG                     g_dropCount = 0;               // for logging when we drop on overflow

// ---- launcher-driven state (mesh peers + LAN sockets) ------------------
//
// Phase 2.b adds three pieces of state the hook needs to remember:
//
//   * g_peerIps: the set of virtual IPv4s the launcher has told us
//     belong to peers in the same lobby. Whenever AoE3 sendto's to one
//     of these (or to 255.255.255.255), we divert the datagram into
//     the bridge instead of letting it hit the wire.
//
//   * g_lanSockets / g_lanSocketsPort: every socket AoE3 has bound to
//     a known LAN port (2299 = matchmaker, 2297 = game session). These
//     are the sockets we'll inject PACKET_IN datagrams into so AoE3's
//     recvfrom returns mesh-delivered bytes.
//
//   * g_recvQueue: per-socket FIFO of pending PACKET_IN datagrams.
//     The pipe reader thread enqueues; Hooked_recvfrom dequeues.
//
// All three are guarded by g_stateLock. We deliberately use a single
// CRITICAL_SECTION rather than three to keep the locking ordering
// trivial: never nest with g_bridgeLock (which BridgeWriteLocked
// holds), and never call BridgeWriteLocked while g_stateLock is
// held.
struct PendingPacket
{
    sockaddr_in            from;
    std::vector<uint8_t>   payload;
};

static CRITICAL_SECTION                                          g_stateLock;
static bool                                                      g_stateLockReady = false;
static std::unordered_set<uint32_t>                              g_peerIps;
static std::unordered_set<SOCKET>                                g_lanSockets;
static std::unordered_map<SOCKET, uint16_t>                      g_lanSocketsPort;
// Phase 2.e: full bound address per socket (IP + port). We need the
// IP — not just the port — so the wake-up trick (see g_wakeSocket
// below) can sendto the exact address AoE3 listened on; a socket
// bound to a specific NIC IP (e.g. 192.168.68.70:2299) won't receive
// packets sent to a different local IP like 127.0.0.1:2299.
static std::unordered_map<SOCKET, sockaddr_in>                   g_lanSocketsBoundAddr;
static std::unordered_map<SOCKET, std::deque<PendingPacket>>     g_recvQueue;

// Phase 2.n: capture the notification handles AoE3 set up for each
// LAN socket via WSAAsyncSelect / WSAEventSelect. The Fase 2.j-2.m
// diagnostics proved AoE3 reads exactly ONE packet per socket through
// our wake-up trick and then stops polling — the OS-level FD_READ
// event we trigger via sendto is being ignored (most likely AoE3
// disabled the notification with lEvent=0 after the first read, or
// is parked deep in its own message pump and never re-arms).
//
// Hooking the two notification-registration APIs lets us see exactly
// what AoE3 wired up, AND lets us bypass the disabled state: after a
// PACKET_IN is queued we ALSO PostMessage the captured (hWnd, wMsg)
// and SetEvent the captured event handle. AoE3's window proc /
// waiting thread runs regardless of whether WinSock thinks the
// notification is active — the only thing we need is the original
// hWnd/wMsg/event pair, which the WSAAsyncSelect/WSAEventSelect call
// hands us at registration time.
struct AsyncNotify
{
    HWND  hWnd;
    UINT  wMsg;
    long  lEvent;
};
struct EventNotify
{
    HANDLE hEvent;
    long   lEvent;
};
static std::unordered_map<SOCKET, AsyncNotify>  g_asyncNotify;
static std::unordered_map<SOCKET, EventNotify>  g_eventNotify;

// Phase 2.e wake-up trick.
//
// Inbound PACKET_IN frames land in g_recvQueue keyed by the destination
// socket, but AoE3 never reads them: the engine uses event-driven I/O
// (WSAEventSelect or similar — it only calls recvfrom when the OS pokes
// the socket to say "data ready"). With nothing on the wire, no poke
// happens and our queue grows unread.
//
// The fix is to actually put a byte on the wire: from this aux UDP
// socket we sendto the SAME IP+port AoE3 bound to. The OS sees an
// inbound datagram, fires the engine's event, AoE3 calls recvfrom —
// at which point Hooked_recvfrom drains a real packet from g_recvQueue
// (the wake-up byte itself is "WAKE" + the OS-delivered copy gets
// filtered out by a small loop in Hooked_recvfrom that retries on the
// marker until either a real packet or WSAEWOULDBLOCK appears).
//
// Lazy-init: we only create the socket the first time we need to wake
// someone up. That defers the WSA call until we're sure WinSock is up
// — by definition it must be, because Hooked_recvfrom is being called
// by AoE3 which has already done WSAStartup. (Unlike BridgeConnectThread
// which runs ridiculously early in DllMain — that's why g_bridgeSocket
// has its own WSAStartup.)
// Moved here (was below with the other Detours trampolines) so
// HandleIncomingFrame can call the un-hooked sendto to send the
// wake-up datagram. Everything that calls Real_sendto from earlier
// in the file does it via the wake-up path; the actual detour is
// installed against this same pointer later in InstallHooks.
static int (WINAPI* Real_sendto)(SOCKET, const char*, int, int, const sockaddr*, int) = ::sendto;
static SOCKET                                                    g_wakeSocket = INVALID_SOCKET;
// 4-byte marker prefixed to wake-up datagrams so Hooked_recvfrom can
// recognise + discard them. AoE3's LAN protocol uses fixed-format
// 21-byte probe packets and 1669-byte lobby replies; 4 bytes is too
// short to collide. The marker itself doesn't matter as long as the
// hook and the wake-sender agree.
static const char                                                g_wakeMarker[4] = { 'W', 'A', 'K', 'E' };

// Phase 2.c.2: pre-bind warmup buffer keyed by destination port.
//
// PACKET_IN frames can land BEFORE AoE3 binds the matching listen
// socket — papillo's logs from 2026-05-17 13:50 show 4 broadcasts
// from the host arriving via mesh ~8 s before age3y.exe got far enough
// to bind anything. We used to silently drop them. Now we park them
// per-port; when Hooked_bind later sees AoE3 bind to that port we
// drain the bucket into the matching socket's g_recvQueue, so AoE3's
// very first recvfrom on that socket sees the deferred datagrams.
//
// Cap per port at kWarmupPerPort so a peer that fires fast forever
// while AoE3 is still on the loading screen can't grow the buffer
// without bound; oldest gets dropped.
static constexpr size_t                                          kWarmupPerPort = 32;
static std::unordered_map<uint16_t, std::deque<PendingPacket>>   g_pendingByPort;

// Write exactly `bytes` bytes to the bridge socket under the lock.
//
// Phase 2.d (TCP migration): synchronous send() on a connected loopback
// socket. Blocks only until the kernel's TCP send buffer accepts the
// data — typically microseconds. The named-pipe overlapped read/write
// dance from Phase 2.c.1-2.c.4 is gone, along with the read-after-write
// freezes that motivated it. The writer thread is dedicated, so a slow
// drain on the launcher side just costs queue depth here (and we drop
// oldest on overflow); the game thread never sees it.
static bool BridgeWriteLocked(const void* data, DWORD bytes)
{
    if (g_bridgeSocket == INVALID_SOCKET) return false;
    const char* p = static_cast<const char*>(data);
    DWORD total = 0;
    while (total < bytes)
    {
        int w = send(g_bridgeSocket, p + total, (int)(bytes - total), 0);
        if (w == SOCKET_ERROR)
        {
            int err = WSAGetLastError();
            Log("Bridge: send failed (WSA %d); closing socket.", err);
            closesocket(g_bridgeSocket);
            g_bridgeSocket = INVALID_SOCKET;
            return false;
        }
        if (w == 0)
        {
            // Per MSDN, send() can return 0 only when bytes==0 (which
            // we never call with). Defensive: treat as a closed peer.
            Log("Bridge: send returned 0; closing socket.");
            closesocket(g_bridgeSocket);
            g_bridgeSocket = INVALID_SOCKET;
            return false;
        }
        total += (DWORD)w;
    }
    return true;
}

// Read exactly `bytes` bytes from the bridge socket.
//
// Phase 2.d: synchronous recv() on a connected loopback socket. Blocks
// indefinitely until data arrives, the peer closes (returns 0 = EOF),
// or shutdown(SD_BOTH) is called from the detach path (which unblocks
// recv with EOF too). No overlapped IO, no timeouts, no event handles —
// none of which were buying us anything on loopback anyway.
static bool BridgeReadExact(void* out, DWORD bytes)
{
    if (g_bridgeSocket == INVALID_SOCKET) return false;
    char* p = static_cast<char*>(out);
    DWORD total = 0;
    while (total < bytes)
    {
        int r = recv(g_bridgeSocket, p + total, (int)(bytes - total), 0);
        if (r == SOCKET_ERROR)
        {
            int err = WSAGetLastError();
            if (err == WSAECONNRESET || err == WSAECONNABORTED)
                Log("Bridge: peer reset (launcher closed).");
            else if (err == WSAEINTR)
                Log("Bridge: recv interrupted (likely shutdown from DLL_PROCESS_DETACH).");
            else
                Log("Bridge: recv failed (WSA %d); closing socket.", err);
            closesocket(g_bridgeSocket);
            g_bridgeSocket = INVALID_SOCKET;
            return false;
        }
        if (r == 0)
        {
            // Clean EOF: launcher called Stop/Disconnect on its end.
            Log("Bridge: EOF (launcher closed cleanly).");
            closesocket(g_bridgeSocket);
            g_bridgeSocket = INVALID_SOCKET;
            return false;
        }
        total += (DWORD)r;
    }
    return true;
}

// Push a fully-built frame into the writer queue. Drops the oldest
// entry if the queue is at capacity — UDP is best-effort anyway, and
// dropping is much better than blocking the game's network thread.
//
// Safe to call from any thread (including the game's network thread);
// holds g_writeQueueLock only for the queue mutation itself and never
// blocks on actual pipe I/O.
static volatile LONG g_enqueueCount = 0;
static void BridgeEnqueueFrame(std::vector<uint8_t>&& frame)
{
    if (!g_writeQueueLockReady)
    {
        Log("Bridge: BridgeEnqueueFrame called but queue lock not ready — dropping.");
        return;
    }
    EnterCriticalSection(&g_writeQueueLock);
    if (g_writeQueue.size() >= kWriteQueueCapacity)
    {
        g_writeQueue.pop_front();
        InterlockedIncrement(&g_dropCount);
    }
    g_writeQueue.push_back(std::move(frame));
    size_t qsize = g_writeQueue.size();
    LeaveCriticalSection(&g_writeQueueLock);
    LONG n = InterlockedIncrement(&g_enqueueCount);
    BOOL ok = FALSE;
    if (g_writeQueueEvent) ok = SetEvent(g_writeQueueEvent);
    if ((n % 20) == 1)
    {
        Log("Bridge: enqueue #%ld qsize=%zu event=%p setEvent=%d (err=%lu)",
            (long)n, qsize, (void*)g_writeQueueEvent, (int)ok,
            ok ? 0 : GetLastError());
    }
}

// Writer thread: sits on g_writeQueueEvent waiting for the game thread
// to enqueue frames, then drains them onto the pipe. Doing the
// (potentially blocking) WriteFile here keeps the game thread unblocked
// even if the launcher stalls reading the pipe — the queue absorbs
// bursts up to kWriteQueueCapacity, beyond which we drop oldest.
static DWORD WINAPI BridgeWriterThread(LPVOID)
{
    Log("Bridge: writer thread started (event=%p, lockReady=%d).",
        (void*)g_writeQueueEvent, (int)g_writeQueueLockReady);
    DWORD iterations = 0;
    while (InterlockedCompareExchange(&g_writerStop, 0, 0) == 0)
    {
        DWORD waitResult = WaitForSingleObject(g_writeQueueEvent, INFINITE);
        if (waitResult != WAIT_OBJECT_0)
        {
            Log("Bridge: writer Wait returned 0x%lx (Win32 err=%lu); exiting.",
                waitResult, GetLastError());
            return 1;
        }

        // Drain whatever's queued. We use a local deque so we can
        // release the queue lock before doing actual I/O. std::deque's
        // O(1) swap moves all internal buckets without copying any
        // payload bytes — exactly what we want here.
        std::deque<std::vector<uint8_t>> drained;
        if (g_writeQueueLockReady)
        {
            EnterCriticalSection(&g_writeQueueLock);
            drained.swap(g_writeQueue);
            LeaveCriticalSection(&g_writeQueueLock);
        }
        size_t drainedCount = drained.size();
        if (drainedCount > 0 && (iterations++ % 20) == 0)
        {
            Log("Bridge: writer drained %zu frame(s) this wake; iteration=%lu.",
                drainedCount, (unsigned long)iterations);
        }

        for (auto& frame : drained)
        {
            // g_bridgeLock still serialises us against the HELLO write
            // on BridgeConnectThread. After Phase 2.c we're the only
            // ongoing writer, but keeping the lock costs nothing and
            // preserves invariants if anyone adds another writer.
            EnterCriticalSection(&g_bridgeLock);
            bool ok = BridgeWriteLocked(frame.data(), (DWORD)frame.size());
            LeaveCriticalSection(&g_bridgeLock);
            if (!ok)
            {
                // BridgeWriteLocked closed the pipe; no point draining
                // the rest.
                if (g_writeQueueLockReady)
                {
                    EnterCriticalSection(&g_writeQueueLock);
                    g_writeQueue.clear();
                    LeaveCriticalSection(&g_writeQueueLock);
                }
                break;
            }
        }

        // Log accumulated drops once per drain so a runaway queue
        // shows up clearly without spamming every drop.
        LONG drops = InterlockedExchange(&g_dropCount, 0);
        if (drops > 0)
            Log("Bridge: writer dropped %ld frames (queue capacity %zu).",
                (long)drops, kWriteQueueCapacity);
    }
    return 0;
}

// Dispatch one inbound frame from the launcher. Called by the bridge
// read loop after a header + payload have been pulled off the pipe.
//
// Lock pattern: take g_stateLock for any g_peerIps / g_lanSockets /
// g_recvQueue mutation, release it before doing anything that could
// take g_bridgeLock (we don't write back from here, but the rule
// keeps DEADLOCK off the table if the routing logic ever grows).
static void HandleIncomingFrame(
    const BridgeFrameHeader& hdr,
    const std::vector<uint8_t>& payload)
{
    switch (hdr.kind)
    {
    case kFrameKindPeerSet:
    {
        // Payload = N * 4 bytes, each = one LE uint32 peer virtual IP.
        // Replace g_peerIps wholesale so a peer leaving the lobby
        // actually stops being intercepted.
        if (payload.size() % 4 != 0)
        {
            Log("Bridge: PEER_SET payload length %zu not a multiple of 4 — ignored.",
                payload.size());
            return;
        }
        std::unordered_set<uint32_t> next;
        next.reserve(payload.size() / 4);
        for (size_t i = 0; i + 4 <= payload.size(); i += 4)
        {
            uint32_t ip =
                  (uint32_t)payload[i]
                | ((uint32_t)payload[i + 1] << 8)
                | ((uint32_t)payload[i + 2] << 16)
                | ((uint32_t)payload[i + 3] << 24);
            next.insert(ip);
        }
        EnterCriticalSection(&g_stateLock);
        g_peerIps.swap(next);
        size_t count = g_peerIps.size();
        LeaveCriticalSection(&g_stateLock);
        Log("Bridge: PEER_SET applied (%zu peer IP%s).",
            count, count == 1 ? "" : "s");
        break;
    }

    case kFrameKindPacketIn:
    {
        // Build the PendingPacket. The frame carries the synthesised
        // virtual IPv4 of the sender in hdr.srcIp (already in LE/wire
        // form, which on x86 is also the in-memory layout of
        // sin_addr.S_un.S_addr), and the source port in hdr.srcPort
        // (host byte order). sin_port is network byte order so we
        // htons the host-order port before stuffing it in.
        PendingPacket pkt = {};
        pkt.from.sin_family             = AF_INET;
        pkt.from.sin_addr.S_un.S_addr   = hdr.srcIp;
        pkt.from.sin_port               = htons(hdr.srcPort);
        pkt.payload.assign(payload.begin(), payload.end());

        // Deliver to every LAN socket bound to hdr.dstPort. Sometimes
        // AoE3 binds the same port on multiple sockets transiently;
        // posting to all of them is harmless (the unrelated sockets
        // will just have an extra recvfrom datum available) and saves
        // us from having to know which socket the engine wants this
        // datagram on. If no socket is currently bound to dstPort,
        // park it in g_pendingByPort (Phase 2.c.2 warmup buffer) —
        // Hooked_bind will drain whatever's parked the moment AoE3
        // actually opens that port.
        size_t delivered = 0;
        size_t crossDelivered = 0;
        size_t parked   = 0;
        // Collect the addresses we need to wake up while holding the
        // lock, then send the wake-ups OUTSIDE the critical section.
        // sendto on a UDP socket can block briefly under firewall /
        // antivirus filters; doing it under g_stateLock would risk
        // starving the bind hook or the reader thread.
        std::vector<sockaddr_in> wakeTargets;
        // Phase 2.n: collect AoE3's captured WSAAsyncSelect /
        // WSAEventSelect notification handles for the sockets we're
        // delivering to. We'll PostMessage / SetEvent those AFTER the
        // wake-up sendto so AoE3's window proc or waiting thread runs
        // even if AoE3 internally disabled FD_READ on the socket.
        struct AsyncFire { HWND hWnd; UINT wMsg; SOCKET sock; };
        std::vector<AsyncFire> asyncFires;
        std::vector<HANDLE>    eventFires;
        EnterCriticalSection(&g_stateLock);
        for (const auto& kv : g_lanSocketsPort)
        {
            if (kv.second != hdr.dstPort) continue;
            g_recvQueue[kv.first].push_back(pkt);
            ++delivered;
            // Phase 2.e: line up a wake-up sendto for this socket.
            // Without a real packet hitting the OS, AoE3's event-driven
            // recvfrom never fires and our queue rots.
            auto addrIt = g_lanSocketsBoundAddr.find(kv.first);
            if (addrIt != g_lanSocketsBoundAddr.end())
                wakeTargets.push_back(addrIt->second);
            // Phase 2.n: ditto for notification triggers.
            auto asyncIt = g_asyncNotify.find(kv.first);
            if (asyncIt != g_asyncNotify.end()
                && (asyncIt->second.lEvent & FD_READ) != 0)
            {
                asyncFires.push_back({ asyncIt->second.hWnd,
                                       asyncIt->second.wMsg,
                                       kv.first });
            }
            auto evtIt = g_eventNotify.find(kv.first);
            if (evtIt != g_eventNotify.end()
                && (evtIt->second.lEvent & FD_READ) != 0)
            {
                eventFires.push_back(evtIt->second.hEvent);
            }
        }
        // Phase 2.l cross-delivery (Phase 2.m tightened): mirror JOIN
        // REQUESTS only — payloads in the 50..200 byte range targeting
        // port 2299 specifically — to every other tracked LAN socket.
        //
        // The Fase 2.j+2.k diagnostics proved AoE3's host responder on
        // sock-bound-to-2299 is a one-shot reader: after answering one
        // probe with the 1669-byte lobby info, it never calls recvfrom
        // on that socket again, leaving the joiner's follow-up 71/75-
        // byte JOIN requests rotting in the queue (45+ "delivered" log
        // entries, exactly zero "INJECT recvfrom" after the first).
        // The game-session socket on :2297 stays actively polled while
        // the host lobby is up. Mirroring the join request there gives
        // AoE3 a chance to see it through a socket it's listening on.
        //
        // The 2.m tightening fixes a regression observed in 2.l logs:
        // the original "any payload > 30 bytes" rule ALSO cross-fired
        // for the 1669-byte lobby-info packets the JOINER receives,
        // duplicating them onto its game-session socket. AoE3 then saw
        // the same lobby-info twice (once on the scanner, once on the
        // game socket where it had no business being), which appears
        // to confuse the lobby state enough to silently reject the
        // user's "Join" click. Constraining the rule to (a) the
        // join-request size band and (b) dstPort=2299 keeps cross-
        // delivery firing for exactly the case it was designed for —
        // the host-side stuck-socket scenario — and never for joiner-
        // side lobby ingress.
        if (delivered > 0
            && hdr.dstPort == 2299
            && pkt.payload.size() >= 50
            && pkt.payload.size() <= 200)
        {
            for (const auto& kv : g_lanSocketsPort)
            {
                if (kv.second == hdr.dstPort) continue; // already delivered
                g_recvQueue[kv.first].push_back(pkt);
                ++crossDelivered;
                auto addrIt = g_lanSocketsBoundAddr.find(kv.first);
                if (addrIt != g_lanSocketsBoundAddr.end())
                    wakeTargets.push_back(addrIt->second);
                // Phase 2.n: trigger notifications for cross-delivery
                // sockets too — same one-shot-poll issue applies.
                auto asyncIt = g_asyncNotify.find(kv.first);
                if (asyncIt != g_asyncNotify.end()
                    && (asyncIt->second.lEvent & FD_READ) != 0)
                {
                    asyncFires.push_back({ asyncIt->second.hWnd,
                                           asyncIt->second.wMsg,
                                           kv.first });
                }
                auto evtIt = g_eventNotify.find(kv.first);
                if (evtIt != g_eventNotify.end()
                    && (evtIt->second.lEvent & FD_READ) != 0)
                {
                    eventFires.push_back(evtIt->second.hEvent);
                }
            }
        }
        if (delivered == 0)
        {
            auto& bucket = g_pendingByPort[hdr.dstPort];
            if (bucket.size() >= kWarmupPerPort) bucket.pop_front();
            bucket.push_back(pkt);
            parked = bucket.size();
        }
        LeaveCriticalSection(&g_stateLock);

        if (delivered == 0)
        {
            Log("Bridge: PACKET_IN for dstPort=%u parked in warmup buffer (size now=%zu).",
                hdr.dstPort, parked);
        }
        else
        {
            // Phase 2.j: log every successful delivery (one line per
            // queued socket) so we can tell, when "INJECT recvfrom"
            // stops appearing, whether the launcher stopped sending
            // (mesh/relay broke) vs the packet arrived but AoE3 isn't
            // pulling it out of the queue (overlapped recv, closed
            // socket, paused event loop).
            //
            // Phase 2.l: the crossDelivered count reports how many
            // extra LAN sockets received a copy of this packet via the
            // one-shot-host workaround (only fires for join-sized
            // payloads, dstPort=2299 mirrored to :2297, etc.).
            Log("Bridge: PACKET_IN delivered to %zu socket(s) for dstPort=%u "
                "(payload=%zu bytes, wakeTargets=%zu, crossDelivered=%zu).",
                delivered, hdr.dstPort, payload.size(),
                wakeTargets.size(), crossDelivered);
            // Phase 2.e: wake up each receiving socket. See comment
            // on g_wakeSocket for the full design. Lazy-init the aux
            // socket the first time we need it — by definition AoE3
            // has already done WSAStartup at this point because we're
            // being driven by an inbound packet from the launcher,
            // which only flows after the game is reaching the network.
            if (g_wakeSocket == INVALID_SOCKET)
            {
                g_wakeSocket = socket(AF_INET, SOCK_DGRAM, 0);
                if (g_wakeSocket == INVALID_SOCKET)
                {
                    Log("Wake: socket() failed (WSA %d); inbound mesh packets "
                        "will pile up in g_recvQueue without ever being read.",
                        WSAGetLastError());
                }
            }
            if (g_wakeSocket != INVALID_SOCKET)
            {
                for (auto& dst : wakeTargets)
                {
                    // Fix #1 (papillo logs 19:55:47): AoE3 often binds
                    // its ephemeral broadcast socket to INADDR_ANY
                    // (0.0.0.0:0) — Windows then picks an ephemeral
                    // port via getsockname, which we capture, but the
                    // IP we stored stays 0.0.0.0. sendto(0.0.0.0:port)
                    // returns WSAEADDRNOTAVAIL (10049) because 0.0.0.0
                    // is not a valid destination.
                    //
                    // Rewrite 0.0.0.0 → 127.0.0.1: a socket bound to
                    // INADDR_ANY accepts inbound on any local IP
                    // including loopback, so the wake-up still fires
                    // the OS notification for AoE3's recvfrom.
                    if (dst.sin_addr.S_un.S_addr == 0)
                        dst.sin_addr.S_un.S_addr = htonl(INADDR_LOOPBACK);

                    // Phase 2.l: fire the wake-up THREE times in a
                    // row. Some Windows event-notification paths are
                    // edge-triggered and AoE3's process-level message
                    // pump (likely WSAAsyncSelect or a custom select
                    // loop) doesn't always re-arm after the FIRST
                    // FD_READ if AoE3 was already in a polling state.
                    // A small burst of identical wake datagrams costs
                    // nothing on loopback delivery and gives multiple
                    // chances for AoE3 to notice "data available".
                    int sent = SOCKET_ERROR;
                    for (int burst = 0; burst < 3; ++burst)
                    {
                        sent = Real_sendto(
                            g_wakeSocket,
                            g_wakeMarker, (int)sizeof(g_wakeMarker), 0,
                            reinterpret_cast<const sockaddr*>(&dst),
                            sizeof(dst));
                        if (sent == SOCKET_ERROR) break;
                    }
                    if (sent == SOCKET_ERROR)
                    {
                        // Log the FIRST failure per session, otherwise
                        // a misconfigured firewall would flood the log.
                        static volatile LONG s_wakeFailLogged = 0;
                        if (InterlockedCompareExchange(&s_wakeFailLogged, 1, 0) == 0)
                        {
                            Log("Wake: sendto(%u.%u.%u.%u:%u) failed (WSA %d). "
                                "AoE3 won't see mesh packets on this port unless something else "
                                "delivers a wake. Further failures suppressed.",
                                dst.sin_addr.S_un.S_un_b.s_b1, dst.sin_addr.S_un.S_un_b.s_b2,
                                dst.sin_addr.S_un.S_un_b.s_b3, dst.sin_addr.S_un.S_un_b.s_b4,
                                ntohs(dst.sin_port), WSAGetLastError());
                        }
                    }
                }
            }

            // Phase 2.n: ALSO fire the captured notifications. These
            // bypass the OS event subsystem entirely — PostMessage
            // queues into AoE3's window message queue and SetEvent
            // signals the kernel event directly. If AoE3 had earlier
            // deregistered FD_READ on the socket (which is what the
            // Fase 2.j+2.m logs strongly suggest, given that AoE3
            // reads exactly one packet per socket and then stops
            // polling forever), the wake-up sendto above does nothing
            // because there's no notification path subscribed. But
            // the hWnd/wMsg/event captured at REGISTRATION time stays
            // valid as long as AoE3 hasn't destroyed them — we can
            // post directly and AoE3's window proc / waiting thread
            // will run regardless of WinSock's view of the world.
            //
            // WSAAsyncSelect notifications encode the event mask in
            // the low word of lParam and the error code in the high
            // word — WSAMAKESELECTREPLY builds that for us. We always
            // synthesise FD_READ with no error since we know data is
            // queued.
            for (const auto& af : asyncFires)
            {
                PostMessageW(af.hWnd, af.wMsg,
                             static_cast<WPARAM>(af.sock),
                             static_cast<LPARAM>(WSAMAKESELECTREPLY(FD_READ, 0)));
            }
            for (HANDLE he : eventFires)
            {
                SetEvent(he);
            }
            if (!asyncFires.empty() || !eventFires.empty())
            {
                Log("Phase2.n: notified %zu AsyncSelect + %zu EventSelect handles for dstPort=%u",
                    asyncFires.size(), eventFires.size(), hdr.dstPort);
            }
        }
        break;
    }

    default:
        Log("Bridge: unknown frame kind=%u payloadLen=%u — ignored.",
            (unsigned)hdr.kind, (unsigned)hdr.payloadLen);
        break;
    }
}

// Worker thread: connect to the launcher's loopback TCP listener,
// send HELLO, then read PEER_SET / PACKET_IN frames in a loop. Stays
// alive until the launcher closes the socket (game exit, bridge
// dispose) or DLL_PROCESS_DETACH shuts the socket down.
static DWORD WINAPI BridgeConnectThread(LPVOID)
{
    wchar_t portStr[16] = {};
    DWORD got = GetEnvironmentVariableW(
        L"AOE_P2P_HOOK_PORT", portStr,
        sizeof(portStr) / sizeof(portStr[0]));
    if (got == 0 || got >= sizeof(portStr) / sizeof(portStr[0]))
    {
        // No bridge configured for this launch — perfectly normal
        // (Solo runs, old launcher builds). Stay in passthrough mode.
        return 0;
    }

    wchar_t* parseEnd = nullptr;
    unsigned long port = wcstoul(portStr, &parseEnd, 10);
    if (port == 0 || port > 65535 || parseEnd == portStr)
    {
        Log("Bridge: AOE_P2P_HOOK_PORT='%ls' is not a valid TCP port; "
            "running without bridge.", portStr);
        return 0;
    }

    // Phase 2.d.1: we MUST call WSAStartup ourselves. The previous
    // assumption — "age3y.exe imports ws2_32, so WinSock is already up
    // by the time DllMain runs" — was wrong: DllMain (which spawned
    // this thread) executes while the game's main thread is still
    // CREATE_SUSPENDED. The engine hasn't reached its own WSAStartup
    // yet, so socket() returns WSANOTINITIALISED (WSA 10093). Calling
    // WSAStartup here is safe because Windows reference-counts the
    // initialisation: the game's later WSAStartup just bumps the
    // count, and our matching WSACleanup at detach time decrements it.
    WSADATA wsaData = {};
    int wsaErr = WSAStartup(MAKEWORD(2, 2), &wsaData);
    if (wsaErr != 0)
    {
        Log("Bridge: WSAStartup failed (WSA %d); running without bridge.", wsaErr);
        return 0;
    }
    // Track that WE init'd WinSock so the DLL_PROCESS_DETACH path can
    // call WSACleanup the matching number of times (just once today,
    // but the symmetry keeps the refcount clean if we ever spin up
    // multiple bridge connect attempts in one DLL load).
    g_bridgeWsaStarted = true;

    SOCKET s = socket(AF_INET, SOCK_STREAM, 0);
    if (s == INVALID_SOCKET)
    {
        Log("Bridge: socket() failed (WSA %d); running without bridge.",
            WSAGetLastError());
        return 0;
    }

    sockaddr_in addr = {};
    addr.sin_family      = AF_INET;
    addr.sin_port        = htons((u_short)port);
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);  // 127.0.0.1

    // Retry connect a few times — the launcher creates the listener
    // before spawning age3y.exe, so the connect should succeed on the
    // first try, but a slow scheduler or AV inspection on the
    // injector's CreateProcess could briefly delay things. ~2 s
    // worth of retries is plenty for loopback.
    bool connected = false;
    int attempts = 20;
    while (attempts-- > 0)
    {
        if (connect(s, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == 0)
        {
            connected = true;
            break;
        }
        int err = WSAGetLastError();
        if (err == WSAECONNREFUSED || err == WSAETIMEDOUT)
        {
            Sleep(100);
            continue;
        }
        Log("Bridge: connect(127.0.0.1:%lu) failed (WSA %d); giving up.",
            port, err);
        closesocket(s);
        return 0;
    }
    if (!connected)
    {
        Log("Bridge: 127.0.0.1:%lu never accepted; running without bridge.", port);
        closesocket(s);
        return 0;
    }

    // Disable Nagle: AoE3 LAN frames are small and we want them on
    // the wire (loopback "wire") immediately. The launcher does the
    // symmetric NoDelay = true on its accepted client.
    BOOL nodelay = TRUE;
    setsockopt(s, IPPROTO_TCP, TCP_NODELAY,
               reinterpret_cast<const char*>(&nodelay), sizeof(nodelay));

    EnterCriticalSection(&g_bridgeLock);
    g_bridgeSocket = s;

    // HELLO: announce our PID so the launcher can correlate this
    // hook instance with the age3y.exe it spawned a moment ago. The
    // payload (if any) carries the absolute path the hook's local log
    // file ended up at — handy for the launcher's debug output when
    // the user can't find AoeP2pHook.log on their machine (LOCALAPPDATA
    // resolved to a weird place, AV quarantined, etc.).
    char logPathUtf8[MAX_PATH * 3] = {}; // worst case 3 bytes per wchar
    int logPathBytes = 0;
    if (g_logPath[0] != L'\0')
    {
        logPathBytes = WideCharToMultiByte(
            CP_UTF8, 0,
            g_logPath, -1,
            logPathUtf8, sizeof(logPathUtf8),
            nullptr, nullptr);
        // WideCharToMultiByte includes the trailing NUL on success;
        // strip it so the launcher gets a clean string length.
        if (logPathBytes > 0) logPathBytes -= 1;
        else                  logPathBytes  = 0;
    }

    BridgeFrameHeader hello = {};
    hello.kind       = kFrameKindHello;
    hello.version    = kBridgeProtocolVersion;
    hello.payloadLen = (uint16_t)logPathBytes;
    hello.dstIp      = GetCurrentProcessId(); // piggy-back PID for cross-referencing
    bool sent = BridgeWriteLocked(&hello, sizeof(hello));
    if (sent && logPathBytes > 0)
        sent = BridgeWriteLocked(logPathUtf8, (DWORD)logPathBytes);
    LeaveCriticalSection(&g_bridgeLock);

    if (sent)
    {
        Log("Bridge: connected to 127.0.0.1:%lu, HELLO sent (PID=%lu, logPath='%ls').",
            port, GetCurrentProcessId(),
            g_logPath[0] ? g_logPath : L"(none)");
    }
    else
    {
        // BridgeWriteLocked already closed g_bridgeSocket on failure;
        // nothing for us to read against, so bail.
        return 0;
    }

    // Stay alive and pull frames the launcher pushes at us (PEER_SET
    // and PACKET_IN today; future kinds are logged + dropped by
    // HandleIncomingFrame). The loop exits when the socket closes
    // (game exit, launcher dispose, DLL_PROCESS_DETACH shutdown) or
    // on a protocol-version mismatch.
    while (g_bridgeSocket != INVALID_SOCKET)
    {
        BridgeFrameHeader hdr = {};
        if (!BridgeReadExact(&hdr, sizeof(hdr)))
            break; // socket closed; teardown handled below + in DLL_PROCESS_DETACH.

        if (hdr.version != kBridgeProtocolVersion)
        {
            Log("Bridge: protocol mismatch — launcher sent version=%u, hook expects %u. "
                "Closing socket; rebuild the launcher.",
                (unsigned)hdr.version, (unsigned)kBridgeProtocolVersion);
            break;
        }

        std::vector<uint8_t> payload(hdr.payloadLen);
        if (hdr.payloadLen > 0)
        {
            if (!BridgeReadExact(payload.data(), hdr.payloadLen))
                break;
        }

        HandleIncomingFrame(hdr, payload);
    }

    // Drop our socket so future BridgeWriteLocked / hook paths no-op
    // cleanly. Use the lock so a sendto in flight on another thread
    // doesn't observe a half-closed socket. BridgeReadExact may have
    // already closed it on the EOF path, so guard against double-close.
    EnterCriticalSection(&g_bridgeLock);
    if (g_bridgeSocket != INVALID_SOCKET)
    {
        closesocket(g_bridgeSocket);
        g_bridgeSocket = INVALID_SOCKET;
    }
    LeaveCriticalSection(&g_bridgeLock);
    Log("Bridge: read loop exited.");
    return 0;
}

// ---- WinSock function pointers (originals) ------------------------------
//
// Detours requires we capture pointers to the real implementations BEFORE
// attaching. After DetourAttach, calling through these jumps the original
// function body — our hook can pre/post-process around it.

// Real_sendto is declared earlier in the file (next to g_wakeSocket)
// because HandleIncomingFrame needs to call it for the wake-up trick.
// The detour against it is still installed below in InstallHooks.
static int (WINAPI* Real_recvfrom)(SOCKET, char*, int, int, sockaddr*, int*)              = ::recvfrom;
static int (WINAPI* Real_bind)(SOCKET, const sockaddr*, int)                              = ::bind;
static int (WINAPI* Real_connect)(SOCKET, const sockaddr*, int)                           = ::connect;

// ---- hook bodies ---------------------------------------------------------
//
// Phase 1 is read-only: we log and pass through. Phase 3 will start
// returning synthetic responses from recvfrom (to populate AoE3's LAN
// list); Phase 4 will rewrite destination addresses in sendto.

static int WINAPI Hooked_sendto(
    SOCKET s, const char* buf, int len, int flags,
    const sockaddr* to, int tolen)
{
    char addrStr[64] = {};
    DescribeAddr(to, tolen, addrStr, sizeof(addrStr));

    // Phase 2.d: sendto divert re-enabled on TCP-on-loopback transport.
    //
    // Phase 2.b shipped this divert with a synchronous WriteFile to the
    // launcher pipe, which blocked the game thread when the pipe's 4
    // KiB kernel buffer filled — AoE3 hung on "Setting Up Network
    // Connection" forever. The Phase 2.a hotfix flipped this flag off
    // to restore same-LAN play via native AoE3 LAN discovery.
    //
    // Phase 2.c rewired the divert through a non-blocking in-process
    // queue + dedicated writer thread (see BridgeEnqueueFrame /
    // BridgeWriterThread above) and grew the launcher-side pipe buffer
    // to 256 KiB. That fixed the game-thread block, but kept exposing
    // intermittent IPC ghosts on the writer-or-reader side (deadlock
    // → overlapped writes → reader frozen after first write → symmetric
    // overlapped pattern still frozen). The combination of
    // FILE_FLAG_OVERLAPPED on the hook handle + asynchronous
    // NamedPipeServerStream on the launcher side kept producing race
    // conditions we couldn't pin down across four debugging passes.
    //
    // Phase 2.d (this commit): switch transport to TCP on 127.0.0.1.
    // Synchronous send()/recv() on a connected loopback socket has none
    // of the overlapped-pipe corner cases, lets us drop the
    // CreateEventW / GetOverlappedResult / CancelIoEx scaffolding, and
    // the writer thread + bounded queue (which were always correct)
    // stay in place above. Everything downstream is still wired (warmup
    // buffer, ephemeral source-port auto-tracking, TURN relay).
    //
    // To disable temporarily during incident response: flip this to
    // false. Hook stays in pure passthrough/log mode and same-WiFi
    // gameplay still works via AoE3's native LAN discovery.
    constexpr bool kSendtoDivertEnabled = true;

    if (kSendtoDivertEnabled
        && to != nullptr
        && tolen >= static_cast<int>(sizeof(sockaddr_in))
        && to->sa_family == AF_INET
        && len >= 0)
    {
        const sockaddr_in* sin = reinterpret_cast<const sockaddr_in*>(to);
        // On x86, sin_addr.S_un.S_addr is already in network byte
        // order — but we treat it as the wire/LE uint32 form for
        // bridge purposes (the launcher side uses the same byte
        // ordering for IPAddress conversions).
        uint32_t dstIpWire = sin->sin_addr.S_un.S_addr;

        bool divert = (dstIpWire == INADDR_BROADCAST);
        if (!divert)
        {
            EnterCriticalSection(&g_stateLock);
            divert = (g_peerIps.find(dstIpWire) != g_peerIps.end());
            LeaveCriticalSection(&g_stateLock);
        }

        if (divert)
        {
            // Build the entire frame in a single contiguous buffer so the writer
            // thread can WriteFile it as one piece (the OS may still split, but
            // the buffer ownership stays simple). Header first, payload after.
            //
            // Also fill srcPort from the actual bound port of `s` via getsockname
            // — Phase 2.b left it at 0, which breaks the receiver's reply path
            // (they'd send replies to port 0). getsockname is cheap (~1 µs) and
            // works even on UDP sockets bound to 0.0.0.0:0 (Windows assigns an
            // ephemeral port at bind time so this returns the real port).
            uint16_t srcPortHost = 0;
            {
                sockaddr_in sa = {};
                int salen = sizeof(sa);
                if (getsockname(s, reinterpret_cast<sockaddr*>(&sa), &salen) == 0
                    && sa.sin_family == AF_INET)
                {
                    srcPortHost = ntohs(sa.sin_port);
                }
            }

            BridgeFrameHeader hdr = {};
            hdr.kind       = kFrameKindPacketOut;
            hdr.version    = kBridgeProtocolVersion;
            hdr.payloadLen = (uint16_t)((len > 0xFFFF) ? 0xFFFF : len);
            hdr.srcIp      = 0;
            hdr.dstIp      = dstIpWire;
            hdr.srcPort    = srcPortHost;
            hdr.dstPort    = ntohs(sin->sin_port);

            std::vector<uint8_t> frame;
            frame.resize(sizeof(hdr) + hdr.payloadLen);
            memcpy(frame.data(), &hdr, sizeof(hdr));
            if (hdr.payloadLen > 0)
                memcpy(frame.data() + sizeof(hdr), buf, hdr.payloadLen);
            BridgeEnqueueFrame(std::move(frame));

            Log("FWD sendto sock=%llu -> %s len=%d (queued, srcPort=%u)",
                static_cast<unsigned long long>(s), addrStr, len, srcPortHost);

            // Phase 2.c bugfix: AoE3's LAN-discovery uses an EPHEMERAL
            // source socket (bind to 0.0.0.0:0 → Windows assigns a port
            // like 50000) for the broadcast probes. The host's response
            // comes back to that same ephemeral port — NOT to the well-
            // known 2299/2297 ports Hooked_bind already tracks. So if
            // we only inject PACKET_IN frames into 2299/2297 sockets,
            // the discovery response never reaches AoE3 and the lobby
            // never shows up in the LAN list.
            //
            // Fix: any socket that we just used to FWD-divert is a
            // candidate for receiving the matching reply. Register it
            // in g_lanSockets keyed by its CURRENT source port so the
            // PACKET_IN router (HandleIncomingFrame) can find it when
            // the peer sends a reply with dstPort = that port. Skip if
            // srcPortHost is 0 (getsockname failed) or if the socket is
            // already registered.
            if (srcPortHost != 0)
            {
                // Capture the full local address again — getsockname
                // here gives us the IP the OS actually chose for the
                // ephemeral bind, which is what the wake-up trick
                // needs to target.
                sockaddr_in localAddr = {};
                int localLen = sizeof(localAddr);
                bool haveLocal = (getsockname(s, reinterpret_cast<sockaddr*>(&localAddr), &localLen) == 0
                                  && localAddr.sin_family == AF_INET);
                bool added       = false;
                bool portChanged = false;
                uint16_t oldPort = 0;
                EnterCriticalSection(&g_stateLock);
                auto portIt = g_lanSocketsPort.find(s);
                if (portIt == g_lanSocketsPort.end())
                {
                    g_lanSockets.insert(s);
                    g_lanSocketsPort[s] = srcPortHost;
                    if (haveLocal) g_lanSocketsBoundAddr[s] = localAddr;
                    added = true;
                }
                else if (portIt->second != srcPortHost)
                {
                    // Fix #2 (papillo logs 19:57): AoE3 rebinds the
                    // same socket handle with a fresh ephemeral port
                    // (sock=3024 jumped 63661 → 56286 across two
                    // bind/sendto pairs). Our tracking has to follow
                    // or replies aimed at the new port land in the
                    // warmup limbo with no matching socket.
                    oldPort = portIt->second;
                    portIt->second = srcPortHost;
                    if (haveLocal) g_lanSocketsBoundAddr[s] = localAddr;
                    portChanged = true;
                }
                LeaveCriticalSection(&g_stateLock);
                if (added)
                {
                    Log("Phase2.c: auto-tracking ephemeral source sock=%llu port=%u "
                        "(seen via FWD sendto) for inject.",
                        static_cast<unsigned long long>(s), srcPortHost);
                }
                else if (portChanged)
                {
                    Log("Phase2.e: re-tracking sock=%llu port %u -> %u "
                        "(rebind reassigned ephemeral).",
                        static_cast<unsigned long long>(s),
                        (unsigned)oldPort, srcPortHost);
                }
            }

            // Always return success to AoE3 — even on bridge failure
            // we don't want the game's stack treating this as a real
            // network error (which would crash the LAN session). The
            // bridge will reopen on the next launch.
            return len;
        }
    }

    // Default: log + passthrough (Phase 1 behaviour).
    Log("sendto sock=%llu -> %s len=%d flags=0x%x",
        static_cast<unsigned long long>(s), addrStr, len, flags);
    return Real_sendto(s, buf, len, flags, to, tolen);
}

static int WINAPI Hooked_recvfrom(
    SOCKET s, char* buf, int len, int flags,
    sockaddr* from, int* fromlen)
{
    // Phase 2.b: if the launcher has queued a PACKET_IN destined for
    // this socket, deliver it FIRST — before going to the OS — so
    // mesh-injected traffic gets the same priority as real LAN
    // traffic. We only consider sockets the bind hook recognised as
    // AoE3 LAN sockets (g_lanSockets); everything else passes
    // through untouched.
    bool isLanSocket = false;
    PendingPacket pkt;
    bool havePkt = false;

    EnterCriticalSection(&g_stateLock);
    if (g_lanSockets.find(s) != g_lanSockets.end())
    {
        isLanSocket = true;
        auto it = g_recvQueue.find(s);
        if (it != g_recvQueue.end() && !it->second.empty())
        {
            pkt = std::move(it->second.front());
            it->second.pop_front();
            havePkt = true;
        }
    }
    LeaveCriticalSection(&g_stateLock);

    if (havePkt)
    {
        int payloadLen = static_cast<int>(pkt.payload.size());
        int toCopy = payloadLen;
        if (buf != nullptr && len > 0)
        {
            if (toCopy > len)
            {
                Log("INJECT recvfrom sock=%llu truncating %d -> %d (caller buffer)",
                    static_cast<unsigned long long>(s), payloadLen, len);
                toCopy = len;
            }
            if (toCopy > 0)
                memcpy(buf, pkt.payload.data(), toCopy);
        }
        else
        {
            toCopy = 0;
        }

        if (from != nullptr && fromlen != nullptr
            && *fromlen >= static_cast<int>(sizeof(sockaddr_in)))
        {
            memcpy(from, &pkt.from, sizeof(sockaddr_in));
            *fromlen = sizeof(sockaddr_in);
        }

        char addrStr[64] = {};
        DescribeAddr(reinterpret_cast<sockaddr*>(&pkt.from),
                     sizeof(sockaddr_in), addrStr, sizeof(addrStr));
        Log("INJECT recvfrom sock=%llu <- %s len=%d (from mesh)",
            static_cast<unsigned long long>(s), addrStr, toCopy);
        return toCopy;
    }

    // Phase 2.e: filter out our own wake-up datagrams. The PACKET_IN
    // handler sends a 4-byte "WAKE" marker to nudge AoE3's event loop;
    // when AoE3 ends up calling recvfrom AFTER our queue drained (or
    // when our queue was already empty), the OS would deliver that
    // marker as a normal datagram. We absorb it here so AoE3 only
    // sees real game traffic. Loop up to a few times to handle the
    // case where multiple wake-ups stacked in the OS buffer.
    //
    // Phase 2.k race-condition fix: between iterations of the WAKE
    // filter loop, RE-CHECK g_recvQueue. The Fase 2.j diagnostics on
    // gorgorito's hook log made it clear that subsequent PACKET_INs
    // (the joiner's 71-byte join requests) were arriving and being
    // delivered to sock=2848's queue, but AoE3 never logged another
    // INJECT recvfrom — meaning AoE3 had entered the hook BEFORE the
    // packet was queued, fell through to Real_recvfrom (which blocked),
    // the wake-up arrived and Real_recvfrom returned the WAKE marker.
    // The old filter loop would then call Real_recvfrom AGAIN looking
    // for another datagram, but the actual lobby/join payload was
    // sitting in g_recvQueue the whole time, never to be retrieved.
    // Re-checking the queue after each WAKE drain lets the real
    // packet escape the queue on the very next iteration.
    int result;
    for (int wakeFilterIter = 0; wakeFilterIter < 8; ++wakeFilterIter)
    {
        // Re-check g_recvQueue every iteration. If HandleIncomingFrame
        // added a packet between our initial check and now (or between
        // a previous WAKE drain and now), pull it out and return it
        // to AoE3 just like the fast-path above.
        if (isLanSocket)
        {
            EnterCriticalSection(&g_stateLock);
            auto it = g_recvQueue.find(s);
            if (it != g_recvQueue.end() && !it->second.empty())
            {
                pkt = std::move(it->second.front());
                it->second.pop_front();
                havePkt = true;
            }
            LeaveCriticalSection(&g_stateLock);

            if (havePkt)
            {
                int payloadLen = static_cast<int>(pkt.payload.size());
                int toCopy = payloadLen;
                if (buf != nullptr && len > 0)
                {
                    if (toCopy > len)
                    {
                        Log("INJECT recvfrom sock=%llu truncating %d -> %d "
                            "(caller buffer, post-wake)",
                            static_cast<unsigned long long>(s), payloadLen, len);
                        toCopy = len;
                    }
                    if (toCopy > 0)
                        memcpy(buf, pkt.payload.data(), toCopy);
                }
                else
                {
                    toCopy = 0;
                }
                if (from != nullptr && fromlen != nullptr
                    && *fromlen >= static_cast<int>(sizeof(sockaddr_in)))
                {
                    memcpy(from, &pkt.from, sizeof(sockaddr_in));
                    *fromlen = sizeof(sockaddr_in);
                }
                char addrStr[64] = {};
                DescribeAddr(reinterpret_cast<sockaddr*>(&pkt.from),
                             sizeof(sockaddr_in), addrStr, sizeof(addrStr));
                Log("INJECT recvfrom sock=%llu <- %s len=%d "
                    "(from mesh, drained on wake iter=%d)",
                    static_cast<unsigned long long>(s), addrStr, toCopy,
                    wakeFilterIter);
                return toCopy;
            }
        }

        result = Real_recvfrom(s, buf, len, flags, from, fromlen);
        if (result != (int)sizeof(g_wakeMarker)
            || buf == nullptr
            || memcmp(buf, g_wakeMarker, sizeof(g_wakeMarker)) != 0)
        {
            break; // Not a wake-up marker (or not enough room to match) — pass through.
        }
        // It IS a wake-up marker. Loop and call Real_recvfrom again,
        // but FIRST re-check the queue at the top of the next iteration
        // — that's the Phase 2.k fix.
        if (wakeFilterIter == 7)
        {
            Log("recvfrom sock=%llu absorbed 8 consecutive WAKE markers; "
                "something is generating a lot of wake-ups without queue items.",
                static_cast<unsigned long long>(s));
        }
    }

    if (result > 0 && from && fromlen && *fromlen >= static_cast<int>(sizeof(sockaddr_in)))
    {
        char addrStr[64] = {};
        DescribeAddr(from, *fromlen, addrStr, sizeof(addrStr));
        Log("recvfrom sock=%llu <- %s len=%d%s",
            static_cast<unsigned long long>(s), addrStr, result,
            isLanSocket ? " (LAN sock)" : "");
    }
    return result;
}

static int WINAPI Hooked_bind(SOCKET s, const sockaddr* addr, int len)
{
    char addrStr[64] = {};
    DescribeAddr(addr, len, addrStr, sizeof(addrStr));
    int result = Real_bind(s, addr, len);
    Log("bind sock=%llu %s -> %d (err=%d)",
        static_cast<unsigned long long>(s),
        addrStr, result,
        result == 0 ? 0 : WSAGetLastError());

    // Phase 2.b: remember sockets bound to AoE3's LAN ports so the
    // pipe-reader thread knows where to inject PACKET_IN datagrams.
    // Port 2299 is the matchmaker (LAN discovery / browse), 2297 is
    // the live game session, 2300 is the data channel used during
    // join handshake / in-game state exchange. Anything else AoE3
    // binds is unrelated to multiplayer LAN and we leave alone.
    //
    // Phase 2.g: added 2300 so that once the joiner clicks "Join" on
    // a lobby we kept alive via heartbeat, AoE3's follow-up unicast
    // to the host's :2300 also routes through the bridge (otherwise
    // the join handshake would dead-end at the joiner's hook with
    // an untracked socket).
    if (result == 0
        && addr != nullptr
        && len >= static_cast<int>(sizeof(sockaddr_in))
        && addr->sa_family == AF_INET)
    {
        const sockaddr_in* sin = reinterpret_cast<const sockaddr_in*>(addr);
        uint16_t portHost = ntohs(sin->sin_port);
        if (portHost == 2299 || portHost == 2297 || portHost == 2300)
        {
            size_t drainedFromWarmup = 0;
            EnterCriticalSection(&g_stateLock);
            g_lanSockets.insert(s);
            g_lanSocketsPort[s] = portHost;
            // Phase 2.e: stash the full sockaddr_in too — the wake-up
            // trick needs to sendto the same IP, not just the port,
            // because age3y.exe binds to a specific NIC IP (e.g.
            // 192.168.68.70:2299) and a datagram aimed at any other
            // local IP on the same port won't reach it.
            g_lanSocketsBoundAddr[s] = *sin;
            // Phase 2.c.2: drain any PACKET_IN frames that arrived
            // BEFORE this bind. The launcher can start fan-out a
            // moment ahead of age3y.exe opening its listen sockets
            // (papillo's 2026-05-17 logs showed 4 broadcasts lost
            // this way). HandleIncomingFrame parked them per-port
            // in g_pendingByPort; now we have a matching socket,
            // move the whole bucket into that socket's recv queue.
            auto warmIt = g_pendingByPort.find(portHost);
            if (warmIt != g_pendingByPort.end())
            {
                auto& bucket = warmIt->second;
                drainedFromWarmup = bucket.size();
                auto& dst = g_recvQueue[s];
                for (auto& p : bucket) dst.push_back(std::move(p));
                bucket.clear();
                g_pendingByPort.erase(warmIt);
            }
            LeaveCriticalSection(&g_stateLock);
            if (drainedFromWarmup > 0)
            {
                Log("Phase2.b: tracking LAN socket sock=%llu port=%u for inject "
                    "(drained %zu warmup frame(s)).",
                    static_cast<unsigned long long>(s), portHost, drainedFromWarmup);
            }
            else
            {
                Log("Phase2.b: tracking LAN socket sock=%llu port=%u for inject.",
                    static_cast<unsigned long long>(s), portHost);
            }
        }
    }

    return result;
}

static int WINAPI Hooked_connect(SOCKET s, const sockaddr* addr, int len)
{
    char addrStr[64] = {};
    DescribeAddr(addr, len, addrStr, sizeof(addrStr));
    Log("connect sock=%llu -> %s",
        static_cast<unsigned long long>(s), addrStr);
    return Real_connect(s, addr, len);
}

// Phase 2.j: log every closesocket so we know when AoE3 drops a socket
// we were tracking. The mystery from Fase 2.i logs is that the host's
// sock=2872 (bound to :2299) responded to the first probe and then went
// silent — we couldn't tell if AoE3 had closed it or was just ignoring
// our wake-up. Removing tracking on close also keeps g_recvQueue from
// hoarding packets for a socket nobody will ever read from.
static int (WINAPI* Real_closesocket)(SOCKET) = ::closesocket;
static int WINAPI Hooked_closesocket(SOCKET s)
{
    bool wasLan = false;
    uint16_t lanPort = 0;
    EnterCriticalSection(&g_stateLock);
    auto pit = g_lanSocketsPort.find(s);
    if (pit != g_lanSocketsPort.end())
    {
        wasLan = true;
        lanPort = pit->second;
        g_lanSocketsPort.erase(pit);
    }
    g_lanSockets.erase(s);
    g_lanSocketsBoundAddr.erase(s);
    g_recvQueue.erase(s);
    g_asyncNotify.erase(s);
    g_eventNotify.erase(s);
    LeaveCriticalSection(&g_stateLock);

    int result = Real_closesocket(s);
    if (wasLan)
    {
        Log("closesocket sock=%llu (was tracked port=%u) -> %d",
            static_cast<unsigned long long>(s), lanPort, result);
    }
    return result;
}

// Phase 2.n: capture AoE3's WSAAsyncSelect registrations so we can
// re-trigger the FD_READ notification manually from HandleIncomingFrame
// even after AoE3 has stopped honouring the OS-level event. The hook
// passes through to Real_WSAAsyncSelect (we don't want to inhibit
// anything AoE3 sets up), but stashes (hWnd, wMsg, lEvent) keyed by
// SOCKET for LAN sockets we care about. lEvent==0 means AoE3 is
// cancelling the registration — we clear our stash to match.
static int (WINAPI* Real_WSAAsyncSelect)(SOCKET, HWND, u_int, long) = ::WSAAsyncSelect;
static int WINAPI Hooked_WSAAsyncSelect(SOCKET s, HWND hWnd, u_int wMsg, long lEvent)
{
    bool isLan = false;
    EnterCriticalSection(&g_stateLock);
    isLan = (g_lanSockets.find(s) != g_lanSockets.end());
    if (isLan)
    {
        if (lEvent != 0 && hWnd != nullptr)
            g_asyncNotify[s] = { hWnd, wMsg, lEvent };
        else
            g_asyncNotify.erase(s);
    }
    LeaveCriticalSection(&g_stateLock);
    if (isLan)
    {
        Log("WSAAsyncSelect sock=%llu hWnd=0x%p wMsg=0x%x lEvent=0x%lx",
            (unsigned long long)s, hWnd, wMsg, lEvent);
    }
    return Real_WSAAsyncSelect(s, hWnd, wMsg, lEvent);
}

// Phase 2.n: same idea as WSAAsyncSelect, but for thread-style event
// notifications (WSAEventSelect signals a Win32 event handle which a
// thread waits on via WaitForSingleObject / WSAWaitForMultipleEvents).
static int (WINAPI* Real_WSAEventSelect)(SOCKET, WSAEVENT, long) = ::WSAEventSelect;
static int WINAPI Hooked_WSAEventSelect(SOCKET s, WSAEVENT hEvent, long lEvent)
{
    bool isLan = false;
    EnterCriticalSection(&g_stateLock);
    isLan = (g_lanSockets.find(s) != g_lanSockets.end());
    if (isLan)
    {
        if (lEvent != 0 && hEvent != nullptr)
            g_eventNotify[s] = { hEvent, lEvent };
        else
            g_eventNotify.erase(s);
    }
    LeaveCriticalSection(&g_stateLock);
    if (isLan)
    {
        Log("WSAEventSelect sock=%llu hEvent=0x%p lEvent=0x%lx",
            (unsigned long long)s, hEvent, lEvent);
    }
    return Real_WSAEventSelect(s, hEvent, lEvent);
}

// Phase 2.j: log WSARecvFrom too. If age3y.exe uses overlapped recv
// for some sockets, our Hooked_recvfrom would never fire and the queue
// drains never happen — we'd see "PACKET_IN delivered" piling up with
// no "INJECT recvfrom" follow-up. This hook just LOGS for now (no
// queue drain logic) so we can confirm whether overlapped is in play
// before committing to a full WSARecvFrom drain path.
static int (WINAPI* Real_WSARecvFrom)(
    SOCKET, LPWSABUF, DWORD, LPDWORD, LPDWORD,
    sockaddr*, LPINT, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE) = ::WSARecvFrom;
static int WINAPI Hooked_WSARecvFrom(
    SOCKET s, LPWSABUF buffers, DWORD bufCount, LPDWORD bytesRecvd,
    LPDWORD flags, sockaddr* from, LPINT fromlen,
    LPWSAOVERLAPPED overlapped, LPWSAOVERLAPPED_COMPLETION_ROUTINE completion)
{
    bool isLanSocket = false;
    uint16_t lanPort = 0;
    EnterCriticalSection(&g_stateLock);
    auto pit = g_lanSocketsPort.find(s);
    if (pit != g_lanSocketsPort.end())
    {
        isLanSocket = true;
        lanPort = pit->second;
    }
    LeaveCriticalSection(&g_stateLock);

    if (isLanSocket)
    {
        Log("WSARecvFrom sock=%llu (tracked port=%u, overlapped=%s)",
            static_cast<unsigned long long>(s), lanPort,
            overlapped != nullptr ? "yes" : "no");
    }
    return Real_WSARecvFrom(s, buffers, bufCount, bytesRecvd, flags,
                            from, fromlen, overlapped, completion);
}

// ---- detours install / uninstall ---------------------------------------

static LONG InstallHooks()
{
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&reinterpret_cast<PVOID&>(Real_sendto),       Hooked_sendto);
    DetourAttach(&reinterpret_cast<PVOID&>(Real_recvfrom),     Hooked_recvfrom);
    DetourAttach(&reinterpret_cast<PVOID&>(Real_bind),         Hooked_bind);
    DetourAttach(&reinterpret_cast<PVOID&>(Real_connect),      Hooked_connect);
    DetourAttach(&reinterpret_cast<PVOID&>(Real_closesocket),    Hooked_closesocket);
    DetourAttach(&reinterpret_cast<PVOID&>(Real_WSARecvFrom),    Hooked_WSARecvFrom);
    DetourAttach(&reinterpret_cast<PVOID&>(Real_WSAAsyncSelect), Hooked_WSAAsyncSelect);
    DetourAttach(&reinterpret_cast<PVOID&>(Real_WSAEventSelect), Hooked_WSAEventSelect);
    return DetourTransactionCommit();
}

static LONG UninstallHooks()
{
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourDetach(&reinterpret_cast<PVOID&>(Real_sendto),         Hooked_sendto);
    DetourDetach(&reinterpret_cast<PVOID&>(Real_recvfrom),       Hooked_recvfrom);
    DetourDetach(&reinterpret_cast<PVOID&>(Real_bind),           Hooked_bind);
    DetourDetach(&reinterpret_cast<PVOID&>(Real_connect),        Hooked_connect);
    DetourDetach(&reinterpret_cast<PVOID&>(Real_closesocket),    Hooked_closesocket);
    DetourDetach(&reinterpret_cast<PVOID&>(Real_WSARecvFrom),    Hooked_WSARecvFrom);
    DetourDetach(&reinterpret_cast<PVOID&>(Real_WSAAsyncSelect), Hooked_WSAAsyncSelect);
    DetourDetach(&reinterpret_cast<PVOID&>(Real_WSAEventSelect), Hooked_WSAEventSelect);
    return DetourTransactionCommit();
}

// ---- DLL entry point ---------------------------------------------------

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID /*reserved*/)
{
    // DetourIsHelperProcess returns TRUE for helper child processes that
    // Detours spawns for some operations. We're not one of those — but
    // documenting the check anyway is the canonical pattern.
    if (DetourIsHelperProcess())
        return TRUE;

    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
    {
        DisableThreadLibraryCalls(hModule);
        LogInit(hModule);
        Log("=== AoeP2pHook attached to PID %lu (log='%ls') ===",
            GetCurrentProcessId(),
            g_logPath[0] ? g_logPath : L"(no writable path found)");

        LONG err = InstallHooks();
        if (err == NO_ERROR)
            Log("InstallHooks OK (sendto/recvfrom/bind/connect/closesocket/WSARecvFrom/"
                "WSAAsyncSelect/WSAEventSelect detoured).");
        else
            Log("InstallHooks FAILED with Detours error %ld.", err);

        // Bridge IPC: spawn a worker thread that opens the named pipe
        // the .NET launcher set up (if any) and sends a HELLO frame.
        // CreateThread, never anything blocking inside the loader lock.
        InitializeCriticalSection(&g_bridgeLock);
        g_bridgeLockReady = true;
        InitializeCriticalSection(&g_stateLock);
        g_stateLockReady = true;

        // Phase 2.c writer queue + writer thread. Created BEFORE the
        // bridge-connect thread so that even the first sendto burst —
        // which can land before the connector finishes the HELLO
        // handshake — has somewhere to enqueue without crashing.
        // BridgeWriterThread holds frames until BridgeWriteLocked
        // succeeds, so pre-handshake enqueues just wait quietly.
        InitializeCriticalSection(&g_writeQueueLock);
        g_writeQueueLockReady = true;
        g_writeQueueEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr); // auto-reset, initially nonsignaled
        if (g_writeQueueEvent == nullptr)
            Log("Bridge: CreateEventW for writer queue failed (Win32 %lu).", GetLastError());
        g_writerThread = CreateThread(nullptr, 0, &BridgeWriterThread, nullptr, 0, nullptr);
        if (g_writerThread == nullptr)
            Log("Bridge: CreateThread for writer failed (Win32 %lu).", GetLastError());

        HANDLE bridgeThread = CreateThread(
            nullptr, 0, &BridgeConnectThread, nullptr, 0, nullptr);
        if (bridgeThread)
            CloseHandle(bridgeThread);
        else
            Log("Bridge: CreateThread for connector failed (Win32 %lu).", GetLastError());
        break;
    }
    case DLL_PROCESS_DETACH:
    {
        Log("=== AoeP2pHook detaching ===");
        UninstallHooks();

        // Stop the Phase 2.c writer thread BEFORE we tear down the
        // pipe — otherwise the writer can race a half-closed handle
        // and crash on its way out. Signal the stop flag, kick the
        // event so the thread sees it, then wait up to 2 s for it to
        // flush whatever's still in the queue.
        InterlockedExchange(&g_writerStop, 1);
        if (g_writeQueueEvent) SetEvent(g_writeQueueEvent);
        if (g_writerThread)
        {
            WaitForSingleObject(g_writerThread, 2000);
            CloseHandle(g_writerThread);
            g_writerThread = nullptr;
        }
        if (g_writeQueueEvent)
        {
            CloseHandle(g_writeQueueEvent);
            g_writeQueueEvent = nullptr;
        }
        if (g_writeQueueLockReady)
        {
            EnterCriticalSection(&g_writeQueueLock);
            g_writeQueue.clear();
            LeaveCriticalSection(&g_writeQueueLock);
            DeleteCriticalSection(&g_writeQueueLock);
            g_writeQueueLockReady = false;
        }

        // Tear down the bridge socket before the log handle so any
        // last-gasp write inside BridgeWriteLocked still gets to log
        // its error. Order matters: log critical section last.
        //
        // shutdown(SD_BOTH) BEFORE closesocket so any recv() in flight
        // on the BridgeConnectThread reader unblocks with EOF instead
        // of hanging on the loopback connection until the process is
        // torn down by the OS. closesocket alone is not guaranteed to
        // wake a blocked recv.
        if (g_bridgeLockReady)
        {
            EnterCriticalSection(&g_bridgeLock);
            if (g_bridgeSocket != INVALID_SOCKET)
            {
                shutdown(g_bridgeSocket, SD_BOTH);
                closesocket(g_bridgeSocket);
                g_bridgeSocket = INVALID_SOCKET;
            }
            LeaveCriticalSection(&g_bridgeLock);
            DeleteCriticalSection(&g_bridgeLock);
            g_bridgeLockReady = false;
        }

        // Phase 2.e: close the wake-up socket BEFORE WSACleanup so
        // the WSA refcount drops correctly (the wake socket was
        // opened via plain socket() — it relies on the game's
        // ws2_32 init being live).
        if (g_wakeSocket != INVALID_SOCKET)
        {
            closesocket(g_wakeSocket);
            g_wakeSocket = INVALID_SOCKET;
        }

        // Phase 2.d.1: balance our WSAStartup (BridgeConnectThread).
        // age3y.exe's own WSAStartup is independent — its matching
        // WSACleanup happens when the process exits. Ours is per-DLL.
        if (g_bridgeWsaStarted)
        {
            WSACleanup();
            g_bridgeWsaStarted = false;
        }

        // Drop the launcher-driven state (peer set + inject queues)
        // before deleting the lock that guards them. Use scoped
        // accessors so we don't depend on container destructors
        // observing a half-deleted critical section if something else
        // is mid-call.
        if (g_stateLockReady)
        {
            EnterCriticalSection(&g_stateLock);
            g_peerIps.clear();
            g_lanSockets.clear();
            g_lanSocketsPort.clear();
            g_recvQueue.clear();
            LeaveCriticalSection(&g_stateLock);
            DeleteCriticalSection(&g_stateLock);
            g_stateLockReady = false;
        }

        if (g_logFile != INVALID_HANDLE_VALUE)
        {
            CloseHandle(g_logFile);
            g_logFile = INVALID_HANDLE_VALUE;
        }
        if (g_logReady)
        {
            DeleteCriticalSection(&g_logLock);
            g_logReady = false;
        }
        break;
    }
    }
    return TRUE;
}
