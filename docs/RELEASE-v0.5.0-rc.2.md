# FHG Proxy v0.5.0-rc.2 — Independent Windows test prerelease

**Windows 10/11 x64 — unsigned experimental prerelease, not a certified secure browser.**

## Changes

- Original Hiddify-style subscription management retained, with its source-modified GUI rebuilt as **FHGProxy.exe**.
- Independent `FHGProxyMutex`, window title, Windows executable metadata, FHG shield/taskbar and tray icons (including connected / disconnected states).
- Dedicated `Hiddify/fhg_proxy_portable_data/` instead of `hiddify_portable_data/`. FHG preferences use a distinct key prefix; failure to write portable data aborts startup instead of falling back to vendor storage.
- No hijacking of the user's installed Hiddify URL protocol registrations; auto-start package identity is distinct.
- **Only Proxy mode** is offered. The local mixed SOCKS5 port is pinned to **127.0.0.1:12345** in default preferences, Core runtime options, imported-profile override hardening, read-only UI, Firefox locked enterprise proxy settings and WFP.
- Source-modified Hiddify disables TUN, VPN, System Proxy, LAN exposure, WARP/Psiphon chaining, upstream auto-updates and analytics. About/Logs are hidden from user menus.
- Firefox remains isolated from the normal Firefox profile; WebRTC is disabled, while common AI site features, file uploads, cookies, JavaScript/WebAssembly and media codecs remain in the bundle.
- FHG's WFP provider/sublayer identity is independent of earlier FHG release versions and blocks Firefox direct fallback at application path scope. Synthetic IPv4/IPv6 TCP and non-admin guard-status tests run in CI.
- The startup launcher checks whether TCP port 12345 is already being listened on before starting its FHG Proxy GUI; it **will not terminate or modify the user's existing Hiddify**.

## Install and launch

1. Download `FHG-ProxyOnly-12345-Windows-x64-v0.5.0-rc.2.zip` and the accompanying `.sha256` file.
2. Verify SHA-256, then extract **all** contents into a writable folder that is not Program Files; do not mix files with earlier FHG versions.
3. Start `FHG.exe`. Windows may ask for Administrator elevation once to install the application-specific WFP guard. The dedicated **FHG Proxy** window should open separately from the original Hiddify.
4. Import or paste your subscription in the FHG Proxy window and connect. Firefox uses only the configured local proxy, and it must not fall back to a direct network connection.

**Port 12345 is not guaranteed free** on every computer. If occupied, close the program using it or use a different FHG build after auditing all components; do not change only the Firefox port or only Hiddify.

## Licensing / source

Upstream Hiddify is copyright its original authors and is licensed under its upstream modified/extended GPLv3 conditions: https://github.com/hiddify/hiddify-app/blob/main/LICENSE.md .
The exact upstream v5.5.5 source commit is `7804cadac01419f31b1ec7802d9e1be599e66de2`.
Source transformations are included in `FHG-SOURCE-PATCH.py`, `FHG-REBRAND-SOURCE.py`, and `FHG-HIDDIFY-SOURCE.patch` inside the ZIP and are also available in this repository.
Redistribution obligations (including Hiddify's special fork conditions) must be reviewed before commercial or production redistribution. This release is marked **prerelease** for development testing.

## Security caveats

- Build-time verification and Windows CI regression tests **are not a full real-network DNS/IPv6/UDP/network-capture privacy audit**. Do not claim guaranteed anonymity or "zero leaks".
- The browser's app-specific WFP filters constrain Firefox processes; Hiddify Core's own routing configuration can still use direct outbound routes / DNS / bootstrap as dictated by upstream.
- The local port is not cryptographically bound to a trusted proxy process; local port takeover remains a risk if the proxy exits.
- The original Hiddify Core binaries remain attributable to upstream. Native FHG exe and the recompiled FHGProxy.exe are unsigned without a validated publisher certificate.
- Firefox in-app auto-update is disabled for audited WFP process compatibility: install new FHG releases promptly to receive browser security fixes.
