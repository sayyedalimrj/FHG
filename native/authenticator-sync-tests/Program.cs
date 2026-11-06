using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

void Check(bool okay, string label)
{
    if (!okay) throw new Exception("TEST FAILED: " + label);
}
string id = Guid.NewGuid().ToString("N");
string id2 = Guid.NewGuid().ToString("N");
string base32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
string folder = AccountVault.AccountFirefoxDir(id);
string other = AccountVault.AccountFirefoxDir(id2);
var first = new FhgAccount(id, "test1@example.invalid", "SYNTHETIC-ONLY", base32,
    "https://example.invalid/sub?test=true", DateTimeOffset.UtcNow);
var second = new FhgAccount(id2, "test2@example.invalid", "SYNTHETIC-ONLY", base32,
    "https://example.invalid/sub?test=true", DateTimeOffset.UtcNow);
try
{
    Check(OriginalAuthenticatorStorage.SyncProfile(id, new[] {first, second}) == 2, "initial sync");
    Check(OriginalAuthenticatorStorage.SyncProfile(id, new[] {first, second}) == 0, "idempotent sync");
    string db = Path.Combine(folder, "storage-sync-v2.sqlite");
    using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
    connection.Open();
    string Read()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM storage_sync_data WHERE ext_id = 'authenticator@mymindstorm'";
        return (string?)command.ExecuteScalar() ?? throw new Exception("no extension record");
    }
    var records = JsonNode.Parse(Read())!.AsObject();
    Check(records.Count == 2, "two records in original signed extension database");
    foreach (var pair in records)
    {
        Check(Regex.IsMatch(pair.Key, @"\A[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-8[a-f0-9]{3}-[a-f0-9]{12}\z"), "valid v4 UUID");
        Check(pair.Value?["type"]?.ToString() == "totp", "upstream OTP type");
        Check(pair.Value?["issuer"]?.ToString() == "ChatGPT", "issuer name");
        Check(pair.Value?["secret"]?.ToString() == base32, "seed stored in extension format");
    }
    // Simulate an unrelated account already managed by Authenticator.
    string foreign = "dace8438-7a2b-4d9f-8cd2-c7fd538746b1";
    records[foreign] = new JsonObject {
        ["dataType"]="OTPStorage", ["hash"]=foreign, ["index"]=9,
        ["type"]="totp", ["encrypted"]=false,
        ["issuer"]="OTHER", ["account"]="do-not-touch", ["secret"]=base32
    };
    using (var cmd = connection.CreateCommand()) {
        cmd.CommandText="UPDATE storage_sync_data SET data=$data WHERE ext_id='authenticator@mymindstorm'";
        cmd.Parameters.AddWithValue("$data", records.ToJsonString());
        cmd.ExecuteNonQuery();
    }
    connection.Close();
    var changed = first with { Label="newname@example.invalid" };
    Check(OriginalAuthenticatorStorage.SyncProfile(id, new[] {changed, second}) == 1, "rename updates existing FHG record");
    connection.Open();
    var after = JsonNode.Parse(Read())!.AsObject();
    Check(after.Count == 3 && after[foreign]?["account"]?.ToString() == "do-not-touch", "keep non-FHG data");
    connection.Close();
    Check(OriginalAuthenticatorStorage.SyncProfile(id, new[] {changed}) == 1, "remove orphaned FHG account");
    connection.Open();
    var reduced = JsonNode.Parse(Read())!.AsObject();
    Check(reduced.Count == 2 && reduced.ContainsKey(foreign), "only remove FHG-owned record");
    connection.Close();

    // Encrypted Authenticator storage must be refused, not overwritten.
    Check(OriginalAuthenticatorStorage.SyncProfile(id2, new[] {second}) == 1, "other profile");
    using (var enc = new SqliteConnection($"Data Source={Path.Combine(other,"storage-sync-v2.sqlite")};Pooling=False"))
    {
        enc.Open();
        using var cmd = enc.CreateCommand();
        cmd.CommandText="SELECT data FROM storage_sync_data WHERE ext_id='authenticator@mymindstorm'";
        var encryptedData = JsonNode.Parse((string)cmd.ExecuteScalar()!)!.AsObject();
        encryptedData["test-key"] = new JsonObject {["dataType"]="Key", ["salt"]="synthetic", ["id"]="test-key"};
        cmd.CommandText="UPDATE storage_sync_data SET data=$d WHERE ext_id='authenticator@mymindstorm'";
        cmd.Parameters.AddWithValue("$d",encryptedData.ToJsonString());
        cmd.ExecuteNonQuery();
    }
    bool rejected = false;
    try { OriginalAuthenticatorStorage.SyncProfile(id2, new[] {second with {Label="changed@example.invalid"}}); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "refuses encrypted format without injected plaintext");
    Console.WriteLine("PASS: original Authenticator sync schema, idempotence, edit, deletion, preservation, encryption guard");
}
finally
{
    foreach (var path in new[] {folder, other})
    {
        try { if(Directory.Exists(path)) Directory.Delete(Path.GetDirectoryName(path)!, true); } catch {}
    }
}
