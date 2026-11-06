using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Expected staged FHG app directory");
        string root = Path.GetFullPath(args[0]);
        string accounts = Path.Combine(root, "Assets", "FHGAccounts.ico");
        string proxy = Path.Combine(root, "Assets", "FHGProxy.ico");
        string firefoxIcon = Path.Combine(root, "Assets", "FHGFirefox.ico");
        foreach (var icon in new [] { accounts, proxy, firefoxIcon })
            Check(File.Exists(icon) && new FileInfo(icon).Length > 3000,
                "Seven-size icon missing: " + icon);

        string[] icons = { accounts, proxy, firefoxIcon };
        Check(icons.Select(x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))))
                  .Distinct().Count() == 3, "Accounts, Proxy, Firefox icons must differ");
        foreach(var icon in icons)
        {
            byte[] bytes = File.ReadAllBytes(icon);
            Check(bytes[0] == 0 && bytes[1] == 0 &&
                  bytes[2] == 1 && bytes[3] == 0 &&
                  bytes[4] == 7 && bytes[5] == 0,
                "Multi-size Windows ICO signature invalid: " + icon);
        }

        string testdir = Path.Combine(Path.GetTempPath(),
            "FHG-SHORTCUTS-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testdir);
        string? previousActive = AccountVault.ActiveId();
        FhgAccount? first = null, second = null;
        try
        {
            first = Add("SHORTCUT-ONE-"+Guid.NewGuid().ToString("N"),testdir);
            AccountVault.SetActive(first.Id);
            Check(ShortcutManager.Sync(root,first.Id),"First account shortcut update");
            VerifyShortcut(root,first.Id);

            second = Add("SHORTCUT-TWO-"+Guid.NewGuid().ToString("N"),testdir);
            AccountVault.SetActive(second.Id);
            Check(ShortcutManager.Sync(root,second.Id),"Second account shortcut update");
            VerifyShortcut(root,second.Id);

            Check(ShortcutManager.FirefoxArguments(second.Id) !=
                  ShortcutManager.FirefoxArguments(first.Id),"Distinct Firefox account profiles");
            Console.WriteLine("PASS: 3 unique icons, direct Firefox shortcut, proxy/account shortcut targets, account switch");
            return 0;
        }
        finally
        {
            if(second != null) AccountVault.DeleteAccount(second.Id);
            if(first != null) AccountVault.DeleteAccount(first.Id);
            if(previousActive != null && AccountVault.Load().Any(a=>a.Id==previousActive))
                AccountVault.SetActive(previousActive);
            ShortcutManager.Sync(root, AccountVault.ActiveId());
            try { Directory.Delete(testdir,true); } catch { }
        }
    }

    private static FhgAccount Add(string label,string directory)
    {
        string file = Path.Combine(directory,label+".txt");
        File.WriteAllLines(file,new [] {
            label, "NONREAL-PASSWORD", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
            "https://example.invalid/subscription"
        });
        try { return AccountVault.ImportFourLineFile(file); }
        finally { File.Delete(file); }
    }

    private static void VerifyShortcut(string root,string id)
    {
        Type cls = Type.GetTypeFromProgID("WScript.Shell") ??
            throw new Exception("WScript shortcut COM unavailable");
        dynamic shell = Activator.CreateInstance(cls)!;
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        dynamic firefox = shell.CreateShortcut(Path.Combine(folder,"FHG Firefox.lnk"));
        dynamic proxy = shell.CreateShortcut(Path.Combine(folder,"FHG Proxy.lnk"));
        dynamic accounts = shell.CreateShortcut(Path.Combine(folder,"FHG Accounts.lnk"));
        string firefoxTarget = (string)firefox.TargetPath;
        Check(Path.GetFullPath(firefoxTarget).Equals(Path.GetFullPath(
              Path.Combine(root,"Firefox","firefox.exe")),StringComparison.OrdinalIgnoreCase),
              "Firefox shortcut must target firefox.exe, not FHG.exe");
        Check(((string)firefox.Arguments) == ShortcutManager.FirefoxArguments(id),
              "Firefox must point to active private account profile");
        Check(((string)firefox.IconLocation).Contains("FHGFirefox.ico",
              StringComparison.OrdinalIgnoreCase),"Firefox icon badge not applied");
        Check(Path.GetFullPath((string)proxy.TargetPath).Equals(
              Path.GetFullPath(Path.Combine(root,"FHG.exe")),StringComparison.OrdinalIgnoreCase),
              "Proxy shortcut must call guarded proxy launcher");
        Check(((string)proxy.Arguments) == "--proxy-only",
              "Proxy shortcut must start proxy-only");
        Check(((string)proxy.IconLocation).Contains("FHGProxy.ico",
              StringComparison.OrdinalIgnoreCase),"Distinct Proxy icon not installed");
        Check(Path.GetFullPath((string)accounts.TargetPath).Equals(
              Path.GetFullPath(Path.Combine(root,"FHGAccountManager.exe")),
              StringComparison.OrdinalIgnoreCase),"Accounts must call account manager");
        Check(((string)accounts.IconLocation).Contains("FHGAccounts.ico",
              StringComparison.OrdinalIgnoreCase),"Distinct Accounts icon not installed");
    }

    private static void Check(bool value,string error)
    {
        if(!value)throw new Exception("FHG shortcut regression: "+error);
    }
}
