using System;
using System.IO;
using System.Linq;

var folder = Path.Combine(Path.GetTempPath(), "fhg-setup-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "sample.txt");
    var example = new [] {
        "demo-user@example.invalid",
        "dummy-password-not-a-real-account",
        "JBSWY3DPEHPK3PXP",
        "https://subscription.example.invalid/list?token=TEST-NOT-REAL"
    };
    File.WriteAllLines(path, example);
    var parsed = FirstRunWizard.ParseFile(path);
    Check(parsed.Label == example[0] && parsed.SubscriptionUrl == example[3],
          "Valid file should return label and HTTPS subscription");
    Check(!parsed.ToString().Contains(example[1]) && !parsed.ToString().Contains(example[2]),
          "Parser must not return password or TOTP seed");
    Check(!File.Exists(Path.Combine(folder, "output.log")), "Input secrets must not be logged");

    File.WriteAllLines(path, example.Take(3));
    Reject(() => FirstRunWizard.ParseFile(path), "Three-line file");
    File.WriteAllLines(path, new [] {example[0],example[1],"INVALID!KEY",example[3]});
    Reject(() => FirstRunWizard.ParseFile(path), "Invalid Base32");
    File.WriteAllLines(path, new [] {example[0],example[1],example[2],"http://plain.example.invalid"});
    Reject(() => FirstRunWizard.ParseFile(path), "HTTP subscription");
    File.WriteAllLines(path, new [] {example[0],example[1],example[2],"https://user:password@example.invalid/sub"});
    Reject(() => FirstRunWizard.ParseFile(path), "URL userinfo");
    File.WriteAllText(path, new string('x', 17000));
    Reject(() => FirstRunWizard.ParseFile(path), "Oversized input");
    Console.WriteLine("PASS: private first-run file parser accepted a fake 4-line file and rejected invalid inputs.");
}
finally {
    try { Directory.Delete(folder, true); } catch { }
}
static void Check(bool ok, string message) {
    if (!ok) throw new Exception("FAIL: " + message);
}
static void Reject(Action action, string label) {
    try { action(); throw new Exception("Invalid input was accepted: " + label); }
    catch (FormatException) { }
}
