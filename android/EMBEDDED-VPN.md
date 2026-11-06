# FHG Android 0.2.1 — embedded sing-box VPN beta

**The embedded-VPN APK uses a separate application ID (`com.fhg.android.embedded`) from the original FHG 0.1 beta, so both can remain installed side by side. This avoids forcing an uninstall when the earlier CI debug signing key differs. Reimport the original four-line file into the new app; Keystore vaults are intentionally not shared across apps. This beta's own debug signing key is also not suitable for reliable future upgrades; a stable production signing key is required.**

**This APK puts the protected GeckoView browser, encrypted account vault, offline TOTP and a real sing-box VPN tunnel into the same application.** There is no second Hiddify app or cross-app import required. This beta has **not** been validated end-to-end on a physical phone.

## Architecture & licensing

- The packet transport is built from the pinned open-source GPLv3 [sing-box libbox Android release 1.14.4](https://github.com/singbox-android/libbox/releases/tag/1.14.4), downloaded at CI with pinned SHA-256:
  `d2685924a657705beda44e94c6dc499e781df1ad20d65474e3591ec2f8b96465`.
- FHG Android connects this native engine to Android `VpnService`, providing a real TUN FD, IPv4/IPv6 routes, DNS addresses and protection of proxy dialer sockets. If engine initialization or TUN establishment fails, the service stops and the FHG browser remains blocked; the APK never pretends that a fake VPN service transports packets.
- Hiddify is acknowledged for its user-facing configuration formats and general architecture. **No code from the specially licensed Hiddify application was copied.** This is an independent sing-box integration, not an Hiddify fork, redistribution or Hiddify-branded product. For libbox GPLv3 distribution, source, notices, modifications and the complete GPLv3 license are provided with this publicly accessible repository under `android/LICENSE-GPL-3.0.txt`. The Android integration is distributed under GPLv3, while the earlier Windows FHG release remains separate.
- A pinned binary means users should inspect upstream updates and provenance before relying on it for sensitive use. The APK is debug-signed, not production-signed.

## Configuration and supported subscriptions

The same **four-line account** format is accepted as FHG Windows:

1. Email address
2. Password
3. Base32 2FA secret
4. HTTPS subscription URL

After importing, **Download subscription** makes a single user-approved HTTPS bootstrap fetch (the very first request may occur before any VPN is connected). It accepts plaintext or base64-encoded VLESS, Trojan, VMess and Shadowsocks links. Supported VLESS/Trojan transports include TCP, WebSocket and gRPC, plus Reality where specified. Unsupported protocols or transports are **rejected**, not silently routed directly. Multiple nodes are configured with sing-box's `urltest` selector. Imported material is encrypted using Android Keystore AES-256-GCM in no-backup private storage.

An **Import local sing-box JSON** picker is also provided. It requires a TUN inbound, a route final, and rejects direct outbounds / arbitrary route exceptions to reduce accidental direct egress. The app currently doesn't support the full range of special Hiddify subscription formats, rulesets and protocols (e.g. TUIC and Hysteria2 subscription-to-config conversion). Already-generated valid sing-box JSON may work, subject to strict import rules.

## Use

1. Install the APK, import your 4-line account file, and tap **Download subscription**, accepting the explicit HTTPS bootstrap disclosure. Or choose **Import sing-box JSON** from local storage.
2. Tap **Connect FHG VPN** and grant the Android VPN permission. The FHG app is the VPN provider, not Hiddify.
3. In Android **Settings → VPN → FHG Android**, enable **Always-on VPN** and **Block connections without VPN**. These system protections require your action and apply device-wide. Never enable a different VPN concurrently; Android normally permits only one active VPN.
4. After FHG shows the integrated TUN connection, open its GeckoView ChatGPT browser, manually sign in and click Continue. Username/password are autofilled from the Android Keystore vault only for allowlisted official ChatGPT/OpenAI domains. TOTP is displayed in the account dashboard.
5. Test on a real device: server reachability and node selection, public IP, IPv4/IPv6 routing, DNS leaks, network roaming (Wi-Fi ↔ mobile), VPN revocation, airplane mode, restart/Always-on startup, proxy failure, Android battery restrictions, Google/Apple sign-in, and Cloudflare. CI cannot prove zero leaks or successful ChatGPT login.

## Important limitations

- **Native VPN startup is not proof of internet reachability.** A genuine TUN may exist while upstream proxy authentication or DNS fails; the app currently has no end-to-end relay health verification.
- In a bootstrap situation, retrieving the subscription may use the underlying direct connection because no tunnel exists yet. This is a visible, explicit one-time step, not a secret background bypass. Without OS VPN Lockdown, other apps may use a direct connection while VPN is down.
- The in-app browser is gated on FHG's own active integrated TUN AND Android VPN network capability; unrelated VPN apps do not satisfy it. Fail-closed behavior in the app is not a substitute for OS Lockdown or kernel/network packet-capture tests.
- This beta does not attempt CAPTCHA bypass, automatic ChatGPT sign-in, Android location/timezone spoofing, external login-provider popup guarantees or Windows-specific Authenticator extension binary import. TOTP is handled natively.
- The embedded bridge is a new adapter against a changing gomobile/libbox API. It has build and unit tests, but **real on-device integration, actual tunnel packet delivery and non-leak validation are outstanding**. Do not use it for high-stakes protection before those tests.

## Build/reproducibility

GitHub Actions workflow: `.github/workflows/fhg-android-embedded-vpn.yml`, branch `android-embedded-singbox-20261106`. It installs Android API 37, Gradle 9.3.1, downloads and SHA-256 verifies the pinned libbox AAR, runs unit tests for TOTP and strict subscription parsing, compiles GeckoView and VPN implementation, verifies the APK zip and publishes a beta APK plus checksum on a successful run. The Android source is available in `android/`. A pinned production signing key is not configured.

No original Recorder, Windows FHG profiles, vaults, Hiddify subscriptions or checkpoints are deleted.
