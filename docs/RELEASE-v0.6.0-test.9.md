# FHG v0.6.0-test.9 — direct ChatGPT login and persistent Firefox handoff

**Windows x64 unsigned engineering prerelease.** Built from the tested FHG v0.6.0-test.8 baseline. Preserve existing FHG account data, private Firefox profiles, subscription database and installation path.

## User-reported regression addressed

FHG v0.6.0-test.8 could briefly open the ChatGPT homepage and then close its automated Firefox session without ever showing the login form. The automation was waiting for a homepage login button with fragile selectors. This code path exited after twelve seconds without a successful click. It was particularly confusing because geckodriver intentionally closes its temporary Firefox window.

## Changes

- Navigate **directly to the exact requested URL** `https://chatgpt.com/login`. Do not search for a homepage Log in button and do not jump to the fragile `/auth/login` server route.
- Continue to use the original ChatGPT/OpenAI login forms only on HTTPS with trusted hostnames, verifying each credential field exactly before clicking the page's own submit control. No special Enter key, normalization, hidden login API, CAPTCHA bypass or credential logging.
- Recognize that the WebDriver browser is temporary. After automation ends, start a **separate, persistent user Firefox window** in the same existing isolated FHG profile. If automation is incomplete, the durable window opens **`https://chatgpt.com/login`**, not the homepage.
- Wait for the temporary browser's profile lock to clear. Retry a user-window launch only if the Firefox process exits immediately, without deleting, resetting or killing existing user profiles.
- Record only fixed, sanitized stage identifiers such as `LOGIN_FORM_NOT_RENDERED` and `PROVIDER_CHALLENGE` in the local FHG guard-launch log. Credentials, OTP values, page content and browser query strings are never logged.
- Preserve the previously installed and SHA256-pinned Mozilla-signed Authenticator and timezone extensions, `Europe/Berlin` extension preferences, the FHG Hiddify GUI, port 12345 and WFP Guard unchanged.

## Expected behavior

1. Select an account in FHG Accounts and start ChatGPT.
2. FHG verifies the existing FHG Proxy and WFP guard.
3. The temporary Firefox automation navigates to `https://chatgpt.com/login`, and attempts only supported credential stages on verified official origins.
4. If automatic sign-in finishes, the long-lived Firefox window opens ChatGPT. If not, the long-lived Firefox window opens the same direct login page for **manual continuation**; it should not silently disappear.
5. If Firefox itself cannot stay open after bounded retries, FHG shows an explicit local warning instead of silently claiming success.

## Remaining limits

No CI system can claim successful sign-in to the user's real ChatGPT identity; all credentials and TOTP values used by Windows CI are synthetic. OpenAI account methods, third-party sign-in, changes to page markup, CAPTCHA, rate limits, VPN/ISP restrictions and upstream `400 Invalid content type` responses may require manual action. Do not bypass provider security checks. No Authenticode certificate is attached to this engineering prerelease, and live network leak-freeness is not certified.

## Windows CI release gates

Windows CI must compile all modified FHG components; pass the DPAPI vault, proxy workspace, login policy and synthetic Unicode login tests; enforce direct `/login` and persistent-browser fallback source invariants; validate the pinned existing Firefox/Hiddify/Guard, original Authenticator and Mozilla-signed Berlin timezone add-on; complete an offline Windows installer install/hash smoke test; and only then publish test.9.
