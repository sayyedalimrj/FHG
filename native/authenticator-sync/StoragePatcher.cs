using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Directly edits the ORIGINAL signed mymindstorm Authenticator's Firefox
// storage.sync database. The signed XPI and Firefox engine remain UNMODIFIED.
// On-disk format verified against Mozilla application-services/webext-storage
// sql/create_schema.sql and src/api.rs.
public static class OriginalAuthenticatorStorage
{
    private const string ExtensionId = "authenticator@mymindstorm";
    private const int MaxBytes = 102400;
    private const int MaxItems = 512;
    private const int MaxItemBytes = 8192;
    private static readonly byte[] BackupEntropy = Encoding.UTF8.GetBytes("FHG-Authenticator-DB-Backup-v1");

    public static string ExpectedProfile(string id) => AccountVault.AccountFirefoxDir(id);

    public static int SyncProfile(string accountId, IReadOnlyList<FhgAccount> accounts)
    {
        if (!AccountVault.ValidId(accountId)) throw new InvalidOperationException("Bad account identifier.");
        string profile = Path.GetFullPath(ExpectedProfile(accountId));
        Directory.CreateDirectory(profile);
        // Refuse symlinks / junctions, which could redirect a write to a
        // different installation or to the user's ordinary Firefox profile.
        if ((new DirectoryInfo(profile).Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Firefox profile is a link; refusing to patch.");
        string root = Path.GetFullPath(AccountVault.Root);
        if (!profile.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Profile escapes FHG directory.");
        if (FhgFirefoxRunning())
            throw new IOException("FHG Firefox is running. Close FHG Firefox to update Authenticator safely.");

        string lockPath = Path.Combine(profile, "parent.lock");
        bool preexistingLock = File.Exists(lockPath);
        using (var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None))
        {
            int added = SyncSQLite(profile, accounts);
            if (!preexistingLock)
            {
                // Ensure the lock handle is released before removing it.
                fileLock.Flush();
            }
            return added;
        }
    }

    private static bool FhgFirefoxRunning()
    {
        string ownInstall = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "Firefox", "firefox.exe"));
        foreach (var p in Process.GetProcessesByName("firefox"))
        {
            using (p)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(p.MainModule?.FileName ?? ""),
                        ownInstall, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Never guess that an inaccessible process is our managed Firefox.
                }
                catch (InvalidOperationException) { }
            }
        }
        return false;
    }

    private static int SyncSQLite(string profile, IReadOnlyList<FhgAccount> accounts)
    {
        string db = Path.Combine(profile, "storage-sync-v2.sqlite");
        bool existed = File.Exists(db);
        var options = new SqliteConnectionStringBuilder {
            DataSource = db,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        using var connection = new SqliteConnection(options.ToString());
        connection.Open();
        using (var command = connection.CreateCommand()) {
            command.CommandText = "PRAGMA busy_timeout=1500; PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();
        }

        if (!existed)
        {
            // Exactly the Firefox 79+ WebExtStorage schema from Mozilla,
            // including the mirror and metadata tables required by sync.
            using var initialize = connection.CreateCommand();
            initialize.CommandText = @"
                CREATE TABLE storage_sync_data (
                    ext_id TEXT NOT NULL PRIMARY KEY,
                    data TEXT,
                    sync_change_counter INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE storage_sync_mirror (
                    guid TEXT NOT NULL PRIMARY KEY,
                    ext_id TEXT UNIQUE,
                    data TEXT,
                    CHECK((ext_id IS NULL AND data IS NULL) OR
                          (ext_id IS NOT NULL AND data IS NOT NULL))
                );
                CREATE TABLE meta (key TEXT PRIMARY KEY, value NOT NULL) WITHOUT ROWID;
                PRAGMA user_version=2;";
            initialize.ExecuteNonQuery();
        }
        else
        {
            using var schemaCheck = connection.CreateCommand();
            schemaCheck.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(schemaCheck.ExecuteScalar());
            if (version != 2) throw new InvalidOperationException(
                "Unsupported Firefox Sync storage version: refusing modification.");
            schemaCheck.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN " +
                "('storage_sync_data','storage_sync_mirror','meta')";
            if (Convert.ToInt32(schemaCheck.ExecuteScalar()) != 3)
                throw new InvalidOperationException("Unexpected Firefox Sync storage layout.");
            schemaCheck.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(schemaCheck.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
                throw new InvalidOperationException("Firefox Sync database failed integrity check.");
        }

        string? raw;
        using (var read = connection.CreateCommand()) {
            read.CommandText = "SELECT data FROM storage_sync_data WHERE ext_id=$id;";
            read.Parameters.AddWithValue("$id", ExtensionId);
            raw = read.ExecuteScalar() as string;
        }
        JsonObject json;
        try { json = string.IsNullOrWhiteSpace(raw) ? new JsonObject() :
                JsonNode.Parse(raw) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidOperationException(
            "Authenticator data is not a JSON object: refusing to overwrite it."); }

        // If the original extension uses a master password, encrypted items
        // must be added through its encryption layer, not as plaintext.
        foreach (var record in json.ToList())
        {
            if (record.Value is not JsonObject item) continue;
            string kind = item["dataType"]?.ToString() ?? "";
            if (kind is "Key" or "EncOTPStorage" || item["encrypted"]?.ToString() == "true")
                throw new InvalidOperationException(
                    "Authenticator contains password-protected items. No plaintext keys were injected.");
        }

        int nextIndex = json
            .Where(entry => entry.Value is JsonObject item &&
                item["dataType"]?.ToString() is "OTPStorage" or "EncOTPStorage")
            .Select(entry => int.TryParse((entry.Value as JsonObject)?["index"]?.ToString(), out int n) ? n : -1)
            .DefaultIfEmpty(-1).Max() + 1;
        int changes = 0;
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in accounts)
        {
            string key = StableUuid(a.Id);
            expected.Add(key);
            // Do not replace an existing entry that is user-managed.
            if (json.TryGetPropertyValue(key, out var oldValue) &&
                oldValue is JsonObject oldItem &&
                oldItem["fhgManaged"]?.ToString() != "FHGv1")
                throw new InvalidOperationException("Account UUID collision in Authenticator; no changes were made.");

            // Respect independently imported entries by same provider, user and
            // secret: don't duplicate them. Never edit their IDs or settings.
            bool alreadyPresent = json.Any(kv =>
                kv.Key != key && kv.Value is JsonObject item &&
                item["issuer"]?.ToString() == "ChatGPT" &&
                item["account"]?.ToString() == a.Label &&
                item["secret"]?.ToString() == a.TotpSeed);
            if (alreadyPresent && !json.ContainsKey(key)) continue;

            int index = json[key] is JsonObject current &&
                int.TryParse(current["index"]?.ToString(), out int ix) ? ix : nextIndex++;
            var row = new JsonObject {
                ["dataType"] = "OTPStorage",
                ["encrypted"] = false,
                ["hash"] = key,
                ["index"] = index,
                ["type"] = "totp",
                ["secret"] = a.TotpSeed,
                ["issuer"] = "ChatGPT",
                ["account"] = a.Label,
                ["period"] = 30,
                ["digits"] = 6,
                ["algorithm"] = "SHA1",
                ["fhgManaged"] = "FHGv1"
            };
            if (json[key]?.ToJsonString() != row.ToJsonString()) {
                json[key] = row;
                changes++;
            }
        }
        // Remove only entries we previously wrote for accounts now deleted.
        foreach (var pair in json.ToList())
        {
            if (pair.Value is JsonObject item &&
                item["fhgManaged"]?.ToString() == "FHGv1" &&
                !expected.Contains(pair.Key)) {
                json.Remove(pair.Key);
                changes++;
            }
        }
        if (json.Count > MaxItems) throw new InvalidOperationException(
            "Authenticator Sync has more than 512 items.");
        foreach (var e in json) {
            if (Encoding.UTF8.GetByteCount(e.Key) + Encoding.UTF8.GetByteCount(
                e.Value?.ToJsonString() ?? "null") > MaxItemBytes)
                throw new InvalidOperationException("Authenticator Sync item exceeds quota.");
        }
        string resultJson = json.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        if (Encoding.UTF8.GetByteCount(resultJson) > MaxBytes)
            throw new InvalidOperationException("Authenticator Sync total exceeds 100 KB quota.");
        if (changes == 0) return 0;

        BackupEncrypted(connection, db, profile);
        using var tx = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO storage_sync_data (ext_id, data, sync_change_counter)
                VALUES ($id, $data, 1)
                ON CONFLICT(ext_id) DO UPDATE SET
                    data=excluded.data,
                    sync_change_counter=storage_sync_data.sync_change_counter+1;";
            cmd.Parameters.AddWithValue("$id", ExtensionId);
            cmd.Parameters.AddWithValue("$data", resultJson);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check;";
            if (check.ExecuteScalar()?.ToString() != "ok")
                throw new InvalidOperationException("Authenticator SQLite verification failed.");
        }
        return changes;
    }

    private static string StableUuid(string accountId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("FHG-Authenticator-v1-" + accountId));
        byte[] id = hash[..16];
        // Produce a stable, RFC4122 v4-looking UUID compatible with
        // Authenticator's UUID-v4 validation; no password/secret in the key.
        id[6] = (byte)((id[6] & 0x0F) | 0x40);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);
        return new Guid(id).ToString("D");
    }

    private static void BackupEncrypted(SqliteConnection source, string db, string profile)
    {
        string folder = Path.Combine(AccountVault.Root, "AuthenticatorBackups");
        Directory.CreateDirectory(folder);
        string name = Path.GetFileName(Path.GetDirectoryName(profile) ?? "unknown");
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss");
        string temp = Path.Combine(folder, "backup-" + Guid.NewGuid().ToString("N") + ".sqlite");
        string encryptedPath = Path.Combine(folder, name + "-" + stamp + "-" +
            Guid.NewGuid().ToString("N") + ".sqlite.dpapi");
        try
        {
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource = temp, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
            }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
                target.Close();
            }
            byte[] clear = File.ReadAllBytes(temp);
            try
            {
                byte[] protectedBytes = ProtectedData.Protect(clear, BackupEntropy,
                    DataProtectionScope.CurrentUser);
                File.WriteAllBytes(encryptedPath, protectedBytes);
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        finally {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
