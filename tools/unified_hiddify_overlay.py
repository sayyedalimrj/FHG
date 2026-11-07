#!/usr/bin/env python3
"""Overlay FHG's isolated browser/account workspace into genuine Hiddify source.
PRIVATE BUILD TEST ONLY. Do not publish a derivative without complying with
the special requirements in hiddify-app/LICENSE.md, including real GitHub fork.
No modification to Hiddify VPNService, Flutter connection flow, or Hiddify Core.
"""
from pathlib import Path
import re, shutil, sys

if len(sys.argv)!=3:
    raise SystemExit("usage: unified_hiddify_overlay.py <FHG-repo-root> <hiddify-source-root>")
fg=Path(sys.argv[1]).resolve()
hd=Path(sys.argv[2]).resolve()
assert (fg/"android/app/src/main/java/com/fhg/android/AccountStore.java").is_file()
assert (hd/"LICENSE.md").is_file()
assert (hd/"android/app/build.gradle").is_file()
assert (hd/"android/app/src/main/AndroidManifest.xml").is_file()
assert (hd/"pubspec.yaml").is_file()
assert "Hiddify Extended GNU" in (hd/"LICENSE.md").read_text()

src=fg/"android/app/src/main/java/com/fhg/android"
dest=hd/"android/app/src/main/java/com/fhg/android"
dest.mkdir(parents=True,exist_ok=True)
for name in ["AccountStore.java","BrowserActivity.java","MainActivity.java","NetworkGate.java","Totp.java"]:
    shutil.copy2(src/name,dest/name)

def rewrite(path, old, new, mandatory=True):
    s=path.read_text(encoding="utf-8")
    if old not in s:
        if mandatory: raise SystemExit(f"Upstream anchor changed: {path}: {old[:120]!r}")
        return
    path.write_text(s.replace(old,new),encoding="utf-8")

# Ensure ONE package and launcher. The original Flutter dashboard is now an
# activity inside the SAME APK, not a separately-installed program.
gradle=hd/"android/app/build.gradle"
rewrite(gradle,'applicationId "app.hiddify.com"','applicationId "com.fhg.android.unified"')
rewrite(gradle,'implementation fileTree(dir: \'libs\', include: [\'*.jar\', \'*.aar\'])',
        '''implementation fileTree(dir: 'libs', include: ['*.jar', '*.aar'])
    implementation 'org.mozilla.geckoview:geckoview:159.0.20261022150525' ''')

settings=hd/"android/settings.gradle"
rewrite(settings,'        mavenCentral()',
    "        mavenCentral()\n        maven { url 'https://maven.mozilla.org/maven2' }")
# Plugin management repositories do not affect Android app dependencies;
# GeckoView must be available in the root *allprojects* repository set.
root_gradle=hd/"android/build.gradle"
rewrite(root_gradle, "        mavenCentral()\n        google()",
    "        mavenCentral()\n        google()\n        maven { url 'https://maven.mozilla.org/maven2/' }")

manifest=hd/"android/app/src/main/AndroidManifest.xml"
rewrite(manifest,'''    <uses-permission android:name="android.permission.INTERNET" />''',
        '''    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />''')

# MainActivity of Flutter must remain alive and keep all its hiddify deep links.
# Remove only its launcher intent filter. FHG starts it by explicit component.
s=manifest.read_text(encoding="utf-8")
launcher='''            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>'''
assert s.count(launcher)==1, "Original Flutter launcher marker changed"
s=s.replace(launcher,"",1)
insertion='''        <!-- FHG account workspace is the ONLY launcher of the combined APK.
             The original Hiddify Flutter activity still owns connection UI,
             profile selection, system VPN consent, and subscription conversion. -->
        <activity
            android:name="com.fhg.android.MainActivity"
            android:exported="true"
            android:windowSoftInputMode="adjustResize"
            android:theme="@style/NormalTheme">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>
        <activity
            android:name="com.fhg.android.BrowserActivity"
            android:exported="false"
            android:launchMode="singleTask"
            android:windowSoftInputMode="adjustResize"
            android:theme="@style/NormalTheme" />
'''
assert s.count("    </application>")==1
s=s.replace("    </application>",insertion+"    </application>")
s=s.replace('android:label="Hiddify"','android:label="FHG Secure"')
manifest.write_text(s,encoding="utf-8")

