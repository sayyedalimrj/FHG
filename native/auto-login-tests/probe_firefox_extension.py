"""Offline CI diagnostic for Firefox's signed Authenticator extension.

Uses a fresh temporary profile with synthetic data only. No real account,
tokens, credentials, pages or browser settings are read or printed.
"""
import json, os, pathlib, socket, subprocess, sys, tempfile, time, urllib.error, urllib.request

root=pathlib.Path(sys.argv[1])
driver=root/"WebDriver"/"geckodriver.exe"
firefox=root/"Firefox"/"firefox.exe"
assert driver.is_file() and firefox.is_file()
sock=socket.socket();sock.bind(("127.0.0.1",0));port=sock.getsockname()[1];sock.close()
proc=subprocess.Popen([str(driver),"--allow-system-access","--host","127.0.0.1","--port",str(port),"--log","fatal"],
                       stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
session=None
def call(method,path,data=None):
    request=urllib.request.Request(f"http://127.0.0.1:{port}/{path}",
                                  data=json.dumps(data).encode() if data is not None else None,
                                  headers={"Content-Type":"application/json"},method=method)
    try:
        with urllib.request.urlopen(request,timeout=30) as response:
            result=json.load(response)
            return result.get("value",result)
    except urllib.error.HTTPError as exc:
        msg=json.loads(exc.read() or b"{}").get("value",{})
        print("HTTP_STATUS",exc.code,"ERROR",msg.get("error","unknown"),
              "DETAIL",str(msg.get("message",""))[:320])
        return {}
try:
    for i in range(75):
        try:
            if call("GET","status").get("ready"):break
        except (OSError,ValueError): pass
        time.sleep(.2)
    else: raise RuntimeError("DRIVER_NOT_READY")
    with tempfile.TemporaryDirectory(prefix="FHG-Gecko-Probe-",ignore_cleanup_errors=True) as profile:
        v=call("POST","session",{"capabilities":{"alwaysMatch":{"browserName":"firefox","moz:firefoxOptions":{
           "binary":str(firefox),"args":["-no-remote","-profile",profile]}}}})
        session=v.get("sessionId")
        if not session: raise RuntimeError("SESSION_NOT_CREATED")
        print("OK_SESSION")
        a=call("POST",f"session/{session}/moz/context",{"context":"chrome"})
        print("CHROME_CONTEXT",str(a)[:60])
        b=call("POST",f"session/{session}/execute/sync",{
            "script":"return typeof ChromeUtils !== 'undefined' && typeof gBrowser !== 'undefined'",
            "args":[]})
        print("PRIVILEGED_OBJECTS",b)
        pre=call("POST",f"session/{session}/execute/sync",{
            "script":"return [typeof Services, typeof ChromeUtils, Services.prefs.getStringPref('extensions.webextensions.uuids','{}').length, Services.prefs.prefHasUserValue('extensions.webextensions.uuids')]", "args":[]})
        print("PRECONDITIONS",pre)
        c=call("POST",f"session/{session}/execute/sync",{
            "script":"const S=Services;const m=JSON.parse(S.prefs.getStringPref('extensions.webextensions.uuids','{}'));return !!m['authenticator@mymindstorm'];",
            "args":[]})
        print("SIGNED_ADDON_UUID_EXISTS",c)
        tz=call("POST",f"session/{session}/execute/sync",{
            "script":"const m=JSON.parse(Services.prefs.getStringPref('extensions.webextensions.uuids','{}'));return Object.keys(m).filter(k=>k.includes('55f61747')||k.includes('authenticator'));",
            "args":[]})
        print("SIGNED_ADDON_INSTALL_IDS",tz)
        timezone=call("POST",f"session/{session}/execute/async",{
            "script":"const done=arguments[arguments.length-1];ChromeUtils.importESModule('resource://gre/modules/AddonManager.sys.mjs').AddonManager.getAddonByID('{55f61747-c3d3-4425-97f9-dfc19a0be23c}').then(a=>done(a?{installed:true,active:a.isActive,signed:a.signedState,disabled:a.appDisabled}:{installed:false})).catch(()=>done({error:'AddonManagerUnavailable'}));",
            "args":[]})
        print("SIGNED_TIMEZONE_ADDON_STATE",timezone)
        openTZ=call("POST",f"session/{session}/execute/sync",{
            "script":"const m=JSON.parse(Services.prefs.getStringPref('extensions.webextensions.uuids','{}'));const id=m['{55f61747-c3d3-4425-97f9-dfc19a0be23c}'];const u='moz-extension://'+id+'/data/options/index.html';gBrowser.selectedBrowser.loadURI(u,{triggeringPrincipal:Services.scriptSecurityManager.getSystemPrincipal()});return !!id;",
            "args":[]})
        print("OPEN_TIMEZONE_OPTIONS",openTZ)
        call("POST",f"session/{session}/moz/context",{"context":"content"})
        url=call("GET",f"session/{session}/url")
        print("TIMEZONE_OPTIONS_PROTOCOL",url.split(":")[0] if isinstance(url,str) else "unknown")
        call("POST",f"session/{session}/moz/context",{"context":"chrome"})
        if c is True:
            d=call("POST",f"session/{session}/execute/sync",{
                "script":"const S=ChromeUtils.importESModule('resource://gre/modules/Services.sys.mjs').Services;const m=JSON.parse(S.prefs.getStringPref('extensions.webextensions.uuids','{}'));const u='moz-extension://'+m['authenticator@mymindstorm']+'/view/import.html?TextImport';gBrowser.selectedTab=gBrowser.addTab(u,{triggeringPrincipal:S.scriptSecurityManager.getSystemPrincipal()});return true;",
                "args":[]})
            print("OPEN_INTERNAL_PAGE",d)
            call("POST",f"session/{session}/moz/context",{"context":"content"})
            time.sleep(1)
            e=call("POST",f"session/{session}/execute/sync",{"script":"return [location.protocol, typeof browser, !!(window.browser && browser.storage)]","args":[]})
            print("STORAGE_API_IN_EXTENSION_CONTEXT",e)
        if session:
            try: call("DELETE",f"session/{session}")
            finally: session=None
finally:
    if session:
        try: call("DELETE",f"session/{session}")
        except Exception:pass
    try:proc.terminate();proc.wait(timeout=3)
    except Exception:proc.kill()
