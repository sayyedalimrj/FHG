# FHG Browser v0.6.0-test.1 — Windows x64 test installer

**This is the complete one-file installer for local Windows testing, NOT a certified release. The executables are currently UNSIGNED, and SmartScreen may show a publisher warning. Verify download and source before installation. Never disable antivirus or system protections.**

This package is generated exclusively by the **patched Hiddify v5.5.5 GUI** workflow, **not** by the retired FHG Secure Browser WPF controller that used port 12334. 

Included:
- Modern `FHG-Setup-Windows-x64-0.6.0-preview.exe`, with destination folder picker, isolated Firefox, patched FHGProxy, WFP guard, and account manager.
- Separate shortcuts for **FHG Firefox**, **FHG Proxy**, and **FHG Accounts**.
- Local four-line file chooser (account label/email, password, Base32 TOTP master seed, HTTPS subscription URL); DPAPI CurrentUser encrypted account archive.
- FHGProxy-only core service on 27178, Clash API on 26756, and SOCKS5 on **127.0.0.1:12345**. Existing original Hiddify can continue using 17078, 16756 and 12334.
- Subscription import attempted from Hiddify native code. Browser opens **only after** a successful HTTPS check through SOCKS5 12345; no direct fallback.
- Each FHG account has its own Firefox and Hiddify user-local data; original Firefox and Hiddify profiles are not modified.
- Signed third-party Authenticator extension pinned to browser toolbar. Adding an Authenticator seed and logging into ChatGPT remain secure user actions; credentials are not injected into ChatGPT.

**Testing and limitations:** CI verifies build, source invariants, synthetic Hiddify behavior, DPAPI, TOTP, WFP smoke tests, installer extraction and file integrity. CI **cannot verify your real subscription**, node, network path, ChatGPT login or absence of every leak. A prior user's FHG v0.5.2 build failed to expose SOCKS5 12345, so this version specifically needs end-to-end testing. Do not use it for anonymity-critical tasks until independently audited.

On the first launch, approve UAC only to install/repair the network guard. Firefox/Proxy should not run elevated. Use the official installer, not any archived `FHG-experimental` controller archive.

**Privacy warning:** the four-line input file contains full account credentials and a long-lived TOTP master secret. Keep it private, do not upload it to GitHub/chat, and do not reuse real secrets in screenshots. The locally encrypted account vault can be read by software with the same Windows-user privileges. ChatGPT sessions may expire; switching an archived Firefox profile does not guarantee automatic sign-in.

**Rollback:** uninstall through Windows Apps; review your `%LOCALAPPDATA%\FHG\Accounts` archive separately. The installer does not erase unrelated Hiddify, regular Firefox or old FHG account data. Keep backups and recovery codes.

Build source: [FHG](https://github.com/sayyedalimrj/FHG), review via [PR #10](https://github.com/sayyedalimrj/FHG/pull/10). Hiddify source patch is included with this release. Source and redistribution are governed by the upstream licenses.
