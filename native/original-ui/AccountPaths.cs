using System;
using System.IO;
using System.Text.RegularExpressions;

// Same ID and folder conventions as the standalone Account Manager.
// Only the account identifier is ever read by this launcher: no password,
// subscription token or TOTP seed is loaded into the browser startup process.
internal static class AccountPaths
{
    internal static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FHG", "Accounts");

    internal static string? ActiveId
    {
        get {
            var file = Path.Combine(Root, "active-id.txt");
            if (!File.Exists(file)) return null;
            var id = File.ReadAllText(file).Trim();
            return Regex.IsMatch(id, @"\A[0-9a-f]{32}\z") ? id : null;
        }
    }

    internal static string FirefoxProfile
    {
        get {
            var id = ActiveId;
            if (id == null) return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FHG", "FirefoxProfile");
            return Path.Combine(Root, id, "FirefoxProfile");
        }
    }

    internal static string? ValidateProvisionTicket()
    {
        var filename = Environment.GetEnvironmentVariable("FHG_SETUP_SUB_TICKET");
        if (string.IsNullOrWhiteSpace(filename) || !File.Exists(filename)) return null;
        var directory = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FHG", "InitialSetup"));
        var actual = Path.GetFullPath(filename);
        if (!string.Equals(Path.GetDirectoryName(actual), directory, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(actual), @"\Aprovision-[0-9a-f]{32}\.txt\z") ||
            new FileInfo(actual).Length is < 4 or > 8192)
            throw new InvalidOperationException("Invalid FHG setup request path.");
        return actual;
    }
}
