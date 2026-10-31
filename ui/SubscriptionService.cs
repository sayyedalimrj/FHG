using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FHG.UI;

public sealed class SubscriptionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New subscription";
    public string Source { get; set; } = "";
    public string? LocalPath { get; set; }
    public string? PreferredServer { get; set; }
    public string? PreferredGroup { get; set; }
    public long? Upload { get; set; }
    public long? Download { get; set; }
    public long? Total { get; set; }
    public long? ExpireUnix { get; set; }
    public DateTimeOffset? RefreshedUtc { get; set; }
    public bool IsRemote => Uri.TryCreate(Source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    [JsonIgnore] public string DisplayName => Name;
    [JsonIgnore] public string Usage
    {
        get
        {
            if (!Upload.HasValue && !Download.HasValue) return "No usage reported";
            double used = Math.Max(0, Upload.GetValueOrDefault()) + Math.Max(0, Download.GetValueOrDefault());
            return Total is > 0 ? $"{FormatBytes(used)} / {FormatBytes(Total.Value)}" : $"{FormatBytes(used)} / unlimited";
        }
    }
    [JsonIgnore] public string Remaining
    {
        get
        {
            if (!Upload.HasValue && !Download.HasValue) return "—";
            if (Total is null or 0) return "Unlimited";
            return FormatBytes(Math.Max(0, (double)Total.Value - Math.Max(0, Upload.GetValueOrDefault()) - Math.Max(0, Download.GetValueOrDefault())));
        }
    }
    [JsonIgnore] public string Expiry
    {
        get
        {
            if (!ExpireUnix.HasValue || ExpireUnix.Value <= 0) return "Not provided";
            try
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(ExpireUnix.Value);
                int days = (int)Math.Ceiling((date - DateTimeOffset.Now).TotalDays);
                return days < 0 ? "Expired" : $"{days} days · {date:yyyy-MM-dd}";
            }
            catch { return "Invalid date"; }
        }
    }
    public static string FormatBytes(double bytes)
    {
        double gib = bytes / 1073741824d;
        return gib >= 1d ? $"{gib:0.##} GiB" : $"{bytes / 1048576d:0.#} MiB";
    }
}

public sealed class ServerItem
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Group { get; set; } = "";
    public string Latency { get; set; } = "—";
    public bool Selected { get; set; }
}

public sealed class SubscriptionStore
{
    private readonly string _path;
    public ObservableCollection<SubscriptionItem> Items { get; } = new();

    public SubscriptionStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "subscriptions.json");
        try
        {
            if (File.Exists(_path))
            {
                var saved = JsonSerializer.Deserialize<SubscriptionItem[]>(File.ReadAllText(_path)) ?? [];
                foreach (var item in saved.Where(x => !string.IsNullOrWhiteSpace(x.Source) || !string.IsNullOrWhiteSpace(x.LocalPath)).Take(100))
                    Items.Add(item);
            }
        }
        catch
        {
            // Do not silently discard a corrupt database by overwriting it.
            throw new InvalidDataException("Cannot load local subscriptions.json. Backup or repair it first.");
        }
    }

    public SubscriptionItem Add(string text)
    {
        text = text.Trim();
        if (text.Length is 0 or > 1048576) throw new InvalidDataException("Profile must be between 1 B and 1 MiB");
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Subscription URLs must use HTTPS; insecure HTTP is not accepted.");
        bool url = Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                   uri.Scheme == Uri.UriSchemeHttps && !text.Contains('\n') && !text.Contains('\r');
        if (url && uri!.UserInfo.Length > 0)
            throw new InvalidDataException("Subscription URLs containing username/password are not supported.");
        // Avoid saving URL in the profile's display name (URL may contain a private token).
        var existing = Items.FirstOrDefault(x => x.Source == text);
        if (existing is not null) return existing;
        var item = new SubscriptionItem
        {
            Source = url ? text : "",
            Name = url ? "Subscription " + (Items.Count + 1) : "Local profile " + (Items.Count + 1),
        };
        if (!url)
        {
            string profileDir = Path.Combine(Path.GetDirectoryName(_path)!, "Profiles");
            Directory.CreateDirectory(profileDir);
            string file = Path.Combine(profileDir, item.Id + ".txt");
            File.WriteAllText(file, text, new UTF8Encoding(false));
            item.LocalPath = file;
            TryReadInlineHeaders(item, text);
        }
        Items.Add(item);
        Save();
        return item;
    }

    public void Remove(SubscriptionItem item)
    {
        if (!Items.Remove(item)) return;
        if (item.LocalPath is not null)
        {
            try
            {
                string folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_path)!, "Profiles")) + Path.DirectorySeparatorChar;
                string target = Path.GetFullPath(item.LocalPath);
                if (target.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) File.Delete(target);
            }
            catch { /* do not fail removing the record because file deletion failed */ }
        }
        Save();
    }

    public void Save()
    {
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Items.ToArray(), new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(tmp, _path, overwrite:true);
    }

    public static void TryReadInlineHeaders(SubscriptionItem item, string content)
    {
        foreach (string line in content.Split('\n').Take(12))
        {
            string trimmed = line.Trim().TrimStart('#', '/').Trim();
            int colon = trimmed.IndexOf(':');
            if (colon < 1) continue;
            string key = trimmed[..colon].Trim();
            string value = trimmed[(colon + 1)..].Trim();
            ApplyHeader(item, key, value);
        }
    }

    public static void ApplyHeader(SubscriptionItem item, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "profile-title":
                if (value.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
                {
                    try { value = Encoding.UTF8.GetString(Convert.FromBase64String(value[7..])); } catch { return; }
                }
                value = new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
                if (value.Length is > 0 and <= 96) item.Name = value;
                break;
            case "subscription-userinfo":
                foreach (var pair in value.Split(';'))
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length != 2 || !long.TryParse(parts[1].Trim(), out var number) || number < 0) continue;
                    switch (parts[0].Trim().ToLowerInvariant())
                    {
                        case "upload": item.Upload = number; break;
                        case "download": item.Download = number; break;
                        case "total": item.Total = number; break;
                        case "expire": item.ExpireUnix = number; break;
                    }
                }
                break;
        }
    }

    public static async Task RefreshRemoteMetadataAsync(SubscriptionItem item, CancellationToken ct = default)
    {
        if (!item.IsRemote) throw new InvalidOperationException("Selected profile is not a subscription URL.");
        // Deliberately no direct HTTP client: every network request must use SOCKS5.
        var proxy = new WebProxy("socks5://127.0.0.1:12334");
        using var handler = new HttpClientHandler
        {
            Proxy = proxy, UseProxy = true, UseCookies = false, AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FHG/0.2.0");
        using var head = new HttpRequestMessage(HttpMethod.Head, item.Source);
        using var headResponse = await http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
        // Most providers expose metadata in HEAD. Some reject HEAD, so fall back to
        // response headers from GET without downloading or buffering its body.
        HttpResponseMessage? getResponse = null;
        try
        {
            var response = headResponse;
            if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, item.Source);
                getResponse = await http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct);
                response = getResponse;
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Subscription metadata unavailable (HTTP {(int)response.StatusCode}).");
            foreach (var key in new[] { "subscription-userinfo", "profile-title" })
                if (response.Headers.TryGetValues(key, out var values))
                    ApplyHeader(item, key, values.FirstOrDefault() ?? "");
            item.RefreshedUtc = DateTimeOffset.UtcNow;
        }
        finally { getResponse?.Dispose(); }
    }
}

