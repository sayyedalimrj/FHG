using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;

// Coordinates an existing, separately branded FHG Proxy process with the
// DPAPI-encrypted account vault. This never touches a regular Hiddify install.
internal static class ProxyAutomation
{
    private static readonly string StateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FHG", "InitialSetup");
    private static readonly string StampFile = Path.Combine(StateRoot, "active-proxy.json");
    private sealed record Stamp(string AccountId, string SubscriptionFingerprint);

    public static FirstRunRequest? Prepare(FirstRunRequest? explicitTicket)
    {
        if (explicitTicket != null) return explicitTicket;
        string? selectedId = AccountPaths.ActiveId;
        if (selectedId == null) return null;

        FhgAccount? account = AccountVault.Load().FirstOrDefault(a => a.Id == selectedId);
        if (account is null) throw new InvalidOperationException("The active FHG account is missing.");
        string signature = Fingerprint(account);
        try {
            if (File.Exists(StampFile)) {
                var stamp = JsonSerializer.Deserialize<Stamp>(File.ReadAllText(StampFile));
                if (stamp?.AccountId == selectedId &&
                    stamp.SubscriptionFingerprint == signature)
                    return null;
            }
        }
        catch (JsonException) { /* Treat broken state as needing reimport. */ }
        catch (IOException) { /* Treat inaccessible state as needing reimport. */ }

        // A randomly named, short-lived ticket contains ONLY the subscription
        // link. It is never passed on a command line and is removed by launcher.
        string ticket = AccountVault.MakeSubscriptionTicket(account);
        return new FirstRunRequest(ticket, ticket + ".status", account.Label);
    }

    public static void MarkConfigured()
    {
        string? id = AccountPaths.ActiveId;
        if (id == null) return;
        var account = AccountVault.Load().FirstOrDefault(a => a.Id == id);
        if (account == null) return;
        Directory.CreateDirectory(StateRoot);
        var stamp = new Stamp(account.Id, Fingerprint(account));
        string staging = StampFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(staging, JsonSerializer.Serialize(stamp), Encoding.UTF8);
            File.Move(staging, StampFile, overwrite: true);
        }
        finally { try { if (File.Exists(staging)) File.Delete(staging); } catch {} }
    }

    private static string Fingerprint(FhgAccount account) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            account.Id + "\n" + account.SubscriptionUrl)));

    public static Process[] OwnedProxyProcesses(string proxyExe)
    {
        string expected = Path.GetFullPath(proxyExe);
        int ownSession = Process.GetCurrentProcess().SessionId;
        return Process.GetProcessesByName("FHGProxy").Where(p => {
            try {
                return p.SessionId == ownSession &&
                    string.Equals(Path.GetFullPath(p.MainModule?.FileName ?? ""),
                    expected, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }).ToArray();
    }

    public static bool StopForNewSubscription(string proxyExe)
    {
        // User initiated account activation / subscription update.
        // First request a normal GUI close to flush Hiddify's portable DB.
        var matches = OwnedProxyProcesses(proxyExe);
        try
        {
            foreach (var process in matches)
            {
                if (process.HasExited) continue;
                try { process.CloseMainWindow(); }
                catch (InvalidOperationException) {}
                if (!process.WaitForExit(7000))
                {
                    // Process is verified to be the FHG-specific executable.
                    // Its child core is part of the same account session.
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) {}
                    if (!process.WaitForExit(5000)) return false;
                }
            }
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                var listeners = System.Net.NetworkInformation.IPGlobalProperties
                    .GetIPGlobalProperties().GetActiveTcpListeners();
                if (!listeners.Any(e => e.Port == 12345 || e.Port == 27178 || e.Port == 26756))
                    return true;
                Thread.Sleep(500);
            }
            return false;
        }
        finally { foreach (var process in matches) process.Dispose(); }
    }

    public static bool BootstrapConsent()
    {
        string path = Path.Combine(StateRoot, "bootstrap-choice.txt");
        try
        {
            if (File.Exists(path)) {
                string choice = File.ReadAllText(path).Trim();
                if (choice == "ALLOW") return true;
                if (choice == "DECLINE") return false;
            }
        }
        catch (IOException) { /* Prompt if an earlier decision is unreadable. */ }

        // Permission is requested ONCE, never on every FHG launch.
        // Only the subscription downloader may use the existing 12334 route.
        // Firefox and FHG proxy traffic remain pinned to FHG 12345.
        bool allow = System.Windows.Forms.MessageBox.Show(
            "The regular Hiddify proxy is available at 127.0.0.1:12334.\\n\\n" +
            "May FHG use that local route ONLY to download your proxy subscriptions?\\n" +
            "The decision will be remembered; this will not change Firefox's proxy.",
            "One-time subscription bootstrap permission",
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Information,
            System.Windows.Forms.MessageBoxDefaultButton.Button2)
            == System.Windows.Forms.DialogResult.Yes;
        Directory.CreateDirectory(StateRoot);
        try { File.WriteAllText(path, allow ? "ALLOW" : "DECLINE"); }
        catch (IOException) { /* Never grant consent implicitly. */ }
        return allow;
    }

    public static void ShowProxyWindow(string proxyExe)
    {
        var matches = OwnedProxyProcesses(proxyExe);
        try {
            foreach (var process in matches)
            {
                IntPtr window = process.MainWindowHandle;
                if (window == IntPtr.Zero) continue;
                ShowWindowAsync(window, 9); // SW_RESTORE
                SetForegroundWindow(window);
                break;
            }
        }
        finally { foreach (var process in matches) process.Dispose(); }
    }

    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
