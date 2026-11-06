# FHG v0.6.1-rc.1 — persistent interactive ChatGPT login (Windows x64)

**Unsigned engineering prerelease.** Installation tests use synthetic credentials, not a real ChatGPT account. Preserve the existing installation, account vault, profiles and FHG Proxy subscriptions.

## Fixed: browser vanishes after email → Continue

Earlier releases used a temporary Firefox WebDriver session that exited on CAPTCHA, unrecognized authentication pages, or a timeout, closing the browser **before the human could finish sign-in**. The launcher then attempted to start a normal Firefox even though sign-in was not verified, which could fail with a profile-lock error.

This update changes the lifecycle:

1. Launch the dedicated guarded Firefox under the existing W3C WebDriver session and navigate to the explicitly provided **https://chatgpt.com/login**.
2. On a recognized official email page only, submit the saved email **at most once**. FHG then deliberately hands password, Continue, 2FA, CAPTCHA, passkey, Google/Apple/Microsoft sign-in, and any recovery steps to the human. No automated password submit means no repeated synthetic Continue requests on the known troublesome page.
3. Keep that **same Firefox window open without a login timeout** while manual work continues, including unrecognized or third-party provider pages. Never attempt to bypass a CAPTCHA or use a private authentication API.
4. Show a companion **FHG · ChatGPT login status** window and per-account Workspace status (starting, email submitted, manual input required, CAPTCHA, verification, success, error, user cancel, handoff failure). Dismissing the status window with X minimizes it; only the explicitly confirmed **Stop sign-in** ends an incomplete attempt.
5. Verify the authenticated UI on the exact **https://chatgpt.com** origin on two consecutive checks. **Only then** shut down the temporary driver session, wait for profile lock release and open normal Firefox in the same isolated profile.
6. If the driver dies or the user explicitly cancels, never claim login success or open a normal Firefox as a misleading fallback. On a real success followed by a normal-browser launch failure, report HANDOFF_FAILED separately.

## Preserved components

- Original Mozilla-signed Authenticator and previously enrolled TOTP data, Mozilla-signed Berlin timezone extension, existing per-account Firefox profiles and vault
- Dedicated Hiddify Proxy UI and subscriptions, SOCKS5 127.0.0.1:12345 and FHG Guard
- Maximum practical Inno Setup LZMA2/ultra64 solid compression from v0.6.0
- No secrets, OTPs, page HTML or sensitive URLs in the new per-account status file

## Release gates

Windows CI must pass DPAPI regression tests (including status lifecycle), native launcher/workspace/login compilation, real Firefox synthetic Authenticator and timezone tests, login input/Unicode test, verified unchanged Firefox/Hiddify/Guard binary hashes from the pinned v0.6.0 baseline, and full installer install/hash smoke tests.

## Limits

Real authorized ChatGPT sign-in and live connectivity on the user's own Windows machine are not tested by CI. The success detector depends on verified signed-in account UI controls and may need updating if the ChatGPT web interface changes. In that event the protected WebDriver Firefox remains available for manual use rather than being closed simply because a selector changed. Publisher Authenticode signing and a zero-leak certification are not claimed.
