# FHG Secure Android 1.0 RC1 — architecture, security, UI and release audit

**Scope:** native Android/GeckoView browser, AES-GCM Android Keystore vault, first-run account file, Hiddify-style proxy subscriptions, single-app sing-box native VPN, privacy controls and user interface.

**Status:** Release candidate. Automated compilation, Java tests, actual sing-box CLI configuration checks and Android APK integrity checks are CI gates. No physical-Android TUN packet/IPv6/DNS leak tests are claimed or invented.

## Sources consulted (official/primary)

1. [Android VpnService API](https://developer.android.com/reference/android/net/VpnService) — explicit user approval via `prepare`, BIND_VPN_SERVICE, onRevoke, protect(fd), Always-on, Lockdown.
2. [Android VPN developer guide](https://developer.android.com/develop/connectivity/vpn) — native tunnel packet processing and system VPN user flow, foreground service, notification, no bypass.
3. [Android Network.openConnection](https://developer.android.com/reference/android/net/Network#openConnection(java.net.URL)) — an HTTPS request must be bound to the specific VPN Network to establish whether traffic actually traverses it.
4. [Android NetworkCapabilities.getOwnerUid](https://developer.android.com/reference/android/net/NetworkCapabilities#getOwnerUid()) — select the application-owned VPN network when exposed by the platform (API 30+).
5. [sing-box 1.14 Tun](https://sing-box.sagernet.org/configuration/inbound/tun/) — auto-route, strict route, DNS hijack and native platform TUN.
6. [Sing-box for Android features](https://sing-box.sagernet.org/clients/android/features/) — Android VpnService implementation, platform-specific limits and comparison.
7. [Mozilla GeckoView architecture](https://firefox-source-docs.mozilla.org/mobile/android/geckoview/contributor/geckoview-architecture.html) — implementer-managed navigation, prompt, cookies and login storage.
8. [Mozilla GeckoView Autocomplete](https://mozilla.github.io/geckoview/javadoc/mozilla-central/org/mozilla/geckoview/Autocomplete.html) — credential storage callbacks; no synthetic login bypass.
9. [Android accessibility guide](https://developer.android.com/guide/topics/ui/accessibility/apps) — 48dp touch targets, accessible descriptions, minimal steps.
10. [Android foreground-service type rules](https://developer.android.com/about/versions/14/changes/fgs-types-required) — VPN qualification for systemExempted and Android 14+ target rules.
11. [Hiddify official extended license](https://github.com/hiddify/hiddify-app/blob/main/LICENSE.md) — conditions for using Hiddify *app source* as a fork.
12. [OpenAI login troubleshooting](https://help.openai.com/en/articles/7426629-why-cant-i-log-in-to-chatgpt) and [network allowlist](https://help.openai.com/en/articles/9247338-network-recommendations-for-chatgpt-errors-on-web-and-apps) — official login route, identity provider sign-in, Cloudflare challenges, cookies/WebSocket requirements.

## Architecture

```
User 4-line TXT file / optional manual subscription update
    ↓ Android file-picker; Keystore AES-256-GCM encrypted no-backup vault
Account → subscription parser (HTTPS only; bounded size and redirects)
    ↓ V2Ray/base64, Clash YAML, sing-box JSON, SS/VMess/VLESS/Trojan/TUIC/HY2
Native Libbox.checkConfig BEFORE replacing last valid stored profile
    ↓
Android VpnService.prepare() → Android *user* consent → foreground service
    ↓
Typed Libbox CommandServer + PlatformInterface → protected upstream sockets
    ↓
Android Builder.establish() TUN (IPv4/IPv6 routes, no allowBypass())
    ↓
Explicit Android OWN-VPN Network.openConnection HTTPS health test
    ↓
VERIFIED state true only after HTTPS 204 from VPN network
    ↓
One GeckoView Activity + persistent contextId/account + official-login-only autofill
    ↓
Manual Continue / CAPTCHA / MFA / OAuth; bound, in-view popup sessions
```

### Actual defects corrected after 0.2.3

- **False-positive health check:** the VPN app process may use a physical default network. Replaced ordinary `new URL(...).openConnection()` with `Network.openConnection()` on a VPN `Network` owned by FHG, along with checks for VPN transport and native TUN health.
- **One-time health check:** added repeated testing while the tunnel remains up. Repeated failures set `RECONNECTING`, revoke browser permission and invoke libbox network reset. A tunnel never becomes reported as working solely from a green Android key icon.
- **Wrong-profile race:** switching accounts invalidates stale subscription callbacks, stops the previous TUN and refuses to launch the wrong account's browser.
- **GeckoView multiple windows / autofill crossing:** BrowserActivity is `singleTask`, maintains one active GeckoView runtime/storage delegate, closes old account sessions before switching, and handles bounded OAuth child sessions inside the same activity. Credential read/write only on exact official HTTPS ChatGPT/OpenAI login origins; no `*.openai.com` wildcard or third-party autofill.
- **Login fragility:** manually click Continue / complete Cloudflare and MFA, open the official `https://chatgpt.com/auth/login` route, do not synthetically click page buttons. Dedicated GeckoView context doesn't apply aggressive tracking blocking to the provider login.
- **Subscription parsing:** all 8 synthetic formats are generated as full *safe TUN* configs and verified against the pinned actual sing-box 1.14.4 Linux binary in CI, not merely accepted by a custom Java parser. Unsupported provider-specific features are explicitly reported; no silent direct route fallback.
- **UI/UX:** one primary VPN button, progress/status and recovery action, integrated account setup, collapsed advanced controls, distinct shield/person/browser labels, accessible ≥48dp controls, readable account picker, edge-to-edge insets, persistent TOTP and exportable *redacted* local diagnostics.
- **Application persistence:** side-by-side release candidate uses a different application ID, protecting previous betas and their Keystore vaults from accidental uninstall or debug-signature conflicts.
- **Package footprint:** ARM64 native libraries only; reference universal APK was ~393.7 MB and ARM64 APK remains around 133.5 MB.

## Security/limitations — do not misrepresent

- The app includes **sing-box libbox**, not a copy of Hiddify's proprietary/extended-license application UI or its complete subscription engine. Hiddify app source may only be reused subject to its [special license](https://github.com/hiddify/hiddify-app/blob/main/LICENSE.md). FHG does **not** claim to be an official Hiddify fork. The embedded GPLv3 libbox license and source references are published in this repo; examine all derivative work obligations before any commercial/app-store distribution.
- **No silent Android VPN approval:** `VpnService.prepare` needs one user consent dialog. OS Always-on and “Block connections without VPN” must also be enabled by the user for whole-device protection. Android only permits one active VPN per user/work profile.
- **Initial subscription bootstrap** is an explicit user-initiated HTTPS request potentially sent over the preexisting network before the VPN is available. Lockdown on another app may block bootstrap; importing a local profile is a fallback.
- **Device traffic is not certified leak-free:** Android VPN ownership, build and config correctness do not replace physical-device packet testing. Test public IPv4/IPv6 address, DNS, IPv6 route handling, switching networks, mid-request disconnect, restart, standby and always-on startup. A server might be unreachable despite a syntactically valid config.
- **This RC is debug-signed by CI.** It is installable/testable but not a stable production upgrade signing identity. A trusted private release-signing key must be configured securely as GitHub Actions secrets; never commit a private key in the repo.
- The user must test Google/Apple/Microsoft login, passkeys, CAPTCHA and site changes on their target Android device. Upstream 400 errors, account access controls or website challenges cannot be guaranteed away by local code.
- No request credentials, full subscription tokens, browser cookies or IP addresses are written to the diagnostic report. The user actively chooses to share it.
- No user Recorder files, Windows FHG profiles, existing Android accounts or checkpoints are modified or deleted.

## Release verification checklist

- [x] Typed libbox 1.14.4, pinned SHA-256 AAR
- [x] Android GeckoView and VPN service compile on API 37
- [x] Native/Java TOTP and subscription parser unit tests
- [x] Native sing-box CLI validates generated VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC, Clash YAML, sing-box JSON
- [x] ZIP/native ARM64-only and package-size checks
- [x] Redacted diagnostic report tests
- [ ] Physical Android VPN permission, tunnel packet transfer and reconnection test
- [ ] IPv4/IPv6/DNS leak validation on Wi-Fi + mobile data
- [ ] Real provider subscription and login verification, including Google/Apple OAuth
- [ ] Durable production APK signing

**Decision:** deliver as a source-backed, testable **v1.0.0 release candidate**. Do not label the tunnel or OS-wide leak protection “production verified” until the unchecked items pass.
