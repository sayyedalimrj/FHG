using System;
using System.Collections.Generic;
using System.IO;

// A small, non-sensitive per-account state file. Both the FHG Login helper
// and Workspace read it. No email, password, OTP, page URL or cookie is stored.
internal static class LoginProgress
{
    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal)
    {
        "NOT_STARTED", "STARTING", "EMAIL_SUBMITTED",
        "PASSWORD_SUBMITTED", "TOTP_SUBMITTED",
        "EMAIL_READY", "PASSWORD_READY", "TOTP_READY",
        "MANUAL_REQUIRED", "MANUAL_CHALLENGE", "MANUAL_BROWSER", "VERIFYING",
        "SUCCESS", "HANDOFF_FAILED", "CANCELED", "ERROR"
    };

    private static string StatePath(string id)
    {
        if (!AccountVault.ValidId(id))
            throw new ArgumentException("Invalid account identifier", nameof(id));
        return Path.Combine(AccountVault.Root, id, "login-status.txt");
    }

    internal static void Write(string id, string code)
    {
        if (!Codes.Contains(code))
            throw new ArgumentException("Unknown login status", nameof(code));
        string path = StatePath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, code + Environment.NewLine);
            File.Move(temp, path, overwrite: true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    internal static string Read(string id)
    {
        try
        {
            string path = StatePath(id);
            if (!File.Exists(path)) return "NOT_STARTED";
            string value = File.ReadAllText(path).Trim();
            return Codes.Contains(value) ? value : "ERROR";
        }
        catch { return "ERROR"; }
    }

    internal static string Display(string code) => code switch
    {
        "STARTING" => "Signing in: opening the protected browser",
        "EMAIL_READY" => "Email entered — click Continue in Firefox",
        "PASSWORD_READY" => "Password entered — click Continue yourself",
        "TOTP_READY" => "2FA code entered — click Continue yourself",
        "MANUAL_BROWSER" => "Security verification: complete login in normal Firefox",
        "EMAIL_SUBMITTED" => "Email submitted automatically — waiting for password",
        "PASSWORD_SUBMITTED" => "Password submitted — waiting for verification",
        "TOTP_SUBMITTED" => "2FA code submitted — waiting for ChatGPT",
        "MANUAL_REQUIRED" => "Action needed: complete the blocked step in Firefox",
        "MANUAL_CHALLENGE" => "Action needed: complete CAPTCHA in Firefox",
        "VERIFYING" => "Checking the authenticated ChatGPT session",
        "SUCCESS" => "LOGIN VERIFIED — authenticated ChatGPT session",
        "HANDOFF_FAILED" => "Login verified, but normal Firefox did not stay open",
        "CANCELED" => "Login canceled by user",
        "ERROR" => "Login could not be verified — see FHG diagnostics",
        _ => "Login not started"
    };
}
