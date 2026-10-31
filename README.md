# FHG — Firefox + Hiddify, isolated browser networking

**Project stage: design and browser policy scaffold; no complete Windows browser binary or verified egress guard yet.**

## Goal

Deliver a **modern, fast, maintained and isolated browser** in which **every browser-originated network connection must go through Hiddify Core**. Hiddify Core runs as a separate, supervised networking process behind a loopback SOCKS5 proxy at `127.0.0.1:12334`.

**When Hiddify is disconnected or crashes, the browser stays open.** Tabs, settings, history, bookmarks and local pages keep working. New online requests fail with a connection/proxy error and **never fall back to DIRECT**. Once Hiddify reconnects, normal browsing can resume without restarting the browser.

The protection is *network fail-closed*, **not** *application shutdown*.

## Browser choice

Start with a maintained upstream **Firefox release**, a separate FHG distribution and profile, and only the minimal patches required for native connection/status UI. Preserve a viable security-update path. Do not freeze Firefox at a known old version or fork the whole networking stack unless necessary.

## Source structure

- [Requirements & acceptance criteria](docs/REQUIREMENTS.md) — user-visible behavior and security goals.
- [Transport architecture](docs/ARCHITECTURE.md) — isolated Hiddify Core process and Windows WFP guard.
- [Firefox policy](firefox/distribution/policies.json) — locked SOCKS5, remote DNS, WebRTC disabled.
- [Policy-check CI](.github/workflows/policy-check.yml) — validates static policy **only**.
- [Native WFP guard prototype](native/guard/FHGGuard.cpp) and [MSVC build workflow](.github/workflows/windows-guard.yml) — builds an experimental executable and runs a localhost socket isolation test; full Firefox/Hiddify leak testing remains undone.
- [Lightweight MVP scope](docs/LEAN-MVP.md) — removes unnecessary product UI, not browser security protections.

## Core invariants

1. All FHG browser executables are subject to outbound WFP protection *before they can access a network*. Only IPv4 TCP to `127.0.0.1:12334` is allowed; disallow other IPv4, all IPv6, other protocols and direct DNS.
2. This protection targets FHG's executable paths only; **existing Firefox installations and other applications are unaffected**.
3. The Hiddify local listener is bound to loopback and under the supervisor's control. Audit Hiddify's outbound routing to rule out unintended DIRECT destinations.
4. A proxy outage produces an offline/error state in open tabs, **not browser termination**. No traffic must escape the FHG browser via OS proxy settings or direct sockets.
5. Verify behavior under disconnects, port takeover, crashes, reinstalls, updates, malicious websites and IPv4/IPv6 DNS/WebRTC tests on real Windows machines before publishing no-leak claims.

## Implementation status

This repo contains an **experimental native WFP guard source** but does **not** yet contain a built Firefox fork, integrated Hiddify Core, runtime-validated WFP guard, validated installer/updater, or real browser + Hiddify leak-test results. These must be implemented and verified; a policy file alone cannot satisfy the requirements.

Upstream: [Firefox](https://github.com/mozilla-firefox/firefox) | [Hiddify Core](https://github.com/hiddify/hiddify-core)

## Verified engineering milestone

[Windows localhost WFP integration run](https://github.com/sayyedalimrj/FHG/actions/runs/41166109092): **passed** on Windows Server 2022. A synthetic FHG-scoped socket client could reach TCP `127.0.0.1:12334`, could not connect to `127.0.0.1:22334` (WSA 10013), an unrelated executable remained able to connect, and a down proxy did not create a new path. This is a **limited test**, not a demonstrated absence of leaks in Firefox or Hiddify. No integrated app release has been produced yet.
