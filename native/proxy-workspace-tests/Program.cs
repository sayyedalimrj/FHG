using System;
using System.IO;
using System.Linq;

void Check(bool ok, string msg) { if(!ok) throw new Exception("TEST FAILED: "+msg); }
string name="synthetic-"+Guid.NewGuid().ToString("N")+"@example.invalid";
string pass="SYNTHETIC-PASSWORD-CI";
string seed="GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
string sub="https://example.invalid/sub?token=SYNTHETIC";
string temp=Path.Combine(Path.GetTempPath(),"fhg-dummy-"+Guid.NewGuid().ToString("N")+".txt");
File.WriteAllLines(temp,new[]{name,pass,seed,sub});
try {
    var added=AccountVault.ImportFourLineFile(temp);
    AccountVault.SetActive(added.Id);
    var request=ProxyAutomation.Prepare(null);
    Check(request is not null,"first-time account must generate proxy ticket");
    Check(File.Exists(request!.SubscriptionFile),"subscription ticket must exist");
    string value=File.ReadAllText(request.SubscriptionFile);
    Check(value.Trim()==sub,"ticket must contain only subscription");
    Check(!value.Contains(pass)&&!value.Contains(seed)&&!value.Contains(name),
        "password, OTP and username must not leak to proxy process");
    FirstRunWizard.CleanupTicket(request);
    ProxyAutomation.MarkConfigured();
    Check(ProxyAutomation.Prepare(null)==null,"unchanged account must not be imported again");
    AccountVault.UpdateSubscription(added.Id,
        "https://example.invalid/changed?token=SYNTHETIC2");
    var updated=ProxyAutomation.Prepare(null);
    Check(updated!=null,"subscription update must auto-create a new proxy ticket");
    FirstRunWizard.CleanupTicket(updated!);
    Console.WriteLine("PASS: per-account one-click subscription tickets, restart fingerprint, vault secret isolation");
}
finally {
    try{File.Delete(temp);}catch{}
}
