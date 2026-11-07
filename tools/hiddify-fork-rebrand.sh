#!/usr/bin/env bash
# Personal/authorized development fork only.
# Run from a REAL GitHub fork of hiddify/hiddify-app (not from FHG's repo).
# Keeps upstream core, Flutter UI, Android VPNService and subscription logic.
set -euo pipefail
root="$(git rev-parse --show-toplevel)"
cd "$root"
test -f LICENSE.md
grep -q 'Hiddify Extended GNU General Public License' LICENSE.md
test -f android/app/build.gradle
test -f pubspec.yaml
test -d android/app/src/main
origin="$(git remote get-url origin)"
case "$origin" in
  *github.com/hiddify/hiddify-app*|*github.com:hiddify/hiddify-app*)
    echo "ERROR: must run in your own genuine GitHub fork, not the upstream repo"
    exit 2 ;;
esac
case "$origin" in
  *github.com*/*/*) : ;;
  *) echo "ERROR: expected a GitHub fork as origin"; exit 3 ;;
esac
python3 - <<'PY'
from pathlib import Path

build=Path("android/app/build.gradle")
s=build.read_text(encoding="utf-8")
before='applicationId "app.hiddify.com"'
after='applicationId "com.fhg.android.vpn"'
if before not in s and after not in s:
    raise SystemExit("Upstream applicationId changed. Inspect before editing.")
build.write_text(s.replace(before,after),encoding="utf-8")

manifest=Path("android/app/src/main/AndroidManifest.xml")
s=manifest.read_text(encoding="utf-8")
before='android:label="Hiddify"'
after='android:label="FHG VPN"'
if before not in s and after not in s:
    raise SystemExit("Upstream Android label changed. Inspect before editing.")
manifest.write_text(s.replace(before,after),encoding="utf-8")

# Keep the Kotlin package, AndroidX and Flutter module names unchanged.
# Hiddify's native VPN bridge uses them internally. Only the applicationId
# and user-visible label are changed in this first bring-up patch.
marker=Path("FHG_FORK_CHANGES.md")
marker.write_text("""# FHG native VPN (experimental Hiddify fork)

Based on Hiddify v5.5.5. Upstream remains
https://github.com/hiddify/hiddify-app

Changes:
- distinct Android applicationId com.fhg.android.vpn
- Android display name FHG VPN
- original Flutter application, Android VPNService and native hiddify-core
  remain unchanged to preserve actual upstream connectivity

Important:
- This is an experimental first bring-up patch, **not** a complete
  rebrand; app-store release requires a distinct visual identity and further
  work to meet upstream naming/interface conditions.
- Original LICENSE.md and copyright notices MUST be preserved.
- All modified code and GitHub Actions release must remain in a public
  GitHub fork of hiddify/hiddify-app under upstream license conditions.
- No release signing secrets belong in source control.
- Real phone VPN, DNS, and no-leak tests are required before production.
""",encoding="utf-8")
PY
git diff --check
grep -F 'applicationId "com.fhg.android.vpn"' android/app/build.gradle
grep -F 'android:label="FHG VPN"' android/app/src/main/AndroidManifest.xml
echo "Prepared authorized FHG/Hiddify fork patch; commit this change in your fork."
