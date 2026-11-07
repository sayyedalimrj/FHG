#!/usr/bin/env bash
# Android install + native VPN start smoke, no real user accounts.
# Run with bash on the GitHub Actions Android emulator runner.
set -Eeuo pipefail
api="${1:?API level}"
pkg="com.fhg.android.secure.rc8"
release="dist/FHG-Secure-Android-1.0.0-rc8-ARM64.apk"
debug="dist/FHG-RC8-debug-test.apk"
mkdir -p smoke-proof
exec > >(tee "smoke-proof/report-${api}.txt") 2>&1

echo "TEST_API=$api"
adb shell getprop ro.build.version.release
adb shell getprop ro.product.cpu.abilist
adb shell getconf PAGE_SIZE
test -s "$release"
test -s "$debug"

# First exercise the EXACT signed release APK, not a fake debug substitute.
adb push "$release" /data/local/tmp/fhg-rc8-release.apk
installed="$(adb shell pm install -r /data/local/tmp/fhg-rc8-release.apk)"
echo "SIGNED_RELEASE_INSTALL=$installed"
[[ "$installed" == *Success* ]] || exit 21
adb shell pm path "$pkg" | grep -F "package:"
launch="$(adb shell am start -W -n "$pkg/com.fhg.android.MainActivity")"
echo "$launch"
[[ "$launch" == *"Status: ok"* ]] || exit 22
sleep 4
test -n "$(adb shell pidof "$pkg" | tr -d '\r')" || exit 23
adb shell pm uninstall "$pkg" | grep -F Success

# DEBUG-ONLY fixture executes the real VpnService/Go JNI entry path.
# Never install debug builds or test credentials on a user's device.
adb install --no-streaming "$debug" | grep -F Success
adb logcat -c
adb shell am broadcast --include-stopped-packages \
  -n "$pkg/com.fhg.android.DebugVpnSmokeReceiver" \
  -a com.fhg.android.DEBUG_SEED
sleep 3
adb logcat -d -s FHG-SMOKE:I '*:S' | tee "smoke-proof/seed-${api}.txt"
grep -q SEEDED_SYNTHETIC_PROFILE "smoke-proof/seed-${api}.txt" || exit 24
# Emulator-only pre-consent. End-user production VPN still requires user
# approval through VpnService.prepare().
adb shell appops set "$pkg" ACTIVATE_VPN allow
adb shell am start -W -n "$pkg/com.fhg.android.MainActivity" | grep -F 'Status: ok'
sleep 14

adb shell am broadcast -n "$pkg/com.fhg.android.DebugVpnSmokeReceiver" \
  -a com.fhg.android.DEBUG_STATUS
sleep 1
adb logcat -d -s FHG-SMOKE:I FHG-VPN:E '*:S' | tee "smoke-proof/native-start-${api}.txt"
adb logcat -d -b crash > "smoke-proof/crash-${api}.txt"

# Every fatal Go panic / SIGABRT is a blocker even though the UI now lives
# in another process and must remain alive for crash reporting.
if grep -q -E "Process: $pkg:vpn|>>> $pkg:vpn <<<|Process: $pkg|>>> $pkg <<<" \
    "smoke-proof/crash-${api}.txt"; then
  echo "FAIL: native or UI crash during Connect"; exit 25
fi

main_pid="$(adb shell pidof "$pkg" | tr -d '\r')"
vpn_pid="$(adb shell pidof "$pkg:vpn" | tr -d '\r')"
echo "UI_PID=$main_pid VPN_PID=$vpn_pid"
test -n "$main_pid" || exit 26
# Strong gate: native process must survive and TUN must be established
# even when the deliberate synthetic example.com route is unreachable.
test -n "$vpn_pid" || exit 27
grep -q -E 'STATE=(CHECKING|CONNECTED|RECONNECTING) UP=true' \
    "smoke-proof/native-start-${api}.txt" || {
  echo "FAIL: VPN native startup did not reach actual Android TUN";
  exit 28;
}
adb shell screencap -p /sdcard/fhg-rc8-smoke.png
adb pull /sdcard/fhg-rc8-smoke.png "smoke-proof/screen-${api}.png"
echo "PASS: release installed, debug synthetic Connect ran, UI+VPN processes survived and TUN_UP was observed on Android $api"
