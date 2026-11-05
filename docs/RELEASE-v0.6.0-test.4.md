# FHG v0.6.0-test.4 — Account Management Repair (UNSIGNED TEST BUILD)

Windows x64 engineering preview, **not a verified production browser**. Install only after reviewing publisher and SHA-256; do not bypass Microsoft Defender or SmartScreen.

## Fixes over test.3

- Full **Edit account** dialog for account name, password, Base32 Authenticator key and HTTPS proxy subscription; masks secrets by default.
- **Delete account** with a confirmation; removes saved record from the encrypted DPAPI vault. Previously saved Firefox profile folders are not deleted automatically, and server-side ChatGPT sessions are not revoked. Active running FHG Proxy must be closed before deleting its active account.
- Preserves the stable account identifier and isolated Firefox profile while editing.
- Longer cards (238px), visible per-account Edit/Delete/Setup Authenticator buttons, expanded two-row toolbar and correct selected-account fallback.
- Validates duplicate names, invalid Base32 keys, missing passwords and malformed HTTPS subscriptions before saving; reports sanitized, actionable messages rather than only "FormatException".
- Added Windows tests for modifying every account field, invalid 2FA rollback, deleting active account, and preserving prior browser profiles.
- Continues to use previously published **test.2 Hiddify, Firefox, launcher and Guard binaries unchanged**; only FHGAccountManager.exe is replaced by the newly compiled executable.

## Authentication state

- FHG Accounts generates live 6-digit codes; this **does not provision** the separately signed Firefox Authenticator extension.
- For Firefox Authenticator integration, select "Setup Firefox Authenticator" on the account card; it copies a standard otpauth URI to the clipboard for 60 seconds. Then in the Firefox extension click gear > Backup > Import Backup > Import Text Backup, paste, and approve import. Never leave or share the secret.
- Automatic ChatGPT login cannot be claimed complete for arbitrary accounts, particularly Google/Microsoft SSO or provider challenges. Open the isolated Firefox profile and use the official ChatGPT login. Username, password and TOTP can be copied individually with time-limited clipboard protection; prior successful website sessions persist while still valid.
- Never import a sample `user@example.invalid` account as a real ChatGPT login.

## Validation gates

CI must verify DPAPI/TOTP functional tests, compile the Account Manager on Windows, verify the original installer by pinned SHA256, repackage the original application baseline without modifying Hiddify, and smoke-test the resulting Setup EXE. These tests do not prove live network connectivity, ChatGPT sign-in, or extension import on the user's machine.

Reference source branch: https://github.com/sayyedalimrj/FHG/pull/10
