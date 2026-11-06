using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

// Operates the EXISTING, signed Authenticator by mymindstorm import page.
// Firefox owns storage.local/storage.sync and their migration / encryption.
// No XPI patching, SQLite/IndexedDB editing or extension replacement.
internal static class AuthenticatorUiImporter
{
    private const string ExtensionId = "authenticator@mymindstorm";
    private const string FindAddonScript = """
        const prefs = Services.prefs;
        const entries = JSON.parse(prefs.getStringPref("extensions.webextensions.uuids", "{}"));
        const id = entries["authenticator@mymindstorm"];
        if (typeof id !== "string" || !/^[a-f0-9-]{36}$/i.test(id)) return "";
        const destination = "moz-extension://" + id + "/view/import.html?TextImport";
        gBrowser.selectedTab = gBrowser.addTab(destination, {
          triggeringPrincipal: Services.scriptSecurityManager.getSystemPrincipal()
        });
        return id;
        """;

    // Do not return other accounts' secrets or extension storage contents to
    // the native program. Return only presence flags for the supplied records.
    private const string CheckImportState = """
        const records = arguments[0], done = arguments[arguments.length-1];
        (async () => {
          try {
            const [local, sync] = await Promise.all([
              chrome.storage.local.get(null), chrome.storage.sync.get(null)
            ]);
            const stored = [...Object.values(local), ...Object.values(sync)];
            const exists = records.map(account => stored.some(item =>
              item && typeof item === "object" &&
              item.type === "totp" &&
              item.issuer === "ChatGPT" &&
              item.account === account.label &&
              item.secret === account.secret
            ));
            done({ok:true,exists});
          } catch {
            done({ok:false,reason:"EXTENSION_STORAGE_UNAVAILABLE"});
          }
        })();
        """;

    public static string Sync(FirefoxDriverBridge driver, IReadOnlyList<FhgAccount> accounts)
    {
        string? uuid = null;
        for (int attempt = 0; attempt < 35; attempt++)
        {
            driver.SetContext("chrome");
            try {
                JsonElement response = driver.ExecuteScript(FindAddonScript);
                uuid = response.ValueKind == JsonValueKind.String ? response.GetString() : null;
            }
            catch (InvalidOperationException) { /* addon may still install */ }
            finally { driver.SetContext("content"); }
            if (!string.IsNullOrWhiteSpace(uuid)) break;
            Thread.Sleep(250);
        }
        if (string.IsNullOrWhiteSpace(uuid)) return "ORIGINAL_ADDON_NOT_INSTALLED";

        bool IsCorrectPage() {
            try {
                Uri? uri = driver.CurrentUri();
                return uri is not null && uri.Scheme == "moz-extension" &&
                    uri.Host.Equals(uuid, StringComparison.OrdinalIgnoreCase) &&
                    uri.AbsolutePath.Equals("/view/import.html", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        for (int i = 0; i < 40 && !IsCorrectPage(); i++) Thread.Sleep(250);
        if (!IsCorrectPage()) return "IMPORT_PAGE_FAILED";

        var entries = accounts.Select(a => new {
            label = a.Label,
            secret = a.TotpSeed.Replace(" ", "").TrimEnd('=').ToUpperInvariant()
        }).ToArray();
        if (entries.Any(e => e.label.Length is < 1 or > 200 ||
            !System.Text.RegularExpressions.Regex.IsMatch(e.secret, @"\A[A-Z2-7]{16,512}\z")))
            return "INVALID_VAULT_RECORD";

        JsonElement check;
        try { check = driver.ExecuteAsyncScript(CheckImportState, entries); }
        catch (InvalidOperationException) { return "STORAGE_CHECK_FAILED"; }
        if (!check.TryGetProperty("ok",out var success) || success.ValueKind != JsonValueKind.True ||
            !check.TryGetProperty("exists",out var exists) ||
            exists.ValueKind != JsonValueKind.Array ||
            exists.GetArrayLength() != entries.Length) return "STORAGE_CHECK_FAILED";

        var missing = new List<string>();
        for (int i=0; i<entries.Length; i++)
        {
            if (exists[i].ValueKind == JsonValueKind.True) continue;
            string safeLabel = entries[i].label.Replace(":", "").Replace("\r", "").Replace("\n", "");
            string uri = "otpauth://totp/" + Uri.EscapeDataString("ChatGPT:" + safeLabel) +
                         "?secret=" + Uri.EscapeDataString(entries[i].secret) +
                         "&issuer=ChatGPT&period=30&digits=6&algorithm=SHA1";
            missing.Add(uri);
        }
        if (missing.Count == 0) return "OK:0";
        if (!IsCorrectPage()) return "IMPORT_PAGE_CHANGED";

        // Fill and submit the signed extension's own official TextImport.vue
        // form, which uses EntryStorage.import to choose local/sync correctly.
        string? input = null;
        for(int i=0;i<30;i++) {
            try { input = driver.FindVisible("#import .import_code textarea"); }
            catch (InvalidOperationException) {}
            if (input is not null) break;
            Thread.Sleep(200);
        }
        if (input is null) return "IMPORT_FORM_UNAVAILABLE";
        driver.Fill(input, string.Join("\n", missing));
        if (!IsCorrectPage()) return "IMPORT_PAGE_CHANGED";
        string? button = driver.FindVisible("#import .import_code ~ button, #import button");
        if (button is null) return "IMPORT_BUTTON_UNAVAILABLE";
        try { driver.Click(button); }
        catch (InvalidOperationException) {
            // This extension alerts success and closes its import window.
            // Do not blindly retry the import; duplicate entries are worse.
        }
        return "SUBMITTED:" + missing.Count;
    }
}
