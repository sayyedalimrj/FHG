# FHG product acceptance requirements

## Non-negotiable user experience

- **Do not close, suspend or kill the browser when Hiddify disconnects.**
- Keep tabs and UI, bookmarks/history, local files, downloads already stored, and browser settings available offline.
- Attempting a new website with the proxy offline should fail with a clear local error, not try a direct connection.
- After Hiddify reconnects, users can retry without closing or reopening the browser.
- No separate Hiddify window, CMD or PowerShell required for normal use. Users interact with a single FHG browser app.

## Security invariants

- All outbound traffic initiated by browser executables, including child helpers, is **restricted to Hiddify's local SOCKS5 endpoint** `127.0.0.1:12334`; no network fallback when the proxy is down.
- Only the separate Hiddify Core process makes remote network connections, and its browser-traffic routes must not unexpectedly use DIRECT.
- Proxy and DNS settings are locked for FHG only. WebRTC and other potential raw-UDP leak paths are disabled or blocked.
- An absent/unverifiable **OS-level network guard** blocks initial launch of network-capable processes. An unavailable **proxy** does not.
- Existing Firefox and other system applications are never modified, blocked or terminated.
- A browser update cannot silently remove the network guard or revert to DIRECT; protection must be verified on every version change.

## Quality targets

- Supported, up-to-date browser engine with timely security fixes. Avoid stale frozen binaries.
- Performant multiprocess browsing, rapid startup and minimal background overhead.
- Offline-ready distribution (no runtime download requirement for first use once package is delivered).
- One desktop shortcut and one product UI; administrative setup only when installing/changing the OS-level security boundary.
- Build and test on Windows 10 and 11 x64.
- Do not call any version "zero leak", "secure final" or "production ready" until the release test plan has passed.

## Status

All requirements above are **targets**, not completed features. The checked-in Firefox policy is an initial defense layer, not a Windows enforcement mechanism.
