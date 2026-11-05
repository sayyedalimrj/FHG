# FHG first-run local setup — engineering preview

**This feature is NOT production-ready. Do not distribute to end users before full Windows end-to-end testing, valid publisher signing and independent review of its treatment of credentials.**

## User-supplied text file

A user can select a local UTF-8 text file containing exactly four lines:

```text
USER_NAME_OR_EMAIL
PASSWORD
BASE32_TOTP_SEED
https://subscription-provider.example/sub?token=REPLACE_ME
```

**Use fake sample values when testing. Never upload this file to GitHub, ChatGPT, bug reports or logs.** This plaintext format puts all account authentication factors together and is not recommended for real-world deployment. An encrypted, user-local credential manager should replace it before production distribution.

FHG currently reads the file ONLY in the user's running program. It validates the four-line layout and HTTPS subscription, but **does not use, store, log or transmit the ChatGPT password or TOTP seed**. The only value passed to Hiddify is the subscription URL, written to a temporary user-local ticket in `%LOCALAPPDATA%\FHG\InitialSetup\`. The ticket contains no ChatGPT password, 2FA seed or account username. The user owns the original input file; FHG does not automatically delete or shred it.

The name on line 1 may be used as a local display label in a later, audited iteration. In this preview it is not used for ChatGPT or the Authenticator extension; do **not** promise zero-click login.

## One-time flow

1. Start `FHG.exe` normally for FHG Firefox. An already elevated launcher only installs/verifies WFP and creates two differently branded shortcuts; it exits rather than launching an elevated browser.
2. On the first unelevated launch, a local file picker is shown. Confirm consent. Cancel keeps manual Hiddify usage available.
3. FHG creates a randomly named temporary subscription URL ticket. The ticket path—not its contents—is passed in `FHG_INITIAL_SUB_FILE` to the child `FHGProxy.exe`. No account password or seed crosses this boundary.
4. FHG Proxy, after its database and Hiddify Core initialize, reads the ticket, calls the upstream `ProfileRepository.upsertRemote(url)` pipeline (download → parse → validate → store), waits for an active profile, then uses the native connection notifier to connect automatically. It writes a no-secret `IMPORTED` or `FAILED` receipt and deletes its temporary ticket.
5. The parent launcher requires a successful import receipt and a real HTTPS response from `https://example.com/` **through** `socks5://127.0.0.1:12345`. There is no direct fallback in this check. If either stage fails, Firefox does not open, the temporary ticket/receipt are cleaned up and the user is instructed to diagnose the proxy.
6. After both stages succeed, FHG creates a no-secret `setup-complete.flag`, opens the isolated Firefox profile with ChatGPT, and uses the preinstalled, signed Authenticator extension. The user then signs in on the genuine ChatGPT website and adds the TOTP key through the extension's own verified UI.

A successful HTTPS probe **does not prove** the user can sign in to ChatGPT, and does not prove zero DNS/UDP leaks. The two software shortcuts must continue to point to the permanent launcher `FHG.exe`.

## Why the launcher does not delete itself

Deleting `FHG.exe` would break both FHG Firefox and FHG Proxy shortcuts, as well as later Guard verification. The one-time **temporary subscription ticket** and receipt are deleted; the permanent launcher remains. A conventional Setup-only executable could be removed with an ordinary uninstall after completion, but self-deleting executables are unnecessary and may increase antivirus suspicion.

## Security and privacy notes

- Never include real user passwords, OAuth tokens, TOTP seeds or subscriptions in build artifacts, command line arguments, screenshots, issue comments or source code.
- A TOTP seed is a long-lived authentication credential, not a one-time code. Loss of the seed compromises future codes. Rotating a seed and backing up recovery codes are the right responses to accidental exposure.
- The Mozilla-signed third-party Authenticator extension has its own storage APIs and security lifecycle. Rewriting its storage or injecting a seed from the launcher without a reviewed supported interface is not performed here.
- Browser automation of the ChatGPT login page is not part of this preview. Sign-in can use SSO, CAPTCHA, passkeys, account-specific security challenges or changing 2FA prompts, and should proceed through the provider's official page.
- After the user completes login, Firefox can remember approved credentials using its standard Password Manager, if enabled. Do not bypass platform defenses, auto-accept login permission prompts or ship an extension that reads ChatGPT page credentials.
- This feature MUST NOT be released without an independent security review, full end-to-end test on a Windows computer, a signer-valid Authenticode certificate for all native FHG components, and compliance with Hiddify's redistribution license.

## How to diagnose the earlier PowerShell execution-policy error

Use the packaged `FHG-Diagnose.cmd` (read and trust its content before running) or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\FHG-Diagnose.ps1"
```

Only the spawned process has a policy override; no machine-wide `Set-ExecutionPolicy` is performed. Domain-enforced PowerShell policies may still block it. This diagnostic does not upload the text-file credentials or modify firewall rules.

## Required acceptance tests

- Test with fake four-line input and a local test HTTP(S) subscription, then with a user-approved real subscription.
- Original Hiddify and isolated FHG must run simultaneously without collisions at SOCKS 12334/12345, gRPC 17078/27178 and Clash API 16756/26756.
- Valid and invalid subscription URLs must produce `IMPORTED` / `FAILED`, with no secrets in logs.
- Verify HTTPS through SOCKS5 12345 and prove Firefox fails closed when FHG core disconnects.
- Reboot and relaunch, verify no more UAC after initial successful Guard installation at unchanged path.
- Ensure first-run flags are only written after successful import *and* HTTPS check; repeated invocation must not duplicate profiles.
- Confirm the existing original Hiddify profile, normal Firefox profile, passwords and unrelated desktop shortcuts are unaffected.
- Confirm Authenticator is visibly pinned and that the user can perform secure setup inside its normal UI.
