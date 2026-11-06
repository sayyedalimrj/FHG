# FHG v0.6.3-rc.1 — keyboard-only autofill, human Continue, safe Cloudflare handoff

**Windows x64 engineering pre-release, unsigned.** This build is intended to address a reported recurring Cloudflare "Just a moment... / Verifying you are human" page at auth.openai.com after an automated login attempt. Do not treat the challenge as a password error, and do not bypass a provider security checkpoint.

## Login behavior

- FHG starts at the user's requested URL: https://chatgpt.com/login.
- On the two allowlisted official HTTPS sites (`chatgpt.com` and `auth.openai.com`) it identifies the visible email, password, or one-time-code input, focuses it, and sends normal WebDriver keyboard input one Unicode scalar at a time. It verifies that the field contains the exact intended value; it refuses to overwrite a value the human has already changed. It does not use JavaScript to assign the input's value or submit a hidden form. No random timing, stealth fingerprinting, provider endpoint impersonation, challenge solving, or security bypass is implemented.
- **The human always clicks the site's Continue button.** No login-stage code calls button.click(), presses Enter, calls a form submit API, or auto-sends credentials. After the user clicks Continue the next official input may be populated automatically. The existing encrypted TOTP vault and original signed Firefox Authenticator remain unchanged.
- On a detected full-page Cloudflare challenge, `Just a moment...`, Turnstile or `Verifying you are human`, the login helper **does not attempt to solve the challenge** or keep repeating it. It records a non-secret manual-browser status and returns a dedicated code so the launcher can stop WebDriver and start ordinary non-WebDriver Firefox using the **same isolated profile**, allowing the user to finish the official security check. A single close/reopen of the automation session is necessary for this handoff; existing account data and saved session cookies are preserved. Handoff is not labeled "LOGIN VERIFIED".
- The normal Firefox handoff resumes via `https://chatgpt.com/login`, not a guessed internal authentication endpoint, because the provider may have short-lived redirect state that cannot safely be reconstructed. The security check might be shown again on the new page. Cloudflare may still reject a proxy exit IP or a browser configuration; FHG cannot guarantee to pass its risk checks.
- If the authenticated ChatGPT UI was already recognized twice, FHG follows its existing verified-success path to normal Firefox. A pending challenge or manual-browser transfer is *not* misreported as a successful login.

## Preserved baseline

The installer is a complete, maximally compressed Inno Setup LZMA2 Ultra64 solid Windows package. The bundled Firefox, original Mozilla-signed Authenticator, Berlin timezone extension, dedicated FHG Proxy and WFP Guard retain their pinned checked binaries. The installation keeps the original application ID and does not wipe DPAPI account vaults, subscription data or per-account Firefox profiles.

## Checks and limitations

Windows CI compiles the launcher/workspace/driver, runs synthetic DPAPI tests, checks Unicode keyboard input and verifies no implicit Continue on a synthetic form, detects a synthetic full-page Cloudflare challenge without solving it, validates the existing Firefox Authenticator and time zone, and installs/hashes the complete resulting installer on Windows. CI cannot test actual ChatGPT sign-in on the user's computer, prove Cloudflare will accept the proxy IP, or certify a leak-free environment. The package has no publisher Authenticode signature.

If the security check also loops in **ordinary Firefox**, inspect the FHG Proxy's working node and avoid rapid repeated retries; test the site's official login in an ordinary, supported browser with standard cookie settings, and use Cloudflare/OpenAI support when appropriate. Do not delete saved accounts or the protected Firefox profile to troubleshoot blindly.
