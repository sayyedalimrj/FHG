# Lightweight FHG MVP scope

## Keep (non-negotiable)

- Current, security-maintained Firefox engine with sandbox, process isolation, HTTPS/TLS checks, safe browsing, certificate validation and security updates.
- Separate supervised Hiddify Core networking process.
- Browser-local locked SOCKS5 and remote DNS.
- Native per-application WFP protection with an explicit `status` verification before networking.
- One normal browser window with tabs, address bar, downloads, bookmarks, extension support, and offline-use UI.
- Isolated Firefox profile; never alter or close the user's existing Firefox.
- Proxy disconnected: remain open and show network failure (no direct fallback).

## Exclude from version 1 UI

- Separate Hiddify dashboard and advanced routing editor: expose just connection indicator, add config, connect/disconnect and clear errors.
- Theme marketplace, ad-based recommendations, in-app news/content feeds, gaming widgets or promotional pages.
- Extra proxy switchers, TUN interface, local HTTP proxy, second VPN service, traffic analytics dashboards.
- Custom auto-updaters built from scratch: plan a signed, auditable browser release/update process using proven tooling instead.

## Keep behind the scenes if required for security

- Native guard installer/status and authenticated Hiddify control.
- Logs that avoid secrets, addresses and personal browsing data; minimize retention.
- UI-independent integration tests, process identity checks, and crash recovery.

## Performance goals (targets, not measured results)

- Avoid extra resident background UI processes; keep one supervised network core process.
- Avoid polling where Windows notifications or Hiddify events suffice.
- Favor direct SOCKS5 transport for browser traffic; no extra TCP relay hops.
- Do not remove Mozilla sandbox, process model, security updates, certificate checks or Safe Browsing just to save memory.
- Performance metrics and actual total RAM/CPU/installer size are not measured yet.

## Build status

Guard C++ source and MSVC build workflow exist. The Firefox fork, Hiddify integration, profile isolation launcher and live network tests are not yet implemented.
