# FHG — Firefox + Hiddify (source project)

> **Development status: security foundation, not a finished browser.**

FHG aims to build a Windows browser based on Firefox source and Hiddify Core with a **fail-closed** network boundary. Browser processes may connect only to a local Hiddify SOCKS5 listener at `127.0.0.1:12334`. Hiddify Core is a separate trusted network process; it is **not** covered by the browser's block rules.

## Repo layout

- `native/guard/`: Windows Filtering Platform (WFP) security boundary. Source and CI build. 
- `firefox/distribution/policies.json`: browser-local locked SOCKS5, remote DNS and WebRTC policy.
- `docs/SECURITY.md`: threat model, validation and open security gaps.
- `.github/workflows/windows-guard.yml`: Windows compiler build and policy tests.

## What is and isn't built

The Windows workflow builds **FHGGuard.exe**, a standalone guard component. This repository does **not yet** contain a compiled Firefox fork, the integrated Hiddify Core UI, or a certified no-leak browser. The guard is intentionally not bundled with any auto-running browser until security tests on Windows succeed.

Firefox source: https://github.com/mozilla-firefox/firefox  
Hiddify Core source: https://github.com/hiddify/hiddify-core

## Security principle

1. Install and verify persistent app-scoped WFP restrictions **before** launching any browser processes.
2. The browser process may open only IPv4 TCP connections to `127.0.0.1:12334`; all IPv6 connections are blocked. An absent proxy must mean no network access, not fallback to DIRECT.
3. Only executables under the FHG Firefox bundle are targeted. Never modify the system Firefox installation, default browser, or general Windows Firewall profiles.
4. No system-wide firewall disabling. Failure to install or verify the guard is a hard stop.
5. Treat runtime traffic capture and forced disconnect tests as release gates, not optional checks.

See [security specification](docs/SECURITY.md) and [Windows build instructions](native/guard/README.md).
