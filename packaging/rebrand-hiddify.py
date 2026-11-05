#!/usr/bin/env python3
"""Give the pinned Hiddify v5.5.5 Windows source a separate FHG Proxy identity.

This patches the checked-out upstream SOURCE before the Windows Flutter build.
It must never mutate or overwrite an installed Hiddify program. All upstream
copyrights and the unmodified upstream LICENSE.md remain in the bundle.
Python standard library only; no external icon/download dependencies.
"""
from __future__ import annotations
import importlib.util
from pathlib import Path
import struct
import sys
import zlib

if len(sys.argv) != 2:
    raise SystemExit("Usage: python packaging/rebrand-hiddify.py <hiddify-v5.5.5-source>")
root = Path(sys.argv[1]).resolve()
if "version: 5.5.5+50505" not in (root / "pubspec.yaml").read_text(encoding="utf-8"):
    raise SystemExit("Unsupported Hiddify source; refusing to rebrand")

def replace(rel: str, old: str, new: str) -> None:
    file = root / rel
    data = file.read_text(encoding="utf-8")
    matches = data.count(old)
    if matches != 1:
        raise SystemExit(f"Rebrand anchor mismatch ({matches}): {rel}: {old[:90]!r}")
    file.write_text(data.replace(old, new, 1), encoding="utf-8", newline="\n")
    print(f"BRAND {rel}")

# The EXE, window name and singleton mutex MUST all be distinct. Merely
# renaming Hiddify.exe after compilation would leave its mutex conflicting.
replace("windows/CMakeLists.txt", 'set(BINARY_NAME "Hiddify")', 'set(BINARY_NAME "FHGProxy")')
replace("windows/runner/main.cpp", 'L"HiddifyMutex"', 'L"FHGProxyMutex"')
replace("windows/runner/main.cpp", 'FindWindowA(NULL, "Hiddify")', 'FindWindowA(NULL, "FHG Proxy")')
f = root / "windows/runner/main.cpp"
data = f.read_text(encoding="utf-8")
assert data.count('L"Hiddify"') == 3, "Unexpected native-window identity count"
f.write_text(data.replace('L"Hiddify"', 'L"FHG Proxy"'), encoding="utf-8", newline="\n")

# Accurate, clearly derivative branded Windows file metadata; attribution stays.
resource = root / "windows/runner/Runner.rc"
s = resource.read_text(encoding="utf-8")
changes = {
    'VALUE "CompanyName", "Hiddify"': 'VALUE "CompanyName", "FHG Proxy (Hiddify derivative)"',
    'VALUE "FileDescription", "Hiddify"': 'VALUE "FileDescription", "FHG Proxy"',
    'VALUE "InternalName", "hiddify"': 'VALUE "InternalName", "FHGProxy"',
    'VALUE "OriginalFilename", "Hiddify.exe"': 'VALUE "OriginalFilename", "FHGProxy.exe"',
    'VALUE "ProductName", "hiddify"': 'VALUE "ProductName", "FHG Proxy"',
    'VALUE "LegalCopyright", "Copyright (C) 2023 Hiddify.com. All rights reserved."':
        'VALUE "LegalCopyright", "Copyright (C) 2023 Hiddify.com; FHG modifications 2026."',
}
for old,new in changes.items():
    if s.count(old)!=1: raise SystemExit(f"Windows metadata anchor mismatch: {old}")
    s=s.replace(old,new,1)
resource.write_text(s, encoding="utf-8", newline="\n")

replace("lib/core/model/constants.dart", 'static const appName = "Hiddify";',
        'static const appName = "FHG Proxy";')
replace("lib/features/auto_start/notifier/auto_start_notifier.dart",
        'packageName: "Hiddify.App.Win",', 'packageName: "FHG.Proxy.Win",')

# An installed Hiddify still owns the Windows-internal gRPC 17078 endpoint.
# A new EXE or mutex is not enough: using 17078 left fgClient uninitialized
# whenever both clients ran together. Keep control RPC separate from 12345.
replace("lib/hiddifycore/core_interface/core_interface_desktop.dart",
        "CoreInterfaceDesktop({this.port = 17078});",
        "CoreInterfaceDesktop({this.port = 27178});")
replace("lib/hiddifycore/core_interface/core_interface_desktop_tun.dart",
        "CoreInterfaceDesktopTun() : super(port: 17078);",
        "CoreInterfaceDesktopTun() : super(port: 27178);")

