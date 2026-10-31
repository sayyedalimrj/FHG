# FHG security review — 2026-10-31

**Scope:** Windows FHG v0.2.0-alpha.1 sources, official Mozilla/Hiddify and Microsoft platform documentation, GitHub Actions results. **This is a technical review, not an independent penetration test.**

## Verified

| Component | Evidence | Assessment |
|---|---|---|
| Official browser engine | Mozilla Firefox 159.0 release notes (2026-10-27), official installer signature checked during Windows packaging | Current at build time; repackage promptly for later Mozilla fixes |
| Firefox proxy lock | Mozilla enterprise policy Proxy: Mode manual, Locked=true, SOCKSProxy=127.0.0.1:12334, SOCKSVersion=5, UseProxyForDNS=true, Passthrough empty | Correct per published policy schema; only FHG distribution policy file is modified |
| WFP app-ID egress guard | MS WFP ALE app-ID conditions, CI test with synthetic socket clients | IPv4 allowed to 127.0.0.1:12334; alternate port blocked; IPv6 blocked; unrelated EXE unaffected |
| HiddifyCLI arguments | Official HiddifyCLI guide: run -c path or URL | Basic CLI interface matches FHG code |
| Icon / artifact / signature | Windows CI reports embedded RT_GROUP_ICON, SHA-256 checksum, Authenticode statuses | Icon present; FHG.exe and FHGGuard.exe **NotSigned**; official Firefox signature valid; Hiddify release asset digest verified |
| User interface startup | GitHub Actions Windows x64 WPF main-process smoke test | Launch smoke test only, NOT functional end-to-end network test |

Successful CI run for this tag: https://github.com/sayyedalimrj/FHG/actions/runs/41175138079

Synthetic WFP flow observations from the release build: IPv4 port 12334 permitted, IPv4 alternate port denied with WSA 10013, IPv6 loopback denied with WSA 10013; a non-FHG program remained unrestricted.

## Not yet verified / known residual risks

1. **Full leak capture is missing.** No sustained Firefox + Hiddify real browsing capture of IPv4, IPv6, DNS, WebRTC, UDP, crash/restart, browser helpers, direct-start or proxy-takeover scenarios was performed. Synthetic socket tests alone cannot prove leak-free Firefox.
2. **Core DIRECT routing** is configurable. The WFP guard restricts the Firefox processes, **not** HiddifyCLI. Hiddify may select direct DNS or destination routes depending on the config. The user-selected routing policy and bootstrap traffic are not yet audited or technically forced to proxy-only by FHG.
3. **Port interception / process identity**: the UI rejects wildcard IPv4 binds and obvious foreign process ownership on 127.0.0.1:12334, but there is still a race if a malicious process takes over the socket. The local SOCKS5 transport has no cryptographic channel binding.
4. **WFP coverage**: app-ID rules are path-specific for EXEs discovered in the bundled Firefox directory. The current scheme does not guarantee restrictions on native messaging hosts, external helper executables, injected code with different process identities or processes launched elsewhere. WFP status checks persisted filters, not all potential third-party filter arbitration paths.
5. **No code-signing certificate**: release FHG.exe/FHGGuard.exe are unsigned. SHA-256 and GitHub build attestation establish file integrity/provenance, but are not trusted Authenticode signatures.
6. **Browser security patches require new FHG releases**: Mozilla DisableAppUpdate=true is temporarily set for alpha so background updates don't add executable paths without WFP coverage. Without regular reviewed builds, the browser becomes outdated. A verified in-app updater/guard coordination is still required.
7. **Connection status and latency**: the UI only reports Connected after HTTPS through SOCKS5 succeeds. That verifies a single URL at a point in time, not every proxy outbound route or any guarantee of anonymity.
8. **Cross-OS/hardware**: GitHub Windows Server 2022 tests do not establish equivalence on user Windows 10/11 and all third-party firewall or VPN combinations.
9. **Secrets**: user-provided configuration and subscription links may be saved in the user's local application-data directory and appear in HiddifyCLI arguments. The project does not yet encrypt saved profiles or support secret rotation.

## Priorities before production use

- Force and audit Hiddify routing for browser traffic to a proxied outbound; explicitly enumerate acceptable bootstrap traffic and DNS behavior.
- Replace or harden local SOCKS endpoint ownership with secure IPC/control and port takeover tests.
- Run real browser traffic captures across shutdown, proxy outage, DNS, IPv6, WebRTC, extension helpers, app relocation and update cases.
- Implement signed and guarded Firefox/Hiddify security updates, installer provenance and release signing with the actual publisher certificate.
- Pen-test on Windows 10/11 x64 with firewall enabled/disabled and a second regular Firefox installation.

## Primary documentation

- Mozilla Proxy enterprise policy: https://firefox-admin-docs.mozilla.org/reference/policies/proxy/
- Mozilla DisableAppUpdate enterprise policy: https://firefox-admin-docs.mozilla.org/reference/policies/disableappupdate/
- Mozilla Firefox 159.0 release: https://www.firefox.com/en-US/firefox/159.0/releasenotes/
- HiddifyCLI usage: https://hiddify.com/app/HiddifyCli-guide/
- Hiddify upstream routing discussions: https://github.com/hiddify/hiddify-app/discussions/2144
- MS WFP ALE conditions: https://learn.microsoft.com/en-us/windows/win32/fwp/filtering-conditions-available-at-each-filtering-layer
- MS ALE reauthorization: https://learn.microsoft.com/en-us/windows/win32/fwp/ale-re-authorization

**Conclusion:** a real experimental executable and distribution now exist, with a verified Windows WFP synthetic test. They are **not** verified zero-leak, fully signed or production ready.
