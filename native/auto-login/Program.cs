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
        if (args.Length != 2 || args[0] != "--account" ||
            !AccountVault.ValidId(args[1]))
            return 31;
        if (AccountVault.ActiveId() != args[1])
            return 31;
        FhgAccount? account;
        try { account = AccountVault.Load().SingleOrDefault(x => x.Id == args[1]); }
        catch { return 31; }
        if (account is null || !TrustedLoginPage.LooksLikeRealAccount(account.Label))
            return 22; // demo account or missing login data

        string firefox = Path.Combine(Root, "Firefox", "firefox.exe");
        string geckodriver = Path.Combine(Root, "WebDriver", "geckodriver.exe");
        string profile = AccountVault.AccountFirefoxDir(args[1]);
        string fhgAccounts = Path.GetFullPath(AccountVault.Root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!Path.GetFullPath(profile).StartsWith(fhgAccounts, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(firefox) || !File.Exists(geckodriver))
            return 30;
        // Guard and authenticated SOCKS5 readiness must have been verified
        // by the caller FHG.exe before this helper is started.
        if (!Directory.Exists(profile)) return 30;
        try { LoginProgress.Write(account.Id, "STARTING"); } catch { }
        try
        {
            using var ui = new LoginProgressWindow();
            using var browser = new FirefoxDriverBridge(geckodriver, firefox, profile);
            return Execute(browser, account, ui);
        }
        catch (Exception e)
        {
            Console.WriteLine("FHG_LOGIN_STAGE:DRIVER_OR_STARTUP_FAILURE");
            try { LoginProgress.Write(account.Id, "ERROR"); } catch { }
            return 30;
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
            string second = OriginalAuthenticatorBrowserSync.Sync(verify, new [] { fake });
            if (second != "OK:0") {
                Console.Error.WriteLine("CI_ORIGINAL_AUTHENTICATOR_REPEAT_SYNC:" + second);
                return 55;
            }
            Console.WriteLine("PASS: bundled Firefox WebDriver and original Authenticator import-page automation");
            return 0;
        }
        catch (Exception) { return 53; }
        finally
        {
            try { Directory.Delete(temporaryProfile, recursive: true); }
            catch { /* Temporary data contains only synthetic values. */ }
        }
    }

    private static void Report(FhgAccount a, LoginProgressWindow ui, string code)
    {
        LoginProgress.Write(a.Id, code);
        ui.Set(LoginProgress.Display(code));
        Console.WriteLine("FHG_LOGIN_STAGE:" + code);
    }

    private static int Execute(FirefoxDriverBridge browser, FhgAccount account,
        LoginProgressWindow ui)
    {
        try
        {
            string state = OriginalAuthenticatorBrowserSync.Sync(browser, AccountVault.Load());
            Console.WriteLine("FHG_ORIGINAL_AUTHENTICATOR:" + state);
        }
        catch (Exception) { Console.WriteLine("FHG_ORIGINAL_AUTHENTICATOR:UNAVAILABLE"); }

        bool emailTried = false, manual = false;
        int verified = 0, lost = 0;
        var clock = Stopwatch.StartNew();
        try { browser.GoTo("https://chatgpt.com/login"); }
        catch (InvalidOperationException) { manual = true; }
        Report(account, ui, manual ? "MANUAL_REQUIRED" : "STARTING");

        // No timeout: only verified login or an EXPLICIT user cancel can end
        // an active browser session. No automatic password/TOTP/SSO submission.
        while (true)
        {
            if (ui.CancelRequested) { Report(account, ui, "CANCELED"); return 21; }
            Thread.Sleep(1300);
            Uri? uri;
            try { uri = browser.CurrentUri(); lost = 0; }
            catch (InvalidOperationException)
            {
                if (++lost >= 12) { Report(account, ui, "ERROR"); return 30; }
                continue;
            }
            LoginStage stage = TrustedLoginPage.Classify(uri);
            bool authenticated = false;
            if (uri != null && stage != LoginStage.Invalid &&
                uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase))
            {
                try { authenticated = browser.IsAuthenticatedUi(); }
                catch (InvalidOperationException) { }
            }
            if (authenticated)
            {
                if (++verified >= 2)
                {
                    Report(account, ui, "SUCCESS");
                    Thread.Sleep(1000);
                    return 0;
                }
                if (LoginProgress.Read(account.Id) != "VERIFYING")
                    Report(account, ui, "VERIFYING");
                continue;
            }
            verified = 0;

            bool challenge = stage != LoginStage.Invalid && HasChallenge(browser);
            if (challenge)
            {
                manual = true;
                if (LoginProgress.Read(account.Id) != "MANUAL_CHALLENGE")
                    Report(account, ui, "MANUAL_CHALLENGE");
                continue;
            }

            if (!manual && !emailTried && stage == LoginStage.Email)
            {
                try
                {
                    string? email = browser.FindVisible(
                        "form input[name='username'][type='email'], " +
                        "form input[type='email'][autocomplete='email'], form input[type='email']");
                    if (email != null)
                    {
                        emailTried = true;
                        bool sent = SubmitEmailOnce(browser, email, account.Label);
                        manual = true; // user owns password, CAPTCHA, 2FA, SSO
                        Report(account, ui, sent ? "EMAIL_SUBMITTED" : "MANUAL_REQUIRED");
                        continue;
                    }
                }
                catch (InvalidOperationException)
                {
                    manual = true;
                    Report(account, ui, "MANUAL_REQUIRED");
                    continue;
                }
            }

            // Unexpected identity-provider origins are NEVER automated; the
            // human may navigate them, and the driver stays connected.
            if (!manual && (stage != LoginStage.Email ||
                clock.Elapsed > TimeSpan.FromSeconds(30)))
                manual = true;
            var current = LoginProgress.Read(account.Id);
            if (manual && current != "MANUAL_REQUIRED" &&
                (current != "EMAIL_SUBMITTED" || stage != LoginStage.Email))
                Report(account, ui, "MANUAL_REQUIRED");
        }
    }

    private static bool HasChallenge(FirefoxDriverBridge browser)
    {
        try { return browser.IsPresent(
            "iframe[src*='challenges.cloudflare.com'], " +
            "input[name='cf-turnstile-response'], iframe[title*='CAPTCHA'], [data-sitekey]"); }
        catch (InvalidOperationException) { return false; }
    }

    private static bool SubmitEmailOnce(FirefoxDriverBridge browser,
        string elementId, string email)
    {
        if (TrustedLoginPage.Classify(browser.CurrentUri()) != LoginStage.Email)
            return false;
        browser.Fill(elementId, email);
        if (!string.Equals(browser.ElementValue(elementId), email,
            StringComparison.Ordinal)) return false;
        if (TrustedLoginPage.Classify(browser.CurrentUri()) != LoginStage.Email ||
            HasChallenge(browser)) return false;
        string? submit = browser.FindVisible(
            "form button[type='submit'], form input[type='submit']");
        if (submit == null) return false;
        browser.Click(submit);
        return true;
    }
}
