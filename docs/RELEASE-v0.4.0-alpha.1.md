# FHG v0.4.0-alpha.1 — Original Hiddify GUI + Isolated Firefox (Windows x64)

**Experimental prerelease. The complete Windows bundle is NOT security-certified or proven leak-free.**

## Why this release exists

Earlier FHG WPF screens attempted to reimplement subscription/profile/node management. They were incomplete and could not replace the native Hiddify experience. Version 0.4 **removes that custom UI from the distributed product**.

- **Original, unmodified upstream Hiddify Windows Portable v5.5.5** is bundled. You use Hiddify's own interface for multiple subscriptions, traffic remaining, expiration, node lists, per-node latency tests, node selection and connection status. FHG does not parse or copy your subscription links.
- A minimal **FHG.exe** launcher opens the original Hiddify interface and starts its own isolated Firefox, subject to a verified Windows Filtering Platform (WFP) guard.
- Full original Hiddify GUI and associated Flutter runtime and native files are included. No separate CLI configuration, no need to manually paste subscription links into an FHG UI.
- Mozilla Firefox is downloaded from the official current Windows stable endpoint during the build; Mozilla Authenticode signatures are checked.
- The official Hiddify release ZIP SHA-256 is pinned to the published upstream release digest:
  `b4579d6a9a7b0bb0dedfd2e462ab4cfefc53ec25b56f287cca5edb435f01f627`.
- A separate Firefox profile is used. Existing Firefox installations, browser defaults and normal Firefox profiles are not changed.
- A user Desktop shortcut is created if one does not already exist. Windows Administrator approval is requested only when the app-specific WFP guard needs installation.

## How to use

1. Download the FHG ZIP below and verify SHA-256 from the second asset.
2. Extract the complete package into a permanent folder.
3. Double-click **FHG.exe**. The **original Hiddify GUI** appears; the FHG Firefox window launches after its security guard is verified.
4. In Hiddify, paste/import your subscription via its normal UI and select a server.
5. **Use Local Proxy (not VPN/TUN or global System Proxy mode)** and configure the local SOCKS5 / mixed proxy to bind on **127.0.0.1:12334**, which is the only egress that the FHG Firefox WFP guard is configured to permit.
6. Connect from Hiddify. Offline Firefox tabs remain open, but online requests should fail until the proxy listener is functioning.

## Important security limitations

- The app enforces Firefox browser-only WFP policy to the local Hiddify SOCKS5 endpoint. **That alone does NOT guarantee every Hiddify Core outbound connection uses a remote proxy.** Hiddify itself can route selected domains/direct DNS by DIRECT depending on service and routing rules. Audit Hiddify's mode and routing policies before sensitive browsing.
- The packaged Hiddify GUI is upstream software, not a security-reviewed FHG fork. Its use of Roaming settings may affect another installed Hiddify version on the same Windows account; upstream's portable data handling is not fully isolated in all versions. Existing Firefox is independently isolated.
- Firefox policy denies direct fallback. Browser executable process list and WFP filter effectiveness on each user's Windows still require live validation.
- No sustained Firefox + actual Hiddify end-to-end Windows 10/11 packet capture, crash/kill-switch scenario, DNS/IPv6/WebRTC and port-hijacking test has been completed. Do not treat this build as an anonymity- or privacy-guaranteed browser.
- FHG.exe and FHGGuard.exe remain UNSIGNED unless a genuine publisher certificate is configured in Actions secrets. The original Hiddify and Mozilla executables are not altered or re-signed.
- Firefox in-app updates are disabled in this alpha to avoid introducing new browser helper binaries outside the verified WFP set. Download newer FHG builds promptly for Mozilla security updates. Hiddify updates are not managed by this launcher.
- For Windows 10/11 x64 only.

## Attribution and license

Hiddify application and the UI are the unmodified upstream build:
- Upstream GitHub repository: https://github.com/hiddify/hiddify-app
- Official Hiddify release: https://github.com/hiddify/hiddify-app/releases/tag/v5.5.5
- License and redistribution conditions: https://github.com/hiddify/hiddify-app/blob/main/LICENSE.md

FHG's launcher source is public in this repository. This package is provided free of charge. Do not redistribute this project commercially without reviewing and satisfying Hiddify's additional license conditions.

Mozilla Firefox is provided from Mozilla's official distribution and retains its trademarks, code-signature and licensing.

**This is a functional integration preview, not a certified security/privacy product.**
