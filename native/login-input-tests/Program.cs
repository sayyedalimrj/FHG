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
        "<button type='submit' onpointerdown='document.body.dataset.pointerdown=event.isTrusted?1:0' onpointerup='document.body.dataset.pointerup=event.isTrusted?1:0'>Continue</button></form></body></html>";
    browser.GoTo("data:text/html;charset=utf-8,"+Uri.EscapeDataString(html));
    var cases=new (string Selector,string Value)[] {
        ("input[name='username']","reader+tag@example.invalid"),
        ("input[name='current-password']","P@ss+é+ک#123"),
        ("input[name='code']","009876")
    };
    foreach(var (css,value) in cases) {
        string? id=browser.FindVisible(css);
        Verify(id!=null,css+" visible");
        browser.TypeIntoField(id!,value);
        Verify(string.Equals(browser.ElementValue(id!),value,StringComparison.Ordinal),
            css+" exact Unicode value, no extra key chars");
    }
    // Automation typed into inputs, but must never submit the form. This
    // browser test separately performs an explicit, simulated USER click.
    Verify(!browser.IsPresent("body[data-ok='1']"),"Continue not automatically submitted");
    string? submit=browser.FindVisible("form button[type='submit']");
    Verify(submit!=null,"submit button");
    browser.Click(submit!);
    Verify(browser.IsPresent("body[data-ok='1']"),"native submit event");
    Verify(browser.IsPresent("body[data-pointerdown='1'][data-pointerup='1']"),"trusted physical pointer down/up events");
    // Full-page Cloudflare-style verification must be recognized without
    // attempting any CAPTCHA solve or submitting credentials to it.
    const string challenge="<html><head><title>Just a moment...</title></head>"+
        "<body><h2>auth.openai.com</h2><p>Verifying you are human.</p></body></html>";
    browser.GoTo("data:text/html;charset=utf-8,"+Uri.EscapeDataString(challenge));
    Verify(browser.IsSecurityChallenge(),"Cloudflare-style security page recognized");
    Verify(OriginalTimezoneSync.GermanDaylightSavingRules(),"Berlin winter/summer offsets");
    Console.WriteLine("PASS: Firefox exact plus/email, Unicode password, six-digit OTP and NO automatic Continue, explicit simulated user click, safe challenge detection; Berlin CET/CEST");
}
finally {
    try{Directory.Delete(dir,true);}catch{}
}
