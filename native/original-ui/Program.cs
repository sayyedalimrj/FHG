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
    private static readonly string ProfileDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "FHG", "FirefoxProfile");

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

            // UI first: a user can import subscriptions, inspect quotas and select nodes
            // even if browser guard setup has not been completed.
            if (!StartOfficialHiddify()) return 3;
            TryCreateDesktopShortcut();

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
            if (!VerifyFirefoxPolicy())
            {
                Warn("FHG Firefox proxy policy is missing or changed. Firefox was NOT started.\n\nHiddify can still be used normally.");
                return 5;
            }
            if (!EnsureGuard())
            {
                Warn("FHG did not open Firefox because its Windows network guard is not installed or verified.\n\n" +
                     "On the next launch, choose YES to install the FHG guard, then approve the Windows UAC administrator prompt.\n" +
                     "If you cancel UAC, Firefox stays closed to prevent direct traffic.\n\nDetails: " + GuardLog);
                return 6;
            }

            Directory.CreateDirectory(ProfileDir);
            InstallChatGptStartTile();
            var browser = new ProcessStartInfo(FirefoxExe)
            {
                WorkingDirectory = FirefoxDir,
                UseShellExecute = false
            };
            browser.ArgumentList.Add("-no-remote");
            browser.ArgumentList.Add("-profile");
            browser.ArgumentList.Add(ProfileDir);
            browser.ArgumentList.Add("https://chatgpt.com/");

            using var firefox = Process.Start(browser);
            if (firefox is null)
            {
                Warn("Unable to start the FHG Firefox browser.");
                return 7;
            }

            // Hiddify owns its connection lifecycle; the Firefox window remains open
            // when the proxy is disconnected and does not fall back to DIRECT.
            return 0;
        }
        catch (Exception ex)
        {
            Warn("Startup failed (" + ex.GetType().Name + "): " + ex.Message);
            return 1;
        }
    }

    private static bool StartOfficialHiddify()
    {
        try
        {
            // A normal installed Hiddify elsewhere on the PC is NOT used or modified.
            // If the bundled instance is already running, leave it alive.
            string expected = Path.GetFullPath(HiddifyExe);
            bool alreadyRunning = Process.GetProcessesByName("FHGProxy").Any(p =>
            {
                try { return string.Equals(Path.GetFullPath(p.MainModule?.FileName ?? ""),
                                          expected, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
                finally { p.Dispose(); }
            });
            if (alreadyRunning) return true;

            // Do not try to bind a port already used by the user's regular Hiddify.
            // If it is occupied, fail closed before Firefox can be started.
            if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Any(endpoint => endpoint.Port == 12345 || endpoint.Port == 27178))
            {
                Warn("FHG cannot start: either SOCKS 12345 or internal control 27178 is in use. " +
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
            if (localBootstrapAvailable)
            {
                var choice = MessageBox.Show(
                    "An existing local proxy is listening on 127.0.0.1:12334.\n\n" +
                    "Use it ONLY to import/update FHG Proxy subscriptions while FHG is not connected?\n" +
                    "Subscription download URLs may include private credentials; only select Yes if this is YOUR trusted proxy.\n\n" +
                    "After FHG Proxy connects, normal FHG traffic uses 127.0.0.1:12345.",
                    ProductName + " - optional subscription bootstrap",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (choice == DialogResult.Yes)
                {
                    childInfo.Environment["FHG_BOOTSTRAP_HTTP_PROXY"] = "127.0.0.1:12334";
                    Log("User opted in to existing loopback subscription bootstrap at port 12334.");
                }
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
            var proxy = parsed.RootElement.GetProperty("policies").GetProperty("Proxy");
            return proxy.GetProperty("Mode").GetString() == "manual"
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
        var answer = isAdmin ? DialogResult.Yes : MessageBox.Show(
            "FHG must install read-only-verifiable Windows network protection for its own Firefox. " +
            "This never changes Windows Firewall profiles or your regular Firefox." +
            "\n\nAdministrator approval is needed only if rules are missing or outdated." +
            "\n\nThe reason for any failure is saved to:\n" + GuardLog +
            "\n\nInstall/repair the guard now?",
            ProductName + " - browser network protection",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
        if (answer != DialogResult.Yes) { Log("Guard installation declined by user."); return false; }

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

    private static void TryCreateDesktopShortcut()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop)) return;
            string link = Path.Combine(desktop, "FHG Browser.lnk");
            // Never overwrite the user's existing shortcut.
            if (File.Exists(link)) return;
            Type? cls = Type.GetTypeFromProgID("WScript.Shell");
            if (cls is null) return;
            dynamic shell = Activator.CreateInstance(cls)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = Path.Combine(Root, "FHG.exe");
            shortcut.WorkingDirectory = Root;
            shortcut.IconLocation = Path.Combine(Root, "FHG.exe") + ",0";
            shortcut.Description = "FHG Proxy (independent Hiddify-based client) + isolated Firefox";
            shortcut.Save();
        }
        catch { /* no shortcut should prevent the browser from starting */ }
    }

    private static void Warn(string text) =>
        MessageBox.Show(text, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
