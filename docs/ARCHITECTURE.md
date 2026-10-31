# FHG secure transport architecture (design, not yet implemented)

## Boundary

```
Firefox fork [untrusted network client]
    | only TCP IPv4 127.0.0.1:12334
    v
Hiddify Core [separate child process, loopback SOCKS5]
    | only configured remote endpoints (routing audited)
    v
Internet
```

Use a separate child process for the Hiddify Core networking engine rather than placing it in the Firefox process. A single user-facing application can still supervise both processes. This permits a Windows Filtering Platform (WFP) application-ID rule to distinguish browser traffic from proxy-engine traffic.

## Enforce fail-closed

1. Build Windows WFP application-scoped outbound filters for **every** browser network-capable executable. Browser may only use TCP to 127.0.0.1:12334; block other IPv4 and ALL IPv6. Do not use a blanket allow rule that can be overridden by higher-priority blocks. Verify effective rules and use fail-closed startup.
2. Install/verify protection *before* spawning Firefox. A browser launched directly must be covered by persistent restrictions. Use separate process identity; no global firewall profile toggles.
3. Verify the local listener is Hiddify Core, not just an arbitrary program on port 12334. Bind on loopback only, guard against port hijacking and ownership races; preferably use authenticated local IPC for lifecycle/control and SOCKS5 for browser traffic.
4. Set browser-local enterprise policy, with SOCKS5 remote DNS and no proxy bypass. Disable WebRTC; audit all browser helpers. Use an independent profile and never change other Firefox installations.
5. Ensure Hiddify Core routing has no unapproved DIRECT outbound route; a browser-only WFP rule cannot detect DIRECT after the bytes enter the core.
6. Failure/crash/restart: never fall back to the system proxy or native sockets; block new connections until core is healthy.

## Release gates

- Automated Windows 10/11 security testing with proxy down, proxy port hijacked, firewall on/off, reboot, process crash, profile update, IPv4/IPv6, TCP/UDP, DNS/WebRTC and helper processes.
- Capture network flows via ETW/WFP + Wireshark while browsing and reconnecting.
- Verify the ordinary installed Firefox and unrelated applications are unchanged.
- Independent security review before claiming zero network leakage.

## Status

This repository currently contains only architecture and policy scaffolding. No WFP filter, Firefox fork, Hiddify Core integration, or Windows runtime test has been implemented yet. CI checks the policy file only; do not treat a green CI run as validation of the security objective.

References: Mozilla Firefox admin policies; Microsoft WFP ALE_AUTH_CONNECT; Hiddify Core source and contributors guide.