# Fail with an actionable error instead of a LateInitializationError whenever
# native library setup or its localhost RPC handshake failed. Do not delete
# any user profiles or reset any state as part of this recovery.
replace("lib/hiddifycore/hiddify_core_service.dart",
        '''      try {
        final response = await core.fgClient.parse(ParseRequest(tempPath: tempPath, configPath: path, debug: false));''',
        '''      if (!core.isInitialized()) {
        await setup().run();
        if (!core.isInitialized()) {
          return left('FHG Proxy engine not ready. Internal control port 27178 may be blocked. Inspect FHG app.log and the local port owner.');
        }
      }
      try {
        final response = await core.fgClient.parse(ParseRequest(tempPath: tempPath, configPath: path, debug: false));''')
replace("lib/hiddifycore/hiddify_core_service.dart",
        '''      loggy.debug("changing options");
      // latestOptions = options;''',
        '''      loggy.debug("changing options");
      if (!core.isInitialized()) {
        await setup().run();
        if (!core.isInitialized()) {
          return left('FHG core did not start. Cannot change connection options. Check local port 27178 and FHG app.log.');
        }
      }
      // latestOptions = options;''')
replace("lib/hiddifycore/hiddify_core_service.dart",
        '''  TaskEither<String, String> generateFullConfigByPath(String path) {
    return TaskEither(() async {
      final response = await core.fgClient.parse''',
        '''  TaskEither<String, String> generateFullConfigByPath(String path) {
    return TaskEither(() async {
      if (!core.isInitialized()) {
        await setup().run();
        if (!core.isInitialized()) {
          return left('FHG core unavailable while parsing profile. Check 27178 listener, logs, and original Hiddify port conflicts.');
        }
      }
      final response = await core.fgClient.parse''')

# On first start of the new FHG build, ignore locale accidentally inherited
# from Hiddify or an earlier FHG package. A later explicit language choice in
# the FHG settings remains preserved in its distinct preference store.
replace("lib/bootstrap.dart",
        "import 'dart:async';",
        "import 'dart:async';\nimport 'dart:io';")
replace("lib/bootstrap.dart",
        '''  await _init("preferences", () => container.read(sharedPreferencesProvider.future));
  // The ring starts at a safe default''',
        '''  await _init("preferences", () => container.read(sharedPreferencesProvider.future));
  if (Platform.isWindows && Environment.isPortable) {
    final prefs = container.read(sharedPreferencesProvider).requireValue;
    if (prefs.getBool('fhg_english_default_initialized') != true) {
      await prefs.setString('locale', 'en');
      await prefs.setBool('fhg_english_default_initialized', true);
    }
  }
  // The ring starts at a safe default''')

# No conflicts with the vendor portable data folder or vendor preference keys.
replace("lib/core/directories/directories_provider.dart",
        "p.join(exeDir.path, 'hiddify_portable_data')",
        "p.join(exeDir.path, 'fhg_proxy_portable_data')")
replace("lib/core/directories/directories_provider.dart",
        """    } else {
      final baseDir = await getApplicationSupportDirectory();""",
        """    } else {
      // Never fall back to another application's roaming folder on Windows.
      if (PlatformUtils.isWindows && Environment.isPortable) {
        throw const FileSystemException('FHG Proxy portable folder must be writable');
      }
      final baseDir = await getApplicationSupportDirectory();""")
replace("lib/core/directories/directories_provider.dart",
        """    } else if (PlatformUtils.isWindows || PlatformUtils.isLinux) {
      return await getApplicationSupportDirectory();""",
        """    } else if (PlatformUtils.isWindows && Environment.isPortable) {
      throw const FileSystemException('FHG Proxy database folder must be writable');
    } else if (PlatformUtils.isWindows || PlatformUtils.isLinux) {
      return await getApplicationSupportDirectory();""")
replace("lib/core/preferences/preferences_provider.dart",
        "SharedPreferences.setPrefix('portable.')",
        "SharedPreferences.setPrefix('fhg_proxy.')")

# The vendor app automatically overwrites Windows default-URL scheme handlers
# at startup. Do NOT steal links from an existing user's Hiddify installation.
replace("lib/core/router/deep_linking/my_app_links.dart",
        """  if (PlatformUtils.isWindows) {
    for (final protocol in LinkParser.protocols) {
      registerProtocolHandler(protocol);
    }
  }""",
        """  // FHG intentionally does not claim the user's existing URL schemes.
  // Subscription links can be pasted and imported inside the FHG Proxy UI.""")
