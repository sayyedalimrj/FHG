# FHG architecture — always-open browser, fail-closed network

**Status: proposed architecture, not a validated implementation.**

```text
FHG browser window — tabs remain open even if proxy fails
  Firefox browser + child/helper processes
      | process-scoped WFP restriction: TCP IPv4 ONLY 127.0.0.1:12334
      | no direct DNS / UDP / IPv6 / alternate loopback destinations
      v
  Hiddify Core SOCKS5 listener (separate supervised process)
      | route-selected, audited proxy outbound; no unintended DIRECT
      v
  Internet
```

A single product/UI can contain multiple internal processes. A separate Hiddify Core process is deliberate: if the same executable owns both the browser and the proxy's external connections, a path-scoped WFP policy cannot distinguish them. The user sees one browser app, not two programs.

## Normal connection lifecycle

- Browser UI is always available. Connection control and settings are local and do not require internet access.
- If guard protection is installed, run the browser with the isolated FHG profile even if the proxy is disconnected.
- The browser config is permanently locked to SOCKS5 on `127.0.0.1:12334`; do not auto-detect system proxy or fallback to DIRECT.
- If the proxy listener is missing, online navigation reports a proxy/connection failure inside the tab; **do not close Firefox**.
- On Hiddify reconnect, requests can be retried and tabs refreshed without relaunching Firefox.
- If the WFP **security guard itself** is absent, compromised, or unverifiable, **do not launch the browser's network-capable process** until protection can be reinstated. This distinction is different from ordinary proxy disconnection.

## Security boundary (must be built and tested)

- Scope WFP outbound filters to the absolute paths of FHG browser/network/helper executables only. Audit all child processes and update/replacement paths. Ordinary installed Firefox and other apps must be unaffected.
- At WFP ALE_AUTH_CONNECT_V4/V6 block connections **except** IPv4 TCP destination `127.0.0.1:12334`. No IPv6 egress, localhost bypass to arbitrary services, DNS, QUIC or UDP from browser-originated processes.
- Install filters **before any browser networking** and enforce policy even when the browser is opened directly. Rules may need one-time admin permissions; normal launches should not ask for elevation.
- Verify the **effective** policy, not just filter insertion. Consider additional Windows products, policy conflicts and other WFP layers. Do not claim the lack of leakage from a successful API call.
- Hiddify Core owns the socket. Prevent port hijacking / foreign listeners; verify process ownership and use authenticated local control IPC. The SOCKS transport itself should not rely on unauthenticated lifecycle commands.
- Ensure Hiddify Core routes browser traffic through the intended proxy outbound, with no unwanted DIRECT fallback, bypass or DNS resolution outside the intended Hiddify path.
- Keep browser profile, Firefox policies, application ID, processes and upgrades isolated from an ordinary Firefox installation.
- No general Windows Firewall profile changes. WFP can be active even when Defender Firewall profiles are disabled; distinguish them during diagnostics.

## Security vs performance

- Use the **current supported upstream Firefox release** and timely security patches. Disabled updates without a replacement update process would be unsafe.
- A pure SOCKS5 TCP gateway cannot carry direct browser UDP; QUIC/HTTP3 and WebRTC may be unavailable. That is an explicit compatibility tradeoff, not a hidden bypass. HTTPS over TCP and HTTP/2 continue to work.
- Do not change global system proxy, DNS or other users' app networking.

## Test/release gates (not yet passed)

1. Proxy up: browser network flows only to Hiddify loopback endpoint; proxy core outbound inspected for DIRECT, DNS and unexpected routes.
2. Proxy down/crashed/restarted: browser UI remains responsive, new external requests **fail** and no outbound browser packets escape locally.
3. Egress tests: IPv4, IPv6, direct DNS, WebRTC, HTTP3/QUIC, helper processes, extensions, updates, captive portal checks.
4. WFP tests: firewall profiles on/off, system reboot, rule tamper, process spawn, direct browser EXE start, program relocation and update.
5. Multi-browser test: another existing Firefox installation works normally and has no FHG policy/rule changes.
6. Independent packet capture (ETW/WFP + Wireshark) and review of the enabled WFP policy.

There is **no general guarantee** of literally zero leakage without a verified, functioning OS-level enforcement boundary and system-specific tests. An administrator or compromised kernel can bypass ordinary app-scoped rules.
