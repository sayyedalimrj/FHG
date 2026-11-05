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
    AccountVault.SetActive(a.Id);
    Check(AccountVault.ActiveId() == a.Id, "active-account selection");
    string vault = File.ReadAllText(Path.Combine(AccountVault.Root, "vault.dpapi"));
    Check(!vault.Contains(pass) && !vault.Contains(sub) && !vault.Contains(seed),
        "no raw account secret in disk vault");
    string path = AccountVault.AccountFirefoxDir(a.Id);
    Check(path.EndsWith(Path.Combine(a.Id, "FirefoxProfile")), "browser isolation");
    Console.WriteLine("PASS: per-user DPAPI archive, account selection, isolated profile and RFC 6238 TOTP");
}
finally {
    try { Directory.Delete(folder, true); } catch {}
}
