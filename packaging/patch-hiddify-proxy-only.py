#!/usr/bin/env python3
"""Patch upstream Hiddify v5.5.5 for the FHG browser-only proxy distribution.

Operates on a clean upstream checkout. Fails immediately when upstream source
no longer matches the reviewed release. Does not change subscription features.
"""
from pathlib import Path
import sys

if len(sys.argv) != 2:
    raise SystemExit("Usage: python patch-hiddify.py <upstream-hiddify-app-dir>")
root = Path(sys.argv[1]).resolve()
if not (root / "pubspec.yaml").is_file():
    raise SystemExit("Expected the Hiddify Flutter source root")
if "version: 5.5.5+50505" not in (root / "pubspec.yaml").read_text(encoding="utf-8"):
    raise SystemExit("Only reviewed Hiddify v5.5.5 sources are supported")

def replace(rel: str, before: str, after: str):
    path = root / rel
    txt = path.read_text(encoding="utf-8")
    matches = txt.count(before)
    if matches != 1:
        raise SystemExit(f"Patch anchor expected once, found {matches}: {rel}: {before[:70]!r}")
    path.write_text(txt.replace(before, after, 1), encoding="utf-8", newline="\n")
    print("PATCH", rel)

# 1) A stored System Proxy/VPN choice cannot be loaded or selected from this build.
p = "lib/singbox/model/singbox_config_enum.dart"
replace(p,
    "static ServiceMode get defaultMode => PlatformUtils.isDesktop && !_macAppStore ? systemProxy : tun;",
    "static ServiceMode get defaultMode => Platform.isWindows ? proxy : (PlatformUtils.isDesktop && !_macAppStore ? systemProxy : tun);")
replace(p,
    "if (Platform.isWindows || Platform.isLinux) {\n      return values;",
    "if (Platform.isWindows) {\n      return const [proxy];\n    } else if (Platform.isLinux) {\n      return values;")

p = "lib/features/settings/data/config_option_repository.dart"
replace(p,
    '''  static final serviceMode = PreferencesNotifier.create<ServiceMode, String>(
    "service-mode",
    ServiceMode.defaultMode,
    // a mode this build does not offer (system proxy, saved before the Mac App Store build) is
    // the default again
    mapFrom: (value) => ServiceMode.choices.firstWhere((e) => e.key == value, orElse: () => ServiceMode.defaultMode),
    mapTo: (value) => value.key,
  );''',
    '''  // FHG: read, import and write are all pinned to proxy-only.
  // Crucially the runtime config is ALSO clamped below.
  static final serviceMode = PreferencesNotifier.create<ServiceMode, String>(
    "service-mode",
    ServiceMode.proxy,
    mapFrom: (_) => ServiceMode.proxy,
    mapTo: (_) => ServiceMode.proxy.key,
    validator: (value) => value == ServiceMode.proxy,
    overrideValue: ServiceMode.proxy,
  );''')

# The public config preference also defaults to and validates the fixed FHG port;
# this is separate from the runtime hard clamp and protects every UI consumer.
replace(p,
    '''  static final mixedPort = PreferencesNotifier.create<int, int>(
    "mixed-port",
    12334,
    validator: (value) => isPort(value.toString()),
  );''',
    '''  static final mixedPort = PreferencesNotifier.create<int, int>(
    "mixed-port",
    12345,
    mapFrom: (_) => 12345,
    mapTo: (_) => 12345,
    validator: (value) => value == 12345,
    overrideValue: 12345,
  );''')

# Original Hiddify's Clash API uses 127.0.0.1:16756. Independent FHG must
# NEVER bind the same management port; keep the local API on :26756.
# The management API remains loopback-only and must not be exposed to LAN.
replace(p,
    '''  static final clashApiPort = PreferencesNotifier.create<int, int>(
    "clash-api-port",
    16756,
    validator: (value) => isPort(value.toString()),
  );''',
    '''  static final clashApiPort = PreferencesNotifier.create<int, int>(
    "clash-api-port",
    26756,
    mapFrom: (_) => 26756,
    mapTo: (_) => 26756,
    validator: (value) => value == 26756,
    overrideValue: 26756,
  );''')

