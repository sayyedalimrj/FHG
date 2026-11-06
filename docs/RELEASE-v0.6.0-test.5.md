# FHG v0.6.0-test.5 — Original Authenticator direct storage integration

**Windows x64 engineering preview, unsigned.** This version does **not** include or require the custom FHG Authenticator extension. It retains the existing **Authenticator by mymindstorm** (`authenticator@mymindstorm`) and its Mozilla-signed XPI unchanged.

## What changed

- Added the local `FHGAuthenticatorSync.exe` program. It reads the existing DPAPI CurrentUser FHG accounts and writes Authenticator-compatible TOTP records directly to the **existing** Firefox profile's `storage-sync-v2.sqlite` database for extension `authenticator@mymindstorm`.
- Account data is written in the exact `storage_sync_data(ext_id, data, sync_change_counter)` JSON storage format from Mozilla's webext-storage source. No modified Firefox extension or Mozilla signing is required.
- Stable UUIDv4-format keys, `dataType=OTPStorage`, `type=totp`, `issuer=ChatGPT`, account email, base32 seed, SHA1 and 30s period are included.
- Synchronization happens automatically when an FHG account is imported, edited, or activated; the FHG launcher checks the selected profile before opening Firefox.
- Writes are idempotent: no duplicated FHG-managed accounts. Existing unrelated Authenticator entries, account IDs, and signed addon files remain untouched. Removed FHG-managed records are pruned when their profile is next synchronized.
- Before modification, the SQLite database is backed up and encrypted with Windows DPAPI for the current user. The backup is stored under `%LOCALAPPDATA%\FHG\Accounts\AuthenticatorBackups`.
- Refuses unknown schema versions, corrupt SQLite data, invalid quotas, UUID collisions and password-encrypted Authenticator records. Never edits a profile while its Firefox process is holding the profile lock.
- Previous FHG Proxy, Firefox, and Guard binaries are reused byte-for-byte from the pinned test.3 installer. Account Manager and launcher are recompiled and include the new integration.

## Important behavior and limits

- **The only direct-write storage format implemented is Firefox storage.sync.** The extension can alternatively store accounts in storage.local, which uses Firefox's compressed IndexedDB and is a different format. If the existing extension is configured for local storage, writing the Sync DB is insufficient; this version must not be claimed as fully automatic for that profile.
- Before installing, close all FHG Firefox windows. Do not close your regular Firefox or regular Hiddify unnecessarily.
- Master-password-encrypted Authenticator data is not altered or bypassed.
- Firefox account Sync could overwrite local storage.sync updates in other configurations. FHG's isolated Firefox disables Firefox Accounts; ordinary Firefox remains unmodified.
- The user's actual Windows profile and live ChatGPT session have not been accessed or tested. GitHub Windows CI uses synthetic credentials and local test databases only.
- Automatic **ChatGPT login itself is not implemented in this patch**. This release fixes the existing Authenticator account-list issue, not third-party identity challenges.
- Example accounts ending in `example.invalid` are not real ChatGPT identities.

## Verify on Windows 11

1. Close FHG Firefox and FHG Accounts, install test.5, then launch FHG Accounts.
2. Select an account and click **Sync Authenticator**, or activate it. Reopen FHG Firefox.
3. Open the original Authenticator by mymindstorm icon. The TOTP entries should be visible if this profile uses storage.sync.
4. Verify 6-digit codes against the FHG Account Manager without sharing secrets/screenshots, and verify both regular Firefox and regular Hiddify are unaffected.
5. Test a second FHG profile, account edit and deletion. Check that existing non-FHG Authenticator entries survive.

Source: https://github.com/sayyedalimrj/FHG/tree/fix-original-authenticator-storage-20261106
