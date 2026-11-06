// FHG Windows startup bridge: delegates all subscriptions, nodes, quota and latency UI
// to the source-modified, separately identified FHG Proxy Windows application.
// FHG only enforces browser-local Firefox policy + the path-scoped WFP guard.
// No separate CLI UI or custom profile parser; the FHG Proxy GUI owns subscriptions.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private const string ProductName = "FHG Browser";
    private static readonly string Root = AppContext.BaseDirectory;
    private static readonly string FirefoxDir = Path.Combine(Root, "Firefox");
    private static readonly string FirefoxExe = Path.Combine(FirefoxDir, "firefox.exe");
    private static readonly string GuardExe = Path.Combine(Root, "FHGGuard.exe");
    private static readonly string GuardLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FHG", "Logs", "guard-launch.log");
    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GuardLog)!);
            File.AppendAllText(GuardLog, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { /* logging must not bypass any network protection */ }
    }
    private static readonly string HiddifyExe = Path.Combine(Root, "Hiddify", "FHGProxy.exe");
    private static string ProfileDir => AccountPaths.FirefoxProfile;

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (!File.Exists(HiddifyExe))
            {
                Warn("The dedicated FHG Proxy interface is missing.\n\nExtract the COMPLETE FHG ZIP, including the Hiddify folder.");
                return 2;
            }

            // Shortcuts always route through FHG.exe, not raw FHGProxy.exe.
            bool proxyOnly = Environment.GetCommandLineArgs().Skip(1)
                .Any(a => a.Equals("--proxy-only", StringComparison.OrdinalIgnoreCase));
            bool accountsOnly = Environment.GetCommandLineArgs().Skip(1)
                .Any(a => a.Equals("--accounts", StringComparison.OrdinalIgnoreCase));
            if (accountsOnly)
            {
                string manager = Path.Combine(Root, "FHGAccountManager.exe");
                if (!File.Exists(manager)) { Warn("Account Manager is missing. Reinstall the complete FHG package."); return 12; }
                Process.Start(new ProcessStartInfo(manager) { WorkingDirectory = Root, UseShellExecute = false });
                return 0;
            }
            // Route a first-use shortcut through the account workspace:
            // its Shown event immediately opens the config file picker.
            // This also avoids asking for elevation before choosing a file.
            if (AccountPaths.ActiveId == null)
            {
                string manager = Path.Combine(Root, "FHGAccountManager.exe");
                if (!File.Exists(manager))
                {
                    Warn("FHG Accounts is missing. Reinstall the complete FHG package.");
                    return 12;
                }
                Process.Start(new ProcessStartInfo(manager)
                {
                    WorkingDirectory = Root, UseShellExecute = false
                });
                return 0;
            }

            bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
            if (elevated)
            {
                // If someone manually selected Run as Administrator, install
                // only the Guard, then EXIT. Never inherit an elevated token
                // into Firefox's sandbox or the user-facing proxy GUI.
                if (!proxyOnly && !EnsureGuard())
                {
                    Warn("The initial network guard installation failed. " +
                         "Firefox remains closed. Details: " + GuardLog);
                    return 6;
                }
                TryCreateDesktopShortcuts();
                Warn("FHG protection is ready. For security, FHG Proxy and " +
                     "Firefox do NOT run as Administrator. Open the FHG Firefox " +
                     "or FHG Proxy desktop shortcut normally.");
                return 0;
            }
            TryCreateDesktopShortcuts();

            // For the normal entry point, ask for elevation of ONLY FHGGuard.
            // The browser and subscription UI remain unelevated; elevating an
            // entire Firefox process would weaken its desktop sandbox.
            if (!proxyOnly && !EnsureGuard())
            {
                Warn("Windows declined or could not verify the FHG network guard. " +
                     "Firefox has not been started. See: " + GuardLog);
                return 6;
            }

            // Both entry points, including --proxy-only, provision the active
            // account's subscription automatically when it changes.
            string? suppliedTicket = AccountPaths.ValidateProvisionTicket();
            FirstRunRequest? firstRun = suppliedTicket == null
                ? ProxyAutomation.Prepare(null)
                : new FirstRunRequest(suppliedTicket, suppliedTicket + ".status", "");
            if (!proxyOnly && firstRun == null &&
                AccountPaths.ActiveId == null && !FirstRunWizard.Completed)
            {
                firstRun = FirstRunWizard.AskForLocalFile();
            }

            if (!StartOfficialHiddify(firstRun))
            {
                if (firstRun is not null) FirstRunWizard.CleanupTicket(firstRun);
                return 3;
            }
            if (proxyOnly && firstRun is null) return 0;

            if (firstRun is not null)
            {
                bool imported = WaitForSubscriptionReceipt(firstRun);
                bool connected = imported && WaitForFhgProxyHttps(TimeSpan.FromSeconds(90));
                if (!connected)
                {
                    FirstRunWizard.CleanupTicket(firstRun);
                    Warn("FHG setup did NOT prove a working subscription and HTTPS proxy connection. " +
                         "Firefox will remain closed to prevent silent direct-traffic fallback.\n\n" +
                         "Use the FHG Proxy shortcut to inspect/import your subscription and run " +
                         "FHG-Connection-Diagnostic.ps1 from the extracted folder.");
                    return 9;
                }
                ProxyAutomation.MarkConfigured();
                FirstRunWizard.MarkCompleted(firstRun);
                if (proxyOnly) return 0;
            }
            else if (!WaitForFhgProxyHttps(TimeSpan.FromSeconds(15)))
            {
                Warn("FHG Proxy is not yet passing HTTPS through SOCKS5 127.0.0.1:12345. " +
                     "Open FHG Proxy, connect a working node, and retry the FHG Firefox shortcut. " +
                     "Firefox was not opened without a verified proxy.");
                return 10;
            }

            if (!File.Exists(FirefoxExe))
            {
                Warn("The bundled Firefox executable is missing. Hiddify remains usable.");
                return 4;
            }
            if (!ConfigureBundledTotp())
            {
                Warn("The bundled Authenticator add-on failed integrity verification. Firefox was not started. " +
                     "Re-extract a clean official FHG release; never use an unknown TOTP add-on.");
                return 8;
            }
            if (!TimezonePolicy.Ensure(FirefoxDir))
            {
                Warn("The bundled signed timezone extension failed integrity verification. " +
                     "FHG Firefox was not started with an unverified timezone setting.");
                return 15;
            }
            if (!VerifyFirefoxPolicy())
            {
                Warn("FHG Firefox proxy policy is missing or changed. Firefox was NOT started.\n\nHiddify can still be used normally.");
                return 5;
            }
            Directory.CreateDirectory(ProfileDir);
            // Entirely manual sign-in. No geckodriver, FHGLogin helper,
            // synthetic Continue clicks, CAPTCHA polling or browser handoff.
            // Firefox itself offers to save credentials after user sign-in.
            if (!EnsureFirefoxPasswordSavingDefaults())
            {
                Warn("Firefox password-saving policy could not be prepared. " +
                     "Your existing account and profile were not modified.");
                return 17;
            }
            var activeId = AccountPaths.ActiveId;
            if (activeId == null)
            {
                Warn("Select a saved FHG account before opening Firefox.");
                return 14;
            }
            InstallChatGptStartTile();
            return OpenOneNativeFirefox(activeId);
        }
        catch (Exception ex)
        {
            Warn("Startup failed (" + ex.GetType().Name + "): " + ex.Message);
            return 1;
        }
    }

    private static bool StartOfficialHiddify(FirstRunRequest? firstRun)
    {
        try
        {
            // A normal installed Hiddify elsewhere on the PC is NOT used or modified.
            // If the bundled instance is already running, leave it alive.
            var owned = ProxyAutomation.OwnedProxyProcesses(HiddifyExe);
            bool alreadyRunning = owned.Length > 0;
            foreach (var p in owned) p.Dispose();
            if (alreadyRunning)
            {
                if (firstRun is not null)
                {
                    // Restart ONLY the verified FHGProxy executable, never the
                    // user's existing separate Hiddify process.
                    Log("FHG account subscription changed; recycling its owned proxy.");
                    if (!ProxyAutomation.StopForNewSubscription(HiddifyExe))
                    {
                        Warn("FHG Proxy could not safely release its reserved ports. " +
                             "Please check its running processes; ordinary Hiddify was not touched.");
                        return false;
                    }
                }
                else
                {
                    ProxyAutomation.ShowProxyWindow(HiddifyExe);
                    return true;
                }
            }

            // Do not try to bind a port already used by the user's regular Hiddify.
            // If it is occupied, fail closed before Firefox can be started.
            if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Any(endpoint => endpoint.Port == 12345 || endpoint.Port == 27178 || endpoint.Port == 26756))
            {
                Warn("FHG cannot start: SOCKS 12345, internal gRPC 27178, or Clash API 26756 is occupied. " +
                     "Do not close your normal Hiddify. Inspect which application owns the port, and retry.");
                return false;
            }

            var childInfo = new ProcessStartInfo(HiddifyExe)
            {
                WorkingDirectory = Path.GetDirectoryName(HiddifyExe)!,
                UseShellExecute = false
            };

            // A separate Hiddify installation may already provide an internet-capable
            // local proxy. Opt-in only: subscription URLs contain private access tokens.
            // FHG's browser NEVER uses this path, only its own guarded 12345 listener.
            bool localBootstrapAvailable = System.Net.NetworkInformation.IPGlobalProperties
                .GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint =>
                    endpoint.Port == 12334 && IPAddress.IsLoopback(endpoint.Address));
            if (localBootstrapAvailable && ProxyAutomation.BootstrapConsent())
            {
                childInfo.Environment["FHG_BOOTSTRAP_HTTP_PROXY"] = "127.0.0.1:12334";
                Log("Previously approved local subscription bootstrap enabled.");
            }
            if (AccountPaths.ActiveId is string id)
            {
                childInfo.Environment["FHG_ACCOUNT_ID"] = id;
            }
            if (firstRun is not null)
            {
                // Pass ONLY an unguessable user-local FILE PATH; never the
                // subscription, password or TOTP key in process arguments.
                childInfo.Environment["FHG_INITIAL_SUB_FILE"] = firstRun.SubscriptionFile;
            }
            using var child = Process.Start(childInfo);
            if (child is null)
            {
                Warn("Could not launch the official Hiddify GUI.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Warn("Could not open Hiddify (" + ex.GetType().Name + "): " + ex.Message);
            return false;
        }
    }

    private static bool WaitForSubscriptionReceipt(FirstRunRequest setup)
    {
        using var progress = new Form
        {
            Text = "FHG — preparing subscription",
            Width = 450,
            Height = 140,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ControlBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };
        var label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Text = "Importing and validating the subscription in FHG Proxy..."
        };
        progress.Controls.Add(label);
        progress.Show();
        try
        {
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(95);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                if (File.Exists(setup.ReceiptFile))
                {
                    string status = File.ReadAllText(setup.ReceiptFile).Trim();
                    if (status == "IMPORTED") return true;
                    if (status == "FAILED") return false;
                }
                Thread.Sleep(400);
            }
            Log("First-run subscription import receipt timed out (no credentials logged).");
            return false;
        }
        finally { progress.Close(); }
    }

    private static bool WaitForFhgProxyHttps(TimeSpan maxWait)
    {
        var end = DateTime.UtcNow + maxWait;
        int attempts = 0;
        while (DateTime.UtcNow < end)
        {
            attempts++;
            try
            {
                // Explicit SOCKS5 route only. No default handler, system proxy
                // or DIRECT fallback is used in this readiness check.
                using var handler = new SocketsHttpHandler
                {
                    Proxy = new WebProxy("socks5://127.0.0.1:12345"),
                    UseProxy = true,
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(4)
                };
                using var client = new HttpClient(handler)
                { Timeout = TimeSpan.FromSeconds(8) };
                using var response = client.GetAsync("https://example.com/",
                    HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 400)
                {
                    Log("FHG SOCKS5 passed HTTPS check. No credentials were transmitted.");
                    return true;
                }
            }
            catch (Exception e)
            {
                if (attempts % 4 == 1)
                    Log("FHG SOCKS5 HTTPS probe failed: " + e.GetType().Name);
            }
            Application.DoEvents();
            Thread.Sleep(1200);
        }
        Log("FHG SOCKS5 HTTPS probe did not succeed.");
        return false;
    }

    // Use the locally bundled, Mozilla-signed TOTP add-on when present, so
    // the extension is available without network access on first Firefox start.
    // Never embed TOTP secrets in any policy, file or launch argument.
    private static bool ConfigureBundledTotp()
    {
        try
        {
            string addon = Path.Combine(FirefoxDir, "distribution", "fhg-authenticator.xpi");
            if (!File.Exists(addon)) { Log("Signed Authenticator XPI absent. FHG refuses to use an incomplete package."); return false; }

            string checksum = addon + ".sha256";
            if (!File.Exists(checksum)) return false;
            string expected = File.ReadAllText(checksum)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(addon)));
            if (expected.Length != 64 || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                Log("Bundled TOTP add-on checksum mismatch; refusing to install it.");
                return false;
            }

            string policyPath = Path.Combine(FirefoxDir, "distribution", "policies.json");
            var doc = JsonNode.Parse(File.ReadAllText(policyPath));
            if (doc?["policies"] is not JsonObject policies) return false;
            if (policies["ExtensionSettings"] is not JsonObject extSettings) return false;
            if (extSettings["authenticator@mymindstorm"] is not JsonObject authenticator) return false;
            string localUri = new Uri(addon).AbsoluteUri;
            if (authenticator["installation_mode"]?.GetValue<string>() != "force_installed") return false;
            if (authenticator["install_url"]?.GetValue<string>() == localUri) return true;
            authenticator["install_url"] = localUri;
            File.WriteAllText(policyPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Log("Mozilla-signed Authenticator forced into Firefox via local XPI; digest verified.");
            return true;
        }
        catch (Exception e)
        {
            Log("TOTP packaging verification failed (" + e.GetType().Name + "): " + e.Message);
            return false;
        }
    }

    private static bool SyncOriginalAuthenticator(string activeId)
    {
        string helperExe = Path.Combine(Root, "FHGAuthenticatorSync.exe");
        if (!File.Exists(helperExe)) { Log("Original Authenticator Sync executable missing."); return false; }
        try
        {
            var info = new ProcessStartInfo(helperExe)
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("--account");
            info.ArgumentList.Add(activeId);
            using var process = Process.Start(info);
            if (process is null) return false;
            if (!process.WaitForExit(25000))
            {
                try { process.Kill(); } catch { }
                Log("Original Authenticator Sync timeout.");
                return false;
            }
            // The helper prints only statuses and never emits secrets or OTPs.
            Log("Original Authenticator sync status " + process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Exception e)
        {
            Log("Original Authenticator Sync failed (" + e.GetType().Name + ").");
            return false;
        }
    }

    private static bool EnsureFirefoxPasswordSavingDefaults()
    {
        try
        {
            string path = Path.Combine(FirefoxDir, "distribution", "policies.json");
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node?["policies"] is not JsonObject policies) return false;
            // Only offer to save credentials by DEFAULT: the user remains
            // free to decline the Save prompt or disable the feature.
            // Never inject DPAPI secrets into login databases, policies or JS.
            bool changed = false;
            if (policies["OfferToSaveLoginsDefault"]?.ToJsonString() != "true")
            {
                policies["OfferToSaveLoginsDefault"] = true;
                changed = true;
            }
            if (policies["OfferToSaveLogins"]?.ToJsonString() == "false")
            {
                policies.Remove("OfferToSaveLogins");
                changed = true;
            }
            if (policies["PasswordManagerEnabled"]?.ToJsonString() == "false")
            {
                // An old policy must not block Firefox's own password UI.
                policies["PasswordManagerEnabled"] = true;
                changed = true;
            }
            if (changed)
                File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions {
                    WriteIndented = true
                }));
            return VerifyFirefoxPolicy();
        }
        catch (Exception e)
        {
            Log("Firefox password manager policy unavailable (" + e.GetType().Name + ").");
            return false;
        }
    }

    private static bool IsProfileInUse(string profile)
    {
        // Firefox holds a native handle on parent.lock while its profile is
        // running. Never delete this lock: doing so can corrupt the session.
        string path = Path.Combine(profile, "parent.lock");
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.None);
            return false; // stale unlocked file: safe to let Firefox decide
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static int OpenOneNativeFirefox(string accountId)
    {
        // One launcher per account at a time. Repeated clicks must never
        // produce fresh auth tabs or attempt to steal another profile's lock.
        using var mutex = new Mutex(false, @"Local\FHG_Firefox_" + accountId);
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                Log("Firefox launcher already active for selected FHG account.");
                return 0;
            }
            if (IsProfileInUse(ProfileDir))
            {
                Log("Firefox is already open with the selected profile; no duplicate tab.");
                try { LoginProgress.Write(accountId, "MANUAL_FIREFOX"); } catch { }
                return 0;
            }
            var info = new ProcessStartInfo(FirefoxExe)
            {
                WorkingDirectory = FirefoxDir,
                UseShellExecute = false
            };
            info.ArgumentList.Add("-no-remote");
            info.ArgumentList.Add("-profile");
            info.ArgumentList.Add(ProfileDir);
            info.ArgumentList.Add("https://chatgpt.com/login");
            using var firefox = Process.Start(info);
            if (firefox == null)
            {
                Log("Native Firefox launch failed.");
                return 7;
            }
            try { LoginProgress.Write(accountId, "MANUAL_FIREFOX"); } catch { }
            // Never retry browser startup or add further login tabs.
            // Child/helper startup can exit quickly while Firefox remains
            // alive, so accept an existing locked profile as success.
            if (firefox.WaitForExit(2500))
            {
                if (IsProfileInUse(ProfileDir))
                {
                    Log("Firefox profile is active in an existing process.");
                    return 0;
                }
                Log("Native Firefox exited immediately without an active profile.");
                return 7;
            }
            Log("Native Firefox launched; login and Firefox Save Password prompt are user-driven.");
            // Keep per-account mutex owned until Firefox exits, preventing
            // repeated Start clicks from spawning additional login tabs.
            firefox.WaitForExit();
            return 0;
        }
        catch (Exception e)
        {
            Log("Native Firefox error type: " + e.GetType().Name);
            return 7;
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private static void InstallChatGptStartTile()
    {
        try
        {
            // The first-run preference is non-destructive for an existing user.js.
            string userJs = Path.Combine(ProfileDir, "user.js");
            if (File.Exists(userJs)) return;
            string pinned = JsonSerializer.Serialize(new[]
            {
                new { url = "https://chatgpt.com/", label = "ChatGPT" }
            });
            File.WriteAllText(userJs,
                "user_pref(\"browser.newtabpage.pinned\", " +
                JsonSerializer.Serialize(pinned) + ");" + Environment.NewLine);
            Log("Created ChatGPT pinned New Tab tile for a fresh FHG profile.");
        }
        catch (Exception e)
        {
            Log("ChatGPT New Tab setup failed: " + e.Message);
        }
    }

    private static bool VerifyFirefoxPolicy()
    {
        try
        {
            string path = Path.Combine(FirefoxDir, "distribution", "policies.json");
            using var parsed = JsonDocument.Parse(File.ReadAllText(path));
            var policies = parsed.RootElement.GetProperty("policies");
            var proxy = policies.GetProperty("Proxy");

            // Do not touch the user's real Firefox installation or browser
            // profile. Every FHG account lives in %LOCALAPPDATA%\FHG.
            var isolatedExe = Path.GetFullPath(FirefoxExe);
            var isolatedDir = Path.GetFullPath(FirefoxDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var isolatedProfile = Path.GetFullPath(ProfileDir);
            var fhgProfileRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FHG"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!isolatedExe.StartsWith(isolatedDir, StringComparison.OrdinalIgnoreCase) ||
                !isolatedProfile.StartsWith(fhgProfileRoot, StringComparison.OrdinalIgnoreCase))
            {
                Log("Refusing to launch a Firefox executable or profile outside isolated FHG directories.");
                return false;
            }

            return policies.GetProperty("DisableProfileImport").GetBoolean()
                && policies.GetProperty("DisableDefaultBrowserAgent").GetBoolean()
                && policies.GetProperty("DefaultBrowserSettingEnabled").GetBoolean() == false
                && policies.GetProperty("DontCheckDefaultBrowser").GetBoolean()
                && proxy.GetProperty("Mode").GetString() == "manual"
                && proxy.GetProperty("Locked").GetBoolean()
                && proxy.GetProperty("SOCKSProxy").GetString() == "127.0.0.1:12345"
                && proxy.GetProperty("SOCKSVersion").GetInt32() == 5
                && proxy.GetProperty("UseProxyForDNS").GetBoolean()
                && proxy.GetProperty("Passthrough").GetString() == "";
        }
        catch { return false; }
    }

    private static bool GuardStatus()
    {
        if (!File.Exists(GuardExe) || !File.Exists(FirefoxExe))
        {
            Log("Guard status cannot run: required executable is missing.");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(GuardExe)
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("status");
            psi.ArgumentList.Add(FirefoxDir);
            using var process = Process.Start(psi);
            if (process is null) { Log("Guard status process did not start."); return false; }
            if (!process.WaitForExit(20000))
            {
                Log("Guard status timed out after 20 seconds.");
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();
            Log("Guard status exit=" + process.ExitCode +
                ", stdout=" + output + ", stderr=" + error);
            return process.ExitCode == 0;
        }
        catch (Exception e)
        {
            Log("Guard status exception: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    private static bool EnsureGuard()
    {
        Log("Checking per-application WFP protection for: " + FirefoxDir);
        if (GuardStatus()) return true;
        if (!File.Exists(GuardExe)) { Log("Guard EXE missing."); return false; }

        bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        // Single UAC confirmation, only when an install/repair is actually
        // necessary. On an already protected system no elevation is requested.
        Log(isAdmin
            ? "Guard repair requested by already elevated caller."
            : "Requesting UAC elevation for FHGGuard only, not for Firefox.");

        try
        {
            var psi = new ProcessStartInfo(GuardExe)
            {
                WorkingDirectory = Root,
                UseShellExecute = !isAdmin,
                Verb = isAdmin ? "" : "runas",
                CreateNoWindow = isAdmin
            };
            psi.ArgumentList.Add("install");
            psi.ArgumentList.Add(FirefoxDir);
            using var process = Process.Start(psi);
            if (process is null) { Log("Elevated guard process did not start."); return false; }
            process.WaitForExit();
            Log("Guard install exit=" + process.ExitCode + (isAdmin ? " (already elevated)" : " (UAC-approved)"));
            if (process.ExitCode != 0) return false;

            bool verified = GuardStatus();
            if (!verified)
                Log("Elevated install succeeded but unelevated WFP verification failed. Check Authenticated Users read ACL and BFE status.");
            return verified;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED is a declined UAC prompt, not a WFP regression.
            Log("Windows administrator approval was canceled (1223). FHG cannot safely open Firefox until the guard is installed.");
            return false;
        }
        catch (Exception e)
        {
            Log("Elevated guard exception: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    private static void TryCreateDesktopShortcuts()
    {
        // The signed installer already manages desktop and Start Menu links.
        // Avoid duplicate icons under CommonDesktop after first-run elevation.
        if (File.Exists(Path.Combine(Root, "unins000.exe")))
        {
            Log("FHG installer manages shortcuts; launcher will not duplicate them.");
            return;
        }
        // If launched with an alternate Administrator identity the per-user
        // Desktop would point to the administrator's account instead of the
        // person who requested FHG. Install to the public Desktop for that
        // one elevated launch; future unelevated launches will reuse it.
        bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        string ownDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string desktop = elevated && Directory.Exists(publicDesktop) ? publicDesktop : ownDesktop;
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            Log("Desktop shortcuts unavailable: no writable Desktop folder could be resolved.");
            return;
        }
        Log("Creating FHG shortcuts in " + (desktop == publicDesktop ? "CommonDesktop" : "UserDesktop"));

        Type? cls = Type.GetTypeFromProgID("WScript.Shell");
        if (cls is null)
        {
            Log("Desktop shortcut: Windows Script Host COM class is unavailable.");
            return;
        }

        // Different names AND different native icons; both start the trusted
        // launcher, never expose the internal raw proxy binary directly.
        foreach (var entry in new[] {
            (Name: "FHG Firefox", Args: "", Description: "FHG isolated Firefox browser (ChatGPT)", Icon: FirefoxExe),
            (Name: "FHG Proxy", Args: "--proxy-only", Description: "FHG Proxy: subscriptions, nodes and latency", Icon: HiddifyExe),
            (Name: "FHG Accounts", Args: "--accounts", Description: "Switch ChatGPT account and subscription", Icon: Path.Combine(Root, "FHGAccountManager.exe"))
        })
        {
            string link = Path.Combine(desktop, entry.Name + ".lnk");
            try
            {
                // Don't make duplicate icons on a user's Desktop after the
                // elevated first-run created an all-users desktop shortcut.
                if (!elevated && File.Exists(Path.Combine(publicDesktop, entry.Name + ".lnk")) &&
                    !File.Exists(link))
                {
                    Log("Shortcut already visible from CommonDesktop: " + entry.Name);
                    continue;
                }
                dynamic shell = Activator.CreateInstance(cls)!;
                dynamic shortcut = shell.CreateShortcut(link);
                if (File.Exists(link))
                {
                    string previous = shortcut.TargetPath as string ?? "";
                    string filename = Path.GetFileName(previous);
                    if (!filename.Equals("FHG.exe", StringComparison.OrdinalIgnoreCase) &&
                        !filename.Equals("FHGProxy.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("Shortcut left unchanged because it belongs to another application: " + entry.Name);
                        continue;
                    }
                }
                shortcut.TargetPath = Path.Combine(Root, "FHG.exe");
                shortcut.Arguments = entry.Args;
                shortcut.WorkingDirectory = Root;
                shortcut.IconLocation = (File.Exists(entry.Icon) ? entry.Icon : Path.Combine(Root, "FHG.exe")) + ",0";
                shortcut.Description = entry.Description;
                shortcut.Save();
                if (!File.Exists(link)) Log("Shortcut not created despite COM Save(): " + entry.Name);
                else Log("Desktop shortcut ready: " + entry.Name);
            }
            catch (Exception e)
            {
                Log("Desktop shortcut " + entry.Name + " error: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    private static void Warn(string text) =>
        MessageBox.Show(text, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
