# FHG v0.6.0-test.2 — Windows x64 usability test

**Unsigned engineering test build: not a verified production browser.** Real subscription connectivity, ChatGPT sign-in, and absence of network leaks still require tests on the user's actual Windows installation. Do not bypass Defender or SmartScreen; verify signatures and SHA-256.

## Changes since test.1

- Removed the automatic **"A big subscription"** interruption in FHG's patched Hiddify client. Big subscriptions default to **all nodes running** (keepTop=-1); manual selection remains available from Hiddify's proxy list.
- Rebuilt **FHG Accounts** with large 146px high account cards, selected/active indicators, subscription host display (not URLs/tokens), encrypted local account archive, and cleaner controls.
- **Live six-digit TOTP and countdown appear directly on every account card**, without an extra Show 2FA button or confirmation. A click on **Copy 2FA** explicitly copies the current one-time code. Never share screenshots or clipboard contents containing codes.
- Separate account profiles for FHG Firefox and patched Hiddify; unchanged normal Windows Firefox and Hiddify.
- Testable one-file Inno Setup installer with custom destination and separate shortcuts for Firefox, Proxy, and Accounts.
- Subscription is imported by patched Hiddify; the browser opens only if SOCKS5 on 127.0.0.1:12345 successfully delivers HTTPS. On failed activation it remains closed (fail-closed).

## What remains manual

The bundled signed Authenticator by mymindstorm has no documented supported native API for externally adding TOTP accounts from FHG. The Windows account manager **already generates those codes locally using a DPAPI-encrypted seed**, but the browser extension account list is separate. Rewriting or manually inserting its browser database is not supported and risks deleting old accounts. Setup in the signed extension must be completed through its own interface until a properly reviewed/signed provisioning integration is available.

Automatic login of arbitrary ChatGPT credentials is **not claimed**. Use the real ChatGPT login page; once signed in, each FHG Firefox profile preserves its existing browser session while valid. Account switching opens a different isolated profile, but does not revoke an old server-side session or defeat provider identity challenges.

**Important:** example values such as `user@example.invalid` are test placeholders, not working ChatGPT accounts. Never distribute a text file with real passwords, OTP seeds or subscription tokens. Delete or secure it after provisioning and keep independent recovery codes.

## Test checklist

1. Install to a custom directory and check desktop shortcuts.
2. In FHG Accounts, import a file with a **real** account and valid HTTPS subscription; check each account card shows its own 6-digit code and refreshed countdown. Keep codes out of screenshots.
3. Confirm Hiddify shows all proxies without a big-subscription dialog; verify all 24 nodes are visible if your subscription has 24.
4. Confirm port 27178 (gRPC) and 26756 (Clash API) do not conflict with regular Hiddify (17078/16756); FHG 12345 connects independently from regular 12334.
5. Verify `curl.exe -q --proxy socks5h://127.0.0.1:12345 https://example.com -I` succeeds after connecting. Disconnect FHG; verify Firefox has no direct-network fallback.
6. Sign into ChatGPT through its official page in FHG Firefox, verify session persistence and switching to another saved FHG account.
7. Check signature: three FHG binaries and Setup.exe are currently UNSIGNED; Mozilla-signed Firefox and signed third-party XPI are separate.

Build source: https://github.com/sayyedalimrj/FHG/pull/10 . Licensing: upstream Hiddify rights and source patch are supplied with the test release.
