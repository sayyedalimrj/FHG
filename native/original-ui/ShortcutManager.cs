using System;
using System.IO;
using System.Linq;

// The Windows shortcuts have three distinct icons. FHG Firefox points
// DIRECTLY to the bundled firefox.exe and uses the selected account's exact
// private profile; the FHG Proxy shortcut remains routed through the guarded
// FHG.exe --proxy-only startup. No login automation or extra tab is involved.
//
// Called after AccountVault.SetActive when the user chooses an account.
// This updates old Inno/previous-release shortcuts as well as new ones.
internal static class ShortcutManager
{
    private static bool Inside(string child, string parent)
    {
        string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        return Path.GetFullPath(child).StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedTarget(string? target, string appRoot)
    {
        if (string.IsNullOrWhiteSpace(target)) return true;
        try
        {
            string full = Path.GetFullPath(target);
            string file = Path.GetFileName(full);
            return Inside(full, appRoot) &&
                (file.Equals("FHG.exe", StringComparison.OrdinalIgnoreCase) ||
                 file.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase) ||
                 file.Equals("FHGAccountManager.exe", StringComparison.OrdinalIgnoreCase) ||
                 file.Equals("FHGProxy.exe", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "") + "\"";

    internal static string FirefoxArguments(string id)
    {
        if (!AccountVault.ValidId(id))
            throw new InvalidOperationException("Invalid account shortcut ID");
        string profile = AccountVault.AccountFirefoxDir(id);
        return "-no-remote -profile " + Quote(profile) +
               " https://chatgpt.com/login";
    }

    private static void CreateShortcut(dynamic shell, string link,
        string executable, string arguments, string icon,
        string workingDir, string description, string appRoot)
    {
        if (File.Exists(link))
        {
            dynamic before = shell.CreateShortcut(link);
            string? oldTarget = before.TargetPath as string;
            if (!IsOwnedTarget(oldTarget, appRoot))
                return; // leave user-owned shortcut untouched
        }
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = executable;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = workingDir;
        shortcut.IconLocation = (File.Exists(icon) ? icon : executable) + ",0";
        shortcut.Description = description;
        shortcut.Save();
    }

    internal static bool Sync(string appRoot, string? selectedAccountId)
    {
        try
        {
            appRoot = Path.GetFullPath(appRoot);
            string firefox = Path.Combine(appRoot, "Firefox", "firefox.exe");
            string launcher = Path.Combine(appRoot, "FHG.exe");
            string accounts = Path.Combine(appRoot, "FHGAccountManager.exe");
            string assets = Path.Combine(appRoot, "Assets");
            if (!File.Exists(firefox) || !File.Exists(launcher) ||
                !File.Exists(accounts)) return false;

            // The selected ID MUST exist in the current user's DPAPI vault.
            bool hasActive = AccountVault.ValidId(selectedAccountId) &&
                AccountVault.ActiveId() == selectedAccountId &&
                AccountVault.Load().Any(a => a.Id == selectedAccountId);
            string? args = hasActive
                ? FirefoxArguments(selectedAccountId!) : null;

            Type? cls = Type.GetTypeFromProgID("WScript.Shell");
            if (cls == null) return false;
            dynamic shell = Activator.CreateInstance(cls)!;
            foreach (string folder in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs)
            })
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    continue;
                CreateShortcut(shell, Path.Combine(folder,"FHG Accounts.lnk"),
                    accounts, "", Path.Combine(assets,"FHGAccounts.ico"),
                    appRoot, "Manage protected FHG accounts", appRoot);
                CreateShortcut(shell, Path.Combine(folder,"FHG Proxy.lnk"),
                    launcher, "--proxy-only", Path.Combine(assets,"FHGProxy.ico"),
                    appRoot, "Manage dedicated FHG Hiddify Proxy", appRoot);

                string browserLink = Path.Combine(folder,"FHG Firefox.lnk");
                if (args != null)
                {
                    CreateShortcut(shell, browserLink, firefox, args,
                        Path.Combine(assets,"FHGFirefox.ico"), Path.Combine(appRoot,"Firefox"),
                        "Open private Firefox directly with the active FHG account", appRoot);
                }
                else if (File.Exists(browserLink))
                {
                    dynamic existing = shell.CreateShortcut(browserLink);
                    if (IsOwnedTarget(existing.TargetPath as string, appRoot))
                        File.Delete(browserLink); // stale app-owned link only
                }
            }
            return true;
        }
        catch
        {
            // Shortcut update failure must never write secrets to logs or
            // corrupt the already-encrypted account vault.
            return false;
        }
    }
}
