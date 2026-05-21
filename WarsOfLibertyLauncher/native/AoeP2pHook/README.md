# AoeP2pHook

DLL (Win32 x86) que el launcher inyecta dentro de `age3y.exe` para
interceptar sus llamadas a WinSock. Es la pieza "Voobly-style" del
launcher — reemplaza el bridge basado en captura de paquetes
(WinDivert + Wintun) con hooks dentro del proceso del juego, lo que
nos permite fabricar respuestas de "host LAN encontrado" sin que
AoE3 nunca llame a `recvfrom` esperando un broadcast real.

## ¿Por qué hookeamos WinSock y no DirectPlay?

Investigamos empíricamente los strings y las DLLs importadas de
`age3y.exe` (Wars of Liberty 1.2.0c2):

* Imports: `ws2_32.dll`, `iphlpapi.dll`, `kernel32.dll`, …
* APIs vistas: `socket`, `connect`, `getaddrinfo`, `select`
* APIs DirectPlay: **cero strings**

AoE3 / WoL implementa su lobby LAN directamente sobre BSD sockets, no
sobre DirectPlay. Voobly hooks DirectPlay porque sus juegos clásicos
(AoE2, AoM) sí lo usan; nosotros estamos en la franja distinta donde
hay que hookear más abajo.

## Arquitectura

```
┌─────────────────────────┐
│  Launcher .NET (x64)    │
└────────────┬────────────┘
             │ CreateProcess(SUSPENDED)
             │ VirtualAllocEx + WriteProcessMemory + CreateRemoteThread(LoadLibraryW)
             ↓
┌─────────────────────────┐
│  age3y.exe (x86)        │
│  + AoeP2pHook.dll       │ ← este proyecto
│    └─ Detours hooks     │
│       sendto/recvfrom/  │
│       bind/connect      │
└────────────┬────────────┘
             │ (Fase 4: hooks redirigen el tráfico)
             ↓
┌─────────────────────────┐
│  Wintun + mesh P2P      │ ← lo que ya teníamos
└─────────────────────────┘
```

## Plan de fases

| Fase | Estado | Qué hace |
|---|---|---|
| **1. Skeleton + log** | ⏳ esta versión | Hookea sendto/recvfrom/bind/connect, escribe cada llamada a `%LOCALAPPDATA%\AoeP2pHook.log`. Lectura nada más, no modifica nada. |
| 2. Análisis | siguiente | Con datos del log identificamos: qué puerto bindea AoE3, qué broadcasts emite el host, qué espera recibir el cliente para listar un host. |
| 3. Fake recvfrom responses | después | En el cliente, cuando AoE3 pide `recvfrom` sobre el puerto de discovery, devolvemos paquetes sintéticos "encontré este host" generados a partir de la lista del lobby. AoE3 popula la lista LAN sin nadie clickear nada. |
| 4. sendto redirect | después | Reescribimos el destino de `sendto` cuando AoE3 trata de conectar a un "host LAN" — lo mandamos por Wintun/mesh en lugar de la red real. |

## Compilación

Requiere:

* Visual Studio 2022 o 2026 Community con workload **"Desktop development with C++"** instalado
  (verificar que incluya MSVC v143+ y Windows 10/11 SDK)
* `Detours/` clonado en `..\..\third_party\Detours\` (ya está si seguiste el setup)

Pasos:

1. Abrir **Developer Command Prompt for VS** (x86) desde el menú Start.
2. `cd` a esta carpeta (`WarsOfLibertyLauncher\native\AoeP2pHook`).
3. Ejecutar `build.bat`.
4. Output: `bin\AoeP2pHook.dll` (Win32 x86, ~200 KB).

El launcher copia el DLL automáticamente al lado de su .exe en el
flujo de publish.

## Diagnóstico

Cada vez que `age3y.exe` arranca con la DLL inyectada, se genera /
añade a `%LOCALAPPDATA%\AoeP2pHook.log` con líneas tipo:

```
[10:23:45.123] === AoeP2pHook attached to PID 12345 ===
[10:23:45.124] InstallHooks OK (sendto/recvfrom/bind/connect detoured).
[10:23:46.789] bind sock=312 0.0.0.0:2299 -> 0 (err=0)
[10:23:48.012] sendto sock=312 -> 255.255.255.255:2299 len=42 flags=0x0
[10:23:50.456] recvfrom sock=312 <- 192.168.68.71:2299 len=42
```

## Licencia y atribución

* Código propio: igual licencia que el resto del launcher.
* Detours: licencia MIT — embebimos su fuente directamente en la DLL,
  no requiere mención adicional en el binario distribuido.
