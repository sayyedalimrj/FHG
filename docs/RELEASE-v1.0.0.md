# FHG Browser v1.0.0 — Final Windows x64 release

This is the fully packaged Windows 10/11 x64 release. Upgrade in place, without uninstalling the existing FHG profile or deleting account vault, Hiddify subscriptions, Authenticator storage, or earlier Firefox data. The installer is unsigned; a verified SHA-256 file accompanies the official release.

## Final requested icon and shortcut fixes

- **FHG Firefox desktop icon:** custom original orange/blue browser artwork with a small **GPT** badge. Its shortcut **Target is the actual bundled `Firefox\firefox.exe`**, never `FHG.exe`. The `-no-remote -profile "<per-account private profile>" https://chatgpt.com/login` arguments select the active account. No GeckoDriver and no login/CAPTCHA automation are involved in normal browsing.
- **FHG Accounts icon:** distinct turquoise multi-person silhouette, embedded in the Account Manager executable and on desktop/Start Menu shortcuts.
- **FHG Proxy icon:** distinct violet networking nodes for desktop/Start Menu; the original pinned Hiddify/FHGProxy binary stays untouched. The Proxy shortcut still targets `FHG.exe --proxy-only` so subscription and proxy management keep their existing FHG workflow.
- Desktop and Start Menu Firefox shortcuts are dynamically updated to the **new active account profile** when the user imports/selects a new account. Old FHG-owned desktop links are repaired; unrelated user-owned links are left alone. The first-run installer creates only Accounts/Proxy if the account vault is empty; the Firefox direct shortcut is created after account import.
- The Windows launcher no longer overwrites a direct Firefox shortcut with `FHG.exe`. All icon assets are multi-resolution embedded Windows ICOs, built at release time.

## Unchanged successful v0.6.4 behavior

- A single native, non-automated Firefox window for login; the user personally clicks Continue, completes password, 2FA, and any service security checks.
- Existing username/password imported **automatically without a Save button** from FHG's DPAPI current-user vault into the encrypted, per-account Firefox NSS Login Manager through a brief **offline/headless** session. User can still change saved passwords later through the account workspace.
- Pinned Mozilla-signed Firefox Authenticator and Germany timezone add-on, original dedicated FHG Hiddify Proxy and subscriptions, locked SOCKS5 proxy/DNS enterprise policy and existing WFP Guard.
- Maximum Inno Setup LZMA2/Ultra64 solid compression, original AppId for in-place upgrade, safe preservation of all original user data.

## Security distinction for the new *direct* Firefox shortcut

A shortcut that directly executes `firefox.exe` **cannot run the FHG.exe launcher's guard/proxy preflight**. This is an intentional result of the requested direct target. **First start FHG Accounts / Start ChatGPT once** to initialize and check Hiddify and the per-app WFP guard. Afterwards the direct shortcut uses the bundled Firefox and its enforced SOCKS5-with-proxy-DNS policy. If the proxy is disconnected, browsing is expected to fail closed rather than silently fall back to a direct connection. This behavior does **not** certify full network leak prevention; a machine-wide live network test is still needed.

## Verification

Windows CI must pass the unchanged vault/proxy/login tests, compile both apps and offline encrypted-password importer, verify the original signed Authenticator and timezone add-ons, run exact headless NSS password persistence checks, generate **three distinct multi-resolution ICO files**, use Windows COM to inspect actual desktop shortcut .lnk **Target**, Arguments and IconLocation for two different active accounts, smoke-install and hash-check all assets in the final full installer, and publish only if every gate passes. Real ChatGPT user sign-in is not automatically tested and is not claimed. Firefox Taskbar/executable branding remains Mozilla's original; the customized GPT badge is on the FHG desktop/start shortcut icon and its FHG launcher.