bridge=r'''package com.fhg.android;

import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.content.ActivityNotFoundException;

/**
 * ONE APP. This opens the original, unmodified Hiddify Flutter VPN dashboard
 * *within this Android package*, not a separately-installed Hiddify client.
 * The upstream Flutter screen handles import consent, server list & VPN.
 */
final class HiddifyBridge {
    static final String PACKAGE="com.fhg.android.unified";
    private static final String VPN_ACTIVITY="com.hiddify.hiddify.MainActivity";
    private HiddifyBridge(){}
    static Intent launchIntent(Context context) {
        return new Intent(Intent.ACTION_MAIN)
            .setClassName(context.getPackageName(),VPN_ACTIVITY);
    }
    static boolean installed(Context context) {
        // Original Hiddify Activity is compiled into this ONE package.
        return context.getPackageName().equals(PACKAGE);
    }
    static boolean open(Context context) {
        try {context.startActivity(launchIntent(context));return true;}
        catch(RuntimeException missing){return false;}
    }
    static Intent subscriptionIntent(String url) {
        Uri u=Uri.parse(url);
        if(!"https".equalsIgnoreCase(u.getScheme()) ||
           u.getHost()==null || u.getUserInfo()!=null || url.length()>4096)
            throw new IllegalArgumentException("Invalid HTTPS subscription");
        Uri deep=new Uri.Builder().scheme("hiddify").authority("import")
            .path("/").appendQueryParameter("url",url).build();
        return new Intent(Intent.ACTION_VIEW,deep)
            .setClassName(PACKAGE,VPN_ACTIVITY);
    }
    static boolean importSubscription(Context context,String url) {
        try {context.startActivity(subscriptionIntent(url));return true;}
        catch(RuntimeException failed){return false;}
    }
    static void openOfficialDownload(Context context) {open(context);}
}
'''
(dest/"HiddifyBridge.java").write_text(bridge,encoding="utf-8")

# UI is FHG only; VPN/server management stays 100% upstream Hiddify.
ui=dest/"MainActivity.java"
s=ui.read_text(encoding="utf-8")
s=s.replace("FHG  /  Secure Workspace","FHG  /  Secure Workspace")
s=s.replace("هیدیفای نصب نیست","مدیر VPN در دسترس نیست")
s=s.replace("نصب Hiddify رسمی","مدیریت VPN")
s=s.replace("هیدیفای را باز کن، سرور دلخواه را انتخاب و VPN را وصل کن.","مدیریت VPN را باز کن، سرور دلخواه را انتخاب و متصل شو.")
s=s.replace("باز کردن Hiddify و اتصال","انتخاب سرور و اتصال")
s=s.replace("باز کردن Hiddify","باز کردن بخش VPN")
s=s.replace("افزودن اشتراک در Hiddify","افزودن اشتراک به VPN")
s=s.replace("VPN با موتور رسمی Hiddify  •  مرورگر اختصاصی FHG",
            "VPN و مرورگر و حساب‌ها · همه در یک برنامه")
s=s.replace("FHG · Hiddify official backend · beta",
            "FHG · Unified Hiddify Core · diagnostic build")
s=s.replace("Hiddify", "Hiddify")  # preserve bridge references
ui.write_text(s,encoding="utf-8")

# The NetworkGate copied from FHG asks Android whether the active VPN network
# belongs to the same package that contains the built-in original Hiddify
# VpnService. A fresh HTTPS 204 over that Network is required for GeckoView.
ng=(dest/"NetworkGate.java").read_text(encoding="utf-8")
assert 'HiddifyBridge.PACKAGE' in ng and 'network.openConnection' in ng
assert 'NetworkCapabilities.TRANSPORT_VPN' in ng
assert 'if(Build.VERSION.SDK_INT>=30)' in ng

# Attribution; no removal of upstream LICENSE.md or source history.
(hd/"FHG_SOURCE_MODIFICATIONS.md").write_text(
"""# FHG combined browser + Hiddify v5.5.5 (private test build)

This private diagnostic build copies the browser, Keystore-encrypted
account store and TOTP support from sayyedalimrj/FHG into the
unmodified Hiddify Flutter VPN client and its original hiddify-core.

Upstream: https://github.com/hiddify/hiddify-app/tree/v5.5.5
Hiddify license and conditions: ./LICENSE.md

Original services, protocols, server selection, DNS/routing, and native
VPN engine are not replaced by FHG code. The app now has exactly ONE
launcher and ONE Android applicationId com.fhg.android.unified.
FHG's browser is locked until Android reports a VPN owned by this package
and an HTTPS 204 challenge over that VPN Network succeeds.

This package is for personal engineering testing. Do not publicly
distribute modified APKs without respecting upstream's fork, source
publication, noncommercial and separate-branding license conditions.
Successful compilation is not proof of native VPN connectivity on Samsung.
""",encoding="utf-8")
print("FHG overlay applied to Hiddify source; original VPN service retained.")
