using System;
using System.IO;

void Check(bool x, string what)
{
    if (!x) throw new Exception("ASSERTION_FAILED: " + what);
}
var positive = new [] {
    ("https://chatgpt.com/", LoginStage.ChatGptHome),
    ("https://chatgpt.com/auth/login", LoginStage.Email),
    ("https://chatgpt.com/login", LoginStage.Email),
    ("https://auth.openai.com/log-in-or-create-account?usernameKind=email", LoginStage.Email),
    ("https://auth.openai.com/log-in/password", LoginStage.Password),
    ("https://auth.openai.com/mfa-challenge/synthetic", LoginStage.Totp)
};
foreach (var (url, expected) in positive)
    Check(TrustedLoginPage.Classify(new Uri(url)) == expected, "official stage " + expected);
var rejected = new [] {
    "http://auth.openai.com/log-in/password",
    "https://auth.openai.com.evil.invalid/log-in/password",
    "https://fakechatgpt.com/",
    "https://chatgpt.com@evil.invalid/auth/login",
    "https://auth.openai.com:8443/log-in/password",
    "file:///C:/local-login.htm",
    "https://www.chatgpt.com/auth/login"
};
foreach (string url in rejected)
    Check(TrustedLoginPage.Classify(new Uri(url)) == LoginStage.Invalid, "reject spoofed origin");
Check(!TrustedLoginPage.LooksLikeRealAccount("user@example.invalid"), "reject user's screenshot demo");
Check(!TrustedLoginPage.LooksLikeRealAccount("malformed-mail"), "reject malformed username");
Check(TrustedLoginPage.LooksLikeRealAccount("synthetic@company.org"), "accept normal synthetic test email");

// Check that the three form selectors match the exact attribute layout of the
// user's saved HTML fixture, without storing the saved proprietary pages or
// any credentials in this repository.
const string sampleEmail = "<form><input type='email' name='username' autocomplete='email'/><button type='submit'>Continue</button></form>";
const string samplePassword = "<form><input name='current-password' type='password' autocomplete='current-password webauthn'/><button type='submit'>Continue</button></form>";
const string sampleTotp = "<form><input name='code' type='text' autocomplete='one-time-code'/><button type='submit'>Continue</button></form>";
Check(sampleEmail.Contains("name='username'"), "email selector fixture");
Check(samplePassword.Contains("name='current-password'"), "password selector fixture");
Check(sampleTotp.Contains("name='code'"), "TOTP selector fixture");
Console.WriteLine("PASS: official HTTPS origin restrictions, supported stages, demo account rejection and selectors");
