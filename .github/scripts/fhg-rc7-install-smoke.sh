#!/usr/bin/env bash
# Run as one bash process: fail on ANY rejected install, adb failure, or crash.
set -Eeuo pipefail
api="${1:?Android API number required}"
apk="dist/FHG-Secure-Android-1.0.0-rc7-ARM64.apk"
package="com.fhg.android.secure.rc7"
mkdir -p install-proof
report="install-proof/report-${api}.txt"
exec > >(tee "$report") 2>&1

echo "FHG RC7 non-incremental local APK installation validation on API $api"
adb devices -l
adb shell getprop ro.build.version.release
adb shell getprop ro.product.cpu.abilist
adb shell getconf PAGE_SIZE
test -s "$apk"

# Transfer the exact signed APK, then request normal PackageManager file
# installation. This avoids ADB incremental/streamed shortcuts and exercises
# the parser / extraction path more similar to a downloaded APK sideload.
adb push "$apk" /data/local/tmp/fhg-rc7.apk
install_output="$(adb shell pm install -r /data/local/tmp/fhg-rc7.apk)"
echo "FIRST_INSTALL: $install_output"
[[ "$install_output" == *Success* ]] || exit 11
installed="$(adb shell pm path "$package")"
echo "INSTALLED_PATH: $installed"
[[ "$installed" == package:* ]] || exit 12

launch="$(adb shell am start -W -n "$package/com.fhg.android.MainActivity")"
echo "LAUNCH: $launch"
[[ "$launch" == *"Status: ok"* ]] || exit 13
sleep 6
pid="$(adb shell pidof "$package" | tr -d '\r')"
echo "APP_PROCESS_PID: $pid"
[[ -n "$pid" ]] || exit 14
adb logcat -d -b crash > "install-proof/crash-${api}.txt"
if grep -qE "Process: $package|>>> $package <<<" "install-proof/crash-${api}.txt"; then
  echo "FAIL: FHG appeared in Android crash buffer"
  exit 15
fi

# Same signed APK must be updatable in place (detects accidental signing
# mismatch and versionCode errors for the current candidate).
reinstall="$(adb shell pm install -r /data/local/tmp/fhg-rc7.apk)"
echo "REINSTALL: $reinstall"
[[ "$reinstall" == *Success* ]] || exit 16
adb shell pm path "$package"
adb shell screencap -p /sdcard/fhg-rc7-screen.png
adb pull /sdcard/fhg-rc7-screen.png "install-proof/screen-${api}.png"
adb shell rm -f /data/local/tmp/fhg-rc7.apk
echo "PASS: non-incremental local APK install, PackageManager registered, cold launch, live process, no FHG crash, same-key reinstall on Android API $api"
