# FHG Browser v0.6.0 — Final packaged Windows x64 build

**Final requested packaging iteration, unsigned.** This is not a certification of successful sign-in to a real ChatGPT account, zero-leak network behavior or publisher identity. The previous test.9 is the pinned binary baseline.

## Final changes

1. **More time to sign in.** The automation window's main timer increases from 135 to 360 seconds, with form discovery and per-stage timeout budgets increased accordingly. The launching process waits up to 420 seconds so it does not prematurely kill an otherwise active login. The correct HTTPS URL remains **https://chatgpt.com/login**.
2. **Physical-pointer Continue after password.** After filling and verifying the existing password input, the client waits briefly for the official UI to process typing. It then uses W3C WebDriver Actions to move a mouse pointer to the visible Continue element, hold primary-button down briefly, and release it. This is not a JavaScript `form.submit()`, a fake HTTP POST, a direct auth API call, or an Enter-key workaround. The official provider form stays in control. Email/TOTP submissions are not changed.
3. **Maximum practical installer compression.** The Windows Inno Setup 6 installer uses `lzma2/ultra64`, solid compression, a 256 MiB dictionary, 273 fast bytes, separate-process compression, and a single LZMA block thread. Memory-sensitive users should allow at least ~256 MiB for decompression; the CI build uses much more. The result is one full installer EXE with the bundled Firefox, original signed Authenticator, timezone add-on, geckodriver, dedicated Hiddify Proxy and WFP guard.
4. **Durable Firefox handoff remains.** The temporary automation Firefox is closed intentionally after an attempt; the real Firefox reopens using the existing isolated profile. If sign-in is incomplete, it stays on **https://chatgpt.com/login** for manual continuation.
5. **Preserve all existing state.** The app installer AppId, account vault, per-account Firefox profiles and Hiddify subscription database are unchanged. Neither personal passwords nor TOTP seeds are put into diagnostics or build artifacts.

## What the HTTP 400 means

The physical-pointer action is a reasonable attempt to better match the user's successful manual Continue click. It does **not** guarantee avoidance of `400 Invalid content type`; server-side authentication, cookies, a provider challenge, upstream responses or a changed page may be involved. The automation does not bypass CAPTCHA, MFA challenges, authorization or provider protections. Do not repeat failed submissions indefinitely.

## CI and security gates

Build from the pinned test.9 release, verify copied Firefox/Hiddify/Guard binary SHA-256, check signed-original Authenticator import and timezone configuration in real Windows Firefox using synthetic data, check real synthetic W3C trusted pointer events on Continue after password, test Unicode password input and original TOTP persistence, build the complete Windows installer, reinstall it silently on a clean GitHub Windows runner and verify the installed file SHA-256 values. Publish only after every gate succeeds.

**Limitations:** live authorized ChatGPT login on the user's PC, proxy connectivity/packet leak-freedom, Windows Defender SmartScreen publisher trust and signing are still unverified. The FHG app is unsigned; use the official GitHub release and verify the SHA-256 checksum. Do not uninstall or delete the existing FHG data during upgrading.
