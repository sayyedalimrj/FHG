# FHG Windows WFP egress guard — experimental

**This is native C++ code under development, not yet field tested and NOT a full browser build.**

Purpose: register persistent Windows Filtering Platform (WFP) filters **only for FHG's Firefox executable paths**, preserving ordinary Firefox and unrelated apps. One-time administrative setup is needed; the status check is read-only and does not change the system firewall profiles.

## Build

On Windows 10/11 x64 with Visual Studio 2022 C++ desktop workload:

```powershell
cmake -S native/guard -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```

Also see the [Windows CI workflow](../../.github/workflows/windows-guard.yml) for a Windows build artifact. The CI job compiles only and does **not** establish any security guarantees or run live leak tests.

## Manual developer usage (after vetting the code; use a test VM)

For a Firefox distribution located in `C:\FHG\Firefox\`:

```powershell
# elevated shell, once; MUST contain firefox.exe at this directory
.\FHGGuard.exe install C:\FHG\Firefox

# read-only check before opening browser, regular user
.\FHGGuard.exe status C:\FHG\Firefox

# elevated shell, explicit cleanup
.\FHGGuard.exe remove
```

The guard enumerates the EXE files under that directory and adds four persistent WFP rules per EXE at outbound ALE authorization layers:

- Block all IPv6 outbound connections.
- Block any IPv4 destination other than `127.0.0.1`.
- Block any IPv4 remote port other than `12334`.
- Block any IPv4 protocol other than TCP.

Therefore if correctly applied, only IPv4 TCP to `127.0.0.1:12334` remains permitted *by these filters*. This does not imply a connection to that endpoint is allowed by all other installed firewall software.

## Caveats

- There is **no live network test** in the code or CI; `status` checks persisted WFP objects and their conditions, **not effective path arbitration**. Test against real network traffic before using this for browsing.
- The installer is an experimental test utility, not a production guard. Do not distribute it to end users as a certified no-leak product.
- If the Firefox directory moves, a helper EXE is added, or a browser update changes its process layout, guard installation/verification must be revisited **before network activity**.
- These rules do not constrain Hiddify Core or a different executable spawned outside the Firefox folder. The final product needs a trusted supervisor, process identity hardening, Hiddify route audit, port ownership checks, and runtime policy validation.
- The `remove` operation removes rules belonging to this FHG provider (not Windows-wide firewall policies). It should be used only after the FHG browser is closed, to avoid opening a direct-network window.
- The guard **does not close the browser if Hiddify goes offline**. Offline proxy traffic should fail as a network request while UI continues working.
- The Windows platform may impose administrative requirements to create persistent WFP policy. A successful compile is not a confirmation of safe runtime enforcement.

## Security documentation

See [architecture](../../docs/ARCHITECTURE.md) and [acceptance requirements](../../docs/REQUIREMENTS.md).
