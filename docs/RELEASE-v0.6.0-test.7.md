# FHG v0.6.0-test.7 — One-click workspace, Hiddify proxy import, original Authenticator UI

**Unsigned Windows x64 experimental release. Do not bypass Windows Defender or SmartScreen. No real account credentials, 2FA seeds, subscription tokens or private user sessions are included in CI.**

## Fixed usability and proxy startup

- Replaces the cramped eleven-button FHG Accounts screen with a new clear workspace: **Start workspace**, **Open FHG Proxy**, **Add account**, edit/delete and per-account controls. 2FA codes remain live and visible; a HTTPS-through-SOCKS connection test is built in.
- The account selected in FHG Accounts becomes the active DPAPI-protected user account and keeps its independent Firefox profile.
- On every FHG launcher start, including the **proxy-only** shortcut, reads the active account's subscription from its Windows DPAPI vault, compares a non-secret fingerprint with the last successfully imported subscription, and generates an account-specific short-lived subscription ticket as needed. No more extra import action in Hiddify.
- When changing accounts or modifying a subscription, FHG first tries to close **only its own verified FHGProxy.exe** normally and waits for the reserved local ports. On a stuck process it may terminate that same FHG-owned process tree; regular Hiddify.exe and the user's normal Firefox are not touched. The process is started again with the new subscription ticket.
- When a matching FHG Proxy instance is already running, the proxy-only shortcut attempts to bring its GUI to the foreground. Otherwise Hiddify is launched with the existing account.
- The optional use of the regular local Hiddify port 12334 **for subscription download only** requires a one-time opt-in that is saved locally. The FHG browser remains pinned to its own SOCKS5 port 12345.
- After import FHG requires an `IMPORTED` receipt and successful HTTPS through its own SOCKS5 port, then remembers the subscription fingerprint to avoid re-importing it on each start.
- No changes to existing FHGProxy, Hiddify/sing-box, Guard, Firefox, or the original signed `Authenticator by mymindstorm` XPI.

## Original Authenticator fix

- Removes the old SQLite/IndexedDB patching action from the new user interface and removes the launcher startup block that incorrectly presented `Authenticator storage needs review`.
- In the local browser automation session, opens the **existing signed Authenticator's own extension page**, uses its Firefox `browser.storage` API for the configured Local/Sync area, merges only missing FHG-managed records, and reads them back to verify persistence. This does not patch the XPI or modify SQLite/IndexedDB directly.
- Windows CI must demonstrate a synthetic account written to the signed original add-on and recognized after closing and reopening Firefox, with zero duplicate additions, before this release is published.
- A profile with a passphrase-encrypted Authenticator may require unlocking the extension. The importer will not bypass a master password.
- The FHG vault is kept encrypted via Windows CurrentUser DPAPI. No seed, password or subscription URL is written to a WebDriver log or process arguments.

## Automatic ChatGPT login

- Retains the official-domain three-stage email, password and TOTP autofill for password-authenticated ChatGPT accounts.
- Stops at SSO, passkeys, CAPTCHA, security prompts or changed forms, leaving the usual isolated Firefox available. A successful synthetic form test cannot prove a live ChatGPT login.

## How to test

1. Close **FHG Firefox**, **FHG Accounts** and **FHG Proxy**, not your original Firefox/Hiddify.
2. Install test.7 over the previous FHG directory, keeping the existing DPAPI vault and profile data.
3. Open **FHG Workspace**, select the account, and press **Start workspace**. If you only need Hiddify GUI, press **Open FHG Proxy**.
4. Confirm FHG Proxy imports its HTTPS subscription and its SOCKS5 HTTPS check succeeds. Open the original Authenticator icon and verify your account appears with matching 6-digit codes.
5. If a real account cannot log in because of provider challenges, use the visible official login page. Do not screenshot or share the 2FA secret.

Windows build success means only synthetic CI verification. An actual end-to-end subscription and ChatGPT account verification on your PC is still required. An unsigned preview is not recommended for distribution.

Source: https://github.com/sayyedalimrj/FHG/tree/fix-original-auth-storage-api-20261106
