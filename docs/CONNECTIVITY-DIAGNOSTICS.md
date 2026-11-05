# FHG connectivity investigation — no new public release before evidence

## Current technical findings

**The previous CI success only established Windows compilation, synthetic WFP port restrictions, and ZIP/7z integrity. It did *not* establish a working user subscription, native gRPC handshake, proxy-node connection, or outbound traffic from the installed user's network.** Never equate build success with connected software.

There are **three different local services** on a normal Windows installation:

| Purpose | Normal Hiddify | FHG after isolation |
| --- | ---: | ---: |
| Local mixed HTTP/SOCKS proxy | 12334 | **12345** |
| Native core gRPC control | 17078 | **27178** |
| Clash management API | 16756 | **26756** |

In prior FHG builds the Clash API **remained on 16756**, so two running Hiddify-derived clients could collide despite independently configured browser and gRPC ports. The source patch now locks the FHG Clash port to 26756 (including profile overrides). This is a concrete defect fix, **not yet proof of why a particular user subscription failed**.

Upstream Hiddify's `HiddifyCoreService.init()` previously swallowed a core setup error after displaying a brief toast. A partially initialized GUI could therefore appear usable even while profile parsing could not reach `fgClient`. A listener on 27178 is necessary but not sufficient evidence that profile imports work.

## User-safe checks (Windows)

1. Extract a fresh, authentic FHG package in a writable folder. **Do not overwrite** the old folder and **do not delete** any original Hiddify profile.
2. Start `FHG.exe`, not `Hiddify/FHGProxy.exe`. FHG requests Administrator approval **only for FHGGuard.exe** if its Firefox-specific WFP protections are absent. Firefox itself is not run elevated.
3. Open PowerShell in the extracted FHG folder. Run the supplied **read-only** script `FHG-Diagnose.ps1`. It checks ports, process owners, code signatures, Guard status and outbound HTTPS through explicitly specified proxy ports. It saves a report to the current user's Desktop.
4. Interpretation: 
   - **No 27178 listener owned by FHGProxy** → native core failed to start or bind. Inspect the sanitized app/core log errors and the DLLs.
   - **27178 present, no 12345 after pressing Connect** → inspect active subscription/config and core start error; a startup listener alone does not mean any node is connected.
   - **12345 present but `curl --proxy socks5h://127.0.0.1:12345 https://example.com` fails** → proxy outbound/route/DNS/node failure, not a Firefox bookmark problem.
   - **12334 works but 12345 fails** → original Hiddify works; FHG core/config/route needs independent debugging.
   - **Both work but Firefox fails** → investigate Mozilla locked proxy policy and FHGGuard separately.
5. If the subscription host is blocked before FHG has a working node, the FHG launcher can *ask* to use an already running, **trusted** local proxy at 12334 strictly for subscription bootstrapping. This does not make the new proxy active, and it should never be selected for someone else's untrusted proxy. Direct import of locally saved supported configs is another option.
6. The real subscription URL may include a bearer token. Never paste it into GitHub issues, chat, screenshots or diagnostic reports. Do not share real TOTP seeds.

## Signature status

Windows CI on 2026-11-05 logged `FHG.exe = NotSigned`, `FHGGuard.exe = NotSigned`, and `No publisher signing certificate in Actions secrets`. The original user's Firefox retains its upstream Mozilla signature; **that is not a certificate signing FHG or the rebuilt FHGProxy.exe**. Recompiling or renaming the Hiddify executable invalidates any vendor code signature. The optional script `packaging/sign-publisher.ps1` can now sign all three FHG-native executables only when the owner has legitimately configured a publisher certificate and password in GitHub Actions.

A public release is now additionally gated behind **`FHG_RELEASE_APPROVED=yes`** and valid Authenticode signatures for `FHG.exe`, `FHGGuard.exe` and `FHGProxy.exe`. CI and test artifacts are available for engineering review, but an unsigned, connectivity-unverified package must not silently be promoted as a trusted customer release.

## About admin rights and desktop shortcuts

Launching Firefox permanently as Administrator is unsafe and unnecessary. Instead the launcher requests UAC once for the guard helper if needed; after that, ordinary launches work without an elevated browser. Shortcut creation now logs COM errors instead of silently swallowing them, repairs earlier FHG shortcuts while preserving unrelated shortcuts, and creates **FHG Browser** and **FHG Proxy** desktop entries. Both route through the launcher.

## Firefox add-ons / future node control

The currently bundled Authenticator extension is **independently signed** by its publisher. Editing its popup to change the add flow would invalidate that signature. Users can pin it using Firefox's Extensions menu → gear/ellipsis → **Pin to Toolbar**. A custom FHG Authenticator with the requested one-step UI requires building and signing a separately reviewed Firefox extension and planning secure private storage/backup of seeds.

A separate Firefox extension for FHG node names, latency and switching could read the localhost Clash API (now 26756), but it should not be distributed until the API has authentication, origin/permissions auditing, and an AMO-signed extension. Unauthenticated HTTP control APIs must never be exposed on LAN or to general web pages.

## Release decision

This change intentionally creates a **diagnostic/source pull request, not a new public release**. To claim the connection issue resolved, require a local end-to-end user test: subscribe/import → parse → select node → Connect → 12345 owned by FHG → HTTPS via SOCKS5 succeeds → Firefox ChatGPT loads → disconnect and fail closed. Confirm repeated startup and independence from normal Hiddify.
