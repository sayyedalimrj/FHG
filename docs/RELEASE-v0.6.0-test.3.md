# FHG v0.6.0-test.3 — secure sign-in handoff (UNSIGNED TEST BUILD)

This is a Windows x64 engineering preview, NOT a verified production browser. Do not disable Defender, SmartScreen or Windows account protections.

## Differences from test.2

- Expanded account cards to 190px and placed username, password, OTP, and browser actions together.
- Explicit **Copy username** and **Copy password** actions on the manager toolbar and account cards. These retrieve values from the existing CurrentUser DPAPI vault; they do not embed secrets in command-line arguments, installer assets or URLs.
- Timed Windows clipboard management: username after 45 seconds, password after 35 seconds, current OTP after 25 seconds, Authenticator setup link after 60 seconds. FHG only clears the clipboard when its exact copied value still remains; newer unrelated clipboard content is preserved.
- Clears FHG-owned temporary clipboard values when switching accounts or closing Account Manager.
- Retains the signed Firefox Authenticator extension, live OTP display, all-nodes Hiddify default, distinct 12345/27178/26756 ports, per-account Firefox profiles, and fail-closed browser startup.

## Important limitations — NOT complete automation

- **ChatGPT login still needs a user action in the official page.** FHG deliberately does not inject the saved password into an arbitrary webpage or bypass provider account challenges, SSO or CAPTCHA. A successful login persists in the isolated account profile while the site's session stays valid.
- **The existing signed Firefox Authenticator still requires a confirmed text-backup import.** Click the account's "Copy setup link for Firefox Authenticator" action, open its Backup > Import > Import Text Backup view, and paste the standard otpauth URI. This is a secret; do not share it or leave it in screenshots.
- Previous account sessions remain archived in separate profiles; switching accounts does not revoke an existing server-side session.
- User-device tests for a real subscription, login, extension import, and traffic leak containment have not been performed. CI success does not establish these outcomes.
- FHG binaries and setup are **not authenticode signed**. Verify the published SHA-256, do not bypass security dialogs.

## Validation

GitHub Actions compiles Windows Hiddify, the launcher, Guard and Account Manager, runs the synthetic DPAPI/TOTP test cases and Windows installer smoke test, and attaches a downloadable EXE artifact. Test again on Windows 11 with an actual authorized account and subscription, checking SOCKS5 HTTPS only through 127.0.0.1:12345.

Build source: https://github.com/sayyedalimrj/FHG/pull/10
