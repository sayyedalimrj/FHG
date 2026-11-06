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
            // Don't include arbitrary exception messages that could expose
            // URLs, seed material or Windows filesystem details.
            Console.Error.WriteLine("FHG_AUTHENTICATOR_UNSUPPORTED_STORAGE: Original Authenticator data or profile needs inspection.");
            return 3;
        }
        catch
        {
            Console.Error.WriteLine("FHG_AUTHENTICATOR_SYNC_FAILED: Existing extension data left unchanged.");
            return 5;
        }
    }
}
