using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

// Automatic sign-in of a single authorized FHG account in the ORIGINAL,
// Mozilla-signed FHG Firefox. Uses normal W3C WebDriver and the existing vault,
// not a new extension, unsafe page scripts, or an auth API workaround.
internal static class Program
{
    private static readonly TimeSpan TotalLimit = TimeSpan.FromSeconds(360);
    private static readonly string Root = AppContext.BaseDirectory;

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test-driver")
            return SelfTestDriver();
        if (args.Length == 2 && args[0] == "--save-password")
            return OfflinePasswordImport(args[1]);
        // Explicitly reject the removed automated login mode. The bundled
        // FHG Login helper cannot submit passwords, OTPs or CAPTCHA steps.
        return 31;
    }

    // The ONLY invoked production mode: write to Firefox's built-in
    // password manager, offline. The normal ChatGPT sign-in stays 100%
    // human-driven. The old --account automation mode is unreachable from
    // the FHG launcher and is not used for user sign-in.
    private static int OfflinePasswordImport(string accountId)
    {
        if (!AccountVault.ValidId(accountId) ||
            AccountVault.ActiveId() != accountId) return 31;
        FhgAccount? account;
        try { account = AccountVault.Load().SingleOrDefault(a => a.Id == accountId); }
        catch { return 31; }
        if (account == null ||
            !TrustedLoginPage.LooksLikeRealAccount(account.Label))
            return 22;
        string root = AppContext.BaseDirectory;
        string profile = AccountVault.AccountFirefoxDir(accountId);
        string accountRoot = Path.GetFullPath(AccountVault.Root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(profile).StartsWith(accountRoot,
            StringComparison.OrdinalIgnoreCase) || !Directory.Exists(profile))
            return 30;
        try
        {
            // Launch a headless *offline* Firefox session in the same private
            // account profile; this code never navigates to ChatGPT.
            using var browser = new FirefoxDriverBridge(
                Path.Combine(root, "WebDriver", "geckodriver.exe"),
                Path.Combine(root, "Firefox", "firefox.exe"),
                profile, headless: true);
            bool imported = FirefoxPasswordStore.Import(browser,
                account.Label, account.Password);
            Console.WriteLine(imported
                ? "FHG_PASSWORD_STORE:VERIFIED"
                : "FHG_PASSWORD_STORE:UNAVAILABLE");
            return imported ? 0 : 32;
        }
        catch
        {
            Console.WriteLine("FHG_PASSWORD_STORE:UNAVAILABLE");
            return 32;
        }
    }

    private static int SelfTestDriver()
    {
        // CI only: synthetic local data URI, no user vault or network login.
        string temporaryProfile = Path.Combine(Path.GetTempPath(),
            "FHG-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryProfile);
        try
        {
            using var driver = new FirefoxDriverBridge(
                Path.Combine(Root, "WebDriver", "geckodriver.exe"),
                Path.Combine(Root, "Firefox", "firefox.exe"),
                temporaryProfile);
            const string fixture = "<html><body><form onsubmit='event.preventDefault();document.body.setAttribute(\"data-test-success\",\"yes\")'>" +
                "<input name='current-password' type='password' autocomplete='current-password'>" +
                "<button type='submit'>Continue</button></form></body></html>";
            driver.GoTo("data:text/html;charset=utf-8," + Uri.EscapeDataString(fixture));
            string? field = driver.FindVisible("form input[name='current-password'][type='password']");
            if (field == null) return 50;
            driver.Fill(field, "SYNTHETIC-WINDOWS-CI-ONLY");
            string? button = driver.FindVisible("form button[type='submit']");
            if (button == null) return 51;
            driver.PointerClick(button);
            Thread.Sleep(350);
            if (!driver.IsPresent("body[data-test-success='yes']")) return 52;
            // Run a real original-addon storage test against the ephemeral
            // profile: only fake credentials, no Internet and no user data.
            string dummyId = Guid.NewGuid().ToString("N");
            var fake = new FhgAccount(dummyId, "test@ci.invalid",
                "NEVER-A-REAL-PASSWORD", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
                "https://example.invalid/unused", DateTimeOffset.UtcNow);
            if (!FirefoxPasswordStore.Import(driver, fake.Label, fake.Password))
                return 56; // NSS-backed Firefox Login Manager unavailable
            string first = OriginalAuthenticatorBrowserSync.Sync(driver, new [] { fake });
            if (first != "OK:1") {
                Console.Error.WriteLine("CI_ORIGINAL_AUTHENTICATOR_FIRST_SYNC:" + first);
                return 54;
            }
            // End the first W3C session and reopen the SAME Firefox profile
            // to prove the signed extension persisted the synthetic account.
            driver.Dispose();
            Thread.Sleep(1200);
            using var verify = new FirefoxDriverBridge(
                Path.Combine(Root, "WebDriver", "geckodriver.exe"),
                Path.Combine(Root, "Firefox", "firefox.exe"),
                temporaryProfile);
            if (!FirefoxPasswordStore.Import(verify, fake.Label, fake.Password))
                return 57; // imported credential did not persist encrypted
            string second = OriginalAuthenticatorBrowserSync.Sync(verify, new [] { fake });
            if (second != "OK:0") {
                Console.Error.WriteLine("CI_ORIGINAL_AUTHENTICATOR_REPEAT_SYNC:" + second);
                return 55;
            }
            // Prove the EXACT production path works without a visible browser
            // or web login. A signed NSS record remains after two restarts.
            verify.Dispose();
            Thread.Sleep(1200);
            using (var headless = new FirefoxDriverBridge(
                Path.Combine(Root, "WebDriver", "geckodriver.exe"),
                Path.Combine(Root, "Firefox", "firefox.exe"),
                temporaryProfile, headless: true))
            {
                if (!FirefoxPasswordStore.Import(headless, fake.Label, fake.Password))
                    return 58;
            }
            Console.WriteLine("PASS: original Authenticator and encrypted Firefox NSS login persisted through headless offline import");
            return 0;
        }
        catch (Exception) { return 53; }
        finally
        {
            try { Directory.Delete(temporaryProfile, recursive: true); }
            catch { /* Temporary data contains only synthetic values. */ }
        }
    }


}
