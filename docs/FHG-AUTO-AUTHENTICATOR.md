# FHG Authenticator & ChatGPT assisted automatic sign-in (engineering preview)

## What now exists
- `native/fhg-authenticator`: a **new separately signed-ready FHG Authenticator Firefox extension**, not an illicit binary rewrite of the original signed Authenticator. Its popup automatically displays every entry in the encrypted FHG account vault and the current 6-digit OTP with a countdown. No account list/seed is manually imported into the extension.
- `native/auth-native-host`: .NET 10 Windows native messaging host that reads the existing Windows DPAPI CurrentUser vault. It returns generated OTP codes to the FHG popup and only the active account's email/password/OTP to recognized official login forms. The TOTP master seed and proxy subscription token are never returned to Firefox.
- `native/fhg-authenticator/login.js`: three automatic form stages identified from the supplied user login capture: `chatgpt.com` email, `auth.openai.com/log-in/password`, and `auth.openai.com/mfa-challenge/*` six-digit OTP. The extension fills and submits fields only when these fields exist, the HTTPS origin and path match, and no CAPTCHA/challenge field is present. It does **not** bypass provider challenges, SSO, CAPTCHAs or rate limits.
- `packaging/FHG-Setup.iss`: registers `HKCU\Software\Mozilla\NativeMessagingHosts\fhg.authenticator` and a native manifest only if the **Mozilla-signed** integrated XPI has been bundled. Uninstall removes only the host registration pointing to its own installation, not saved user profiles.
- `native/original-ui/Program.cs`: prefers the signed FHG integrated XPI when present and retains fallback to the existing signed add-on when it is not. The original add-on's stored data is not deleted.

## Signing gate (required for a usable Firefox release)
Ordinary Firefox does not trust an edited XPI just because FHG packaged it. A newly created extension needs Mozilla AMO signing, including unlisted/self-distributed extensions. The GitHub Actions workflow **fails closed** for packaging integrated Firefox when credentials are absent.

The repository owner must create Mozilla AMO Developer API credentials and set these as GitHub Actions repository secrets (never paste either secret into chat or commit files):

- `FHG_AMO_JWT_ISSUER`: AMO API issuer/key.
- `FHG_AMO_JWT_SECRET`: AMO API JWT secret.

Then run `FHG native Authenticator and trusted login integration` through GitHub Actions (workflow dispatch). It will compile all native components, validate native messaging and browser origin restrictions, request Mozilla's **unlisted** signature via `web-ext sign`, and, only after a signed XPI exists, stage a Windows test installer preserving the Hiddify, Firefox and Guard binaries from the pinned `v0.6.0-test.3` installer.

A successful CI result **without these secrets means source and native binaries built only**; it does not mean an integrated browser installer exists.

## Operational limits
- The sign-in flow was validated against the structure of the supplied static `login.zip` pages, not against a live account session.
- No process here can access the user's installed Windows browser or operate their real ChatGPT session. An actual-device E2E test must validate proxy connectivity, extension installation, available account selection, login and session persistence.
- SSO via Google/Microsoft, password resets, multi-factor methods other than the saved TOTP seed, and new page layouts need manual fallback or a later reviewed integration.
- Sample records such as `user@example.invalid` will never authenticate to ChatGPT.
- Existing Firefox profiles and Hiddify data must not be deleted or overwritten; no secrets should be submitted to GitHub CI.

## Verification
Windows GitHub Actions compiles all three .NET executables and the Inno Setup script with throwaway synthetic data; runs AccountVault/TOTP tests and native messaging framing/allowed-caller/origin tests; runs Node tests of extension message routing. The unsigned source package is for code review only, **not an installable production add-on**.
