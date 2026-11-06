# FHG v0.6.2-rc.1 — smart, single-window ChatGPT sign-in

Windows x64, unsigned engineering prerelease. This is an incremental fix based on the pinned v0.6.0 full Windows installer; it retains the existing Hiddify Proxy, WFP Guard, original Mozilla-signed Firefox Authenticator and Germany timezone extension.

## Fixed from user screenshot of v0.6.1-rc.1

- **No separate login status popup.** The previously created WinForms LoginProgressWindow was removed from the project. The account card in FHG Workspace already displays non-sensitive progress; no extra dialog overlays the login form.
- **Automatic email, password and 2FA.** The earlier flow intentionally submitted email only and immediately switched to manual. This update detects the *visible* form control on the current official HTTPS ChatGPT/OpenAI page instead of relying only on a URL path and narrow email selector. It sends each stage no more than once, validates the exact value, uses the page's actual submit control, and uses native WebDriver mouse pointer actions for password Continue. Fresh 30-second TOTP codes are generated from the existing encrypted account vault.
- **Manual intervention only when required.** CAPTCHA, unknown steps, SSO to other identity providers, an inaccessible form, an unrecognized form, stalled transitions or a server-side 400 are left to the human in that same browser. The automation does not bypass challenges, guess credentials for external origins, repeat unsuccessful submissions or close the browser just because a timeout elapsed. Monitoring resumes after the user completes the step.
- **Single startup tab.** Initial setup tabs from the original signed Firefox Authenticator and Spoof Timezone extensions are closed after their preferences/import are processed. The original Firefox startup tab is reused for https://chatgpt.com/login, instead of leaving New Tab and extension options open.
- **Strict completion.** The automation browser is disposed and a normal Firefox session is started with the same profile only after two consecutive detections of the authenticated ChatGPT UI at the official chatgpt.com HTTPS origin. An incomplete sign-in never counts as success.
- **Status in Workspace.** Starting, email submitted, password submitted, TOTP submitted, CAPTCHA/manual, verifying and verified success are represented in a per-account file with no credentials, cookies or URLs. This is separate from the account's ACTIVE selection.

The network guard and no-direct-bypass policy remain unchanged, as does the maximally compressed LZMA2/ultra64 solid installer.

## Validation and limitations

Windows CI tests synthetic browser actions, existing Authenticator, time zone behavior, account vault and full installer file integrity, and checks source invariants for no popup, startup-tab cleanup and automatic credential stages. It does not have a real user's credentials and **cannot attest that OpenAI will accept the actual login**. Real sign-in, CAPTCHA and any upstream HTTP 400 response must be verified on the user's Windows machine.

The installer is **unsigned**. Keep all existing FHG data and install into the same directory rather than uninstalling or clearing profiles.
