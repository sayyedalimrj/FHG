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
    private static readonly TimeSpan TotalLimit = TimeSpan.FromSeconds(135);
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
        try
        {
            using var browser = new FirefoxDriverBridge(geckodriver, firefox, profile);
            return Execute(browser, account);
        }
        catch (IOException) { return 20; }
        catch (OperationCanceledException) { return 20; }
        catch (InvalidOperationException) { return 20; }
        catch (Exception) { return 30; }
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
            driver.Click(button);
            Thread.Sleep(350);
            if (!driver.IsPresent("body[data-test-success='yes']")) return 52;
            // Run a real original-addon storage test against the ephemeral
            // profile: only fake credentials, no Internet and no user data.
            string dummyId = Guid.NewGuid().ToString("N");
            var fake = new FhgAccount(dummyId, "test@ci.invalid",
                "NEVER-A-REAL-PASSWORD", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
                "https://example.invalid/unused", DateTimeOffset.UtcNow);
            string first = OriginalAuthenticatorBrowserSync.Sync(driver, new [] { fake });
            if (first != "OK") {
                Console.Error.WriteLine("CI_ORIGINAL_AUTHENTICATOR_FIRST_SYNC:" + first);
                return 54;
            }
            string second = OriginalAuthenticatorBrowserSync.Sync(driver, new [] { fake });
            if (second != "OK") {
                Console.Error.WriteLine("CI_ORIGINAL_AUTHENTICATOR_REPEAT_SYNC:" + second);
                return 55;
            }
            Console.WriteLine("PASS: bundled Firefox W3C protocol and original Authenticator browser.storage API sync");
            return 0;
        }
        catch (Exception) { return 53; }
        finally
        {
            try { Directory.Delete(temporaryProfile, recursive: true); }
            catch { /* Temporary data contains only synthetic values. */ }
        }
    }

    private static int Execute(FirefoxDriverBridge browser, FhgAccount account)
    {
        try {
            string result = OriginalAuthenticatorBrowserSync.Sync(browser, AccountVault.Load());
            // Only sanitized status strings, never contents, leave this process.
            Console.WriteLine("FHG_ORIGINAL_AUTHENTICATOR:" + result);
        }
        catch (Exception) {
            Console.WriteLine("FHG_ORIGINAL_AUTHENTICATOR:UNAVAILABLE");
            // Do not lose browser access if only the 2FA popup failed.
        }
        var clock = Stopwatch.StartNew();
        var submitted = new System.Collections.Generic.HashSet<LoginStage>();
        var whenSubmitted = TimeSpan.Zero;
        bool triedOfficialLogin = false;
        browser.GoTo("https://chatgpt.com/");

        while (clock.Elapsed < TotalLimit)
        {
            Thread.Sleep(600);
            Uri? uri;
            try { uri = browser.CurrentUri(); }
            catch (InvalidOperationException) { continue; }
            var stage = TrustedLoginPage.Classify(uri);
            if (stage == LoginStage.Invalid) return 20; // never follow credentials into foreign origins
            if (HasChallenge(browser)) return 20; // stop for CAPTCHA / provider checks

            if (stage == LoginStage.ChatGptHome)
            {
                if (browser.IsAuthenticatedUi()) return 0;
                var emailAtHome = browser.FindVisible(
                    "form input[name='username'][type='email'], form input[type='email'][autocomplete='email']");
                if (emailAtHome != null)
                {
                    if (!submitted.Add(LoginStage.Email)) { /* already tried */ }
                    else if (!FillAndSubmit(browser, emailAtHome, account.Label, stage))
                        return 20;
                    else whenSubmitted = clock.Elapsed;
                    continue;
                }
                // First landing page can have a Log in button but no input.
                // Navigate to the official ChatGPT login path once.
                if (!triedOfficialLogin && clock.Elapsed > TimeSpan.FromSeconds(5))
                {
                    triedOfficialLogin = true;
                    browser.GoTo("https://chatgpt.com/auth/login");
                    continue;
                }
                if (triedOfficialLogin && clock.Elapsed > TimeSpan.FromSeconds(25) &&
                    submitted.Count == 0)
                    return 20; // no recognizable form; don't delay normal browser
            }
            else if (stage == LoginStage.Email)
            {
                string? email = browser.FindVisible(
                    "form input[name='username'][type='email'], form input[type='email'][autocomplete='email'], form input[type='email']");
                if (email != null && !submitted.Contains(LoginStage.Email))
                {
                    submitted.Add(LoginStage.Email);
                    if (!FillAndSubmit(browser, email, account.Label, stage)) return 20;
                    whenSubmitted = clock.Elapsed;
                }
            }
            else if (stage == LoginStage.Password)
            {
                string? password = browser.FindVisible(
                    "form input[name='current-password'][type='password'], form input[autocomplete*='current-password'][type='password']");
                if (password != null && !submitted.Contains(LoginStage.Password))
                {
                    submitted.Add(LoginStage.Password);
                    if (!FillAndSubmit(browser, password, account.Password, stage)) return 20;
                    whenSubmitted = clock.Elapsed;
                }
            }
            else if (stage == LoginStage.Totp)
            {
                string? otp = browser.FindVisible(
                    "form input[name='code'][autocomplete='one-time-code'], form input[name='code']");
                if (otp != null && !submitted.Contains(LoginStage.Totp))
                {
                    submitted.Add(LoginStage.Totp);
                    // Never send an OTP that will expire during an HTTP
                    // request/redirect: wait for the next time window.
                    while (30 - (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30) < 12)
                        Thread.Sleep(350);
                    string code;
                    try { code = AccountVault.GenerateTotp(account.TotpSeed, DateTimeOffset.UtcNow); }
                    catch { return 20; }
                    if (!FillAndSubmit(browser, otp, code, stage)) return 20;
                    whenSubmitted = clock.Elapsed;
                }
            }
            else if (stage == LoginStage.UnknownOfficial)
            {
                // SSO, provider challenge, account recovery, passkeys or UI
                // changed: leave to the user. Never guess fields or click.
                if (clock.Elapsed > TimeSpan.FromSeconds(7)) return 20;
            }

            // Wrong password/OTP or a stalled form is not retried blindly:
            // repeated submissions can lock the user's account.
            if (submitted.Count > 0 &&
                clock.Elapsed - whenSubmitted > TimeSpan.FromSeconds(22))
            {
                if (stage is LoginStage.Email or LoginStage.Password or LoginStage.Totp ||
                    stage is LoginStage.UnknownOfficial or LoginStage.ChatGptHome)
                    return 20;
            }
        }
        return 20;
    }

    private static bool HasChallenge(FirefoxDriverBridge browser)
    {
        try {
            return browser.IsPresent(
                "iframe[src*='challenges.cloudflare.com'], input[name='cf-turnstile-response'], iframe[title*='CAPTCHA'], [data-sitekey]");
        }
        catch { return false; }
    }

    private static bool FillAndSubmit(FirefoxDriverBridge browser, string elementId,
        string credential, LoginStage expected)
    {
        // Two independent origin checks before sending credentials.
        if (TrustedLoginPage.Classify(browser.CurrentUri()) != expected) return false;
        try
        {
            browser.Fill(elementId, credential);
            if (TrustedLoginPage.Classify(browser.CurrentUri()) != expected) return false;
            if (HasChallenge(browser)) return false;
            var submit = browser.FindVisible("form button[type='submit'], form input[type='submit']");
            if (submit != null) browser.Click(submit);
            else browser.Enter(elementId);
            return true;
        }
        catch (InvalidOperationException)
        {
            // A successful submit can invalidate the element by navigating.
            // Check the next URL on the next loop rather than sending it twice.
            return TrustedLoginPage.Classify(browser.CurrentUri()) != expected;
        }
    }
}
