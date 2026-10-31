# FHG WPF UI alpha

A clean, offline-capable WPF Windows 10/11 desktop interface. Built with .NET 10 self-contained single-file Windows x64, embedded icon and without bundled WebView/Electron.

Features: multi-line paste and import, share links/URL/raw JSON, separate SOCKS5 HTTPS latency probe, status cards, native core process supervision, isolated Firefox profile, per-app WFP guard validation, and per-user Desktop shortcut.

Important:
- The `HiddifyCli run -c` argument accepts a profile path or URL. Plain JSON/text pasted in the UI is written under the user's local application data before running. JSON text is syntax-checked; subscriptions and share links are interpreted by Hiddify Core. That DOES NOT guarantee every imported format is accepted by Hiddify.
- `Latency` is time to fetch a small HTTPS endpoint **through SOCKS5**; it is not ICMP ping or the core's per-node URL test.
- FHG's Core process must bind TCP 127.0.0.1:12334. The UI refuses to report Connected when a different process owns the port.
- Hiddify Core can use DIRECT routes according to config; that concern is not solved by UI or browser-only WFP filtering.
- No claim of no leaks or final sign-off until comprehensive Firefox process and Hiddify route traffic capture testing.
- The WPF controller is intentionally separate from the browser engine for current alpha builds. A future version may integrate controls inside the browser toolbar.

To build: `dotnet publish ui/FHG.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-ui`.
