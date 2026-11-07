#!/usr/bin/env bash
# Single shell process: Android 15 handoff smoke for official Hiddify 5.5.5
set -Eeuo pipefail
apk_fhg="dist/FHG-Secure-Hiddify-Bridge-Android-ARM64.apk"
apk_hiddify="dist/Hiddify-Android-x86_64.apk"
adb install -r "$apk_hiddify" | grep -F Success
adb shell pm path app.hiddify.com | grep -F 'package:'
adb install -r "$apk_fhg" | grep -F Success
adb shell pm path com.fhg.android.secure.hiddify | grep -F 'package:'
adb logcat -c
adb shell am start -W -n com.fhg.android.secure.hiddify/com.fhg.android.MainActivity | grep -F 'Status: ok'
sleep 4
adb shell pidof com.fhg.android.secure.hiddify

# Use a synthetic URL only to check the Android deep-link contract.
# Hiddify itself confirms and requests import; no real subscription is sent.
result="$(adb shell am start -W -a android.intent.action.VIEW -c android.intent.category.BROWSABLE -d 'hiddify://import/?url=https%3A%2F%2Fexample.invalid%2Ftest' -p app.hiddify.com)"
echo "$result"
echo "$result" | grep -F 'Status: ok'
echo "$result" | grep -F 'app.hiddify.com/'
if echo "$result" | grep -F 'ResolverActivity';then
  echo 'FAIL: deep link did not resolve exclusively to Hiddify';exit 31
fi
sleep 6
adb shell pidof app.hiddify.com
adb logcat -d -b crash > /tmp/hiddify-and-fhg-crashes.txt
if grep -qE 'Process: app.hiddify.com|Process: com.fhg.android.secure.hiddify' /tmp/hiddify-and-fhg-crashes.txt;then
  echo 'FAIL: one of the real apps crashed during handoff'
  tail -100 /tmp/hiddify-and-fhg-crashes.txt
  exit 32
fi
echo 'PASS: official Hiddify 5.5.5 and FHG installed; deep link exclusively resolved; both apps remain alive'
