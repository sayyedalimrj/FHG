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

# Flutter 3.44 marks IconData final; upstream v5.5.5 dependencies
# still subclassed IconData. Upgrade only those icon libraries and
# migrate the upstream FontAwesome widget usages to FaIcon.
# Keep Flutter 3.44 for AGP9 legacy-Kotlin compatibility.
pubspec = hd/"pubspec.yaml"
rewrite_target = pubspec.read_text(encoding="utf-8")
for before, after in [
    ("  font_awesome_flutter: ^10.7.0", "  font_awesome_flutter: ^11.0.0"),
    ("  simple_icons: ^10.1.3", "  simple_icons: ^16.23.0"),
]:
    assert rewrite_target.count(before) == 1, f"Expected exact upstream dependency: {before}"
    rewrite_target = rewrite_target.replace(before, after, 1)
pubspec.write_text(rewrite_target, encoding="utf-8")

json_editor = hd/"lib/features/profile/details/json_editor.dart"
icon_source = json_editor.read_text(encoding="utf-8")
for icon_name in ("caretDown", "caretRight"):
    before = f"Icon(FontAwesomeIcons.{icon_name}, size: 14)"
    after = f"FaIcon(FontAwesomeIcons.{icon_name}, size: 14)"
    assert icon_source.count(before) == 1, f"Expected FontAwesome widget: {before}"
    icon_source = icon_source.replace(before, after, 1)
json_editor.write_text(icon_source, encoding="utf-8")
print("FHG: migrated Flutter 3.44 IconData-final incompatible dependencies.")

# New simple_icons removed several historical brand glyphs for upstream
# provider labels; substitute stable Material glyphs (already imported).
ip_widget = hd/"lib/features/proxy/active/ip_widget.dart"
ip_text = ip_widget.read_text(encoding="utf-8")
for before, after in [
    ("OrgIconData(SimpleIcons.microsoftazure, SimpleIconColors.microsoftazure)", "OrgIconData(Icons.cloud_outlined, Colors.blue)"),
    ("OrgIconData(SimpleIcons.amazonaws, SimpleIconColors.amazonaws)", "OrgIconData(Icons.cloud_outlined, Colors.orange)"),
    ("OrgIconData(SimpleIcons.oracle, SimpleIconColors.oracle)", "OrgIconData(Icons.cloud_outlined, Colors.red)"),
]:
    assert ip_text.count(before)==1, f"Missing upstream IP icon anchor: {before}"
    ip_text = ip_text.replace(before,after,1)
ip_widget.write_text(ip_text,encoding="utf-8")
print("FHG: removed unavailable v16 simple_icons glyph names in IP widget.")

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
# GeoView 159 and fresh AndroidX runtime require API 37 + AGP 9.1.
# Flutter 3.44 supports AGP 9 with legacy Kotlin/DSL compatibility flags.
rewrite(gradle, 'compileSdkVersion 36', 'compileSdkVersion 37')
rewrite(gradle, 'minSdkVersion flutter.minSdkVersion', 'minSdkVersion 26')
rewrite(settings, 'version \'8.6.0\' apply false',
        'version \'9.1.1\' apply false')
rewrite(settings, 'version "2.1.0" apply false',
        'version "2.2.10" apply false')
wrapper=hd/"android/gradle/wrapper/gradle-wrapper.properties"
rewrite(wrapper,'gradle-8.7-all.zip','gradle-9.3.1-bin.zip')
gradle_props=hd/"android/gradle.properties"
# AGP9 removed the global buildconfig default; request it per-module.
props_text=gradle_props.read_text(encoding="utf-8")
assert "android.defaults.buildfeatures.buildconfig=true" in props_text
gradle_props.write_text(
    props_text.replace("android.defaults.buildfeatures.buildconfig=true\n",""),
    encoding="utf-8")
rewrite(gradle, '    buildFeatures {\n        viewBinding true',
        '    buildFeatures {\n        buildConfig true\n        viewBinding true')
