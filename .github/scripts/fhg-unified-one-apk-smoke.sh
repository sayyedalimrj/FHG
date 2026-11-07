#!/usr/bin/env bash
set -Eeuo pipefail
APK="dist/FHG-Hiddify-UNIFIED-x64-TEST.apk"
PACKAGE="com.fhg.android.unified"
test -s "$APK"
echo 'Installing one combined VPN/browser APK from device-local file'
adb push "$APK" /data/local/tmp/fhg-one.apk
out="$(adb shell pm install -r /data/local/tmp/fhg-one.apk)"
echo "$out"
[[ "$out" == *Success* ]]
adb shell pm path "$PACKAGE" | grep -F package:
echo 'Cold-launch FHG workspace'
launch="$(adb shell am start -W -n "$PACKAGE/com.fhg.android.MainActivity")"
echo "$launch"
[[ "$launch" == *"Status: ok"* ]]
sleep 6
test -n "$(adb shell pidof "$PACKAGE" | tr -d '\r')"
echo 'Open the original Hiddify Flutter VPN dashboard *inside the same APK*'
launch="$(adb shell am start -W -n "$PACKAGE/com.hiddify.hiddify.MainActivity")"
echo "$launch"
[[ "$launch" == *"Status: ok"* ]]
sleep 6
test -n "$(adb shell pidof "$PACKAGE" | tr -d '\r')"
echo 'Run built-in synthetic deep-link import resolution (no user subscription)'
launch="$(adb shell am start -W -a android.intent.action.VIEW -d 'hiddify://import/?url=https%3A%2F%2Fexample.invalid%2Ftest' -n "$PACKAGE/com.hiddify.hiddify.MainActivity")"
echo "$launch"
[[ "$launch" == *"Status: ok"* ]]
adb logcat -d -b crash >/tmp/fhg-unified-crash.txt
if grep -F "Process: $PACKAGE" /tmp/fhg-unified-crash.txt;then
   echo 'FAIL: FHG/Hiddify unified app crashed'
   tail -80 /tmp/fhg-unified-crash.txt
   exit 44
fi
echo 'PASS: ONE APK installed, FHG browser launcher + Hiddify VPN dashboard and profile import opened successfully'
echo 'NOTE: real remote subscription and successful native VPN connection still require device tests'
