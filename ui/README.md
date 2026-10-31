# FHG WPF UI v0.3.0-alpha.1 — Subscription Management Preview

A native modern WPF Windows 10/11 desktop interface built as a self-contained .NET 10 single-file x64 executable. No Electron or WebView runtime is needed.

## Three tabs

**Import**: paste a Hiddify subscription URL (HTTPS), proxy/share-link text, or JSON/YAML config; import a local file. On Connect, it is stored as a distinct local profile.

**Subscriptions**: choose between saved profiles. Real `subscription-userinfo` headers provide upload, download, total, remaining and expiry. Optional/missing limits are treated as unlimited or unavailable. Names can come from `profile-title` or the safe subscription hostname. Quota is fetched using HTTP **through the Hiddify SOCKS5 proxy**; no UI-direct HTTP fallback.

**Servers**: when Core is running, FHG queries the authenticated Clash-compatible loopback API to display actual outbound names, types and last-known delays. Ping a node using the Core delay API; select a node through the selector PUT API. Per-subscription node selection is persisted locally and restored if the node/group still exists. If Hiddify does not expose an API, the pane reports it as unavailable instead of fabricating nodes.

## Security boundaries

- The local Clash API port is randomized per app process, protected by a 256-bit random bearer token and checked against the Hiddify-owned Windows listener PID before use.
- SOCKS5 proxy must be owned by FHG's Hiddify child process and bound specifically to 127.0.0.1:12334. Wildcard-bound listeners are rejected.
- The FHG browser uses a separate Firefox profile and the persistent WFP browser-only egress guard. It stays open if the proxy stops.
- Neither the Core's outbound routing policies nor all browser processes have been leak-tested; production confidentiality is **not** certified.
- Secret subscription URLs/config content are stored on the user's device in local AppData. Do not upload these files to GitHub.

## CI and checks

`dotnet run --project tests/SubscriptionChecks.csproj -c Release` validates quotas, unlimited plans, encoded titles and profile lifecycle. Windows Actions compiles/runs the WPF app, tests a synthetic WFP IPv4/IPv6 restriction, validates bundled upstream binary signatures/digests and checks the Hiddify CLI command-line flags.

`dotnet publish ui/FHG.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-ui`

This is a prerelease. Functional user-subscription traffic tests across all provider formats and route-safety tests remain open.