with gradle_props.open("a",encoding="utf-8") as gp:
    gp.write("\nandroid.builtInKotlin=false\nandroid.newDsl=false\nandroid.nonFinalResIds=true\n")

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
            .setClassName(context.getPackageName(),VPN_ACTIVITY)
            .addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
    }
    // Explicit tap only; Hiddify itself handles profile, consent and native core.
    static boolean openForConnection(Context context) {
        try {
            Intent intent=launchIntent(context);
            intent.putExtra("fhg_connect_requested",true);
            context.startActivity(intent);
            return true;
        } catch(RuntimeException failed) {return false;}
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

# In upstream Hiddify, the VPN application excludes *its own package*
# from the tunnel. After merging GeckoView into that SAME Android package,
# leaving this behavior would route browser traffic outside the VPN.
# Keep the existing protect(fd) callback for the Go outbound sockets, but
# make the browser-containing package eligible for the TUN.
vpn_service=hd/"android/app/src/main/kotlin/com/hiddify/hiddify/bg/VPNService.kt"
vpn=vpn_service.read_text(encoding="utf-8")
guard='''        if (packageName == this.packageName) { 
            Log.d("VpnService","Cannot include myself: $packageName")
            return
        }
'''
assert vpn.count(guard)==1,"Hiddify self-inclusion guard changed"
vpn=vpn.replace(guard,"",1)
assert vpn.count('addExcludePackage(builder,packageName)')==1
assert vpn.count('addExcludePackage(builder, packageName)')==1
vpn=vpn.replace('addExcludePackage(builder,packageName)',
                '// FHG: do not exclude VPN-owner package; it also contains GeckoView.')
vpn=vpn.replace('addExcludePackage(builder, packageName)',
                '// FHG: keep browser within TUN; native engine uses protect(fd).')
assert vpn.count('//                    addIncludePackage(builder,packageName)')==2
vpn=vpn.replace('//                    addIncludePackage(builder,packageName)',
                'addIncludePackage(builder, packageName)')
vpn_service.write_text(vpn,encoding="utf-8")
print("FHG: VPN-owner UID included in tunnel so embedded GeckoView cannot bypass it.")

# A real one-shot connection request, handled by the original Hiddify
# notifier, with Android VPN consent preserved. No automatic reconnection loop.
kt=hd/"android/app/src/main/kotlin/com/hiddify/hiddify/MainActivity.kt"
k=kt.read_text(encoding="utf-8")
assert k.count("import android.content.Intent")==1
k=k.replace("import android.content.Intent",
    "import android.content.Intent\nimport io.flutter.plugin.common.MethodChannel",1)
anchor="        flutterEngine.plugins.add(LogHandler())"
assert k.count(anchor)==1
k=k.replace(anchor,anchor+'''
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger,
            "com.fhg.android.unified/connection").setMethodCallHandler { call, result ->
            when(call.method) {
                "consumeConnectRequest" -> {
                    val requested = intent?.getBooleanExtra("fhg_connect_requested",false) == true
                    intent?.removeExtra("fhg_connect_requested")
                    if(requested) Log.i("FHGUnifiedBridge","connect request delivered to Flutter")
                    result.success(requested)
                }
                else -> result.notImplemented()
            }
        }''',1)
anchor="    fun reconnect() {"
assert k.count(anchor)==1
k=k.replace(anchor,'''    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        if (intent.getBooleanExtra("fhg_connect_requested",false)) {
            intent.removeExtra("fhg_connect_requested")
            flutterEngine?.let {
                MethodChannel(it.dartExecutor.binaryMessenger,
                    "com.fhg.android.unified/connection").invokeMethod("connectNow",null)
            }
        }
    }

'''+anchor,1)
kt.write_text(k,encoding="utf-8")

dart=hd/"lib/features/home/widget/home_page.dart"
d=dart.read_text(encoding="utf-8")
anchor="import 'package:flutter/material.dart';"
assert d.count(anchor)==1
d=d.replace(anchor,anchor+'''
import 'package:flutter/services.dart';
import 'package:hiddify/features/connection/notifier/connection_notifier.dart';''',1)
anchor="    final activeProfile = ref.watch(activeProfileProvider);"
assert d.count(anchor)==1
d=d.replace(anchor,anchor+'''
    // A user-initiated FHG Connect tap enters Hiddify's ordinary connection
    // notifier. If there is no profile, no start occurs. Never bypass Android
    // VPN permission prompts or report that a connection is established here.
    useEffect(() {
      var disposed = false;
      const channel = MethodChannel('com.fhg.android.unified/connection');
      Future<void> connectSelected() async {
        if (disposed) return;
        try {
          final selected = await ref.read(activeProfileProvider.future);
          if (disposed || selected == null) {
            debugPrint('FHG_CONNECT: no active profile; select a server first');
            return;
          }
          // The notifier is a stream; if it is still loading, mayConnect()
          // silently does nothing. Await the first real status before tap.
          await ref.read(connectionNotifierProvider.future);
          if (disposed) return;
          debugPrint('FHG_CONNECT: requesting native VPN start through original Hiddify notifier');
          await ref.read(connectionNotifierProvider.notifier).mayConnect();
        } catch (error) {
          debugPrint('FHG_CONNECT: original notifier returned error: $error');
        }
      }
      channel.setMethodCallHandler((call) async {
        if (call.method == 'connectNow') await connectSelected();
      });
      Future<void> consume() async {
        try {
          if (await channel.invokeMethod<bool>('consumeConnectRequest') == true) {
            await connectSelected();
          }
        } on PlatformException catch (error) {
          debugPrint('FHG_CONNECT: bridge unavailable: $error');
        }
      }
      Future.microtask(consume);
      return () { disposed = true; channel.setMethodCallHandler(null); };
    }, const []);
''',1)
dart.write_text(d,encoding="utf-8")
print("FHG: explicit Connect forwards once to original Hiddify connection notifier.")

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
