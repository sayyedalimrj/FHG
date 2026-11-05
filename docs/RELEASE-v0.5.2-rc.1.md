# FHG Proxy v0.5.2-rc.1 — coexistence, profile import and startup repair

**Windows 10/11 x64 · experimental prerelease · no verified publisher signature.**

## Technical changes

- Independent native FHG Proxy GUI, mutex and portable profile directory. No changes to the installed Hiddify or installed Firefox.
- FHG internal gRPC control: **127.0.0.1:27178** (not vendor Hiddify's 17078). FHG mixed SOCKS5: **127.0.0.1:12345**.
- Remote profile import is bounded to 40 seconds. Connection failures surface a clear, redacted message rather than an unbounded "Adding profile..." modal. Local config import is still available.
- When starting FHG Proxy from **FHG.exe**, if a separate trusted local proxy is listening at **127.0.0.1:12334**, the launcher can ask for explicit consent to use it **only to fetch FHG subscriptions while disconnected**. This is optional; accepting sends subscription URLs (which may contain tokens) through that local proxy. No direct fallback is allowed once the user chooses a proxy. Firefox never uses this bootstrapping path.
- Startup of the browser requires the path-scoped WFP guard to pass verification. If **FHG.exe is already elevated** with Administrator rights, it installs/repairs the guard without requesting a redundant second UAC prompt. If UAC was declined, the browser remains closed (fail closed). Running `Hiddify/FHGProxy.exe` alone **does not** install the guard.
- Firefox opens **ChatGPT** on startup, has ChatGPT in its bookmarks and pins it to Firefox Home Top Sites on a fresh FHG profile. Browser UI and initial FHG Proxy locale default to English.
- Replaces the old manual-generate TOTP add-on with the **Mozilla-signed Authenticator by mymindstorm** extension. A forced-installed, SHA-verified offline copy is bundled. Click its toolbar icon to add an account **once**, then its popup displays automatically updated, timed TOTP codes and supports copying. The add-on is authored by an independent developer, not OpenAI/FHG; consult its permissions and security policy.
- The previous TOTP extension is not forcibly removed from existing user profiles, to avoid destroying authenticator seeds. Migrate only when you have verified backup/recovery codes; remove it manually afterward if desired.
- Keeps the full signed Firefox runtime and critical codecs. Both normal ZIP and solid LZMA2 compact 7z are published with SHA-256.

## Running safely

1. Verify the file's SHA-256 against the release, then extract into a new private writable folder; **do not overlay old files**.
2. Start **FHG.exe** from the extracted root, **not** `Hiddify/FHGProxy.exe`.
3. You may choose to **Run as administrator** for the first guard installation. Windows SmartScreen may still warn about unsigned FHG executables. Do not disable Windows security or use a bypass script; a validated publisher code-signing certificate and Windows reputation are the real long-term solutions.
4. Confirm guard installation. If WFP status does not verify, Firefox must remain closed. Check `%LOCALAPPDATA%\FHG\Logs\guard-launch.log` and Windows Security protection history.
5. If a separate trusted Hiddify runs its mixed proxy on 12334, you may opt in to using it for subscription bootstrap. This does **not** give FHG a VPN or system-wide proxy capability.
6. If profile import is stuck, cancel the operation, check whether the subscription server is reachable, and try importing a local supported profile text. FHG logs live under its dedicated `Hiddify/fhg_proxy_portable_data` folder; redact subscriptions and account secrets before sharing.
7. In Firefox, confirm `about:policies`, `about:addons`, ChatGPT startup and the Authenticator toolbar icon. For a fresh FHG profile, Firefox Home/New Tab also pins ChatGPT.
8. Add accounts inside the Authenticator popup. A valid TOTP seed must stay **private**. Never include real seeds in tickets/screenshots; rotate any that were previously exposed and store recovery codes separately.

## Scope and limitations

CI checks validate Windows compilation, checksums, archive integrity and synthetic IPv4/IPv6 app-scoped WFP protection. **They do not prove** on-device profile connectivity, firewall interoperability, DNS/UDP/QUIC leak freedom, cryptographic binding of the local proxy port to the expected process, or SmartScreen reputation.

The native FHG apps are unsigned unless a publisher certificate has been configured in the signing workflow. Do not claim the warning is removed by renaming or packing files.

Upstream code: Hiddify v5.5.5 (pinned commit `7804cadac01419f31b1ec7802d9e1be599e66de2`), Firefox and the independently published Authenticator extension. Hiddify's Extended GPLv3 terms require a GitHub fork and attribution for modified distributions. Before broader redistribution, ensure this requirement is satisfied; merely keeping patches in FHG is not a complete compliance determination.

Firefox's enterprise `SkipTermsOfUse` option means the deployer accepts its terms/privacy notice on behalf of users. Use this policy only with appropriate consent and authority.