replace("lib/core/router/deep_linking/my_app_links.dart",
        "import 'package:hiddify/core/router/deep_linking/url_protocol/api.dart';\n", "")
replace("lib/core/router/deep_linking/my_app_links.dart",
        "import 'package:hiddify/utils/utils.dart';\n", "")

# Settings screen is proxy-locked already, but vendor Windows TRAY still shows
# all service modes. Hide that menu to avoid misleading mode selections.
replace("lib/features/system_tray/notifier/system_tray_notifier.dart",
        """      MenuItem.submenu(
        label: t.pages.settings.inbound.serviceMode,
        icon: Assets.images.trayIconIco,
        submenu: Menu(
          items: [
            ...ServiceMode.values.map(
              (e) => MenuItem.checkbox(checked: e == serviceMode, key: e.name, label: e.present(t)),
            ),
          ],
        ),
      ),""",
        """      MenuItem(key: 'fhg-proxy-locked', label: 'Proxy only: 127.0.0.1:12345', disabled: true),""")
replace("lib/features/system_tray/notifier/system_tray_notifier.dart",
        """    } else {
      final newMode = ServiceMode.values.byName(menuItem.key!);
      loggy.debug("switching service mode: [$newMode]");
      await ref.read(ConfigOptions.serviceMode.notifier).update(newMode);
    }""",
        """    }
    // FHG never accepts a system-proxy / VPN switch from the tray menu.""")

# Upstream Makefile pipes a 504/502 HTML error into tar (without curl -f),
# causing flaky CI; make core-library acquisition retry and fail on HTTP errors.
replace("Makefile",
        "curl -L $(CORE_URL)/$(CORE_NAME)-windows-amd64.tar.gz | tar xz -C $(DESKTOP_OUT)/",
        "curl -fSL --retry 8 --retry-all-errors --retry-delay 6 --connect-timeout 25 --max-time 420 $(CORE_URL)/$(CORE_NAME)-windows-amd64.tar.gz | tar xz -C $(DESKTOP_OUT)/")

# Original author's translated common title is still "Hiddify 5.5.5" on the
# home screen even after branding the EXE/icon. Keep upstream version accurate.
replace("lib/features/home/widget/home_page.dart",
        "TextSpan(text: t.common.appTitle),",
        "const TextSpan(text: 'FHG Proxy'),")

# FHG subscription fetches may use only the user-approved loopback:12334
# bootstrap. This DOES NOT change Firefox (which always uses guarded :12345).
replace("lib/core/http_client/dio_http_client.dart",
        """          final client = HttpClient()..userAgent = userAgent;
          client.findProxy = (url) {""",
        """          final client = HttpClient()..userAgent = userAgent;
          final bootstrap = Platform.environment['FHG_BOOTSTRAP_HTTP_PROXY'] ?? '';
          final safeBootstrap = RegExp(r'^127\.0\.0\.1:12334$').hasMatch(bootstrap);
          client.findProxy = (url) {""")
replace("lib/core/http_client/dio_http_client.dart",
        """            } else if (mode == "direct") {
              return "DIRECT";
            } else {
              return "PROXY $proxy; DIRECT";
            }""",
        """            } else if (mode == "direct") {
              return safeBootstrap ? "PROXY $bootstrap" : "DIRECT";
            } else {
              // No direct fallback when the user's FHG proxy is active.
              return "PROXY $proxy";
            }""")
replace("lib/core/http_client/dio_http_client.dart",
        """  Future<bool> isPortOpen(String host, int port, {Duration timeout = const Duration(seconds: 5)}) async {
    try {""",
        """  Future<bool> isPortOpen(String host, int port, {Duration timeout = const Duration(seconds: 5)}) async {
    if (port <= 0 || port > 65535) return false;
    try {""")

# Remote subscriptions can be unreachable before proxy connection. The import
# must time out (rather than spin forever), with sanitized failure messages.
replace("lib/features/profile/data/profile_parser.dart",
        "import 'dart:convert';",
        "import 'dart:async';\nimport 'dart:convert';")
