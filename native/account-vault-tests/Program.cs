using System;
using System.IO;
using System.Linq;

string folder = Path.Combine(Path.GetTempPath(), "FHG-Vault-TEST-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try {
    void Check(bool yes, string message) {
        if (!yes) throw new Exception("FAIL: " + message);
    }
    var seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    var code = AccountVault.GenerateTotp(seed, DateTimeOffset.FromUnixTimeSeconds(59));
    Check(code == "287082", "RFC 6238 six-digit SHA1 vector");
    var fake = Path.Combine(folder, "synthetic.txt");
    var name = "EXAMPLE-" + Guid.NewGuid().ToString("N");
    string sub = "https://subscription.example.invalid/list?token=NOT-A-REAL-CREDENTIAL";
    string pass = "TESTONLY-never-a-real-password";
    File.WriteAllLines(fake, new[] {name, pass, seed, sub});
    var a = AccountVault.ImportFourLineFile(fake);
    Check(AccountVault.ValidId(a.Id) && a.Label == name, "account identifier and label");
    Check(AccountVault.Load().Any(x => x.Id == a.Id && x.Password == pass), "DPAPI decrypt");
    LoginProgress.Write(a.Id, "STARTING");
    if (LoginProgress.Read(a.Id) != "STARTING") throw new Exception("status start");
    LoginProgress.Write(a.Id, "MANUAL_CHALLENGE");
    if (LoginProgress.Read(a.Id) != "MANUAL_CHALLENGE") throw new Exception("manual CAPTCHA");
    LoginProgress.Write(a.Id, "SUCCESS");
    if (LoginProgress.Read(a.Id) != "SUCCESS") throw new Exception("verified session");
    LoginProgress.Write(a.Id, "HANDOFF_FAILED");
    if (LoginProgress.Read(a.Id) != "HANDOFF_FAILED") throw new Exception("handoff distinction");
    AccountVault.SetActive(a.Id);
    Check(AccountVault.ActiveId() == a.Id, "active-account selection");
    // A completed encrypted Firefox import is identified only by a harmless
    // marker: changing the login invalidates it, subscription changes do not.
    string importMarker=Path.Combine(AccountVault.Root,a.Id,"firefox-password-imported.v1");
    Directory.CreateDirectory(Path.GetDirectoryName(importMarker)!);
    File.WriteAllText(importMarker,"IMPORTED_TO_FIREFOX_NSS_V1");
    string vault = File.ReadAllText(Path.Combine(AccountVault.Root, "vault.dpapi"));
    Check(!vault.Contains(pass) && !vault.Contains(sub) && !vault.Contains(seed),
        "no raw account secret in disk vault");
    string path = AccountVault.AccountFirefoxDir(a.Id);
    Check(path.EndsWith(Path.Combine(a.Id, "FirefoxProfile")), "browser isolation");
    // End-user regression: edits retain per-account browser ID and encrypted vault,
    // deletion removes only vault record, not the Firefox profile/session directory.
    string updatedName = name + "-EDITED";
    string updatedSub = "https://sub.example.invalid/changed?token=NOT-REAL";
    string updatedPass = "TESTONLY-new-password";
    AccountVault.UpdateAccount(a.Id, updatedName, updatedPass, seed, updatedSub);
    Check(!File.Exists(importMarker),"new username or password invalidates encrypted Firefox import");
    var changed = AccountVault.Load().Single(x => x.Id == a.Id);
    Check(changed.Label == updatedName && changed.Password == updatedPass &&
          changed.SubscriptionUrl == updatedSub, "edit every account field while preserving ID");
    Check(AccountVault.ActiveId() == a.Id, "edited active account remains active");
    bool badSeedWasRejected = false;
    try { AccountVault.UpdateAccount(a.Id, updatedName, updatedPass, "NOT-BASE32!", updatedSub); }
    catch (FormatException) { badSeedWasRejected = true; }
    Check(badSeedWasRejected, "invalid 2FA seed rejected without replacing saved credentials");
    Check(AccountVault.Load().Single(x => x.Id == a.Id).TotpSeed == seed, "bad edit rollback");
    Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, "keep.txt"), "retained session");
    AccountVault.DeleteAccount(a.Id);
    Check(!AccountVault.Load().Any(x => x.Id == a.Id), "deleted account no longer in encrypted vault");
    Check(AccountVault.ActiveId() == null, "deleted active account ID cleared");
    Check(File.Exists(Path.Combine(path, "keep.txt")), "deleted account profile preserved for explicit cleanup");
    Console.WriteLine("PASS: DPAPI archive, edit/delete, isolated profile preservation and RFC 6238 TOTP");
}
finally {
    try { Directory.Delete(folder, true); } catch {}
}
