using System;
using System.Text.Json;

// Offline, Firefox-owned password-manager import. The pre-existing FHG vault
// already holds the credentials encrypted under the Windows current user.
// No website is visited and no secret is written to temp files, browser
// policies, command-line arguments, logs or source control. Firefox itself
// encrypts imported logins with the existing profile's NSS key store.
internal static class FirefoxPasswordStore
{
    private const string Script = """
        const entry = arguments[0];
        const done = arguments[arguments.length - 1];
        (async () => {
          try {
            if (!entry || typeof entry.email !== "string" ||
                typeof entry.password !== "string" ||
                !entry.email.includes("@") || !entry.password)
              return done({state:"INVALID"});
            const origins = [
              "https://auth.openai.com",
              "https://chatgpt.com"
            ];
            let changed = 0;
            for (const origin of origins) {
              const existing = (await Services.logins.getAllLogins())
                .find(x => x.origin === origin &&
                           x.httpRealm == null &&
                           x.username === entry.email);
              const login = Cc["@mozilla.org/login-manager/loginInfo;1"]
                .createInstance(Ci.nsILoginInfo);
              // Empty form-action origin accepts changing official
              // first-party actions, without needing a live website.
              login.init(origin, "", null, entry.email, entry.password,
                         "username", "password");
              if (existing) {
                if (existing.password !== entry.password) {
                  await Services.logins.modifyLoginAsync(existing, login);
                  changed++;
                }
              } else {
                await Services.logins.addLoginAsync(login);
                changed++;
              }
            }
            const stored = await Services.logins.getAllLogins();
            const verified = origins.every(origin => stored.some(x =>
              x.origin === origin && x.username === entry.email &&
              x.password === entry.password && x.httpRealm == null));
            done({state: verified ? "OK" : "VERIFY_FAILED", changed});
          } catch {
            // Never return the error: NSS errors can contain profile
            // details, and the credential must not escape via diagnostics.
            done({state:"FIREFOX_MANAGER_UNAVAILABLE"});
          }
        })();
        """;

    internal static bool Import(FirefoxDriverBridge browser, string email,
        string password)
    {
        browser.SetContext("chrome");
        try
        {
            JsonElement result = browser.ExecuteAsyncScript(Script,
                new { email, password });
            return result.ValueKind == JsonValueKind.Object &&
                   result.TryGetProperty("state", out var state) &&
                   state.ValueKind == JsonValueKind.String &&
                   state.GetString() == "OK";
        }
        finally { browser.SetContext("content"); }
    }
}
