using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FHG.UI;

public partial class MainWindow : Window
{
    private const int ProxyPort = 12334;
    private static readonly Uri LatencyUrl = new("https://www.gstatic.com/generate_204");
    private readonly string _root = AppContext.BaseDirectory;
    private readonly string _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FHG");
    private readonly string _firefoxDirectory;
    private readonly string _firefox;
    private readonly string _coreExecutable;
    private readonly string _guardExecutable;
    private readonly DispatcherTimer _timer;
    private Process? _core;
    private bool _guardReady;
    private bool _busy;
    private bool _checking;
    private int _ticks;
    private DateTime _lastLatency = DateTime.MinValue;

    private static readonly Brush Good = Brush("#4ADEB5");
    private static readonly Brush Warning = Brush("#E9BD77");
    private static readonly Brush Neutral = Brush("#9DB2D1");

    public MainWindow()
    {
        _firefoxDirectory = Path.Combine(_root, "Firefox");
        _firefox = Path.Combine(_firefoxDirectory, "firefox.exe");
        _coreExecutable = Path.Combine(_root, "Core", "HiddifyCli.exe");
        _guardExecutable = Path.Combine(_root, "FHGGuard.exe");
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += Timer_Tick;
        ConfigInput.TextChanged += (_, _) => UpdateConfigHint();
        Loaded += Window_Loaded;
        Closing += Window_Closing;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_dataDirectory);
        TryCreateShortcut();
        var remembered = Path.Combine(_dataDirectory, "last-profile.txt");
        // A profile may contain credentials; do not automatically display them again in the UI.
        if (File.Exists(remembered))
            ConfigHint.Text = "A local profile is saved. Paste another profile or click Connect to reuse it.";
        await RefreshGuardAsync();
        RefreshCoreStatus();
        _timer.Start();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        StopCore();
        // Firefox stays open; its persistent WFP rules remain installed.
    }

    private void TryCreateShortcut()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop)) return;
            string shortcutPath = Path.Combine(desktop, "FHG Browser.lnk");
            if (File.Exists(shortcutPath)) return;
            Type? type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Path.Combine(_root, "FHG.exe");
            shortcut.WorkingDirectory = _root;
            shortcut.Description = "FHG Firefox + Hiddify";
            shortcut.IconLocation = Path.Combine(_root, "FHG.exe") + ",0";
            shortcut.Save();
        }
        catch
        {
            // Desktop shortcut is optional; never block browser functionality.
        }
    }

    private void UpdateConfigHint()
    {
        string content = ConfigInput.Text.Trim();
        if (content.Length == 0)
        {
            ConfigHint.Text = "Paste a profile or import a file. Saved profile can be reused.";
            return;
        }
        if (content.Length > 1024 * 1024)
        {
            ConfigHint.Text = "Profile too large (max 1 MiB).";
            return;
        }
        if (content.StartsWith("{", StringComparison.Ordinal) || content.StartsWith("[", StringComparison.Ordinal))
        {
            try { using var _ = JsonDocument.Parse(content); ConfigHint.Text = "JSON syntax valid. Ready to connect."; }
            catch { ConfigHint.Text = "JSON syntax error. Correct it before connecting."; }
        }
        else if (content.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ConfigHint.Text = "Subscription URL recognized. Hiddify Core will load it.";
        else if (content.Contains("://", StringComparison.Ordinal))
            ConfigHint.Text = "Proxy/share link recognized. Hiddify Core will parse it.";
        else
            ConfigHint.Text = "Text profile detected. Hiddify Core will validate its format.";
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText()) { SetMessage("Clipboard has no text."); return; }
            string value = Clipboard.GetText();
            if (value.Length > 1024 * 1024) { SetMessage("Clipboard profile is over 1 MiB."); return; }
            ConfigInput.Text = value.Trim();
            SetMessage("Profile pasted locally. Nothing has been uploaded by the FHG UI.");
        }
        catch { SetMessage("Could not access the clipboard."); }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a Hiddify configuration",
            Filter = "Profiles|*.json;*.txt;*.yaml;*.yml;*.conf|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException("Profile is over 1 MiB.");
            ConfigInput.Text = await File.ReadAllTextAsync(dialog.FileName);
            SetMessage("File loaded locally. Review the content and press Connect.");
        }
        catch (Exception ex) { SetMessage("Import failed: " + ex.Message); }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ConfigInput.Clear();
        SetMessage("Input cleared. Existing Hiddify connection is unchanged.");
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ConnectButton.IsEnabled = false;
        try { await ConnectCoreAsync(); }
        finally { _busy = false; ConnectButton.IsEnabled = true; }
    }

    private async Task ConnectCoreAsync()
    {
        if (CoreRunning)
        {
            SetMessage("Hiddify Core is already running. Disconnect before changing profile.");
            return;
        }
        if (!File.Exists(_coreExecutable))
        {
            SetMessage("Core/HiddifyCli.exe is missing from the bundle.");
            return;
        }
        if (HasForeignListener())
        {
            SetMessage("Port 12334 is already owned by another process. Refusing to connect.");
            return;
        }
        string input = ConfigInput.Text.Trim();
        string? source = null;
        if (input.Length == 0)
        {
            string saved = Path.Combine(_dataDirectory, "last-profile.txt");
            if (File.Exists(saved)) source = saved;
            else { SetMessage("Paste a share link/URL/JSON or import a file first."); return; }
        }
        else if (input.Length > 1024 * 1024)
        {
            SetMessage("Profile is larger than 1 MiB."); return;
        }
        else if (input.StartsWith("{", StringComparison.Ordinal) || input.StartsWith("[", StringComparison.Ordinal))
        {
            try { using var json = JsonDocument.Parse(input); }
            catch (JsonException) { SetMessage("Invalid JSON syntax. No core started."); return; }
        }

        if (source is null)
        {
            // Hiddify's run -c accepts a URL or a local file. Use the CLI parser for share links.
            // Do not resolve URLs or fetch subscriptions directly from the UI.
            if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                !input.Contains('\n'))
            {
                source = input;
            }
            else
            {
                source = Path.Combine(_dataDirectory, "last-profile.txt");
                await File.WriteAllTextAsync(source, input, new UTF8Encoding(false));
            }
        }
        try
        {
            var start = new ProcessStartInfo(_coreExecutable)
            {
                WorkingDirectory = Path.GetDirectoryName(_coreExecutable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add("run");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(source);
            start.ArgumentList.Add("--in-proxy-port");
            start.ArgumentList.Add(ProxyPort.ToString());
            start.ArgumentList.Add("--log-level");
            start.ArgumentList.Add("warn");
            // No TUN, no Windows-wide proxy settings.
            _core = new Process { StartInfo = start, EnableRaisingEvents = true };
            _core.OutputDataReceived += (_, _) => { }; // never log profile contents or URLs
            _core.ErrorDataReceived += (_, _) => { };
            _core.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                CoreStatus.Text = "Stopped";
                CoreStatus.Foreground = Warning;
                if (ConnectionBadge.Text != "Offline")
                    SetMessage("Hiddify Core stopped. Browser stays open; online requests must fail.");
                UpdateConnectionState(false);
            });
            if (!_core.Start()) throw new InvalidOperationException("Process could not start");
            _core.BeginOutputReadLine();
            _core.BeginErrorReadLine();
            SetMessage("Hiddify Core started. Checking its local SOCKS5 listener...");
            CoreStatus.Text = "Starting";
            CoreStatus.Foreground = Warning;
            await Task.Delay(900);
            RefreshCoreStatus();
        }
        catch (Exception ex)
        {
            SetMessage("Could not start Hiddify Core: " + ex.Message);
            StopCore();
        }
    }

    private bool CoreRunning
    {
        get
        {
            try { return _core is { HasExited: false }; }
            catch { return false; }
        }
    }

    private void StopCore()
    {
        try
        {
            if (CoreRunning) _core!.Kill(entireProcessTree: true);
        }
        catch { }
        try { _core?.Dispose(); } catch { }
        _core = null;
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        StopCore();
        UpdateConnectionState(false);
        CoreStatus.Text = "Stopped";
        LatencyStatus.Text = "—";
        SetMessage("Hiddify disconnected. Firefox stays open; no direct fallback is configured.");
    }

    private static uint ConvertPort(uint port) => (port & 0xFF) << 8 | ((port >> 8) & 0xFF);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, bool order, int family, int tableClass, uint reserved);

    private static uint? ProxyListenerPid()
    {
        uint size = 0;
        const int AF_INET = 2;
        const int TCP_TABLE_OWNER_PID_LISTENER = 3;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) != 122 || size == 0) return null;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return null;
            int count = Marshal.ReadInt32(buffer);
            for (int i = 0; i < count; ++i)
            {
                int offset = 4 + i * 24;
                uint ip = unchecked((uint)Marshal.ReadInt32(buffer, offset + 4));
                uint port = unchecked((uint)Marshal.ReadInt32(buffer, offset + 8));
                uint pid = unchecked((uint)Marshal.ReadInt32(buffer, offset + 20));
                bool addressMatch = ip == 0 || ip == 0x0100007F; // 0.0.0.0 or 127.0.0.1 on little-endian Windows
                if (addressMatch && ConvertPort(port) == ProxyPort) return pid;
            }
            return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private bool HasForeignListener()
    {
        uint? owner = ProxyListenerPid();
        if (owner is null) return false;
        return !CoreRunning || owner.Value != (uint)_core!.Id;
    }

    private bool ProxyOwnedByCore()
    {
        uint? owner = ProxyListenerPid();
        return CoreRunning && owner is not null && owner.Value == (uint)_core!.Id;
    }

    private void RefreshCoreStatus()
    {
        bool active = ProxyOwnedByCore();
        CoreStatus.Text = !CoreRunning ? "Stopped" : active ? "Running" : "Starting";
        CoreStatus.Foreground = active ? Good : Warning;
        ProxyStatus.Text = active ? "Available" : HasForeignListener() ? "Other process" : "Unavailable";
        ProxyStatus.Foreground = active ? Good : Warning;
        UpdateConnectionState(active);
    }

    private void UpdateConnectionState(bool connected)
    {
        ConnectionBadge.Text = connected ? "Connected" : "Offline";
        StateDot.Foreground = connected ? Good : Warning;
        if (!connected) LatencyStatus.Text = "—";
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            RefreshCoreStatus();
            ++_ticks;
            if (_ticks % 4 == 0) await RefreshGuardAsync();
            if (ProxyOwnedByCore() && (DateTime.UtcNow - _lastLatency).TotalSeconds > 18)
                await CheckLatencyAsync();
        }
        finally { _checking = false; }
    }

    private void Ping_Click(object sender, RoutedEventArgs e) => _ = CheckLatencyAsync();

    private async Task CheckLatencyAsync()
    {
        _lastLatency = DateTime.UtcNow;
        if (!ProxyOwnedByCore())
        {
            LatencyStatus.Text = "Offline";
            SetMessage("HTTPS latency test is disabled because Hiddify does not own port 12334.");
            return;
        }
        LatencyStatus.Text = "Testing…";
        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy("socks5://127.0.0.1:12334"),
                UseProxy = true,
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(9) };
            using var request = new HttpRequestMessage(HttpMethod.Get, LatencyUrl);
            var watch = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            watch.Stop();
            LatencyStatus.Text = response.IsSuccessStatusCode ? $"{watch.ElapsedMilliseconds} ms" : $"HTTP {(int)response.StatusCode}";
            if (response.IsSuccessStatusCode) SetMessage("HTTPS round trip measured through Hiddify SOCKS5 (not ICMP ping).");
        }
        catch (Exception)
        {
            LatencyStatus.Text = "Timeout / error";
            SetMessage("HTTPS test through Hiddify failed. No direct-network fallback was used by the test.");
        }
    }

    private async Task<int> GuardStatusAsync()
    {
        if (!File.Exists(_guardExecutable) || !File.Exists(_firefox)) return -1;
        try
        {
            var psi = new ProcessStartInfo(_guardExecutable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _root,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("status");
            psi.ArgumentList.Add(_firefoxDirectory);
            using var process = Process.Start(psi);
            if (process is null) return -1;
            // Read both pipes concurrently to prevent producer buffer deadlocks.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode;
        }
        catch { return -1; }
    }

    private async Task RefreshGuardAsync()
    {
        _guardReady = await GuardStatusAsync() == 0;
        GuardStatus.Text = _guardReady ? "Protected" : "Needs setup";
        GuardStatus.Foreground = _guardReady ? Good : Warning;
    }

    private async Task<bool> EnsureGuardAsync()
    {
        await RefreshGuardAsync();
        if (_guardReady) return true;
        var result = MessageBox.Show(this,
            "FHG must install isolated Windows network filtering for this Firefox folder before it can open.\n\nOnly the FHG browser executables are targeted; ordinary Firefox is unaffected.\n\nWindows will ask for Administrator access once. Continue?",
            "Activate FHG network guard", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) { SetMessage("Guard not active. Browser launch blocked."); return false; }
        try
        {
            var psi = new ProcessStartInfo(_guardExecutable)
            {
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = _root
            };
            psi.ArgumentList.Add("install");
            psi.ArgumentList.Add(_firefoxDirectory);
            using var elevated = Process.Start(psi);
            if (elevated is null) throw new InvalidOperationException("Guard installer did not start");
            await elevated.WaitForExitAsync();
            if (elevated.ExitCode != 0) throw new InvalidOperationException("Windows network guard rejected the policy");
            await RefreshGuardAsync();
            if (!_guardReady) throw new InvalidOperationException("Installed WFP rules could not be verified");
            SetMessage("Network guard installed and verified for FHG's browser folder.");
            return true;
        }
        catch (Exception ex)
        {
            SetMessage("Guard setup failed: " + ex.Message);
            return false;
        }
    }

    private async void Guard_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try { await EnsureGuardAsync(); }
        finally { _busy = false; }
    }

    private async void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        BrowserButton.IsEnabled = false;
        try
        {
            if (!File.Exists(_firefox)) { SetMessage("Firefox/firefox.exe is missing."); return; }
            if (!await EnsureGuardAsync()) return;
            // An offline Hiddify is allowed. Firefox UI stays usable; browser network remains WFP-restricted.
            string profile = Path.Combine(_dataDirectory, "FirefoxProfile");
            Directory.CreateDirectory(profile);
            var start = new ProcessStartInfo(_firefox)
            {
                UseShellExecute = false,
                WorkingDirectory = _firefoxDirectory
            };
            start.ArgumentList.Add("-no-remote");
            start.ArgumentList.Add("-profile");
            start.ArgumentList.Add(profile);
            start.ArgumentList.Add("about:blank");
            using var firefox = Process.Start(start);
            if (firefox is null) throw new InvalidOperationException("Failed to launch Firefox");
            SetMessage("FHG Firefox opened with its own profile. No effect on installed Firefox.");
        }
        catch (Exception ex) { SetMessage("Browser launch failed: " + ex.Message); }
        finally { _busy = false; BrowserButton.IsEnabled = true; }
    }

    private void SetMessage(string text) => MessageStatus.Text = text;
}