replace("lib/features/profile/data/profile_parser.dart",
        """    final rs = await _httpClient
        .download(
          url.trim(),
          tempFilePath,
          cancelToken: cancelToken,
        )
        .catchError((err) {
          if (CancelToken.isCancel(err as DioException)) {
            throw const ProfileFailure.cancelByUser('HTTP request for getting profile content canceled by user.');
          }
          throw err;
        });""",
        """    final token = cancelToken ?? CancelToken();
    late final Response rs;
    try {
      rs = await _httpClient.download(
        url.trim(),
        tempFilePath,
        cancelToken: token,
      ).timeout(const Duration(seconds: 40), onTimeout: () {
        token.cancel('FHG subscription fetch timed out');
        throw const ProfileFailure.invalidUrl(
          'Subscription download timed out. Enable a trusted local bootstrap proxy or import a local config.',
        );
      });
    } on DioException catch (e) {
      if (CancelToken.isCancel(e)) {
        throw const ProfileFailure.cancelByUser('Subscription download was canceled.');
      }
      // Do not expose DioException URLs; they can contain subscription tokens.
      throw const ProfileFailure.invalidUrl(
        'Subscription could not be downloaded. Check proxy connection or use local import.',
      );
    }""")

# With user-approved bootstrap, a live but not CONNECTED FHG SOCKS listener
# must never shadow the working normal-Hiddify proxy. Current upstream
# selects "both" solely because 12345 accepts TCP even if all nodes are dead.
# Always use the explicitly approved bootstrap for subscription HTTP fetches,
# while the Firefox traffic path remains strictly FHG's own 12345 listener.
replace("lib/core/http_client/dio_http_client.dart",
        '''    final portOpen = await isPortOpen("127.0.0.1", port);
    final mode = proxyOnly && portOpen''',
        '''    final portOpen = await isPortOpen("127.0.0.1", port);
    final bootstrapApproved = Platform.environment['FHG_BOOTSTRAP_HTTP_PROXY'] == '127.0.0.1:12334';
    final mode = bootstrapApproved ? "direct" : proxyOnly && portOpen''')
replace("lib/core/http_client/dio_http_client.dart",
        '''    final mode = proxyOnly
        ? "proxy"
        : await isPortOpen("127.0.0.1", port)
        ? "both"
        : "direct";''',
        '''    final bootstrapApproved = Platform.environment['FHG_BOOTSTRAP_HTTP_PROXY'] == '127.0.0.1:12334';
    final mode = bootstrapApproved ? "direct" : proxyOnly
        ? "proxy"
        : await isPortOpen("127.0.0.1", port)
        ? "both"
        : "direct";''')

# Enforce proxy-only at the DesktopTun -> native core boundary too, not only
# Flutter preferences. Never touch HiddifyAdminBG / ProgramData of the vendor
# installation, even if a stale preference or a config tries to request TUN.
replace("lib/hiddifycore/core_interface/core_interface_desktop_tun.dart",
        """    _options = options;
    // When admin_bgservice will own TUN, fg must not enable TUN; full settings go in Ensure.
    final adminOwnsTun = useAdminBgService || (options.enableTun && !await _isPrivileged());""",
        """    if (options.enableTun || options.setSystemProxy || options.allowConnectionFromLan) {
      throw StateError('FHG supports only local proxy mode; no VPN, system proxy or LAN listener.');
    }
    useAdminBgService = false;
    _options = options;
    // FHG never installs or attaches the vendor HiddifyAdminBG service.
    const adminOwnsTun = false;""")
replace("lib/hiddifycore/core_interface/core_interface_desktop_tun.dart",
        """    final wantAdminBg = useAdminBgService || options.enableTun;
    if (!wantAdminBg) {""",
        """    // FHG does not permit admin background services or TUN at the core boundary.
    if (options.enableTun || options.setSystemProxy || options.allowConnectionFromLan) {
      throw StateError('FHG refuses TUN, system proxy and LAN outbound modes.');
    }
    const wantAdminBg = false;
    if (!wantAdminBg) {""")

# Original graphic is generated in FHG and is not an upstream trademark.
repo = Path(__file__).resolve().parents[1]
icon_src = repo / "native/app/resources/generate_icon.py"
spec = importlib.util.spec_from_file_location("fhg_icon", icon_src)
assert spec is not None and spec.loader is not None
brand = importlib.util.module_from_spec(spec)
spec.loader.exec_module(brand)

