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
            using var browser = new FirefoxDriverBridge(geckodriver, firefox, profile);
            return Execute(browser, account);
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


    private static void Report(FhgAccount a, string code)
    {
        try { LoginProgress.Write(a.Id, code); }
        catch (IOException) { /* UI state is best-effort, not an auth requirement. */ }
        Console.WriteLine("FHG_LOGIN_STAGE:" + code);
    }

    private static int Execute(FirefoxDriverBridge browser, FhgAccount account)
    {
        try
        {
            string state = OriginalAuthenticatorBrowserSync.Sync(browser, AccountVault.Load());
            Console.WriteLine("FHG_ORIGINAL_AUTHENTICATOR:" + state);
        }
        catch (Exception) { Console.WriteLine("FHG_ORIGINAL_AUTHENTICATOR:UNAVAILABLE"); }

        var attempted = new System.Collections.Generic.HashSet<LoginStage>();
        var timer = Stopwatch.StartNew();
        TimeSpan lastProgress = TimeSpan.Zero;
        int verified = 0, lost = 0;
        string status = "STARTING";

        void State(string code)
        {
            if (status == code) return;
            status = code;
            Report(account, code);
        }

        // Reuse the original tab and close ONLY the setup tabs opened by our
        // own signed extensions; no extra status dialog or dummy new tab.
        try { browser.CloseSetupTabsAndNavigate("https://chatgpt.com/login"); }
        catch (InvalidOperationException)
        {
            State("MANUAL_REQUIRED");
            // Even an upstream navigation error must leave Firefox visible.
        }

        // There is intentionally NO overall timeout. CAPTCHA, provider SSO,
        // failed requests and unfamiliar screens remain available to the user.
        while (true)
        {
            Thread.Sleep(1250);
            Uri? uri;
            try { uri = browser.CurrentUri(); lost = 0; }
            catch (InvalidOperationException)
            {
                if (++lost >= 120) { State("ERROR"); return 30; }
                continue;
            }

            var originStage = TrustedLoginPage.Classify(uri);
            bool official = originStage != LoginStage.Invalid;
            bool atChatGpt = official && uri != null &&
                uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase);

            bool loggedIn = false;
            if (atChatGpt)
            {
                try { loggedIn = browser.IsAuthenticatedUi(); }
                catch (InvalidOperationException) { }
            }
            if (loggedIn)
            {
                if (++verified >= 2) { State("SUCCESS"); return 0; }
                State("VERIFYING");
                continue;
            }
            verified = 0;

            // CAPTCHA detection never triggers window closure or bypass.
            if (official && HasChallenge(browser))
            {
                State("MANUAL_CHALLENGE");
                continue;
            }
            if (!official)
            {
                State("MANUAL_REQUIRED"); // External IdP: user controls it.
                continue;
            }

            // Detect actual input rather than assuming a specific route.
            // The current /login page can show email, password, or OTP.
            LoginStage stage = LoginStage.UnknownOfficial;
            string? field = null;
            try
            {
                field = browser.FindVisible(
                    "input[autocomplete='one-time-code'], input[name='code'], " +
                    "input[name='otp'], input[name='mfa_code'], " +
                    "input[inputmode='numeric'][maxlength='6']");
                if (field != null) stage = LoginStage.Totp;
                else
                {
                    field = browser.FindVisible(
                        "input[type='password'][autocomplete*='current-password'], " +
                        "input[name='password'][type='password'], input[type='password']");
                    if (field != null) stage = LoginStage.Password;
                    else
                    {
                        field = browser.FindVisible(
                            "input[type='email'], input[name='email'], " +
                            "input[id='email'], input[autocomplete='email'], " +
                            "input[autocomplete='username'], input[name='username']");
                        if (field != null) stage = LoginStage.Email;
                    }
                }
            }
            catch (InvalidOperationException) { /* Redirect may be in progress. */ }

            if (field != null && !attempted.Contains(stage))
            {
                // One genuine UI submission per stage. Never race a user who
                // has already typed a different value into the field.
                attempted.Add(stage);
                bool sent = false;
                try
                {
                    string value;
                    if (stage == LoginStage.Email) value = account.Label;
                    else if (stage == LoginStage.Password) value = account.Password;
                    else
                    {
                        // Avoid sending an OTP at the end of its 30s validity.
                        while (30 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 < 12)
                            Thread.Sleep(350);
                        value = AccountVault.GenerateTotp(account.TotpSeed, DateTimeOffset.UtcNow);
                    }
                    sent = SubmitOnce(browser, field, value, stage);
                }
                catch (InvalidOperationException) { /* Navigation/element changed. */ }
                catch (ArgumentException) { /* A broken seed needs manual action. */ }

                lastProgress = timer.Elapsed;
                State(sent ? stage switch
                {
                    LoginStage.Email => "EMAIL_SUBMITTED",
                    LoginStage.Password => "PASSWORD_SUBMITTED",
                    _ => "TOTP_SUBMITTED"
                } : "MANUAL_REQUIRED");
                continue;
            }

            // The user sees a clear manual status only if a stage is stuck or
            // unknown; later stages can still be automated after they appear.
            if (timer.Elapsed - lastProgress > TimeSpan.FromSeconds(35) &&
                status != "MANUAL_REQUIRED" && status != "MANUAL_CHALLENGE" &&
                status != "SUCCESS")
                State("MANUAL_REQUIRED");
        }
    }

    private static bool HasChallenge(FirefoxDriverBridge browser)
    {
        try { return browser.IsPresent(
            "iframe[src*='challenges.cloudflare.com'], " +
            "input[name='cf-turnstile-response'], iframe[title*='CAPTCHA'], [data-sitekey]"); }
        catch (InvalidOperationException) { return false; }
    }

    private static bool SubmitOnce(FirefoxDriverBridge browser,
        string field, string desired, LoginStage kind)
    {
        // No credentials ever go to non-HTTPS or other providers.
        if (TrustedLoginPage.Classify(browser.CurrentUri()) == LoginStage.Invalid)
            return false;
        string? before = browser.ElementValue(field);
        if (!string.IsNullOrEmpty(before) &&
            !string.Equals(before, desired, StringComparison.Ordinal))
            return false; // The human has started editing.
        if (!string.Equals(before, desired, StringComparison.Ordinal))
            browser.Fill(field, desired);
        if (!string.Equals(browser.ElementValue(field), desired, StringComparison.Ordinal))
            return false;
        if (TrustedLoginPage.Classify(browser.CurrentUri()) == LoginStage.Invalid ||
            HasChallenge(browser)) return false;
        string? button = browser.FindVisible(
            "form button[type='submit'], form input[type='submit'], " +
            "button[type='submit'], input[type='submit']");
        if (button == null) return false;
        if (kind == LoginStage.Password)
        {
            Thread.Sleep(800);
            if (TrustedLoginPage.Classify(browser.CurrentUri()) == LoginStage.Invalid ||
                !string.Equals(browser.ElementValue(field), desired, StringComparison.Ordinal))
                return false;
            browser.PointerClick(button);
        }
        else browser.Click(button);
        return true;
    }
}
