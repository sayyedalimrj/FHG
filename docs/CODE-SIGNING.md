# FHG — free code-signing and release security

**Current status (2026-10-31): experimental/unsigned. Not security-certified.**
The native controller and WFP guard compile in GitHub Actions, but a successful build or provenance attestation does **not** prove a leak-free network path.

## 1. What the repository can do today — no paid certificate

The Windows build workflow now:

- builds `FHG.exe` and `FHGGuard.exe` on a GitHub-hosted Windows runner;
- checks the Mozilla Firefox installer and extracted firefox.exe Authenticode signatures;
- assembles the offline experimental browser with HiddifyCLI/Core;
- creates `FHG-experimental-win-x64.zip.sha256` for the exact packaged ZIP;
- creates a GitHub Artifact Attestation for the ZIP on non-PR runs; and
- uploads the experimental package and checksum as a workflow artifact.

To verify an artifact **that came from a run containing the attestation step**:

```bash
gh attestation verify FHG-experimental-win-x64.zip --repo sayyedalimrj/FHG
```

To check the SHA-256 on Windows:

```powershell
Get-FileHash .\FHG-experimental-win-x64.zip -Algorithm SHA256
Get-Content .\FHG-experimental-win-x64.zip.sha256
```

Compare the digest strings. The checksum by itself is not authenticated; verify the GitHub attestation/source of the checksum. GitHub attestation **is not** Microsoft Authenticode code signing and does not make the EXEs trusted publishers in Windows.

## 2. Free Authenticode signing: SignPath Foundation

Application: https://signpath.org/apply

Rules and technical integration:
- https://signpath.org/terms
- https://docs.signpath.io/trusted-build-systems/github

SignPath Foundation does not automatically approve applications or issue a certificate to new repositories. The **publisher** on approved signatures is SignPath Foundation, not the project owner's name.

### Prerequisites / blockers for FHG

- [ ] Choose and add an appropriate OSI-approved open-source LICENSE with the owner's authorization. A public repository without a LICENSE is not sufficient.
- [ ] Make an actual publicly available release in the same distribution format to be signed; currently the repo has CI artifacts but no GitHub Release.
- [ ] Document software functionality, installation, uninstall/WFP-rule removal, and system modifications/elevation in the README and the release page.
- [ ] Publish any applicable upstream license notices for bundled Mozilla Firefox/Hiddify files and check the upstream licenses.
- [ ] Enable MFA for every maintainer and configure source-code review and signing-approval responsibilities.
- [ ] Configure an approved trusted GitHub build, SignPath project and policy, and repository origin verification.
- [ ] Publish a tested, maintained release. **Do not claim leak-free** until the documented runtime tests pass.

**Sign only binaries built from FHG's own source**, such as `FHG.exe` and `FHGGuard.exe`. Preserve Firefox's upstream digital signature. Do not re-sign HiddifyCLI/Core or other upstream binaries as though FHG authored them.

### When approval is granted

1. Install and authorize the SignPath GitHub App for this repo; set up a SignPath organization, project, artifact configuration and release-signing policy.
2. Give the signing process **only** the GitHub-hosted-built native EXEs in a signing artifact; do not send the entire re-packaged Firefox/Hiddify bundle for binary re-signing.
3. Put `SIGNPATH_API_TOKEN` in GitHub Actions secrets and the non-secret organization/project/policy IDs into Actions variables.
4. Integrate `signpath/github-action-submit-signing-request@v3` using the artifact ID returned by `actions/upload-artifact@v4` or later. Restrict signing to an audited, reviewed release path on the trusted branch and require explicit signing approval.
5. Verify the returned EXEs with `signtool verify /pa` and `Get-AuthenticodeSignature`. Assemble the **final** package with the approved signed EXEs; hash and attest that final package **after signing**, then publish.
6. Never publish a release described as *signed* if either native EXE fails signature verification; never check a private key or .pfx into Git.

**Important:** The existing `packaging/sign-publisher.ps1` supports a different optional publisher-provided PFX method. A SignPath Foundation signing workflow requires the SignPath service and cannot be enabled by inventing a certificate or secret.

## 3. Release-security blockers independent of signing

A valid digital signature does not stop browser traffic leaks:

- The WFP guard has a synthetic localhost integration test, but not a complete desktop packet-capture test for Firefox/Hiddify on Windows 10 and Windows 11.
- The current guard does not cryptographically bind the local SOCKS listener to the Hiddify process: socket ownership checks may race.
- Hiddify routing can choose `DIRECT` independently of Firefox's SOCKS setting. An outbound audit/enforcement mechanism is required to guarantee traffic through proxy nodes.
- Verify fail-closed behavior on IPv4, IPv6, DNS, UDP/WebRTC, disconnect/reconnect, controller failure, guard removal/tampering, and privilege boundaries.
- Pin and audit downloaded dependency versions and hashes; the current Firefox download URL is mutable (`firefox-latest`).
- Provide a signed, verifiable update process for Firefox security fixes.

**Release gate:** until these issues are resolved by real tests, publish only as **EXPERIMENTAL — NOT VERIFIED LEAK-FREE**, not as a secure production browser.
