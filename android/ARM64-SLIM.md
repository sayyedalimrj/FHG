# FHG Android 0.2.2 — ARM64 slim beta

This is the **single-APK ARM64-only** build of the existing FHG Android 0.2.1 integrated GeckoView + sing-box prototype. Nothing has been removed from the app's browser, encrypted account vault, 2FA, or VPN integration source. The reduced file contains native GeckoView and libbox code only for `arm64-v8a`, instead of multiple CPU architectures.

## Why the universal APK was enormous

The previous fully verified artifact `FHG-Android-0.2.1-embedded-vpn-beta.apk` was **393,657,988 bytes**. Its Gradle configuration had no native ABI filter. GeckoView (Firefox browser engine) and GPLv3 libbox (sing-box native VPN engine) each contain substantial platform-specific native code. Shipping several ABIs together wastes space for phones requiring only arm64. This variant uses Gradle `ndk.abiFilters 'arm64-v8a'`, verified by inspecting native library paths inside the final APK.

## Install & compatibility

- Compatible only with devices that expose the **arm64-v8a** Android ABI (many modern Android phones). **32-bit Android and x86 emulators cannot install or run this APK.**
- Android 8 (API 26) and later, subject to the installed GeckoView release's actual OS requirements.
- Keeps package ID `com.fhg.android.embedded`, and increments version code to 4.
- **Important:** This beta uses the GitHub runner's auto-generated debug signing key. Since the previous beta likely has a different key, Android may refuse an in-place update with “App not installed / package conflicts.” Do **not** delete the previous app if it contains accounts you cannot reimport. Export/reuse the original user-provided four-line text file; do not expect Keystore secrets to migrate through a reinstall. A production release requires a durable signing key.
- Signed debug builds and automated CI tests are **not proof** of tunnel correctness or prevention of DNS/IPv6 leaks. The embedded libbox bridge and VPN transport still need actual Android device testing. A reported active TUN interface does not prove the proxy is successfully relaying traffic.

## What is unchanged

- Native GeckoView Firefox rendering engine; separate browser contexts for each account, manual ChatGPT login and automatic encrypted password autofill.
- Keystore AES-256-GCM account/config storage and local TOTP.
- Embedded pinned GPLv3 sing-box libbox 1.14.4 and Android VpnService, user-confirmed VPN permission, per-app browser VPN gate, and optional Android Always-on/Lockdown settings.
- VLESS/Trojan/VMess/Shadowsocks subscription parser and offline sing-box JSON import with fail-closed path for unsupported inputs.
- GPLv3 license and source attribution under `android/LICENSE-GPL-3.0.txt`.

## Build evidence

The accompanying GitHub Actions workflow `.github/workflows/fhg-android-slim-arm64.yml` runs automated Java/TOTP/subscription tests, downloads the same pinned native libbox binary with SHA-256 validation, assembles an APK, checks ZIP integrity, ensures all packaged native libraries are `lib/arm64-v8a/*`, compares the exact output bytes against the universal APK and publishes a matching SHA-256 file.

This release optimizes **download size**, not in-memory engine footprint or networking behavior. If a much smaller APK is required later, retaining full Firefox/GeckoView may become a deliberate product tradeoff.
