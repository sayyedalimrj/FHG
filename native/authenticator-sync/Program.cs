using System;
using System.IO;
using System.Linq;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Only an FHG-owned account ID travels through the process command
        // line. Passwords, TOTP seeds and subscriptions stay in DPAPI vault.
        if (args.Length != 2 || args[0] != "--account" || !AccountVault.ValidId(args[1]))
        {
            Console.Error.WriteLine("FHG Authenticator Sync: --account <FHG-ID> required.");
            return 4;
        }
        try
        {
            var existing = AccountVault.Load();
            if (!existing.Any(a => a.Id == args[1]))
                return 4;
            int changes = OriginalAuthenticatorStorage.SyncProfile(args[1], existing);
            Console.WriteLine("FHG_AUTHENTICATOR_SYNC_OK:" + changes);
            return 0;
        }
        catch (IOException)
        {
            Console.Error.WriteLine("FHG_AUTHENTICATOR_PROFILE_BUSY: Close FHG Firefox and retry.");
            return 2;
        }
        catch (InvalidOperationException)
        {
            // The Firefox 159 extension may use storage.local (IndexedDB)
            // rather than the older SQLite schema. The signed original addon
            // is synchronized later by Firefox's browser.storage API.
            Console.WriteLine("FHG_AUTHENTICATOR_DEFERRED_TO_BROWSER_API");
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("FHG_AUTHENTICATOR_SYNC_FAILED: Existing extension data left unchanged.");
            return 5;
        }
    }
}
