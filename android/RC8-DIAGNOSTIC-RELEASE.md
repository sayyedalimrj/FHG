# FHG Secure Android RC8 — Diagnostic Preview (NOT Production)

This is an **experimental diagnostic preview** of the first Android version that
runs the sing-box Go VPN engine in a dedicated `:vpn` process. If libbox
crashes, the app UI and its GeckoView browser remain in the main process, while
the OS tears down the failed VPN interface. A fail-closed status provider
blocks browser access unless the tunnel was independently verified.

## Included
- Previous 21-server subscription importing and actual server/location choice.
- Native Android VPN via libbox 1.14.4; previous IPv6 zone sanitizer retained.
- Professional RTL UI and per-account encrypted vault / 2FA.
- Redacted startup-phase diagnostics and new VPN_NATIVE_PROCESS_EXITED state.
- Non-sticky VPN service, cross-process heartbeat, and redacted in-app report.

## Verification
- RC8 release APK, app manifest and signature validated by Ubuntu CI.
- Signed release APK installs and UI launches on disposable Android 15/16 emulators.
- A synthetic Connect test intentionally fails on x86_64 Android emulators due
  to SIGILL in ARM64 libbox running through the **Berberis translator**.
  This is not proof the native ARM64 code fails on a Samsung ARM64 CPU.
- The native Apple Silicon ARM64 emulator test is separately tracked in
  GitHub Actions. If it is not green, VPN connection remains **unverified**.
- Real user subscription, native Samsung VPN behavior and IP/DNS leak
  protection are **not verified**.

This RC is **not claimed to fix every native startup error or provide verified
VPN functionality**. The goal is to keep the UI alive on a native engine failure
and allow an in-app diagnostics report without ADB.

Installation note: this preview uses the isolated
`com.fhg.android.secure.rc8` package and does not overwrite RC7. A separate
one-off signing key is used unless persistent repository signing secrets are
configured; in-place upgrades may be incompatible.