# 2) Hard restrictions are imposed in the config actually sent to Hiddify Core,
# not merely on UI widgets. This also defeats stored legacy values.
replace(p, "    final mode = ref.watch(serviceMode);", "    const mode = ServiceMode.proxy; // FHG runtime invariant")
for before,after in (
    ("mixedPort: ref.watch(mixedPort),", "mixedPort: 12345,"),
    ("clashApiPort: ref.watch(clashApiPort),", "clashApiPort: 26756,"),
    ("enableMixedPort: ref.watch(enableMixedPort),", "enableMixedPort: true,"),
    ("enableTproxyPort: ref.watch(enableTproxyPort),", "enableTproxyPort: false,"),
    ("enableDirectPort: ref.watch(enableDirectPort),", "enableDirectPort: false,"),
    ("enableRedirectPort: ref.watch(enableRedirectPort),", "enableRedirectPort: false,"),
    ("enableTun: mode == ServiceMode.tun,", "enableTun: false,"),
    ("setSystemProxy: mode == ServiceMode.systemProxy,", "setSystemProxy: false,"),
    ("allowConnectionFromLan: ref.watch(allowConnectionFromLan),", "allowConnectionFromLan: false,"),
    ("localProxyAuth: ref.watch(localProxyAuth),", "localProxyAuth: false,"),
    ("chainStatus: ref.watch(chainStatus),", "chainStatus: ChainStatus.off,"),
):
    replace(p, before, after)

# Profile-specific settings overrides are applied after normal options. Reassert
# the invariant after merging, so imported configs can't enable VPN/system proxy,
# expose LAN listeners, move port 12345, or enable WARP/Psiphon chain.
replace(p,
    '''          final merged = ProfileParser.applyProfileOverride(options.toJson(), profileOverride);
          return SingboxConfigOption.fromJson(keepOwnDnsBehindExtraSecurity(own, merged));''',
    '''          final merged = ProfileParser.applyProfileOverride(options.toJson(), profileOverride);
          final safe = keepOwnDnsBehindExtraSecurity(own, merged);
          return SingboxConfigOption.fromJson({
            ...safe,
            'mixed-port': 12345,
            'clash-api-port': 26756,
            'enable-mixed-port': true,
            'enable-tun': false,
            'set-system-proxy': false,
            'allow-connection-from-lan': false,
            'local-proxy-auth': false,
            'enable-tproxy-port': false,
            'enable-direct-port': false,
            'enable-redirect-port': false,
            'chain-status': ChainStatus.off.key,
          });''')

# 3) No controls through which end users can re-enable modes, WARP or LAN sharing.
(root / "lib/core/router/bottom_sheets/widgets/quick_settings_modal.dart").write_text(
    """import 'package:flutter/material.dart';

/// FHG intentionally presents no mode, LAN-sharing or WARP controls.
class QuickSettingsModal extends StatelessWidget {
  const QuickSettingsModal({super.key});

  @override
  Widget build(BuildContext context) => const SafeArea(
    child: Padding(
      padding: EdgeInsets.all(20),
      child: ListTile(
        leading: Icon(Icons.lock_outline_rounded),
        title: Text('Browser proxy mode'),
        subtitle: Text('Locked to 127.0.0.1:12345. System Proxy, VPN, LAN sharing and WARP are unavailable in FHG.'),
      ),
    ),
  );
}
""", encoding="utf-8", newline="\n")

(root / "lib/features/settings/overview/sections/inbound_options_page.dart").write_text(
    """import 'package:flutter/material.dart';

/// FHG network interface settings are deliberately read-only.
class InboundOptionsPage extends StatelessWidget {
  const InboundOptionsPage({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Browser proxy')),
    body: ListView(children: const [
      ListTile(
        leading: Icon(Icons.lock_outline_rounded),
        title: Text('Proxy only'),
        subtitle: Text('System Proxy and VPN are disabled and cannot be changed.'),
      ),
      ListTile(
        leading: Icon(Icons.settings_ethernet_rounded),
        title: Text('127.0.0.1:12345'),
        subtitle: Text('Fixed local mixed SOCKS5 proxy. LAN sharing is disabled.'),
      ),
      ListTile(
        leading: Icon(Icons.shield_outlined),
        title: Text('Extra hops disabled'),
        subtitle: Text('WARP, Psiphon and other extra chain hops are disabled in FHG.'),
      ),
    ]),
  );
}
""", encoding="utf-8", newline="\n")

# Keep the rest of original Hiddify UI (subscription quota/nodes/delays) intact.
p = "lib/features/settings/overview/settings_page.dart"
replace(p,
    '''          if (ref.watch(hasAnyProfileProvider).value ?? false)
            SettingsSection(
              title: t.pages.settings.chain.title,
              icon: Icons.webhook_rounded,
              subtitle: Text(t.pages.settings.chain.subtitle),
              namedLocation: context.namedLocation('chainOptions'),
            ),''',
    '''          // FHG: advanced chain/WARP page is intentionally absent.''')

# 4) Crash analytics permanently OFF, including for old saved preferences.
p = "lib/core/analytics/analytics_controller.dart"
replace(p,
    '''bool readAnalyticsEnabled(SharedPreferences preferences) {
  return preferences.getBool(enableAnalyticsPrefKey) ?? false;
}''',
    '''bool readAnalyticsEnabled(SharedPreferences preferences) => false; // FHG privacy rule''')
