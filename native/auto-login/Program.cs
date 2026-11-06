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
        try
        {
            using var browser = new FirefoxDriverBridge(geckodriver, firefox, profile);
            return Execute(browser, account);
        }
        catch (IOException) { Console.WriteLine("FHG_LOGIN_STAGE:IO_FAILURE"); return 20; }
        catch (OperationCanceledException) { Console.WriteLine("FHG_LOGIN_STAGE:CANCELED"); return 20; }
        catch (InvalidOperationException) { Console.WriteLine("FHG_LOGIN_STAGE:DRIVER_FAILURE"); return 20; }
        catch (Exception) { Console.WriteLine("FHG_LOGIN_STAGE:UNEXPECTED_FAILURE"); return 30; }
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
        // The user-specified, supported entrypoint is /login. Do NOT depend
        // on changing homepage button markup or deep-link to /auth/login.
        Console.WriteLine("FHG_LOGIN_STAGE:DIRECT_LOGIN_ENTRY");
        browser.GoTo("https://chatgpt.com/login");

        while (clock.Elapsed < TotalLimit)
        {
            Thread.Sleep(600);
            Uri? uri;
            try { uri = browser.CurrentUri(); }
            catch (InvalidOperationException) { continue; }
            var stage = TrustedLoginPage.Classify(uri);
            if (stage == LoginStage.Invalid)
            {
                Console.WriteLine("FHG_LOGIN_STAGE:UNTRUSTED_OR_UNAVAILABLE_ORIGIN");
                return 20; // never follow credentials into foreign origins
            }
            if (HasChallenge(browser))
            {
                Console.WriteLine("FHG_LOGIN_STAGE:PROVIDER_CHALLENGE");
                return 20; // provider challenge remains user-driven
            }

            if (stage == LoginStage.ChatGptHome)
            {
                if (browser.IsAuthenticatedUi()) return 0;
                // /login can redirect to the homepage if authentication or
                // an upstream access check already decided the destination.
                // Never hunt for a homepage button: keep the direct /login
                // entrypoint and let the launcher show it for manual login.
                if (clock.Elapsed > TimeSpan.FromSeconds(45))
                {
                    Console.WriteLine("FHG_LOGIN_STAGE:REDIRECTED_HOME_WITHOUT_SESSION");
                    return 20;
                }
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
                    Console.WriteLine("FHG_LOGIN_STAGE:EMAIL_SUBMITTED");
                }
                else if (email == null && submitted.Count == 0 &&
                         clock.Elapsed > TimeSpan.FromSeconds(90))
                {
                    Console.WriteLine("FHG_LOGIN_STAGE:LOGIN_FORM_NOT_RENDERED");
                    return 20;
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
                    Console.WriteLine("FHG_LOGIN_STAGE:PASSWORD_SUBMITTED");
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
                    Console.WriteLine("FHG_LOGIN_STAGE:TOTP_SUBMITTED");
                }
            }
            else if (stage == LoginStage.UnknownOfficial)
            {
                // SSO, provider challenge, account recovery, passkeys or UI
                // changed: leave to the user. Never guess fields or click.
                if (clock.Elapsed > TimeSpan.FromSeconds(50))
                {
                    Console.WriteLine("FHG_LOGIN_STAGE:UNKNOWN_OFFICIAL_PAGE");
                    return 20;
                }
            }

            // Wrong password/OTP or a stalled form is not retried blindly:
            // repeated submissions can lock the user's account.
            if (submitted.Count > 0 &&
                clock.Elapsed - whenSubmitted > TimeSpan.FromSeconds(75))
            {
                if (stage is LoginStage.Email or LoginStage.Password or LoginStage.Totp ||
                    stage is LoginStage.UnknownOfficial or LoginStage.ChatGptHome)
                    return 20;
            }
        }
        Console.WriteLine("FHG_LOGIN_STAGE:TIME_LIMIT");
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
            // Compare the browser input's actual value to the original vault
            // value. Never append the W3C Enter special key (U+E007), or
            // silently insert a stray character on the login page.
            if (!string.Equals(browser.ElementValue(elementId), credential,
                    StringComparison.Ordinal))
                return false;
            if (TrustedLoginPage.Classify(browser.CurrentUri()) != expected) return false;
            if (HasChallenge(browser)) return false;
            var submit = browser.FindVisible(
                "form button[type='submit'], form input[type='submit']");
            if (submit == null) return false; // leave manual form visible
            // The password page is known to produce 400 errors with a
            // synthesized submission on some sessions, while the user's
            // physical Continue click works. Give the web app time to
            // finish processing its input event, then send an actual W3C
            // mouse pointer move/down/up sequence on the rendered button.
            // Never forge form POSTs, invoke JavaScript submit(), or press Enter.
            if (expected == LoginStage.Password)
            {
                Thread.Sleep(1100);
                if (TrustedLoginPage.Classify(browser.CurrentUri()) != expected ||
                    HasChallenge(browser) ||
                    !string.Equals(browser.ElementValue(elementId), credential,
                        StringComparison.Ordinal))
                    return false;
                browser.PointerClick(submit);
                Console.WriteLine("FHG_LOGIN_STAGE:PASSWORD_CONTINUE_MOUSE_CLICKED");
            }
            else
                browser.Click(submit);
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
