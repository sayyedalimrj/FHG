using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;

// Narrow W3C WebDriver client, kept loopback-only and alive for a single
// sign-in attempt. It is shut down before regular Firefox opens.
// No passwords, OTP codes, page HTML, browser URL queries or session IDs
// are written to logs or command-line arguments.
internal sealed class FirefoxDriverBridge : IDisposable
{
    private const string ElementKey = "element-6066-11e4-a52e-4f735466cecf";
    private readonly HttpClient client;
    private readonly Process service;
    private string? sessionId;
    private bool closed;

    public FirefoxDriverBridge(string driverExecutable, string firefoxExecutable, string profile)
    {
        var local = new TcpListener(IPAddress.Loopback, 0);
        local.Start();
        int port = ((IPEndPoint)local.LocalEndpoint).Port;
        local.Stop();

        var info = new ProcessStartInfo(driverExecutable) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(driverExecutable)!
        };
        // Required since Firefox 138 for automated access to Mozilla-owned
        // extension pages. The session remains localhost-only and temporary.
        info.ArgumentList.Add("--allow-system-access");
        info.ArgumentList.Add("--host");
        info.ArgumentList.Add("127.0.0.1");
        info.ArgumentList.Add("--port");
        info.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("--log");
        info.ArgumentList.Add("fatal");
        service = Process.Start(info) ?? throw new InvalidOperationException("GECKODRIVER_UNAVAILABLE");
        // Consume diagnostic pipes in memory but do not save or reveal output.
        service.OutputDataReceived += (_, _) => { };
        service.ErrorDataReceived += (_, _) => { };
        service.BeginOutputReadLine();
        service.BeginErrorReadLine();

        client = new HttpClient(new HttpClientHandler {
            UseProxy = false,
            AllowAutoRedirect = false
        }) {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        bool ready = false;
        for (int i = 0; i < 75 && !service.HasExited; i++)
        {
            try {
                using var reply = client.GetAsync("status").GetAwaiter().GetResult();
                if (reply.IsSuccessStatusCode) { ready = true; break; }
            }
            catch (HttpRequestException) {}
            catch (TaskCanceledException) {}
            Thread.Sleep(200);
        }
        if (!ready) throw new InvalidOperationException("WEBDRIVER_NOT_READY");

        // Use the EXISTING account-specific Firefox directory (not an
        // ephemeral copy), so successful login sessions persist across runs.
        // Firefox's bundled enterprise policies enforce its SOCKS5-only path.
        var capabilities = new {
            capabilities = new {
                alwaysMatch = new Dictionary<string,object> {
                    ["browserName"] = "firefox",
                    ["acceptInsecureCerts"] = false,
                    ["pageLoadStrategy"] = "eager",
                    ["unhandledPromptBehavior"] = "dismiss and notify",
                    ["moz:firefoxOptions"] = new {
                        binary = firefoxExecutable,
                        args = new []{"-no-remote","-profile",profile},
                        log = new { level = "fatal" }
                    }
                }
            }
        };
        var created = Request(HttpMethod.Post, "session", capabilities);
        if (!created.TryGetProperty("sessionId", out var field) ||
            string.IsNullOrEmpty(field.GetString()))
            throw new InvalidOperationException("WEBDRIVER_SESSION_INVALID");
        sessionId = field.GetString()!;
    }

