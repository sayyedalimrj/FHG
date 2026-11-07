# FHG Android — official Hiddify backend integration

## Why this version exists

RC1-RC8 tried to reimplement Android VPN service, sing-box routing and
native Go bridge and repeatedly crashed on user hardware. This preview
**removes FHG's custom Android VpnService entirely** and uses the
**unmodified, officially signed Hiddify Android app** for all network
tunnelling, subscription parsing, server/location selection, network
monitoring, DNS, and recovery. FHG retains its encrypted four-line account
vault, per-account GeckoView browser and 2FA display.

Upstream: <https://github.com/hiddify/hiddify-app>
Pinned integration tested against source tag: `v5.5.5`, 2026-11-04.
Hiddify Android package: `app.hiddify.com`.
FHG companion package: `com.fhg.android.secure.hiddify`.

This is **not a single-APK Hiddify fork**, not an unauthorized rebranding
of the Hiddify app and not a claim that FHG owns the external VPN.

## User flow

1. Install Hiddify from the official GitHub release (ARM64, v5.5.5).
2. Install the FHG companion APK (separate app).
3. In FHG, select the original four-line account file. Account fields are
   encrypted locally with Android Keystore; FHG sends ONLY the subscription
   HTTPS URL to Hiddify.
4. FHG opens the upstream deep link
   `hiddify://import/?url=<URL-encoded subscription URL>`.
   Hiddify itself displays an import confirmation and fetches the link.
5. In Hiddify, choose one of the subscription servers/locations and press
   Connect. Android shows the VPN permission prompt as usual.
6. Return to FHG. Its browser is enabled only after Android reports an
   active VPN network and an HTTPS 204 probe succeeds over that exact
   VPN Network. Any network change or stale result blocks the GeckoView
   session again.

**No FHG-created VPN, TUN, libbox, custom proxy parser, or fake state
indicator exists in this branch.**

## Security and limitations

- Package visibility is limited to `app.hiddify.com`. The external intent
  is explicitly package-targeted (not broadcast to arbitrary apps).
- If Android reports the VPN owner UID, FHG requires it to match
  the installed Hiddify package. On OEM ROMs returning an unknown UID,
  FHG requires Hiddify to be installed plus a real OS VPN network, but this
  cannot cryptographically identify the tunnel owner.
- The browser checks the OS VPN transport, active Network identity, and
  a fresh HTTPS 204 challenge bound to that VPN Network. No direct
  fallback is allowed. OS-level always-on/lockdown remains the user's
  choice and is not silently activated.
- This does NOT automatically bypass Hiddify's subscription consent or
  Android's mandatory VPN permission dialog.
- It does NOT include Hiddify source code or alter Hiddify's credentials,
  subscription storage, VPN implementation, or app signature.
- Until the Hiddify application and a real subscription are present on
  a physical Android device, end-to-end tunneling remains unverified.
- FHG's GeckoView app and Hiddify are separate APKs, so app size and
  update lifecycle are independent. It is an intentionally honest
  transitional architecture rather than a promised single APK.

## Why not copy all of Hiddify into the FHG repository immediately?

The upstream `LICENSE.md` is **Hiddify Extended GPLv3** and requires
derivatives to be available as a **GitHub fork of hiddify/hiddify-app**,
with attribution, source and automated release requirements, restrictions
on commercial use and distinct app-store branding. An authorized
single-APK derivative should be created from a genuine GitHub fork,
with its real `hiddify-core` and Gradle/Flutter build intact, then
integrate FHG's account/GeckoView functionality; importing the upstream
engine into an unrelated FHG repository does not satisfy that condition.
No such single-APK build is claimed or published by this preview.

## Engineering verification

- CI must compile Android and run Java unit tests/lint.
- CI must verify no `BIND_VPN_SERVICE` in the resulting merged manifest.
- CI must verify it did not bundle the custom libbox Java bridge.
- Signed APK must pass apksigner and Android PackageManager install.
- Release publication must be gated on app cold-launch test.
- Real device integration requires verifying Hiddify's VPN route, DNS,
  IPv4/IPv6 behavior, subscription display, and browser lockout during
  network disconnection before declaring production readiness.

Upstream licenses and attribution remain with their original authors.
