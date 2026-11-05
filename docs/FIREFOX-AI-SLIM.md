# Firefox AI-focused safe slimming for FHG

FHG targets **ChatGPT, Gemini, Claude, Copilot and similar interactive AI websites**. The goal is reduced background functionality and cleaner UI **without breaking JavaScript, WebAssembly, WebGL, file/image/PDF upload, audio codecs, TLS/certificates, cookies, storage and downloads**.

## What is actually removed

The full FHG Windows ZIP is repackaged from the previously Windows-tested Hiddify/guard bundle. The script [trim-firefox-safe.ps1](../packaging/trim-firefox-safe.ps1) removes only these optional Firefox installation/maintenance programs:
- `Firefox/uninstall/helper.exe`
- `Firefox/maintenanceservice.exe`
- `Firefox/maintenanceservice_installer.exe`
- `Firefox/default-browser-agent.exe`

Measured against the archived build from 2026-11-05: around **2,004,944 bytes (1.91 MiB) of installed size**, saving around **754,095 bytes (0.72 MiB) in the older ZIP compression level**. Actual final ZIP delta depends on compression and may differ.

These files are installer/maintenance helpers, not Gecko/XUL runtime or the Hiddify network core. Disabling Firefox's internal auto-update was already an FHG alpha restriction. FHG still requires new full releases to receive Firefox security fixes.

## What is disabled through official Firefox enterprise policies

- Mozilla Firefox account/sync UI, default browser agent and default-browser prompt.
- Default bookmarks on a new isolated profile and New Tab advertising/content.
- Search-engine suggestions and Mozilla sponsored/online suggestions while typing.
- Telemetry and Firefox Studies.
- Firefox's own unsupervised in-app automatic updating, to ensure new binaries can be audited by the app-scoped WFP guard.
- Existing FHG traffic routing remains fixed to SOCKS5 127.0.0.1:12334 with remote proxy DNS and no bypass list.
- WebRTC remains disabled in this privacy-focused alpha until a voice-compatible proxy-only configuration is tested against IP/DNS leakage.

These policies generally simplify startup/user interface and background activity; **they do not physically shrink a DLL or reliably reduce page RAM by a large amount**.

## High-impact components that MUST stay in the safe build

Measured extracted Firefox size in the 2026-11-05 bundle: ~355.7 MiB (Firefox ZIP compressed ~132.7 MiB).
- `xul.dll` ~171.7 MiB
- `browser/omni.ja` and `omni.ja` ~98.8 MiB combined
- `dxcompiler.dll` ~23.1 MiB
- `onnxruntime.dll` ~11.4 MiB
- `mozinference.dll` ~3.4 MiB
- Other Gecko files, codecs, certificates, DLLs and font fallback.

Do **not** delete `xul.dll`, `omni.ja`, `nss3.dll`, `libGLESv2.dll`, `dxcompiler.dll`, `mozavcodec.dll`, `gkcodecs.dll` or the installed security/runtime files. Also do not disable site isolation/Fission, safe browsing, WebGL, JavaScript, WebAssembly, cookies or service workers. Deleting `onnxruntime.dll` or `mozinference.dll` could reduce size further (~14.8 MiB combined), but can break modern Firefox built-in functionality or runtime initialization and is **not done** without explicit end-to-end regression tests.

## Voice and microphone

Web ChatGPT Voice uses browser microphone access; other AI apps may depend on WebRTC. The existing FHG policy sets `media.peerconnection.enabled=false`, which **might block or impair voice calls**. The safe profile remains text/images/docs focused. A voice-enabled mode must first validate its WebRTC network traffic through Hiddify; enabling WebRTC without leak checks is not a responsible proxy-only default.

## Actual verification and remaining limitations

- Policy JSON validity and official Firefox binary signature can be checked in CI.
- Tests inspect Gecko/XUL/media runtime file presence after trimming.
- A `firefox.exe --version` smoke test is not equivalent to full ChatGPT/Gemini/Claude sign-in, uploads, voice, rendering, and session testing on Windows user hardware.
- The FHG guard's **scope and Hiddify Core DIRECT routing limitations remain unchanged**. Slimming does not prove leak-free browsing.
- For more meaningful total ZIP size reductions, consider replacing the 49 MiB extracted .NET self-contained launcher with an independently verified native launcher; this would affect packaging size rather than Firefox's runtime size and is **not implemented here**.

[Build pipeline](../.github/workflows/firefox-slim-offline.yml) | [Mozilla enterprise policy reference](https://firefox-admin-docs.mozilla.org/reference/policies/) | [Mozilla website](https://www.firefox.com/)
