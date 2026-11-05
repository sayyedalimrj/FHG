// FHG Windows startup bridge: delegates all subscriptions, nodes, quota and latency UI
// to the UNMODIFIED official Hiddify Windows Portable application.
// FHG only enforces browser-local Firefox policy + the path-scoped WFP guard.
// No Hiddify Core CLI, no custom profile parser, and no duplicate proxy GUI.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    private const string ProductName = "FHG Browser";
    private static readonly string Root = AppContext.BaseDirectory;
    private static readonly string FirefoxDir = Path.Combine(Root, "Firefox");
    private static readonly string FirefoxExe = Path.Combine(FirefoxDir, "firefox.exe");
    private static readonly string GuardExe = Path.Combine(Root, "FHGGuard.exe");
    private static readonly string HiddifyExe = Path.Combine(Root, "Hiddify", "Hiddify.exe");
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
                Warn("The original Hiddify interface is missing.\n\nExtract the COMPLETE FHG ZIP, including the Hiddify folder.");
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
            if (!VerifyFirefoxPolicy())
            {
                Warn("FHG Firefox proxy policy is missing or changed. Firefox was NOT started.\n\nHiddify can still be used normally.");
                return 5;
            }
            if (!EnsureGuard())
            {
                Warn("FHG did not start Firefox because its Windows network guard could not be verified.\n\nThe original Hiddify window remains available.");
                return 6;
            }

            Directory.CreateDirectory(ProfileDir);
            var browser = new ProcessStartInfo(FirefoxExe)
            {
                WorkingDirectory = FirefoxDir,
                UseShellExecute = false
            };
            browser.ArgumentList.Add("-no-remote");
            browser.ArgumentList.Add("-profile");
            browser.ArgumentList.Add(ProfileDir);
            browser.ArgumentList.Add("about:blank");

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
            bool alreadyRunning = Process.GetProcessesByName("Hiddify").Any(p =>
            {
                try { return string.Equals(Path.GetFullPath(p.MainModule?.FileName ?? ""),
                                          expected, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
                finally { p.Dispose(); }
            });
            if (alreadyRunning) return true;

            using var child = Process.Start(new ProcessStartInfo(HiddifyExe)
            {
                WorkingDirectory = Path.GetDirectoryName(HiddifyExe)!,
                UseShellExecute = false
            });
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

    private static bool VerifyFirefoxPolicy()
    {
        try
        {
            string path = Path.Combine(FirefoxDir, "distribution", "policies.json");
            using var parsed = JsonDocument.Parse(File.ReadAllText(path));
            var proxy = parsed.RootElement.GetProperty("policies").GetProperty("Proxy");
            return proxy.GetProperty("Mode").GetString() == "manual"
                && proxy.GetProperty("Locked").GetBoolean()
                && proxy.GetProperty("SOCKSProxy").GetString() == "127.0.0.1:12334"
                && proxy.GetProperty("SOCKSVersion").GetInt32() == 5
                && proxy.GetProperty("UseProxyForDNS").GetBoolean()
                && proxy.GetProperty("Passthrough").GetString() == "";
        }
        catch { return false; }
    }

    private static bool GuardStatus()
    {
        if (!File.Exists(GuardExe) || !File.Exists(FirefoxExe)) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(GuardExe)
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = "status \"" + FirefoxDir + "\""
            });
            if (process is null) return false;
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool EnsureGuard()
    {
        if (GuardStatus()) return true;
        if (!File.Exists(GuardExe)) return false;
        var answer = MessageBox.Show(
            "To keep Firefox restricted to Hiddify's local SOCKS5 port, FHG needs to install its app-specific Windows network rules once.\n\n" +
            "This does NOT turn Windows Firewall profiles on/off or change your regular Firefox.\n\n" +
            "The Windows Administrator prompt appears only when rules are missing or invalid.\n\nInstall now?",
            ProductName + " - network protection", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
        if (answer != DialogResult.Yes) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(GuardExe)
            {
                WorkingDirectory = Root,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "install \"" + FirefoxDir + "\""
            });
            if (process is null) return false;
            process.WaitForExit();
            return process.ExitCode == 0 && GuardStatus();
        }
        catch { return false; }
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
            shortcut.Description = "Original Hiddify interface + isolated FHG Firefox";
            shortcut.Save();
        }
        catch { /* no shortcut should prevent the browser from starting */ }
    }

    private static void Warn(string text) =>
        MessageBox.Show(text, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
