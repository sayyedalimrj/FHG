# FHG Android 0.2.3 — automatic profile & typed sing-box VPN repair (ARM64 beta)

This is a **testable engineering beta**, not a security certification. It is an independent FHG Android GeckoView + native sing-box/libbox project, **not an unmodified Hiddify app or a complete port of Hiddify's subscription engine**.

## User-facing changes

- **First launch:** opens the original four-line account-file picker. Importing a file now immediately starts the provider subscription download, detects the response format, stores a validated encrypted sing-box profile, then opens the official Android VPN permission dialog and starts the embedded VPN if approved. No separate manual profile-install screen.
- Supports **V2Ray base64/plain links**, **Clash/Clash.Meta YAML**, sing-box JSON/outbounds and common link schemes **VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC**. Not all Hiddify options/rules/protocols are supported (e.g., WireGuard/SSH/ShadowTLS, proxy-provider URL chains, advanced transports).
- A provider may serve a normal HTML user dashboard instead of a machine-readable subscription. In that case FHG shows **SUBSCRIPTION_RETURNED_HTML** with a simple **Edit subscription link** action, rather than pretending the profile installed or retrying indefinitely.
- Negotiation tries a bounded set of provider-recognized **SFA, ClashMeta, HiddifyNext, v2rayNG** User-Agent headers over validated HTTPS, with limited HTTPS-only redirects. The user-supplied subscription token stays in the encrypted Android Keystore vault; no URL or credential is written to logs.
- Replaced fragile runtime reflection with typed Java implementation of the exact pinned **libbox 1.14.4 PlatformInterface and CommandServerHandler**. The app compiles against the actual JNI bindings, config is validated by `Libbox.checkConfig` before import, sockets use Android `protect(fd)`, and native TUN addresses use the real `RoutePrefix.address()/prefix()` calls.
- The VPN connection is **not** marked connected just because a TUN descriptor exists. A normal HTTPS 204 check must succeed across the active tunnel; the browser remains disabled while waiting or after failure, and errors are surfaced without revealing account secrets.
- Redesigned RTL dashboard: one prominent connect/disconnect control, live status, selected account, installed node count, 2FA, browser launch. Advanced actions (manual JSON import, update link, Android Lockdown settings) are collapsed.

## Important install constraints

- Package/application ID: `com.fhg.android.repair`, intentionally distinct from previous `com.fhg.android.embedded`, so the beta can be **installed side by side** without uninstalling or deleting an existing Keystore vault.
- Reimport the original four-line file into the new app. Android Keystore data **cannot** be shared between those applications. Do not uninstall the old app or erase its data until this beta works and the original account file is backed up.
- ARM64 (`arm64-v8a`) Android 8+ only. APK around 133–135 MB, dependent on compression.
- On first import, fetching the HTTPS subscription may use the existing network **before** the embedded VPN is running (necessary to bootstrap). This is disclosed in the UI. If you already enabled VPN Lockdown for a different app, you may need to supply a local config or manually switch the system VPN.
- Android requires the user to confirm the system VPN permission once. FHG cannot silently grant it or silently enable system **Always-on VPN** / **Block connections without VPN**. For system-wide fail-closed traffic, enable those options manually for the new FHG app in Android Settings.
- The initial APK uses a CI-generated debug signature. A permanent signing key is required before shipping updatable production releases.

## Verification scope

The GitHub Actions workflow compiles real GeckoView + libbox ARM64, runs TOTP and subscription parser tests, checks profile sanitization, downloads pinned libbox 1.14.4 using SHA-256 and validates the app's generated Trojan and VLESS config fixtures using the **actual sing-box 1.14.4 Linux binary**. It also checks the actual APK contents (only arm64 native libraries), ZIP integrity, package size and SHA-256 digest.

**Still not validated on a physical Android phone:** actual provider subscription account access, Android VPN permission/device vendor behavior, TUN packet forwarding, live proxy connectivity and DNS/IPv6 leak resilience, reconnection across Wi-Fi/cellular, Google/Apple login and device-specific OS restrictions. Build success and proxy config checks do not prove those properties. Do not use this beta for high-stakes traffic protection without testing.

## If a subscription or VPN still fails

The top card will show a non-secret error code. Examples: `SUBSCRIPTION_RETURNED_HTML`, `SUBSCRIPTION_ACCESS_DENIED`, `SUBSCRIPTION_DNS_ERROR`, `SUBSCRIPTION_TLS_ERROR`, `VPN_CONFIG_REJECTED`, `LIBBOX_START_ERROR`, `PROXY_NOT_REACHABLE`. For HTML, use the direct HTTPS subscription endpoint rather than the portal/dashboard URL. For a config rejection, try a local sing-box JSON profile or a node format supported in the beta. Never share a subscription URL containing a personal token or your password/TOTP secret in a public issue or screenshot.

The older FHG Windows 1.0.0 and Android APKs are left untouched. GPLv3 libbox licensing/source are retained with this repository.
