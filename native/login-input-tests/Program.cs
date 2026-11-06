using System;
using System.IO;

static void Verify(bool yes, string label) {
    if (!yes) throw new Exception("SYNTHETIC_LOGIN_TEST_FAILED: " + label);
}
Verify(args.Length==1 && Directory.Exists(args[0]), "stage argument");
string root=Path.GetFullPath(args[0]);
string dir=Path.Combine(Path.GetTempPath(),"FHG-Input-Test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try {
    using var browser=new FirefoxDriverBridge(
        Path.Combine(root,"WebDriver","geckodriver.exe"),
        Path.Combine(root,"Firefox","firefox.exe"),dir);
    const string html="<html><body><form onsubmit='event.preventDefault();document.body.dataset.ok=\"1\"'>"+
        "<input type='email' name='username' autocomplete='email'>"+
        "<input type='password' name='current-password' autocomplete='current-password'>"+
        "<input type='text' name='code' autocomplete='one-time-code'>"+
        "<button type='submit'>Continue</button></form></body></html>";
    browser.GoTo("data:text/html;charset=utf-8,"+Uri.EscapeDataString(html));
    var cases=new (string Selector,string Value)[] {
        ("input[name='username']","reader+tag@example.invalid"),
        ("input[name='current-password']","P@ss+é+ک#123"),
        ("input[name='code']","009876")
    };
    foreach(var (css,value) in cases) {
        string? id=browser.FindVisible(css);
        Verify(id!=null,css+" visible");
        browser.Fill(id!,value);
        Verify(string.Equals(browser.ElementValue(id!),value,StringComparison.Ordinal),
            css+" exact Unicode value, no extra key chars");
    }
    string? submit=browser.FindVisible("form button[type='submit']");
    Verify(submit!=null,"submit button");
    browser.Click(submit!);
    Verify(browser.IsPresent("body[data-ok='1']"),"native submit event");
    Verify(OriginalTimezoneSync.GermanDaylightSavingRules(),"Berlin winter/summer offsets");
    Console.WriteLine("PASS: Firefox exact plus/email, Unicode password, six-digit OTP and native button click; Berlin CET/CEST");
}
finally {
    try{Directory.Delete(dir,true);}catch{}
}