def styled_png(size: int, status: str = "none") -> bytes:
    source = brand.artwork(size)
    if status == "none":
        return source
    # The original PNG from the FHG icon generator has one IDAT and RGBA rows.
    pos = 8
    chunks = []
    pixels = None
    while pos < len(source):
        length = struct.unpack(">I", source[pos:pos+4])[0]
        name = source[pos+4:pos+8]
        value = source[pos+8:pos+8+length]
        pos += 12 + length
        if name == b"IDAT":
            pixels = bytearray(zlib.decompress(value))
        else:
            chunks.append((name, value))
    if pixels is None or len(pixels) != (size*4+1)*size:
        raise SystemExit("FHG icon generator changed unexpectedly")
    cx, cy, radius = .80*size, .81*size, .14*size
    fill = (18, 188, 115, 255) if status=="connected" else (235, 75, 76, 255)
    for y in range(size):
        for x in range(size):
            dist = ((x+0.5-cx)**2 + (y+0.5-cy)**2)**.5
            if dist > radius: continue
            offset = y*(size*4+1)+1+x*4
            pixels[offset:offset+4] = bytes((255,255,255,255) if dist>=radius-max(1,size*.024) else fill)
    out=b"\x89PNG\r\n\x1a\n"
    for name,value in chunks:
        if name==b"IEND":
            out+=brand.png_chunk(b"IDAT",zlib.compress(bytes(pixels),9))
        out+=brand.png_chunk(name,value)
    return out

def ico(status: str) -> bytes:
    sizes=[16,24,32,48,64,128,256]
    images=[styled_png(sz,status) for sz in sizes]
    buf=bytearray(struct.pack("<HHH",0,1,len(sizes)))
    offset=6+16*len(sizes)
    for n,img in zip(sizes,images):
        buf.extend(struct.pack("<BBBBHHII",n if n<256 else 0,n if n<256 else 0,0,0,1,32,len(img),offset))
        offset+=len(img)
    for img in images: buf.extend(img)
    return bytes(buf)

iconfiles = {
    "windows/runner/resources/app_icon.ico": ico("none"),
    "assets/images/tray_icon.ico": ico("none"),
    "assets/images/tray_icon_dark.ico": ico("none"),
    "assets/images/tray_icon_connected.ico": ico("connected"),
    "assets/images/tray_icon_disconnected.ico": ico("disconnected"),
}
for rel,data in iconfiles.items():
    (root/rel).write_bytes(data)
    print(f"ICON {rel}: {len(data)} bytes")
pngfiles = {
    "assets/images/tray_icon.png": "none",
    "assets/images/tray_icon_dark.png": "none",
    "assets/images/tray_icon_connected.png": "connected",
    "assets/images/tray_icon_connected_dark.png": "connected",
    "assets/images/tray_icon_connected_light.png": "connected",
    "assets/images/tray_icon_connected_outlined.png": "connected",
    "assets/images/tray_icon_disconnected.png": "disconnected",
}
for rel,status in pngfiles.items():
    (root/rel).write_bytes(styled_png(128,status))
    print(f"ICON {rel}")
(root/"assets/images/logo.svg").write_text(
    """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 180 180">
<rect width="180" height="180" rx="36" fill="#102c44"/>
<path d="M90 24 L142 45 L139 107 Q132 138 90 157 Q48 138 41 107 L38 45 Z" fill="#12bba7"/>
<path d="M90 37 L128 53 L126 103 Q120 126 90 143 Q60 126 54 103 L52 53 Z" fill="#113750"/>
<rect x="70" y="84" width="40" height="38" rx="5" fill="#ecfbf9"/>
<path d="M78 86 V77 a12 12 0 0 1 24 0 V86" stroke="#ecfbf9" stroke-width="7" fill="none"/>
<circle cx="90" cy="103" r="5" fill="#113750"/>
</svg>
""",encoding="utf-8",newline="\n")

checks = {
    "windows/CMakeLists.txt": 'set(BINARY_NAME "FHGProxy")',
    "windows/runner/main.cpp": "FHGProxyMutex",
    "windows/runner/Runner.rc": '"FHG Proxy"',
    "lib/core/directories/directories_provider.dart": "fhg_proxy_portable_data",
    "lib/core/preferences/preferences_provider.dart": "fhg_proxy.",
    "lib/core/model/constants.dart": '"FHG Proxy"',
}
for rel,expected in checks.items():
    assert expected in (root/rel).read_text(encoding="utf-8"), f"Missing FHG identity: {rel}"
assert "HiddifyMutex" not in (root/"windows/runner/main.cpp").read_text(encoding="utf-8")
print("PASS: distinct FHG Proxy executable, mutex/window/tray/icons, dedicated portable data and no URL-handler takeover.")
