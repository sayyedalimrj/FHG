# FHG Proxy-only Hiddify — source patch (experimental)

Based on the pinned upstream [Hiddify v5.5.5](https://github.com/hiddify/hiddify-app/releases/tag/v5.5.5), commit `7804cadac01419f31b1ec7802d9e1be599e66de2`.

## Requested FHG behavior

- Exactly one service mode: **Proxy**, fixed to **127.0.0.1:12334**. Neither VPN/TUN nor System Proxy may be selected.
- The enforcement occurs both in the Flutter GUI and in the Singbox config **after** profile overrides are merged. Old saved settings cannot silently reactivate other modes or ports.
- No WARP/extra-security/Unblocker toggles. The resulting Core chain status is always **off**.
- LAN sharing, remote proxy authentication, transparent forwarding ports and VPN/TUN are disabled in the Core config. Hiddify does not change Windows-wide proxy settings.
- Analytics/crash-reporting is disabled in the build, and the analytics switch is removed.
- Original Hiddify subscriptions, traffic usage, expiration, node list and ping UI stay intact.

## Reproducible build

`packaging/patch-hiddify-proxy-only.py` applies changes to a **clean, pinned checkout** of the upstream Hiddify release. The GitHub Actions `hiddify-proxy-only.yml` compiles the Flutter desktop app with Hiddify's documented Flutter 3.38.5 toolchain and bundles it with the existing FHG v0.4 Firefox and WFP guard. The workflow fails rather than silently building a different upstream version.

## Remaining security issues

- **WFP restricts the FHG Firefox application**, but Hiddify Core itself can still contain DIRECT or DNS/bootstrap routes according to its routing profile. This patch locks the operating mode, not every possible outbound network route; zero leakage has not been proved.
- The Hiddify original GUI remains in a **separate window** beside Firefox. Disconnected proxy means Firefox stays open but its network requests should fail.
- Full packet capture, interruption tests, Windows 10/11 tests, trusted publisher signing and upstream fork/license compliance are still required before a production release.
- All modified Hiddify source is derivative of the Hiddify app. The upstream [extended GPLv3 license](https://github.com/hiddify/hiddify-app/blob/main/LICENSE.md) includes attribution, share-alike, automatic releases, restrictions on commercial use and a requirement for a GitHub **fork** of the upstream app. Merely having the patch in FHG does not establish complete license compliance; do not publish a modified Hiddify binary commercially or as a final public release until these are addressed.

**This is a development build, not a fully tested secure consumer release.**
