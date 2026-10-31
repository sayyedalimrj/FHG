# FHG — Firefox + Hiddify Secure Browser (experimental alpha)

**Status: a Windows x64 offline build exists. It is NOT certified leak-free or production-ready.**

FHG combines an unmodified, current official Firefox distribution, a self-contained .NET 10 WPF desktop controller and HiddifyCLI/Core in a single downloadable offline package. It is not yet a custom-built Firefox fork or an in-browser connection-settings UI.

## Download / build

- **[Download FHG v0.2.0-alpha.1 (Windows x64) from GitHub Releases](https://github.com/sayyedalimrj/FHG/releases/tag/v0.2.0-alpha.1)** — experimental, unsigned, not certified leak-free.
- [Windows controller CI and full offline build](https://github.com/sayyedalimrj/FHG/actions/workflows/windows-controller.yml).
- [Security review (2026-10-31)](docs/SECURITY-REVIEW-2026-10-31.md) — verified controls and outstanding risks.
- The artifact **FHG-experimental-OFFLINE-browser-windows-x64** contains a ZIP; extract that ZIP to a permanent writable folder.
- Launch **FHG.exe** (not firefox.exe). A per-user Desktop shortcut is created on first launch. No separate Hiddify UI or CMD window is needed.
- Paste a **Hiddify share link, subscription URL or configuration text**, or import a JSON/YAML/TXT file; select *Connect*, then *Open secure browser*. The UI shows proxy status, guard state and HTTPS latency measured through SOCKS5. The first browser start asks for Administrator permission only if the WFP guard rules are missing.
- If your Hiddify CLI needs exported app configuration, put it at `Data/hiddify-options.json` (local only, never commit secrets). The expected mixed SOCKS5 listener is `127.0.0.1:12334`.
- When the local proxy stops, **the browser stays open** and online pages should fail rather than fall back to direct connections. You can reconnect without reopening Firefox.

## Architecture

```text
FHG.exe                     (normal user, modern .NET 10 WPF UI and core supervision)
 ├─ FHGGuard.exe             (one-time elevated WFP policy installer; read-only status later)
 ├─ Core/HiddifyCli.exe      (supervised subprocess, hiddify-core.dll)
 └─ Firefox/firefox.exe      (dedicated FHG profile, locked SOCKS5 settings)

Firefox processes -- only outbound IPv4 TCP 127.0.0.1:12334 --> HiddifyCLI
HiddifyCLI -- configured proxy nodes --> external network
```

The FHGGuard Windows Filtering Platform rules are path-scoped to FHG's Firefox executables; they do not change Windows Firewall profiles or the existing Firefox installation. Source: [native app](native/app/FHG.cpp), [WFP guard](native/guard/FHGGuard.cpp), [policy](firefox/distribution/policies.json), [bundle script](packaging/build-offline.ps1).

## Code signing policy and verification

**Publisher signing is pending.** FHG's native EXEs are currently unsigned, and the browser is **not** validated leak-free. A GitHub Artifact Attestation proves which public workflow produced an artifact; it does not provide Microsoft Authenticode trust or establish network privacy.

Only the native `FHG.exe` and `FHGGuard.exe` built from FHG source are candidates for future SignPath Foundation signing. Upstream Firefox and Hiddify binaries retain their own publishers and are **never re-signed as FHG**. Every future public signing request must be made from a trusted, reviewed GitHub release build with explicit manual approval; release signing is **not enabled today**.

The offline GitHub Actions artifact includes a `.sha256` checksum. On provenance-enabled runs, verify the exact extracted ZIP with `gh attestation verify FHG-experimental-win-x64.zip --repo sayyedalimrj/FHG` before trusting its origin.

**[Code signing policy, prerequisites and pending security tests](docs/CODE-SIGNING.md).** FHG has no publicly issued code-signing certificate and no production-ready release yet.

## What is tested

- Compiles both native EXEs in GitHub Actions on Windows x64.
- Windows synthetic network test of guard allowing TCP to `127.0.0.1:12334` and blocking another port, without affecting a separate executable: [test run](https://github.com/sayyedalimrj/FHG/actions/runs/41166109092).
- Full bundle generated from Mozilla's official latest Windows Firefox release and the official Hiddify Windows portable v4.1.1 archive; CI verifies ZIP integrity and required files.

## What is NOT tested / limitations

- **The integrated Firefox and HiddifyCLI have not been exercised together on a real desktop**, and the app has not passed full IPv4/IPv6/DNS/WebRTC packet-capture tests.
- WFP guard status tests stored rules, not complete effective-policy arbitration against other WFP providers or all spawned helper processes.
- A malicious process may race to bind the local SOCKS endpoint after Hiddify exits; TCP port ownership is observed but not cryptographically pinned.
- Hiddify Core routing can include `DIRECT` decisions; restrict and audit your config if privacy from the target network is required.
- The Firefox release must be updated frequently; this portable ZIP does not provide a signed, validated automatic update workflow.
- The WPF connection dashboard is a separate window, not yet inside the Firefox toolbar, and the unsigned binaries may trigger SmartScreen warnings.
- Code-signing certificates, a custom app icon, complete cross-machine testing, and security hardening are still needed.

**Do not use this alpha for high-risk or anonymity-critical browsing.** Browser networking must be verified with live packet captures before claiming no leaks.

## Roadmap

1. Secure the local Hiddify endpoint against port takeover, audit outbound routes.
2. Automate in-VM Firefox + Hiddify traffic-leak tests, crash/disconnect/reconnect tests, Windows 10/11 coverage.
3. Fold connection control into the browser experience, implement signed updates and branding without disabling Firefox security features.

[Architecture](docs/ARCHITECTURE.md) • [Requirements](docs/REQUIREMENTS.md) • [Slim scope](docs/LEAN-MVP.md)

## UI v2 and signing

- Source: [WPF dashboard](ui/MainWindow.xaml) and [controller logic](ui/MainWindow.xaml.cs).
- Uses a dark two-panel layout, clipboard paste, local file import, profile validation feedback, live guard/core/proxy indicators and HTTPS round-trip latency explicitly over SOCKS5.
- Desktop shortcut creation and independent Firefox profile. The offline bundle includes the self-contained runtime; no separate .NET installation required.
- The CI code-signing stage only signs when a real publisher PFX and password are present in configured Actions secrets. Otherwise the files are **unsigned**; build provenance attestation is not a trusted Authenticode signature.
- This UI does not imply the full network stack is leak-tested; alpha-only until full browser + Hiddify packet capture and security review.
