# FHG v0.6.0-test.8 — safer ChatGPT login, Germany timezone, immediate first-use setup

**Windows x64 engineering prerelease, unsigned.** Preserve the existing FHG installation directory and DPAPI-protected account vault.

## Changes

- **First launch:** when no FHG accounts exist, the FHG Workspace immediately opens the Windows file picker for the user's four-line local configuration file. FHG Firefox and FHG Proxy shortcuts also open the workspace when no active account is set. Existing accounts, independent Firefox profiles and proxy database are preserved. Cancel leaves the workspace usable; it does not silently select a file.
- **ChatGPT login:** avoid directly navigating to fragile auth form route URLs; use the current site's own visible login link/button instead. After filling the official HTTPS form, check that the input element's actual value **exactly equals** the original account value, then click its visible submit button. The old W3C special Enter key is never inserted. Passwords are not trimmed, normalized, converted or logged. If official login markup is missing, SSO, CAPTCHA or a provider challenge appears, stop automatic submission rather than guessing or retrying.
- **German browser timezone:** use the existing, third-party, **Mozilla-signed Spoof Timezone 0.5.0** extension by Joue (MPL-2.0), installed only into the bundled FHG Firefox via local enterprise policy. Configure its persistent preference to **Europe/Berlin**, automatic IP updates OFF, random zone OFF, on/off ON. Germany uses CET (UTC+1) in winter and CEST (UTC+2) in summer. This does **not** alter the Windows clock, network exit location, local Hiddify, original Firefox, or TOTP timestamp calculation. No privately modified extension is shipped.
- **Security:** pin the signed extension archive by SHA256 and check its manifest identity and Mozilla signature through normal Firefox installation policy. The extension has broad website access, because spoofing site-visible timezone requires it. Use only if you consent to that third-party permission; do not install a copy from untrusted mirrors.
- **Unchanged:** the original signed Authenticator by mymindstorm, patched FHG Hiddify, Firefox binary, WFP Guard, all existing account archives and localhost SOCKS5 port 12345 are reused without modification.

## What the 400 HTML content-type error means

An error of the form **Route Error (400 Invalid content type: text/html; charset=UTF-8)** has also been reported in the official ChatGPT desktop login flow. It is not proof of an incorrectly typed password or OTP. It can reflect an upstream/auth intermediary returning HTML when the identity flow expects a different response. FHG fixes its own fragile navigation and unexpected key input, but **cannot guarantee** that an OpenAI authentication service, upstream proxy or Cloudflare challenge will accept the session.

If the issue remains: use the genuine on-screen login flow, check that your chosen password/Apple/Google/Microsoft sign-in method matches the account, and review the proxy connection and cookie/JavaScript settings. Do not bypass CAPTCHA or provider security checks.

## Windows release gates

Release only when Windows CI succeeds for: old DPAPI vault and automatic FHG subscription flow, compiled FHG Launcher/Workspace/Login, signed-original Authenticator write and readback on a fresh synthetic Firefox profile, signed timezone extension installation and saved Berlin setting, exact synthetic email/password/OTP character round-trip, and an end-to-end installer smoke test with SHA256 integrity checks. These are **synthetic tests**; no real ChatGPT account or private VPN subscription is used on CI.

The release remains a preview until real authorized login and connectivity tests on the user's Windows machine and publisher Authenticode signing have been verified.
