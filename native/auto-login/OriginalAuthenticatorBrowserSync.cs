using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Synchronizes through the EXISTING Mozilla-signed mymindstorm extension's own
// browser.storage APIs. Works with storage.local (IndexedDB) and storage.sync
// without reading or directly editing Firefox databases.
internal static class OriginalAuthenticatorBrowserSync
{
    private const string ExtensionId = "authenticator@mymindstorm";

    private const string OpenExtensionScript = """
        // Marionette chrome context already exposes Services on this Firefox.
        const mapping = JSON.parse(Services.prefs.getStringPref("extensions.webextensions.uuids", "{}"));
        const uuid = mapping["authenticator@mymindstorm"];
        if (typeof uuid !== "string" || !/^[0-9a-f-]{36}$/i.test(uuid)) return "";
        const url = "moz-extension://" + uuid + "/view/import.html?TextImport";
        const principal = Services.scriptSecurityManager.getSystemPrincipal();
        gBrowser.selectedTab = gBrowser.addTab(url, { triggeringPrincipal: principal });
        return uuid;
        """;

    private const string BrowserStorageScript = """
        const accounts = arguments[0];
        const done = arguments[arguments.length-1];
        (async () => {
          try {
            if (location.protocol !== "moz-extension:" ||
                typeof browser === "undefined" || !browser.storage)
              return done({status:"WRONG_CONTEXT"});
            const [local, sync] = await Promise.all([
              browser.storage.local.get(null), browser.storage.sync.get(null)
            ]);
            const rawLocation = local.UserSettings?.storageLocation;
            let area;
            if (rawLocation === "local" || rawLocation === "sync") area = rawLocation;
            else {
              const localSize = Object.keys(local).filter(x =>
                x !== "LocalStorage" && x !== "UserSettings").length;
              const syncSize = Object.keys(sync).filter(x =>
                x !== "LocalStorage" && x !== "UserSettings").length;
              area = localSize > syncSize && syncSize === 0 ? "local" : "sync";
            }
            const entries = area === "local" ? local : sync;
            for (const [key, entry] of Object.entries(entries)) {
              if (key === "key" ||
                  (entry && typeof entry === "object" &&
                    (entry.dataType === "Key" ||
                     entry.dataType === "EncOTPStorage" ||
                     entry.encrypted === true))) {
                return done({status:"ENCRYPTED",area});
              }
            }
            const changes = {};
            let count = 0;
            for (const account of accounts) {
              if (!account || typeof account.key !== "string" ||
                  typeof account.label !== "string" ||
                  !/^[A-Z2-7]{16,512}$/.test(account.seed))
                return done({status:"INVALID_ACCOUNT"});
              const prior = entries[account.key];
              if (prior && prior.fhgManaged !== "FHGv2")
                return done({status:"KEY_CONFLICT"});
              const duplicate = Object.entries(entries).some(([key, value]) =>
                key !== account.key && value && typeof value === "object" &&
                value.issuer === "ChatGPT" && value.account === account.label &&
                value.secret === account.seed
              );
              if (duplicate && !prior) continue;
              const oldIndices = Object.values(entries).map(v => Number(v?.index));
              const maxIndex = Math.max(-1, ...oldIndices.filter(Number.isFinite));
              const next = {
                dataType:"OTPStorage", encrypted:false,
                hash:account.key, index: Number.isInteger(prior?.index) ? prior.index : maxIndex + count + 1,
                type:"totp", secret:account.seed, issuer:"ChatGPT",
                account:account.label, period:30, digits:6, algorithm:"SHA1",
                fhgManaged:"FHGv2"
              };
              if (JSON.stringify(prior) !== JSON.stringify(next)) {
                changes[account.key] = next;
                count++;
              }
            }
            // Only new records and previously managed FHG keys are modified.
            // Existing unrelated OTP records are never deleted or overwritten.
            if (Object.keys(changes).length)
              await browser.storage[area].set(changes);
            const after = await browser.storage[area].get(Object.keys(changes));
            for (const key of Object.keys(changes)) {
              if (!after[key] || after[key].secret !== changes[key].secret)
                return done({status:"VERIFY_FAILED",area});
            }
            done({status:"OK",area,count});
          } catch {
            done({status:"STORAGE_API_FAILED"});
          }
        })();
        """;

    public static string Sync(FirefoxDriverBridge driver, IReadOnlyList<FhgAccount> accounts)
    {
        // Existing signed XPI and encrypted vault remain untouched.
        driver.SetContext("chrome");
        string? uuid;
        try {
            var result = driver.ExecuteScript(OpenExtensionScript);
            uuid = result.ValueKind == JsonValueKind.String ? result.GetString() : null;
        }
        finally { driver.SetContext("content"); }

        if (string.IsNullOrWhiteSpace(uuid)) return "EXTENSION_NOT_READY";
        // Firefox may load the import page slowly on the first installation.
        bool loaded = false;
        for (int i=0; i<35; i++)
        {
            try {
                var current = driver.CurrentUri();
                if (current is not null && current.Scheme == "moz-extension" &&
                    current.Host.Equals(uuid, StringComparison.OrdinalIgnoreCase) &&
                    current.AbsolutePath == "/view/import.html") {
                    loaded = true; break;
                }
            }
            catch (InvalidOperationException) {}
            System.Threading.Thread.Sleep(200);
        }
        if (!loaded) return "EXTENSION_PAGE_UNAVAILABLE";

        var records = accounts.Select(a => new {
            key = StableUuid(a.Id),
            label = a.Label,
            seed = a.TotpSeed.Replace(" ", "").TrimEnd('=').ToUpperInvariant()
        }).ToArray();
        if (records.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x.seed, @"\A[A-Z2-7]{16,512}\z")))
            return "INVALID_ACCOUNT";

        // JSON credentials are submitted only to a local, checked moz-extension
        // page via a temporary localhost-only WebDriver session. They are not
        // logged to disk or exposed to websites.
        JsonElement response = driver.ExecuteAsyncScript(BrowserStorageScript, records);
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("status",out var status) ||
            status.ValueKind != JsonValueKind.String)
            return "INVALID_REPLY";
        return status.GetString() ?? "INVALID_REPLY";
    }

    private static string StableUuid(string accountId)
    {
        string h = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "FHG-Authenticator-v2-" + accountId))).ToLowerInvariant();
        return h[..8] + "-" + h.Substring(8,4) + "-4" + h.Substring(13,3)
            + "-8" + h.Substring(17,3) + "-" + h.Substring(20,12);
    }
}