    private JsonElement Request(HttpMethod method, string relative, object? payload = null)
    {
        using var request = new HttpRequestMessage(method, relative);
        if (payload != null)
        {
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload));
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }
        using var response = client.Send(request);
        // Discard response bodies on unsuccessful requests; never print
        // errors that could contain sensitive input or browser state.
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("WEBDRIVER_REQUEST_REJECTED");
        using var data = JsonDocument.Parse(response.Content.ReadAsStream());
        if (!data.RootElement.TryGetProperty("value", out var value))
            throw new InvalidOperationException("WEBDRIVER_BAD_REPLY");
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("error",out _))
            throw new InvalidOperationException("WEBDRIVER_ERROR");
        return value.Clone();
    }

    private string PathFor(string suffix)
    {
        if (sessionId == null) throw new InvalidOperationException("WEBDRIVER_SESSION_MISSING");
        return "session/" + Uri.EscapeDataString(sessionId) + "/" + suffix;
    }

    public void GoTo(string url) =>
        Request(HttpMethod.Post, PathFor("url"), new { url });

    public void SetContext(string context)
    {
        if (context != "chrome" && context != "content")
            throw new ArgumentException("Invalid Marionette context");
        Request(HttpMethod.Post, PathFor("moz/context"), new { context });
    }

    public JsonElement ExecuteScript(string script, params object[] args) =>
        Request(HttpMethod.Post, PathFor("execute/sync"), new { script, args });

    public JsonElement ExecuteAsyncScript(string script, params object[] args)
    {
        Request(HttpMethod.Post, PathFor("timeouts"), new { script = 15000 });
        return Request(HttpMethod.Post, PathFor("execute/async"), new { script, args });
    }

    public Uri? CurrentUri()
    {
        static Uri? Decode(JsonElement value) =>
            value.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) ? uri : null;
        Uri? current = Decode(Request(HttpMethod.Get, PathFor("url")));
        if (current is null || (current.Scheme != "about" &&
                                current.Scheme != "data" &&
                                current.Scheme != "moz-extension"))
            return current;

        // Mozilla opens its signed add-on import page in a new tab. W3C
        // WebDriver remains bound to the old tab until we switch handles.
        // Only switch to a trusted internal moz-extension import page, never
        // to another website or third-party login origin.
        JsonElement handles = Request(HttpMethod.Get, PathFor("window/handles"));
        if (handles.ValueKind != JsonValueKind.Array || handles.GetArrayLength() < 2)
            return current;
        JsonElement previous = Request(HttpMethod.Get, PathFor("window"));
        string? previousId = previous.ValueKind == JsonValueKind.String
            ? previous.GetString() : null;
        foreach (var entry in handles.EnumerateArray().Reverse())
        {
            string? handle = entry.GetString();
            if (string.IsNullOrWhiteSpace(handle) || handle == previousId) continue;
            try
            {
                Request(HttpMethod.Post, PathFor("window"), new { handle });
                Uri? trial = Decode(Request(HttpMethod.Get, PathFor("url")));
                if (trial?.Scheme == "moz-extension" &&
                    trial.AbsolutePath == "/view/import.html")
                    return trial;
            }
            catch (InvalidOperationException) { }
        }
        if (previousId != null)
            Request(HttpMethod.Post, PathFor("window"), new { handle = previousId });
        return current;
    }

    public bool IsPresent(string css)
    {
        var result = Request(HttpMethod.Post, PathFor("elements"), new { @using = "css selector", value = css });
        return result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0;
    }

    public string? FindVisible(string css)
    {
        var matches = Request(HttpMethod.Post, PathFor("elements"), new { @using = "css selector", value = css });
        if (matches.ValueKind != JsonValueKind.Array) return null;
        foreach (var entry in matches.EnumerateArray().Take(6))
        {
            if (!entry.TryGetProperty(ElementKey, out var v)) continue;
            string? id = v.GetString();
            if (id == null) continue;
            try
            {
                var displayed = Request(HttpMethod.Get, PathFor("element/" + Uri.EscapeDataString(id) + "/displayed"));
                var enabled = Request(HttpMethod.Get, PathFor("element/" + Uri.EscapeDataString(id) + "/enabled"));
                if (displayed.ValueKind == JsonValueKind.True && enabled.ValueKind == JsonValueKind.True)
                    return id;
            }
            catch (InvalidOperationException) { /* stale element */ }
        }
        return null;
    }

    public void Fill(string id, string value)
    {
        string safe = Uri.EscapeDataString(id);
        Request(HttpMethod.Post, PathFor("element/" + safe + "/clear"), new {});
        Request(HttpMethod.Post, PathFor("element/" + safe + "/value"), new { text = value });
    }

    public void Click(string id) =>
        Request(HttpMethod.Post, PathFor("element/" + Uri.EscapeDataString(id) + "/click"), new {});

    public void Enter(string id) =>
        Request(HttpMethod.Post, PathFor("element/" + Uri.EscapeDataString(id) + "/value"),
            new { text = "\uE007" });

    public bool IsAuthenticatedUi()
    {
        var result = Request(HttpMethod.Post, PathFor("execute/sync"), new {
            script = "return !!document.querySelector('[data-testid=\"profile-button\"], [data-testid=\"account-menu-button\"]') && !!document.querySelector('[data-testid=\"prompt-textarea\"], #prompt-textarea');",
            args = Array.Empty<object>()
        });
        return result.ValueKind == JsonValueKind.True;
    }

    public void Dispose()
    {
        if (closed) return;
        closed = true;
        try
        {
            if (sessionId != null)
            {
                // Deleting session terminates the automation browser and
                // flushes its account-specific profile before normal Firefox starts.
                Request(HttpMethod.Delete,
                    "session/" + Uri.EscapeDataString(sessionId), null);
            }
        }
        catch {}
        try { if (!service.HasExited) service.Kill(entireProcessTree: true); }
        catch {}
        try { service.WaitForExit(1500); } catch {}
        service.Dispose();
        client.Dispose();
    }
}
