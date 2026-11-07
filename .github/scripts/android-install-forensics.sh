#!/usr/bin/env bash
set -euo pipefail
mkdir -p install-forensics
LOG="install-forensics/adb-install-report.txt"
exec > >(tee -a "$LOG") 2>&1
echo "=== ADB device / Android properties ==="
adb devices -l
adb shell getprop ro.build.version.release
adb shell getprop ro.product.cpu.abilist
adb shell df -h /data
echo "=== Install RC5 FIRST on otherwise clean emulator ==="
set +e
adb install -r /tmp/fhg-apks/rc5.apk
code5=$?
set -e
echo "RC5_INSTALL_EXIT_CODE=$code5"
adb shell pm path com.fhg.android.secure.rc5 || true
adb shell df -h /data
echo "=== Install RC6.1 SIDE BY SIDE (same order as user) ==="
set +e
adb install -r /tmp/fhg-apks/rc61.apk
code61=$?
set -e
echo "RC61_INSTALL_EXIT_CODE=$code61"
adb shell pm path com.fhg.android.secure.rc61 || true
adb shell pm list packages | grep -F 'com.fhg.android.secure' || true
adb shell df -h /data
if [[ $code5 -ne 0 || $code61 -ne 0 ]]; then
  echo "::error::APK installation failed on Android emulator. Error code above."
  adb logcat -d -v time > install-forensics/all-emulator-logcat.txt || true
  exit 1
fi
echo "=== Launch RC6.1 dashboard ==="
adb logcat -c || true
set +e
adb shell am start -W -n com.fhg.android.secure.rc61/com.fhg.android.MainActivity
launch=$?
set -e
echo "RC61_LAUNCH_EXIT_CODE=$launch"
sleep 5
echo "=== Is app still alive? ==="
adb shell pidof com.fhg.android.secure.rc61 || true
adb logcat -d -b crash -v time > install-forensics/crash-buffer.txt || true
adb logcat -d -v time > install-forensics/main-buffer.txt || true
adb shell screencap -p /sdcard/fhg-rc61-home.png || true
adb pull /sdcard/fhg-rc61-home.png install-forensics/rc61-home.png || true
echo "=== Reinstall exact same RC6.1 APK as an update ==="
set +e
adb install -r /tmp/fhg-apks/rc61.apk
reinstall=$?
set -e
echo "RC61_REINSTALL_EXIT_CODE=$reinstall"
if [[ $launch -ne 0 || $reinstall -ne 0 ]]; then
  echo "::error::Install succeeded but launch/update failed."
  exit 1
fi
if grep -E 'FATAL EXCEPTION|Fatal signal|Failed to load' install-forensics/crash-buffer.txt | grep -F 'com.fhg.android.secure.rc61'; then
  echo "::error::App may have crashed shortly after start"
  exit 1
fi
echo "SUCCESS: side-by-side install + RC6.1 reinstall + launch command"
