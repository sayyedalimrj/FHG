using System;
using System.Text.Json;
using System.Threading;

// Configure the unmodified, Mozilla-signed Spoof Timezone by Joue.
// It is installed only into FHG's isolated Firefox; this code changes
// only its own extension-local preferences, not OS or other browsers.
internal static class OriginalTimezoneSync
{
    private const string ExtensionId = "{55f61747-c3d3-4425-97f9-dfc19a0be23c}";
    private const string Zone = "Europe/Berlin";
    private const string OpenOptionsPage = """
        const map = JSON.parse(Services.prefs.getStringPref("extensions.webextensions.uuids","{}"));
        const uuid = map["{55f61747-c3d3-4425-97f9-dfc19a0be23c}"];
        if (typeof uuid !== "string" || !/^[a-f0-9-]{36}$/i.test(uuid)) return "";
        gBrowser.selectedBrowser.loadURI(
            "moz-extension://" + uuid + "/data/options/index.html",
            {triggeringPrincipal: Services.scriptSecurityManager.getSystemPrincipal()});
        return uuid;
        """;

    private const string SetAddonPreferences = """
        const done=arguments[arguments.length-1];
        (async()=>{
          try {
            if (location.protocol !== "moz-extension:" ||
                !chrome?.storage?.local)
              return done({state:"BAD_EXTENSION_CONTEXT"});
            const timezone="Europe/Berlin";
            const name=new Intl.DateTimeFormat("en",{
                timeZone:timezone,timeZoneName:"longOffset"
            }).formatToParts(new Date()).find(p=>p.type==="timeZoneName")?.value;
            const parts=/GMT([+-])(\d{2}):(\d{2})/.exec(name??"");
            const offset=parts ? (parts[1]==="-" ? -1 : 1) *
                (Number(parts[2])*60+Number(parts[3])) : 60;
            await chrome.storage.local.set({
                timezone,offset,active:true,random:false,update:false,
                scope:["*://*/*"],
                whitelist:["*://challenges.cloudflare.com/*"]
            });
            try { await chrome.runtime.sendMessage({method:"update-offset"}); }
            catch { /* The background worker may be sleeping on Firefox startup. */ }
            const saved=await chrome.storage.local.get(["timezone","offset","active","random","update"]);
            done({state:saved.timezone===timezone&&saved.active===true&&
                  saved.random===false&&saved.update===false ? "OK":"VERIFY_FAILED",
                  zone:saved.timezone,offset:saved.offset});
          }
          catch {done({state:"STORAGE_FAILED"});}
        })();
        """;

    internal static string Apply(FirefoxDriverBridge browser)
    {
        string? uuid = null;
        for(int i=0;i<40;i++)
        {
            browser.SetContext("chrome");
            try {
                JsonElement x=browser.ExecuteScript(OpenOptionsPage);
                uuid=x.ValueKind==JsonValueKind.String?x.GetString():null;
            }
            catch(InvalidOperationException) {}
            finally {browser.SetContext("content");}
            if(!string.IsNullOrEmpty(uuid)) break;
            Thread.Sleep(220);
        }
        if (string.IsNullOrEmpty(uuid)) return "SIGNATURE_OR_INSTALL_NOT_READY";
        bool loaded=false;
        for(int i=0;i<35;i++)
        {
            Uri? url=null;
            try { url=browser.CurrentUri(); }
            catch(InvalidOperationException) {}
            if(url?.Scheme=="moz-extension" &&
                url.Host.Equals(uuid,StringComparison.OrdinalIgnoreCase) &&
                url.AbsolutePath=="/data/options/index.html")
            {
                loaded=true;
                break;
            }
            Thread.Sleep(250);
        }
        if(!loaded) return "OPTIONS_PAGE_NOT_READY";
        try {
            JsonElement result=browser.ExecuteAsyncScript(SetAddonPreferences);
            if(result.ValueKind!=JsonValueKind.Object ||
                !result.TryGetProperty("state",out var state) ||
                state.ValueKind!=JsonValueKind.String)
                return "VERIFY_FAILED";
            return state.GetString() ?? "VERIFY_FAILED";
        }
        catch(InvalidOperationException) {return "WEBDRIVER_UNAVAILABLE";}
    }

    internal static bool GermanDaylightSavingRules()
    {
        var january = TimeZoneInfo.ConvertTimeFromUtc(
            new DateTime(2026,1,15,12,0,0,DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"));
        var july = TimeZoneInfo.ConvertTimeFromUtc(
            new DateTime(2026,7,15,12,0,0,DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"));
        return january.Hour==13 && july.Hour==14;
    }
}
