# FHG Proxy v0.5.1-rc.1 — Windows 11 test release

Experimental, unsigned portable build; keep your original Hiddify installed separately.

## Fixed since v0.5.0-rc.2

- **Hiddify coexistence**: the upstream desktop core was still trying to start its control gRPC on **127.0.0.1:17078**, which is also used by the ordinary Hiddify client. FHG now uses its own **127.0.0.1:27178** control service. The Firefox SOCKS5 proxy remains fixed at **127.0.0.1:12345**. These are different ports serving different purposes.
- **Profile import failure**: if core startup fails, the app now presents an engine-not-ready error instead of the `LateInitializationError: fgClient has not been initialized` stack error. Do not delete subscriptions or checkpoints.
- **English by default** on the first start of this FHG build, irrespective of Windows locale or an inherited preference. Later manual user language changes are respected.
- **Firefox first run**: skip the welcome tour, default-browser checks, and the Firefox Terms/Privacy modal. **Important:** Mozilla documents that `SkipTermsOfUse=true` represents the deployer accepting its terms and privacy notice on the users' behalf. Only deploy when you have authority to accept these; review https://www.mozilla.org/about/legal/terms/firefox/ and https://www.mozilla.org/privacy/firefox/ .
- **Firefox bookmarks**: ChatGPT (`https://chatgpt.com/`) on the visible bookmarks toolbar.
- **TOTP authenticator**: Firefox automatically installs the Mozilla AMO-signed [TOTP by Turistu](https://addons.mozilla.org/en-US/firefox/addon/totp/) extension, which uses the Firefox local password manager, WebCrypto and no site content scripts. The CI attempts to bundle the signed `.xpi` for offline install; if unavailable the official AMO URL is used when the proxy is online. Users manually add their Base32 seeds **inside Firefox only**; the release does not include sample or user secrets. This is an additional 2FA credential store—use a Firefox Primary Password or a separate authenticator for high-value accounts, retain service recovery codes and back up the protected Firefox profile. The supplied sample code has NOT been embedded.
- **Firefox size**: optional installer/updater/telemetry executables are removed without trimming browser engine, AI-site WebAssembly support, necessary media codecs, profile files or runtime libraries. A second **solid LZMA2 `.7z`** archive is provided alongside the standard ZIP; actual byte sizes are printed by CI.
- **Guard UX**: when the Windows Administrator prompt was canceled, explain that the browser intentionally remains closed until the local WFP guard is verified. Installing the guard requires choosing **Yes** in the app and then **Yes** in Windows UAC; do not disable it to work around this.

## Running

1. Extract either the compact 7z or normal ZIP fully into a **new** writable folder outside `Program Files`; do not overlay the previous version.
2. Double-click `FHG.exe`. Allow the UAC prompt to install FHG's application-specific Windows WFP guard once, or Firefox correctly refuses to open.
3. Add your Hiddify subscription in **FHG Proxy** and connect. The normal Hiddify can stay open, but FHG Proxy cannot bind if TCP port **12345** or its internal gRPC **27178** is already taken by something else.
4. In Firefox, click the TOTP add-on icon (after its initial installation), choose to add a key, and type your secret locally. Your one-time codes update every 30 seconds. **Never send authentication seeds or recovery codes to support or chat.**
5. Check `about:policies` in Firefox for applied policies, and `about:addons` for the TOTP extension if it did not appear. The signed extension is installed per Firefox's enterprise policy `Extensions.Install`.
6. If the guard fails, inspect `%LOCALAPPDATA%\FHG\Logs\guard-launch.log`. If subscriptions fail, inspect `Hiddify\fhg_proxy_portable_data\` application logs without sharing unredacted tokens.

## Security and licensing

This prerelease is **not** a complete browser traffic-leak / DNS / QUIC / UDP / WFP bypass audit. A signed Firefox binary does not imply FHG has a trusted publisher certificate. Browser JavaScript remains enabled, WebRTC intentionally disabled, and proxy routing rules can still specify direct outbound behavior. The local SOCKS endpoint is not cryptographically bound to the FHG process.

Upstream Hiddify v5.5.5 (SHA `7804cadac01419f31b1ec7802d9e1be599e66de2`) is subject to the [Hiddify Extended GPLv3](https://github.com/hiddify/hiddify-app/blob/main/LICENSE.md), which additionally requires publishing modified code as a GitHub fork of Hiddify. This requirement has **not been verified as satisfied** for the separate FHG repository; distribution compliance needs further review. The source transformation patches and original license accompany this release. The TOTP extension is separately licensed under GPLv3 (as listed on Mozilla Add-ons); no secret keys are distributed.

The original user's installed Hiddify and Firefox are never modified by this package.
