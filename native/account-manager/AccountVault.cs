using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// FHG private account registry. All credentials and labels are encrypted with
// Windows DPAPI CurrentUser; this is bound to this Windows account and not
// portable to another machine/profile. No secrets in installer or app folder.
public sealed record FhgAccount(
    string Id, string Label, string Password, string TotpSeed,
    string SubscriptionUrl, DateTimeOffset CreatedUtc);

public sealed class AccountVault
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FHG-AccountVault-v1-CurrentUser");
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FHG", "Accounts");
    private static readonly string VaultFile = Path.Combine(Root, "vault.dpapi");
    private static readonly string ActiveFile = Path.Combine(Root, "active-id.txt");
    public static readonly string SetupRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FHG", "InitialSetup");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static IReadOnlyList<FhgAccount> Load()
    {
        if (!File.Exists(VaultFile)) return Array.Empty<FhgAccount>();
        byte[] encrypted = File.ReadAllBytes(VaultFile);
        byte[] json = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var accounts = JsonSerializer.Deserialize<List<FhgAccount>>(json) ?? new();
            if (accounts.Any(a => !ValidId(a.Id))) throw new FormatException("Vault contains an invalid account ID.");
            return accounts.AsReadOnly();
        }
        finally { CryptographicOperations.ZeroMemory(json); }
    }

    private static void Write(IEnumerable<FhgAccount> records)
    {
        Directory.CreateDirectory(Root);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(records, JsonOptions);
        try
        {
            byte[] secret = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            var temp = Path.Combine(Root, "vault-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(secret);
                    fs.Flush(flushToDisk: true);
                }
                File.Move(temp, VaultFile, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static FhgAccount ImportFourLineFile(string input)
    {
        var file = new FileInfo(input);
        if (!file.Exists || file.Length < 4 || file.Length > 16384)
            throw new FormatException("Select a local text file of at most 16 KB.");

        var lines = File.ReadAllLines(input, Encoding.UTF8);
        try
        {
            if (lines.Length != 4) throw new FormatException("The file must contain exactly 4 lines.");
            var label = lines[0].Trim();
            var password = lines[1];
            var totp = Regex.Replace(lines[2].ToUpperInvariant(), @"[\s-]", "");
            var url = lines[3].Trim();

            if (label.Length is < 1 or > 200 || password.Length is < 1 or > 4096)
                throw new FormatException("Account name/password is missing or too long.");
            if (totp.Length is < 16 or > 512 || !Regex.IsMatch(totp, @"^[A-Z2-7]+=*$"))
                throw new FormatException("TOTP key must be a valid Base32 string.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
                uri.Host.Length == 0 || url.Length > 8192)
                throw new FormatException("Subscription URL must use HTTPS without embedded user credentials.");

            var all = Load().ToList();
            if (all.Any(a => a.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
                throw new FormatException("An account with that name already exists. Select it or change its label.");
            var item = new FhgAccount(Guid.NewGuid().ToString("N"), label, password, totp, url, DateTimeOffset.UtcNow);
            all.Add(item);
            Write(all);
            return item;
        }
        finally { Array.Clear(lines); }
    }

    public static void UpdateSubscription(string id, string newUrl)
    {
        if (!Uri.TryCreate(newUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
            uri.Host.Length == 0 || newUrl.Length > 8192)
            throw new FormatException("Subscription must be an HTTPS URL.");
        var accounts = Load().ToList();
        int i = accounts.FindIndex(x => x.Id == id);
        if (i < 0) throw new InvalidOperationException("Account not found.");
        accounts[i] = accounts[i] with { SubscriptionUrl = newUrl };
        Write(accounts);
    }

    public static void SetActive(string id)
    {
        if (!ValidId(id) || !Load().Any(a => a.Id == id))
            throw new InvalidOperationException("Unknown FHG account.");
        Directory.CreateDirectory(Root);
        var temp = ActiveFile + ".tmp";
        File.WriteAllText(temp, id, Encoding.ASCII);
        File.Move(temp, ActiveFile, true);
    }

    public static string? ActiveId()
    {
        if (!File.Exists(ActiveFile)) return null;
        string id = File.ReadAllText(ActiveFile).Trim();
        return ValidId(id) ? id : null;
    }

    public static bool ValidId(string? id) =>
        id != null && Regex.IsMatch(id, @"\A[0-9a-f]{32}\z");

    public static string AccountFirefoxDir(string id)
    {
        if (!ValidId(id)) throw new FormatException("Invalid account ID.");
        return Path.Combine(Root, id, "FirefoxProfile");
    }

    public static string MakeSubscriptionTicket(FhgAccount a)
    {
        if (!ValidId(a.Id)) throw new InvalidOperationException("Invalid account ID.");
        Directory.CreateDirectory(SetupRoot);
        string file = Path.Combine(SetupRoot, "provision-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, a.SubscriptionUrl + Environment.NewLine, new UTF8Encoding(false));
        File.SetAttributes(file, FileAttributes.Hidden);
        return file;
    }

    public static string GenerateTotp(string base32, DateTimeOffset now)
    {
        string clean = base32.Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        var values = new List<byte>();
        int buffer = 0, bits = 0;
        foreach(char c in clean)
        {
            int n = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);
            if (n < 0) throw new FormatException("Invalid Base32.");
            buffer = ((buffer << 5) | n) & 0xffff;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                values.Add((byte)(buffer >> bits));
            }
        }
        byte[] key = values.ToArray();
        try
        {
            long step = now.ToUnixTimeSeconds() / 30;
            byte[] counter = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(counter);
            using var hmac = new HMACSHA1(key);
            byte[] hash = hmac.ComputeHash(counter);
            int offset = hash[^1] & 0xf;
            int number = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) |
                         (hash[offset + 2] << 8) | hash[offset + 3];
            return (number % 1000000).ToString("D6");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
