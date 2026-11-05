// Native Messaging bridge for FHG's separately signed Firefox companion.
// Reads DPAPI CurrentUser vault and never gives out a TOTP seed or subscription.
// stdout is reserved EXCLUSIVELY for the Firefox native messaging protocol.
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Linq;

internal static class Program
{
    private const string AuthorizedExtension = "fhg-authenticator@fhg.local";
    private const int MaximumRequestBytes = 8192;
    private const int MaximumReplyBytes = 1048576;

    public static int Main(string[] args)
    {
        // Firefox passes native manifest path and the calling add-on ID.
        // Refuse direct invocations and requests from another Firefox extension.
        if (args.Length < 2 || args[1] != AuthorizedExtension) return 3;
        using Stream stdin = Console.OpenStandardInput();
        using Stream stdout = Console.OpenStandardOutput();
        try
        {
            while (true)
            {
                byte[] prefix = new byte[4];
                int count = stdin.Read(prefix, 0, 4);
                if (count == 0) return 0;
                if (count != 4) return 4;
                int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
                if (length <= 0 || length > MaximumRequestBytes) return 5;
                byte[] request = new byte[length];
                stdin.ReadExactly(request, 0, length);
                object response;
                try
                {
                    using var json = JsonDocument.Parse(request);
                    response = Handle(json.RootElement);
                }
                catch (Exception ex)
                {
                    // No exception text: .NET exceptions may include URLs or secrets.
                    response = new { ok = false, error = ex is JsonException ? "INVALID_JSON" : "REQUEST_FAILED" };
                }
                byte[] reply = JsonSerializer.SerializeToUtf8Bytes(response);
                if (reply.Length > MaximumReplyBytes) return 6;
                byte[] header = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(header, reply.Length);
                stdout.Write(header);
                stdout.Write(reply);
                stdout.Flush();
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(request);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(reply);
            }
        }
        catch { return 7; }
    }

    private static object Handle(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("action", out var actionProperty) ||
            actionProperty.ValueKind != JsonValueKind.String)
            return new { ok = false, error = "INVALID_REQUEST" };

        string action = actionProperty.GetString() ?? "";
        var stored = AccountVault.Load();
        string? activeId = AccountVault.ActiveId();
        FhgAccount? active = stored.FirstOrDefault(a => a.Id == activeId);
        switch (action)
        {
            case "list":
                // Every imported account appears automatically in FHG Authenticator,
                // with no extension-side persistence of seeds, passwords or tokens.
                long seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return new {
                    ok = true,
                    accounts = stored.Select(a => {
                        try {
                            return new {
                                id = a.Id,
                                label = a.Label,
                                active = a.Id == activeId,
                                code = AccountVault.GenerateTotp(a.TotpSeed, DateTimeOffset.UtcNow),
                                secondsRemaining = 30 - (int)(seconds % 30),
                                error = (string?)null
                            };
                        }
                        catch {
                            return new {
                                id = a.Id, label = a.Label, active = a.Id == activeId,
                                code = "", secondsRemaining = 0,
                                error = (string?)"INVALID_TOTP"
                            };
                        }
                    }).ToArray()
                };
            case "email":
                if (!IsTrustedRequest(root, "https://chatgpt.com", "/"))
                    return new { ok = false, error = "ORIGIN_REJECTED" };
                if (active is null) return new { ok = false, error = "NO_ACTIVE_ACCOUNT" };
                return new { ok = true, value = active.Label };
            case "password":
                if (!IsTrustedRequest(root, "https://auth.openai.com", "/log-in/password"))
                    return new { ok = false, error = "ORIGIN_REJECTED" };
                if (active is null) return new { ok = false, error = "NO_ACTIVE_ACCOUNT" };
                return new { ok = true, value = active.Password };
            case "totp":
                if (!IsTrustedRequest(root, "https://auth.openai.com", "/mfa-challenge/"))
                    return new { ok = false, error = "ORIGIN_REJECTED" };
                if (active is null) return new { ok = false, error = "NO_ACTIVE_ACCOUNT" };
                try { return new { ok = true, value = AccountVault.GenerateTotp(active.TotpSeed, DateTimeOffset.UtcNow) }; }
                catch { return new { ok = false, error = "INVALID_TOTP" }; }
            default:
                return new { ok = false, error = "UNKNOWN_ACTION" };
        }
    }

    private static bool IsTrustedRequest(JsonElement request, string allowedOrigin, string pathPrefix)
    {
        if (!request.TryGetProperty("origin", out var origin) ||
            origin.ValueKind != JsonValueKind.String ||
            origin.GetString() != allowedOrigin)
            return false;
        if (!request.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String)
            return false;
        string p = path.GetString() ?? "";
        if (p.Length > 2048 || !p.StartsWith(pathPrefix, StringComparison.Ordinal))
            return false;
        if (allowedOrigin == "https://chatgpt.com")
            return p == "/" || p.StartsWith("/login", StringComparison.Ordinal) ||
                   p.StartsWith("/auth/login", StringComparison.Ordinal);
        return true;
    }
}