# Remove the entire enabling implementation (including Sentry initialization).
import re
analytics_path = root / p
analytics_text = analytics_path.read_text(encoding="utf-8")
pattern = r"  Future<void> enableAnalytics\(\) async \{.*?(?=  Future<void> disableAnalytics\(\) async \{)"
replacement = """  Future<void> enableAnalytics() async {
    // FHG forcibly disables crash reporting, even for old saved preferences.
    await _preferences.setBool(enableAnalyticsPrefKey, false);
    await Sentry.close();
    state = const AsyncData(false);
  }

"""
analytics_text, count = re.subn(pattern, replacement, analytics_text, flags=re.DOTALL)
if count != 1:
    raise SystemExit(f"Analytics enforcement patch mismatch: found {count} implementations")
analytics_path.write_text(analytics_text, encoding="utf-8", newline="\n")
replace(p,
    '''    return readAnalyticsEnabled(_preferences);''',
    '''    await _preferences.setBool(enableAnalyticsPrefKey, false);
    await Sentry.close();
    return false;''')
p = "lib/features/settings/overview/sections/general_page.dart"
replace(p, "          const EnableAnalyticsPrefTile(),", "          // FHG: analytics is disabled and its toggle is removed.")

# 5) Prevent Hiddify's own upstream updater from replacing the modified
# proxy-locked binary with an ordinary vendor build, which could restore
# system-proxy/VPN options. FHG will supply complete vetted updates.
p = "lib/features/app_update/notifier/app_update_notifier.dart"
update_path = root / p
update_text = update_path.read_text(encoding="utf-8")
check_pattern = r"  Future<AppUpdateState> check\(\) async \{.*?(?=  PreferencesEntry<int, dynamic> get _lastCheckPref)"
check_repl = """  Future<AppUpdateState> check() async {
    return state = const AppUpdateState.disabled();
  }

"""
update_text, count = re.subn(check_pattern, check_repl, update_text, flags=re.DOTALL)
if count != 1:
    raise SystemExit("Cannot disable upstream Hiddify update checker")
startup_pattern = r"  Future<RemoteVersionEntity\?> checkOnStart\(\) async \{.*?(?=  Future<void> ignoreRelease)"
startup_repl = """  Future<RemoteVersionEntity?> checkOnStart() async => null;

"""
update_text, count = re.subn(startup_pattern, startup_repl, update_text, flags=re.DOTALL)
if count != 1:
    raise SystemExit("Cannot disable upstream Hiddify startup update prompt")
update_path.write_text(update_text, encoding="utf-8", newline="\n")

# 6) Reduce end-user clutter without stripping internal diagnostic logging.
# Desktop navigation and optional mobile Settings menu must both omit
# About / Logs. Diagnostic files remain available to developers.
p = "lib/core/router/adaptive_layout/my_adaptive_layout.dart"
replace(p,
    """    if (!isMobileBreakpoint) ShellRouteAction(Icons.description_rounded, t.pages.logs.title),
    if (!isMobileBreakpoint) ShellRouteAction(Icons.info_rounded, t.pages.about.title),""",
    """    // FHG: advanced logs/about screens intentionally hidden from user navigation.""")
p = "lib/features/settings/overview/settings_page.dart"
replace(p,
    """          if (Breakpoint(context).isMobile()) ...[
            SettingsSection(
              title: t.pages.logs.title,
              icon: Icons.description_rounded,
              namedLocation: context.namedLocation('logs'),
            ),
            SettingsSection(
              title: t.pages.about.title,
              icon: Icons.info_rounded,
              namedLocation: context.namedLocation('about'),
            ),
          ],""",
    """          // FHG: optional log/about menu entries removed from settings.""")
# English for a new installation; respect existing explicit language selections.
p = "lib/core/localization/locale_preferences.dart"
replace(p,
    '''    if (persisted == null) return AppLocaleUtils.findDeviceLocale();''',
    '''    if (persisted == null) return AppLocale.en; // FHG fresh-install default''')

# Assert all important invariants statically, before any expensive Flutter build.
config = (root / "lib/features/settings/data/config_option_repository.dart").read_text(encoding="utf-8")
for marker in ("'mixed-port': 12345", "'clash-api-port': 26756", "'enable-tun': false", "'set-system-proxy': false",
               "'allow-connection-from-lan': false", "'chain-status': ChainStatus.off.key"):
    assert marker in config, f"Missing enforced config invariant {marker}"
assert "const EnableAnalyticsPrefTile()" not in (root / "lib/features/settings/overview/sections/general_page.dart").read_text()
assert "SegmentedButton(" not in (root / "lib/core/router/bottom_sheets/widgets/quick_settings_modal.dart").read_text()
print("PASS: proxy-only defaults and runtime restrictions; WARP and analytics disabled; Hiddify subscription UI preserved.")
