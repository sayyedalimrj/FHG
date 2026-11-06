# FHG v0.6.4-rc.2 — normal Firefox login, automatic encrypted password saving

**Windows x64 full installer, engineering pre-release, unsigned.**

## User-requested behavior

- FHG verifies the existing dedicated Hiddify proxy, hardened Firefox enterprise proxy settings and WFP guard before opening anything.
- FHG chooses the existing private Firefox profile for the active account. It does **not** automate login, submit forms, click Continue, retry the Cloudflare human check, or attempt to navigate under a WebDriver-controlled online session.
- **Automatic Save without Save prompt:** On the first start for an account, a short *headless and offline* Firefox session calls Mozilla's native `Services.logins` APIs to import the username and password already stored in FHG's DPAPI current-user account vault. Login records for both `https://auth.openai.com` and `https://chatgpt.com` are stored inside that account's own encrypted Firefox NSS Password Manager. This does not visit either website, open a visible tab, save plaintext files, perform a sign-in or expose credentials on the command line.
- On subsequent starts the helper is **skipped** if the non-secret `firefox-password-imported.v1` completion marker exists. Editing an account's username/password invalidates the marker, causing the normal encrypted Firefox password update on next closed-profile launch. Changing only the proxy subscription does not invalidate it.
- If the private Firefox profile is **already open**, FHG does not start another Firefox, open a new tab, delete locks, or attempt a simultaneous password store write. The import can occur on a later launch after closing that browser.
- Finally FHG starts **one normal Firefox** with that account's existing profile at `https://chatgpt.com/login`. The human handles the website login and security checks. Firefox offers its normal autofill feature from the stored credential; **user clicks Continue themselves**.
- If the native Firefox password manager import is unavailable, FHG does **not** claim success; it reports a non-sensitive failure state in the account card and still allows manual sign-in in normal Firefox.

## Security and state

The credential is never placed in a plaintext intermediary file, browser policy, temporary profile directory, command line or diagnostic log. The offline helper communicates exclusively with an ephemeral localhost WebDriver; the browser uses its own NSS-backed Login Manager for storage. Existing Firefox password-manager entries belonging to other usernames are not overwritten. The original DPAPI vault and per-account Firefox profile remain present. Primary Password behavior (if enabled by the user) is owned by Firefox.

The original signed Authenticator and Berlin timezone add-ons, Hiddify Proxy, strict SOCKS5 DNS policy, WFP guard, Windows shortcut flow and maximal LZMA2/Ultra64 installer compression are retained.

## Validation

Windows CI: compile all three native apps; test vault integrity and import-marker invalidation; verify synthetic, encrypted password entries are inserted in native Firefox Login Manager and persist across Firefox restarts **and through the exact headless/offline import mode**; test original signed Authenticator persistence, Berlin time zone and synthetic Unicode input without real credentials; smoke-install and hash-check complete Windows installer. The offline password import may be sensitive to changes in future Firefox NSS Login Manager APIs and may need an update after browser upgrades. Real-account ChatGPT sign-in and provider Cloudflare acceptance cannot be tested by CI and are not claimed.

**Do not uninstall FHG, delete Firefox profiles, or clear the existing Authenticator database when upgrading.** Reinstall into the existing directory.

### RC2 reliability improvement

This revision also waits for the hidden helper's Firefox profile lock to release before it launches the single visible Firefox browser, preventing startup races and accidental false-positive browser-open status.
