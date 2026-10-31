# FHG Browser v0.3.0-alpha.1 — Subscriptions & Servers Preview

**Experimental Windows x64 prerelease. Not certified leak-free; FHG binaries are not Authenticode signed unless actual repository signing secrets are configured.**

## What's new

- Reworked subscription experience, replacing the single config textbox workflow with three tabs: **Import**, **Subscriptions**, and **Servers**.
- Store multiple subscription URLs and imported local profiles separately. Activate, refresh, or remove profiles; profiles remain on the local device.
- Show real subscription usage (upload + download), traffic remaining, a usage progress bar and expiry when the provider supplies metadata. Missing quota and unlimited plans are displayed explicitly rather than fabricating a balance.
- Understand official Hiddify `subscription-userinfo` and `profile-title` headers, including Base64 titles and optional `total`/`expire`.
- Read subscription metadata through **Hiddify's local SOCKS5** after connection; there is no direct network fallback in the FHG quota request. Automatic refresh checks no more often than every 10 minutes while connected.
- Show node names, types and last test latency from the authenticated localhost Clash API exposed by Hiddify Core; **Test ping** for a selected node and **Select** the outbound using the documented Clash API.
- Remember preferred node per subscription; restore it when the same selector and node remain available.
- Random localhost Clash API port and freshly generated authentication token. The UI requires the endpoint to be owned by its own Hiddify Core process.
- Network guard, isolated Firefox profile, original icon, WPF dashboard and checksum/build security report remain in place.

## Test scope

- The CI pipeline runs a .NET subscription parser/profile store test: standard quota, unlimited/missing total, expiry, Base64 title and persistence.
- The Windows UI is compiled and smoke-tested.
- Synthetic Windows WFP test blocks unwanted IPv4/IPv6 outbound attempts for the protected app without impacting an unrelated executable.
- Bundled HiddifyCLI is checked for the three command-line flags required by the local SOCKS and authenticated Clash API.
- Official browser signature, Hiddify archive pinned digest and release ZIP checksum are verified.

## Important remaining limits

- Subscription data is visible only when the provider supplies quota headers; otherwise the UI says it is unavailable. The provider may reject HEAD/GET or block the proxy, which can prevent refreshing.
- The Servers tab requires the running Hiddify build to expose the local Clash API. Server lists and per-node ping are implemented against the documented API, but **full GUI-to-Hiddify end-to-end interaction is not yet tested on a real user machine**.
- FHG does not yet guarantee that the Hiddify Core route policy never selects `DIRECT` or sends bootstrap DNS directly. Browser-only WFP guard cannot establish that.
- The WFP filter is a path-scoped prototype; full browser packet-capture tests, port-hijack tests, Windows 10/11 system tests and signed publisher updates are still required. **Do not use this alpha for sensitive or anonymity-critical browsing.**
- Subscriptions may include secret tokens. FHG stores profile URLs and imported content within the user's local application data. Do not share that directory.
- Firefox auto-update is disabled for this alpha to prevent new executable paths bypassing the installed guard; install reviewed full FHG releases promptly for Mozilla security fixes.

## Install

Download `FHG-experimental-win-x64.zip` from the Assets below and verify `FHG-experimental-win-x64.zip.sha256`. Extract to a stable folder, run `FHG.exe` and use **Import** to paste a subscription. The new Subscriptions and Servers tabs are available in the main window.

[Source](https://github.com/sayyedalimrj/FHG) • [Security review](https://github.com/sayyedalimrj/FHG/blob/main/docs/SECURITY-REVIEW-2026-10-31.md)
