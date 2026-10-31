# FHG — secure Firefox + Hiddify browser project

**Status: architecture and Firefox policy scaffold committed; no integrated browser binary exists yet.**

Goal: create a Windows browser based on Firefox with separately isolated Hiddify Core networking and a fail-closed network boundary. The browser should only connect over TCP to a local SOCKS5 listener, currently `127.0.0.1:12334`. Hiddify Core handles remote network traffic in its own process.

## Current files

- [Security architecture](docs/ARCHITECTURE.md): process isolation, WFP requirements and release security tests.
- [Firefox browser-local policy](firefox/distribution/policies.json): locked SOCKS5 proxy, proxy-side DNS, WebRTC disabled.
- [CI policy validation](.github/workflows/policy-check.yml): static configuration assertions. **A green CI run is not a leak test.**

## Implementation still required

1. Build and test a Windows Filtering Platform guard with browser-executable-scoped allow-only-local-SOCKS enforcement and verified persistent fail-closed behavior. Include install/uninstall and policy checks.
2. Integrate Hiddify Core in a supervised, separate subprocess with authenticated control IPC and fail-safe startup/shutdown.
3. Build a Firefox fork and user interface for connection management, keeping browser profile and executable isolation from users' existing Firefox installations.
4. Run real Windows 10/11 egress capture, crash/reconnect, direct launch, IPv6/DNS/WebRTC, port hijack and cross-application impact tests.
5. Only distribute once runtime security gates pass.

Source projects: [Firefox](https://github.com/mozilla-firefox/firefox), [Hiddify Core](https://github.com/hiddify/hiddify-core).

**Do not advertise zero leaks or a finished release at this stage.**