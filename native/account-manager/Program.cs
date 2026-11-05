using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new AccountsWindow());
    }
}

internal sealed class AccountsWindow : Form
{
    private static readonly Color Navy = Color.FromArgb(14, 28, 48);
    private static readonly Color Ink = Color.FromArgb(28, 44, 66);
    private static readonly Color Muted = Color.FromArgb(103, 119, 139);
    private static readonly Color Surface = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(220, 228, 238);
    private static readonly Color Accent = Color.FromArgb(33, 121, 222);
    private static readonly Color Mint = Color.FromArgb(18, 154, 122);
    private static readonly Font NameFont = new("Segoe UI", 13f, FontStyle.Bold);
    private static readonly Font CodeFont = new("Consolas", 20f, FontStyle.Bold);
    private static readonly Font SmallFont = new("Segoe UI", 9f);
    private static readonly Font SmallBoldFont = new("Segoe UI", 9f, FontStyle.Bold);

    private readonly FlowLayoutPanel cards = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(18, 14, 18, 12),
        BackColor = Surface
    };
    private readonly Label subtitle = new()
    {
        AutoSize = false, ForeColor = Color.FromArgb(178, 196, 215),
        Font = new Font("Segoe UI", 10f)
    };
    private readonly Label status = new()
    {
        AutoSize = false, Font = new Font("Segoe UI", 9.3f),
        ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label empty = new()
    {
        AutoSize = false, Font = new Font("Segoe UI", 12f),
        ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter,
        Text = "No accounts yet.\nSelect Add account to import your local four-line file."
    };
    private readonly Dictionary<string, (Label code, Label time)> otpViews = new();
    private readonly System.Windows.Forms.Timer otpTimer = new() { Interval = 1000 };
    // Clear only clipboard values this manager explicitly copied, and only if unchanged.
    private readonly System.Windows.Forms.Timer clipboardTimer = new() { Interval = 1000 };
    private string? lastSensitiveCopy;
    private DateTimeOffset clipboardExpires;
    private FhgAccount[] accounts = Array.Empty<FhgAccount>();
    private string? selectedId;

    public AccountsWindow()
    {
        Text = "FHG Accounts | Secure Workspace";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1030, 735);
        MinimumSize = new Size(835, 575);
        Font = new Font("Segoe UI", 10f);
        BackColor = Surface;
        AutoScaleMode = AutoScaleMode.Dpi;
        MaximizeBox = true;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Surface, ColumnCount = 1, RowCount = 4,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Navy };
        root.Controls.Add(header, 0, 0);
        var brand = new Label
        {
            Text = "◆", AutoSize = false, BackColor = Color.FromArgb(26, 72, 101),
            ForeColor = Color.FromArgb(41, 213, 190), TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 29f, FontStyle.Bold),
            Left = 25, Top = 25, Width = 58, Height = 58
        };
        header.Controls.Add(brand);
        var title = new Label
        {
            Text = "FHG  /  ACCOUNTS", Left = 102, Top = 23, Width = 520, Height = 40,
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(title);
        subtitle.Left = 103; subtitle.Top = 68; subtitle.Width = 710; subtitle.Height = 23;
        subtitle.Text = "Private Firefox sessions   •   Local proxy   •   Encrypted vault";
        header.Controls.Add(subtitle);
        var headerRight = new Label
        {
            Text = "LOCAL VAULT", AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = Color.FromArgb(137, 228, 211), BackColor = Color.FromArgb(24, 65, 84),
            Font = SmallBoldFont, TextAlign = ContentAlignment.MiddleCenter,
            Width = 135, Height = 30, Top = 38
        };
        header.Controls.Add(headerRight);
        header.Resize += (_, _) => headerRight.Left = header.ClientSize.Width - headerRight.Width - 28;
        headerRight.Left = header.Width - headerRight.Width - 28;

        var toolbar = new FlowLayoutPanel
        {
            BackColor = Color.White, Dock = DockStyle.Fill, WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(20, 16, 10, 6), AutoScroll = true
        };
        root.Controls.Add(toolbar, 0, 1);
        toolbar.Controls.Add(MakeButton("+  Add account", AddAccount, true, 156));
        toolbar.Controls.Add(MakeButton("Open selected", () => ActivateAccount(Selected()), false, 151));
        toolbar.Controls.Add(MakeButton("Copy username", () => CopyUsername(Selected()), false, 147));
        toolbar.Controls.Add(MakeButton("Copy password", () => CopyPassword(Selected()), false, 147));
        toolbar.Controls.Add(MakeButton("Change subscription", EditSubscription, false, 171));
        toolbar.Controls.Add(MakeButton("FHG Proxy", OpenProxy, false, 122));
        toolbar.Controls.Add(MakeButton("Refresh", RefreshCards, false, 101));
        toolbar.Controls.Add(MakeButton("Account folder", OpenAccountFolder, false, 140));

        root.Controls.Add(cards, 0, 2);
        cards.Resize += (_, _) => ResizeCards();

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        root.Controls.Add(footer, 0, 3);
        var separator = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Border };
        footer.Controls.Add(separator);
        status.Dock = DockStyle.Fill;
        status.Padding = new Padding(25, 0, 20, 0);
        status.Text = "FHG is ready. Codes are visible locally; don't share them in screenshots.";
        footer.Controls.Add(status);

        otpTimer.Tick += (_, _) => UpdateAllCodes();
        otpTimer.Start();
        clipboardTimer.Tick += (_, _) =>
        {
            if (lastSensitiveCopy != null && DateTimeOffset.UtcNow >= clipboardExpires)
                ClearOwnedClipboard();
        };
        clipboardTimer.Start();
        FormClosed += (_, _) => ClearOwnedClipboard();
        LoadCards();

        Shown += (_, _) =>
        {
            if (accounts.Length == 0) BeginInvoke(new Action(AddAccount));
        };
    }

    private Button MakeButton(string title, Action action, bool primary, int width)
    {
        var button = new Button
        {
            Text = title, Width = width, Height = 40,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Ink,
            Margin = new Padding(0, 1, 10, 5),
            TextAlign = ContentAlignment.MiddleCenter
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(24, 100, 195) : Surface;
        button.Click += (_, _) => Safely(action);
        return button;
    }

    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            // Never display a subscription URL, password or TOTP secret
            // embedded inside a thrown exception.
            status.Text = "Action failed. Check account data, permissions and the FHG installation.";
            MessageBox.Show(this,
                "This action could not complete (" + error.GetType().Name + ").\n" +
                "Check that FHG is correctly installed and your input is valid.\n" +
                "No account passwords or subscription URLs were logged.",
                "FHG Accounts", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadCards(string? focusId = null)
    {
        accounts = AccountVault.Load().ToArray();
        selectedId = focusId ?? selectedId ?? AccountVault.ActiveId();
        otpViews.Clear();
        cards.SuspendLayout();
        cards.Controls.Clear();
        if (accounts.Length == 0)
        {
            empty.Width = Math.Max(400, cards.ClientSize.Width - 60);
            empty.Height = 280;
            cards.Controls.Add(empty);
        }
        else
        {
            foreach (var account in accounts)
                cards.Controls.Add(CreateCard(account));
        }
        cards.ResumeLayout();
        ResizeCards();
        UpdateAllCodes();
        subtitle.Text = accounts.Length + " encrypted account(s)  •  Isolated sessions  •  Proxy 127.0.0.1:12345";
        status.Text = "Select an account to open its isolated Firefox session. 6-digit codes update automatically every 30 seconds.";
    }

    private Panel CreateCard(FhgAccount account)
    {
        bool active = account.Id == AccountVault.ActiveId();
        bool selected = account.Id == selectedId;
        var card = new Panel
        {
            Width = Math.Max(690, cards.ClientSize.Width - 58),
            Height = 190, BackColor = selected ? Color.FromArgb(239, 248, 255) : Color.White,
            BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 13),
            Tag = account.Id, Cursor = Cursors.Hand
        };
        var strip = new Panel
        {
            Dock = DockStyle.Left, Width = 5,
            BackColor = active ? Mint : selected ? Accent : Border
        };
        card.Controls.Add(strip);

        string host;
        try { host = new Uri(account.SubscriptionUrl).Host; }
        catch { host = "Subscription saved"; }

        var name = new Label
        {
            Text = account.Label, Font = NameFont, AutoEllipsis = true,
            ForeColor = Ink, Left = 25, Top = 18, Width = card.Width - 360, Height = 30,
            Cursor = Cursors.Hand
        };
        card.Controls.Add(name);
        var state = new Label
        {
            Text = active ? "●  ACTIVE SESSION" : "○  SAVED / ARCHIVED",
            Font = SmallBoldFont, ForeColor = active ? Mint : Muted,
            Left = 26, Top = 58, Width = 215, Height = 22, Cursor = Cursors.Hand
        };
        card.Controls.Add(state);
        var sub = new Label
        {
            Text = "Subscription: " + host + "   •   Separate Firefox + Proxy profile",
            Font = SmallFont, ForeColor = Muted, AutoEllipsis = true,
            Left = 26, Top = 92, Width = card.Width - 355, Height = 24, Cursor = Cursors.Hand
        };
        card.Controls.Add(sub);
        var setupLink = new LinkLabel
        {
            Text = "Copy setup link for Firefox Authenticator",
            Left = 26, Top = 115, Width = 400, Height = 22,
            LinkColor = Accent, ActiveLinkColor = Mint, Font = SmallFont,
            Cursor = Cursors.Hand
        };
        setupLink.LinkClicked += (_, _) => Safely(() => CopyAuthenticatorSetup(account));
        card.Controls.Add(setupLink);
        var code = new Label
        {
            Text = "••• •••", Font = CodeFont, ForeColor = Ink,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Left = card.Width - 312, Top = 13, Width = 172, Height = 44
        };
        card.Controls.Add(code);
        var countdown = new Label
        {
            Text = "", Font = SmallBoldFont, ForeColor = Muted,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Left = card.Width - 143, Top = 22, Width = 110, Height = 25
        };
        card.Controls.Add(countdown);
        var copyUsername = MakeButton("Copy username", () => CopyUsername(account), false, 142);
        copyUsername.Left = 25; copyUsername.Top = 144; copyUsername.Height = 36;
        card.Controls.Add(copyUsername);
        var copyPassword = MakeButton("Copy password", () => CopyPassword(account), false, 142);
        copyPassword.Left = 180; copyPassword.Top = 144; copyPassword.Height = 36;
        card.Controls.Add(copyPassword);
        var copy = MakeButton("Copy 2FA", () => CopyCode(account), false, 132);
        copy.Left = 335; copy.Top = 144; copy.Height = 36;
        card.Controls.Add(copy);
        var open = MakeButton(active ? "Open Firefox" : "Activate", () => ActivateAccount(account), true, 132);
        open.Left = 480; open.Top = 144; open.Height = 36;
        card.Controls.Add(open);
        card.Resize += (_, _) =>
        {
            name.Width = Math.Max(180, card.ClientSize.Width - 360);
            sub.Width = Math.Max(180, card.ClientSize.Width - 355);
            code.Left = card.ClientSize.Width - 312;
            countdown.Left = card.ClientSize.Width - 143;
        };
        code.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        countdown.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        otpViews[account.Id] = (code, countdown);

        void choose(object? sender, EventArgs e)
        {
            if (selectedId == account.Id) return;
            selectedId = account.Id;
            foreach (Control element in cards.Controls)
            {
                if (element is Panel p && p.Tag is string id)
                    p.BackColor = id == selectedId ? Color.FromArgb(239, 248, 255) : Color.White;
            }
        }
        card.Click += choose;
        name.Click += choose;
        state.Click += choose;
        sub.Click += choose;
        return card;
    }

    private void ResizeCards()
    {
        if (cards.IsDisposed) return;
        int width = Math.Max(680, cards.ClientSize.Width - 57);
        foreach (Control item in cards.Controls) item.Width = width;
    }

    private void UpdateAllCodes()
    {
        long epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int remaining = 30 - (int)(epoch % 30);
        foreach (var account in accounts)
        {
            if (!otpViews.TryGetValue(account.Id, out var fields)) continue;
            try
            {
                fields.code.Text = AccountVault.GenerateTotp(account.TotpSeed, DateTimeOffset.UtcNow);
                fields.time.Text = remaining + "s remaining";
                fields.time.ForeColor = remaining <= 5 ? Color.FromArgb(204, 96, 48) : Muted;
            }
            catch (FormatException)
            {
                fields.code.Text = "Invalid key";
                fields.time.Text = "Check Base32";
            }
        }
    }

    private FhgAccount Selected()
    {
        var selected = accounts.FirstOrDefault(a => a.Id == selectedId);
        if (selected is null) throw new InvalidOperationException("Select an account.");
        return selected;
    }

    private void CopyCode(FhgAccount account)
    {
        // One-time codes must not remain on the clipboard indefinitely.
        string code = AccountVault.GenerateTotp(account.TotpSeed, DateTimeOffset.UtcNow);
        CopySensitive(code, "6-digit code", 25);
    }

    private void CopyUsername(FhgAccount account) =>
        CopySensitive(account.Label, "Username", 45);

    private void CopyPassword(FhgAccount account)
    {
        // Decrypted only for the current Windows user; never inject into an unverified origin.
        CopySensitive(account.Password, "Password", 35);
    }

    private void CopySensitive(string value, string description, int seconds)
    {
        if (string.IsNullOrEmpty(value)) throw new InvalidOperationException("Empty account field.");
        Clipboard.SetText(value);
        lastSensitiveCopy = value;
        clipboardExpires = DateTimeOffset.UtcNow.AddSeconds(seconds);
        status.Text = description + " copied locally. Paste only into the intended " +
                      "ChatGPT sign-in page or trusted Authenticator import view. " +
                      "FHG will clear its clipboard value after " + seconds + " seconds.";
    }

    private void ClearOwnedClipboard()
    {
        string? previous = lastSensitiveCopy;
        lastSensitiveCopy = null;
        if (previous is null) return;
        try
        {
            // Preserve newer clipboard content from other apps or user actions.
            if (Clipboard.ContainsText() &&
                string.Equals(Clipboard.GetText(), previous, StringComparison.Ordinal))
            {
                Clipboard.Clear();
                status.Text = "FHG temporary clipboard contents cleared.";
            }
        }
        catch
        {
            // Clipboard may be held by another process. Do not clear unrelated data.
        }
    }

    private void CopyAuthenticatorSetup(FhgAccount account)
    {
        // Authenticator by mymindstorm explicitly supports importing text
        // containing otpauth:// lines (its published interoperable format).
        // Do NOT edit its signed XPI, Firefox storage or private database.
        // A master secret goes to Windows clipboard only after this click.
        string seed = account.TotpSeed.Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        string label = Uri.EscapeDataString(account.Label.Replace(":", ""));
        string uri = "otpauth://totp/ChatGPT:" + label
            + "?secret=" + Uri.EscapeDataString(seed)
            + "&issuer=ChatGPT&algorithm=SHA1&digits=6&period=30";
        CopySensitive(uri, "Confidential Authenticator setup link", 60);
        status.Text = "Authenticator setup link copied for 60 seconds. In FHG Firefox open Authenticator > Settings > Backup > Import > Import Text Backup and paste. The extension must confirm the import.";
    }

    private void AddAccount()
    {
        using var picker = new OpenFileDialog
        {
            Filter = "Four-line account file (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Choose your private 4-line FHG account file",
            CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this,
            "This file contains your password, a 2FA master secret and a subscription token.\n\n" +
            "Store them encrypted only for this Windows user with DPAPI?\n" +
            "They will not be entered into ChatGPT or written into Firefox extension storage automatically.",
            "FHG — import private credentials",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        var added = AccountVault.ImportFourLineFile(picker.FileName);
        LoadCards(added.Id);
        ActivateAccount(added);
    }

    private void ActivateAccount(FhgAccount account)
    {
        string? previous = AccountVault.ActiveId();
        bool proxyRunning = Process.GetProcessesByName("FHGProxy").Length > 0;
        if (previous != account.Id && proxyRunning)
        {
            status.Text = "Close the existing FHG Proxy before switching accounts. The original Hiddify can remain open.";
            MessageBox.Show(this,
                "FHG Proxy is still using the other account's local ports.\n" +
                "Close only FHG Proxy, then activate this account again.\n\n" +
                "Your previous account, Firefox profile and subscription will remain archived.",
                "Finish the current session first",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ClearOwnedClipboard();
        AccountVault.SetActive(account.Id);
        LoadCards(account.Id);
        string? ticket = null;
        try
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "FHG.exe"))
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false
            };
            if (!proxyRunning)
            {
                ticket = AccountVault.MakeSubscriptionTicket(account);
                info.Environment["FHG_SETUP_SUB_TICKET"] = ticket;
            }
            if (Process.Start(info) is null)
                throw new IOException("Could not start the FHG launcher");
            status.Text = "Connecting FHG for " + account.Label + " and opening ChatGPT when SOCKS5 HTTPS is healthy.";
        }
        catch
        {
            if (ticket != null) { try { File.Delete(ticket); } catch { } }
            throw;
        }
    }

    private void EditSubscription()
    {
        var account = Selected();
        using var dialog = new Form
        {
            Text = "Update subscription — " + account.Label,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Surface, Font = Font, Width = 660, Height = 225,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var prompt = new Label
        {
            Left = 24, Top = 22, Width = 586, Height = 38,
            Text = "HTTPS subscription URL (stored encrypted under your Windows user account)",
            ForeColor = Ink
        };
        var input = new TextBox
        {
            Left = 24, Top = 67, Width = 582, Height = 34,
            Text = account.SubscriptionUrl
        };
        var save = MakeButton("Save subscription", () => {}, true, 170);
        save.Left = 436; save.Top = 121; save.DialogResult = DialogResult.OK;
        dialog.Controls.AddRange(new Control[] { prompt, input, save });
        dialog.AcceptButton = save;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AccountVault.UpdateSubscription(account.Id, input.Text.Trim());
        LoadCards(account.Id);
        status.Text = "Subscription saved. Close FHG Proxy and activate this account to apply the new link.";
    }

    private void OpenProxy()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "FHG.exe");
        Process.Start(new ProcessStartInfo(exe, "--proxy-only")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false
        });
        status.Text = "FHG Proxy opened. The regular Hiddify installation is separate.";
    }

    private void RefreshCards() => LoadCards(selectedId);

    private void OpenAccountFolder()
    {
        Directory.CreateDirectory(AccountVault.Root);
        Process.Start(new ProcessStartInfo("explorer.exe", AccountVault.Root) { UseShellExecute = true });
        status.Text = "Archive opened. Don't edit or share DPAPI vault files, passwords or master keys.";
    }
}
