# FHG Secure Browser v0.2.0-alpha.1 — Windows x64

> **Experimental prerelease — not security certified. Do not rely on it for anonymity-critical use.**

## Included

- Current official Firefox Windows x64 release at build time, with locked localhost SOCKS5 proxy at `127.0.0.1:12334`, proxy DNS and WebRTC disabled.
- HiddifyCli + Hiddify Core from the upstream Hiddify Windows portable release.
- Updated, dark **WPF desktop interface**: paste or import subscriptions and configurations, Connect/Disconnect, Core/Guard status, HTTPS latency tested over Hiddify SOCKS5.
- **FHGGuard.exe**, an app-scoped Windows Filtering Platform (WFP) outbound filter for the FHG browser folder only. No global firewall profile changes.
- Desktop shortcut; isolated browser profile; offline ZIP; build SHA-256 checksum.

## Verified on GitHub Windows runners

- WPF controller compiled and passed a **startup smoke test**.
- Embedded icon and application metadata checked.
- Official Firefox installer and browser executable Authenticode signatures verified. Hiddify upstream portable archive SHA-256 verified against a pinned digest.
- ZIP archive integrity and required executable files checked.
- Synthetic outbound filtering [Windows WFP test](https://github.com/sayyedalimrj/FHG/actions/runs/41174966562) passed: allowed IPv4 TCP 127.0.0.1:12334; denied IPv4 alternate port and IPv6 loopback; unrelated executables remained unaffected; proxy absence did not permit bypass.

## Unresolved risks and limitations — read before using

- **FHG.exe / FHGGuard.exe are not Authenticode-signed unless repository signing secrets contain a valid publisher certificate.** Build provenance attestation and hashes are **not** publisher certificates.
- Comprehensive Firefox + Hiddify packet capture has **not** been done. These network tests use a synthetic test EXE, not real browser or Hiddify application flows. Do not claim zero network leakage.
- Hiddify Core's route selection can include `DIRECT` destinations or DNS/bootstrap traffic unless the supplied profile is configured and audited appropriately. Browser-only WFP restrictions do not filter the Core process.
- Local listener ownership is checked before reporting Ready, but port hijacking/races are not completely resolved.
- Firefox in-app self-update is **disabled for this alpha** to prevent unreviewed new helper EXEs from bypassing path-scoped rules. You must install newly rebuilt FHG releases promptly for Firefox security fixes; this is not a production update strategy.
- The app does not yet embed the Hiddify UI inside Firefox itself. The WPF controller is one separate window.
- Normal browser launch should not require elevated permissions after the one-time WFP installation. If the guard cannot be verified, launching FHG Firefox is blocked. If Hiddify disconnects, the existing browser window remains open and web requests should fail.
- Do not relocate the extracted directory after guard installation without reinstalling/verifying app-scoped filters.

## How to use

1. Download `FHG-experimental-win-x64.zip` from the assets below and check it against `FHG-experimental-win-x64.zip.sha256`.
2. Extract completely into a stable, writable directory on Windows 10/11 x64.
3. Run `FHG.exe`. Paste a share link, subscription URL, or configuration; or import a local profile.
4. Click **Connect**, then **Open secure browser**. Accept one-time administrative authorization **only to install the FHG-specific WFP policy**.
5. Watch the HTTPS latency indicator; a listening SOCKS port alone does not guarantee tunnel connectivity.

Source and security implementation: [FHG repository](https://github.com/sayyedalimrj/FHG) | [Security architecture](https://github.com/sayyedalimrj/FHG/blob/main/docs/ARCHITECTURE.md)
