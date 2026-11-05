# FHG Browser 0.6.0 RC — Windows 10/11 x64

**One-file installer candidate. Not approved for public distribution yet.** The user-provided v0.5.2 diagnostic proved the original Hiddify SOCKS5 12334 is healthy but FHG's SOCKS5 12345 was not listening even though FHG core gRPC 27178 was. A successful CI build alone does not prove this issue fixed. Do not describe this build as connected or production-safe until an on-device subscription import and HTTPS SOCKS5 test succeed.

## Installation

- `FHG-Setup-Windows-x64-0.6.0-preview.exe` uses a normal, modern Inno Setup wizard. The user selects the destination folder. Per-user installation avoids requesting Administrator for installing app files.
- Installed app folder contains `FHG.exe`, `FHGGuard.exe`, `FHGAccountManager.exe`, `Firefox/`, `Hiddify/`, and `licenses/`; no raw Python source or debug symbols. The installer creates **FHG Firefox**, **FHG Proxy**, **FHG Accounts** desktop and Start Menu shortcuts.
- After installation, the Account Manager starts. Users import a local UTF-8 text file containing 4 lines: username/label, password, Base32 TOTP master seed, and HTTPS Hiddify subscription URL.
- The Account Manager encrypts the account archive with **Windows DPAPI CurrentUser** and stores it beneath `%LOCALAPPDATA%\FHG\Accounts\vault.dpapi`. It does not store passwords or master TOTP seeds in the installation folder. DPAPI is not a cross-device backup format; users must keep their normal recovery codes somewhere safe.
- Each account has separate Firefox profile `%LOCALAPPDATA%\FHG\Accounts\ACCOUNT_ID\FirefoxProfile\` and Hiddify database `%LOCALAPPDATA%\FHG\Accounts\ACCOUNT_ID\HiddifyData\`.
- Selecting a profile asks to close existing **FHG** (not ordinary Hiddify) if switching, preserving old profiles. Previous ChatGPT sessions are isolated, not forcibly logged out remotely. One-click re-entry works **when the stored browser session is still valid**; otherwise sign in normally.
- Account Manager can replace an account's subscription and show its current 6-digit TOTP from the locally DPAPI-encrypted secret. The account password and seed are never sent to Hiddify or prefilled into ChatGPT by the installer.
- The first-run handoff sends only a temporary file with the subscription URL to FHG. The native Hiddify repository downloads, validates, parses and saves it; the program then requests connection and independently verifies HTTPS via socks5://127.0.0.1:12345 before opening Firefox at chatgpt.com. **No DIRECT fallback.**
- The Windows UAC prompt is only for installing/repairing the app-scoped FHGGuard.exe rules. Neither FHG Proxy nor Firefox should run permanently with Administrator privileges.

## Functional release gates

1. Windows build, tested local private file parser and DPAPI account archive, correct RFC6238 TOTP, source/patch invariants, Windows WFP smoke tests, one-file installer compilation and silent install into a custom folder.
2. Real Windows device evidence: ordinary Hiddify 12334 still works independently; FHG native control 27178, Clash API 26756 and SOCKS5 12345 operate without port collisions; import actual approved subscription; connect node; curl through FHG SOCKS5 returns HTTPS 200; Firefox ChatGPT opens; disconnect fails closed.
3. Real publisher certificate signs and validates **FHG.exe, FHGGuard.exe, FHGProxy.exe, FHGAccountManager.exe, and the Setup.exe itself**. Firefox retains Mozilla's embedded signature; it does not confer trust on our rebuilt binaries.
4. Hiddify Extended GPLv3 fork/source distribution compliance and Authenticator extension permissions reviewed.

## Known limits

- No unattended login to ChatGPT, password injection, automated CAPTCHA/SSO/passkey bypass, or automatic transfer of TOTP seed to a third-party extension. Users log in on the genuine ChatGPT website and can optionally use Firefox Password Manager for future sign-ins.
- The archived password and TOTP seed are protected against casual disk access using DPAPI CurrentUser but remain available to programs running as the same Windows user. OS account security, disk encryption and endpoint protection still matter.
- Initial plaintext four-line source remains in the user's control. It should be deleted **by the user** after backup and account recovery methods are verified; deleting a file cannot guarantee secure wiping on SSD.
- Existing FHG portable data and the installed ordinary Hiddify are not deleted or overwritten. Uninstallation does not automatically erase DPAPI account records, credentials or Firefox sessions; wiping these is a separate explicit action.
- Firefox's older bundled release disables automatic browser updates. A tested automatic security-update workflow is required before shipping this as a long-lived consumer browser.
- A valid publisher certificate cannot be copied from Mozilla's Firefox or rebuilt Hiddify binaries. GitHub Actions artifacts are engineering test outputs and do not guarantee SmartScreen acceptance.

**Do not publish a public Release or distribute credentials until these gates pass.**
