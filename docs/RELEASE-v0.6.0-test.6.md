# FHG v0.6.0-test.6 — Automatic ChatGPT email/password/2FA sign-in

**Unsigned Windows x64 engineering preview. Never bypass Microsoft Defender or SmartScreen.**

## Automatic login (NEW)

- Does **not** add or modify any Firefox extension. Retains the original Mozilla-signed **Authenticator by mymindstorm** and direct account synchronization from test.5.
- Adds `FHGLogin.exe` and an independently verified **official Mozilla geckodriver v0.37.1** executable. A local W3C WebDriver session opens the existing account-specific **bundled FHG Firefox**, not the normal system Firefox.
- Once the FHG WFP Guard, dedicated FHG Proxy HTTPS check, Firefox policy check and original Authenticator sync pass, the launcher runs the login helper automatically **before** opening ordinary Firefox with the same profile.
- The helper fills/submits email, password and six-digit TOTP from the encrypted Windows CurrentUser DPAPI account vault. It recognizes the official three-step forms saved by the user: `chatgpt.com` email, `auth.openai.com/log-in/password` and `auth.openai.com/mfa-challenge/...`. All credential writes are preceded by a strict HTTPS hostname/path check; no secret is put into a command line, source file or log.
- Stops instead of bypassing CAPTCHA, Cloudflare challenge, Google/Microsoft SSO, passkey or recovery prompts. Does not retry bad credentials repeatedly.
- Verifies the logged-in ChatGPT interface before treating authentication as successful. Even if the provider changes its interface or challenges the user, it closes its temporary WebDriver process and launches normal Firefox for manual continuation.
- Explicit **Auto login & open** action in the account UI; editing and deleting accounts remain available.
- No synthetic `user@example.invalid` account is submitted to a real login website.
- A login in Firefox with WebDriver ends in the same account profile. Selenium does not install an addon or copy cookies outside the guarded Firefox directory.

## Unchanged (pinned baseline)
- Firefox, mymindstorm Authenticator XPI, FHGProxy/Hiddify, Guard, and their configuration reuse test.5 files byte-for-byte.
- All account archives and existing proxy checkpoints remain intact.
- SOCKS5 local port 12345 and non-browser fail-closed WFP guard behavior remain unchanged.

## Validation limits

Windows Actions checks the FHG DPAPI vault and original Authenticator SQLite integration, compiles all changed Windows executables, tests the strict official-origin classifier and browser-native W3C HTTP communication, then runs an **offline synthetic login form** in the real bundled Firefox and test-installs the complete EXE. The tests do **not** use or verify a real ChatGPT username/password, server-side authentication or CAPTCHA. Form HTML was reviewed from the user's `login.zip` file but no actual account data is bundled.

The user still needs to test a real authorized account on Windows 11 while FHG Proxy is connected. For a provider UI update, check if email/password/TOTP selectors need updating. Never interpret CI success as a real site login result.

## Attribution

Official Mozilla geckodriver v0.37.1 (2026-07-20):
https://github.com/mozilla/geckodriver/releases/tag/v0.37.1

Source branch:
https://github.com/sayyedalimrj/FHG/tree/fix-original-authenticator-storage-20261106
