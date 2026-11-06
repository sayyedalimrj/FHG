using System;

internal enum LoginStage { Invalid, ChatGptHome, Email, Password, Totp, UnknownOfficial }

internal static class TrustedLoginPage
{
    public static LoginStage Classify(Uri? uri)
    {
        if (uri == null || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            return LoginStage.Invalid;
        string path = uri.AbsolutePath;
        if (uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase))
        {
            if (path == "/") return LoginStage.ChatGptHome;
            if (path.Equals("/auth/login", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/auth/login/", StringComparison.OrdinalIgnoreCase))
                return LoginStage.Email;
            return LoginStage.UnknownOfficial;
        }
        if (uri.Host.Equals("auth.openai.com", StringComparison.OrdinalIgnoreCase))
        {
            if (path.StartsWith("/log-in/password", StringComparison.OrdinalIgnoreCase))
                return LoginStage.Password;
            if (path.StartsWith("/mfa-challenge/", StringComparison.OrdinalIgnoreCase))
                return LoginStage.Totp;
            if (path.StartsWith("/log-in-or-create-account", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/log-in", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/log-in/", StringComparison.OrdinalIgnoreCase))
                return LoginStage.Email;
            return LoginStage.UnknownOfficial;
        }
        return LoginStage.Invalid;
    }

    public static bool LooksLikeRealAccount(string email) =>
        !string.IsNullOrWhiteSpace(email) &&
        email.Length <= 200 &&
        email.Contains('@') &&
        !email.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) &&
        !email.EndsWith(".example", StringComparison.OrdinalIgnoreCase) &&
        !email.EndsWith(".test", StringComparison.OrdinalIgnoreCase) &&
        !email.Contains('\r') && !email.Contains('\n') &&
        System.Net.Mail.MailAddress.TryCreate(email, out var addr) &&
        string.Equals(addr.Address, email, StringComparison.OrdinalIgnoreCase);
}