public sealed class ClashControl : IDisposable
{
    private readonly HttpClient _client;
    private readonly int _port;
    private readonly string _secret;

    public ClashControl(int port, string secret)
    {
        _port = port;
        _secret = secret;
        _client = new HttpClient(new HttpClientHandler
        {
            UseProxy = false, AllowAutoRedirect = false, UseCookies = false
        }) { Timeout = TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
    }

    public void Dispose() => _client.Dispose();
    private Uri Api(string path) => new($"http://127.0.0.1:{_port}{path}");

    public async Task<(ServerItem[] Servers, string Selector)> GetServersAsync(CancellationToken ct = default)
    {
        using var response = await _client.GetAsync(Api("/proxies"), ct);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct);
        if (content.Length > 4 * 1024 * 1024) throw new InvalidDataException("Clash API payload too large");
        using var doc = JsonDocument.Parse(content);
        var dictionary = doc.RootElement.GetProperty("proxies");
        var groups = dictionary.EnumerateObject()
            .Where(x => x.Value.TryGetProperty("type", out var t) && t.GetString() == "Selector" &&
                        x.Value.TryGetProperty("all", out var a) && a.ValueKind == JsonValueKind.Array)
            .ToArray();
        // Prefer a selector offering actual proxy nodes; never treat GLOBAL as a single server.
        var chosen = groups.OrderByDescending(g => g.Value.GetProperty("all").GetArrayLength())
                           .ThenBy(g => g.Name == "GLOBAL" ? 1 : 0)
                           .FirstOrDefault();
        string groupName = chosen.Name ?? "";
        var members = chosen.Value.ValueKind == JsonValueKind.Object
            ? chosen.Value.GetProperty("all").EnumerateArray()
                  .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToHashSet()
            : new System.Collections.Generic.HashSet<string>();
        var list = new System.Collections.Generic.List<ServerItem>();
        foreach (var member in dictionary.EnumerateObject())
        {
            if (member.Value.ValueKind != JsonValueKind.Object || !member.Value.TryGetProperty("type", out var type)) continue;
            string typeStr = type.GetString() ?? "";
            if (typeStr is "Direct" or "Reject" or "Block" or "DNS" or "Selector" or "URLTest") continue;
            if (members.Count > 0 && !members.Contains(member.Name)) continue;
            string latency = "—";
            if (member.Value.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Array && history.GetArrayLength() > 0)
            {
                var last = history[history.GetArrayLength() - 1];
                if (last.TryGetProperty("delay", out var delay) && delay.TryGetInt32(out var ms) && ms > 0)
                    latency = $"{ms} ms";
            }
            list.Add(new ServerItem { Name = member.Name, Type = typeStr, Group = groupName, Latency = latency,
                Selected = chosen.Value.ValueKind == JsonValueKind.Object &&
                           chosen.Value.TryGetProperty("now", out var now) && now.GetString() == member.Name });
        }
        return (list.ToArray(), groupName);
    }

    public async Task<int?> TestNodeAsync(string tag, CancellationToken ct=default)
    {
        string safe = Uri.EscapeDataString(tag);
        string endpoint = $"/proxies/{safe}/delay?timeout=6000&url={Uri.EscapeDataString("https://www.gstatic.com/generate_204")}";
        using var result = await _client.GetAsync(Api(endpoint), ct);
        if (!result.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await result.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("delay", out var value) && value.TryGetInt32(out int ms) && ms > 0 ? ms : null;
    }

    public async Task SelectNodeAsync(string group, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(group)) throw new InvalidOperationException("No selectable server group exposed by Hiddify.");
        using var body = new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json");
        using var response = await _client.PutAsync(Api("/proxies/" + Uri.EscapeDataString(group)), body, ct);
        response.EnsureSuccessStatusCode();
    }

    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
