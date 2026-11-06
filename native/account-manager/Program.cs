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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
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
            Padding = new Padding(20, 10, 10, 3), AutoScroll = false
        };
        root.Controls.Add(toolbar, 0, 1);
        toolbar.Controls.Add(MakeButton("+  Add account", AddAccount, true, 156));
        toolbar.Controls.Add(MakeButton("Open selected", () => ActivateAccount(Selected()), false, 151));
        toolbar.Controls.Add(MakeButton("Sync Authenticator", () => SyncOriginalAuthenticator(Selected()), true, 174));
        toolbar.Controls.Add(MakeButton("Edit account", () => EditAccount(Selected()), false, 132));
        toolbar.Controls.Add(MakeButton("Delete account", () => DeleteAccount(Selected()), false, 142));
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
            string explanation = SafeError(error);
            status.Text = explanation;
            MessageBox.Show(this, explanation, "FHG Accounts",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SafeError(Exception error)
    {
        // Only predefined, non-secret messages may be shown. Do not print
        // arbitrary exception.Message containing a URL or a credential.
        if (error is FormatException)
        {
            string message = error.Message;
            string[] safe = {
                "Account name must", "Account name must be", "Password is empty",
                "Authenticator secret must", "Authenticator secret could not",
                "Subscription address must", "Another account already uses",
                "An account with that name already exists",
                "The file must contain exactly 4 lines",
                "Select a local text file", "Invalid Base32"
            };
            if (safe.Any(prefix => message.StartsWith(prefix, StringComparison.Ordinal)))
                return message + "\n\nUse Edit account to correct the saved fields.";
            return "The account input has an invalid format. Check the account name, Base32 2FA secret and HTTPS subscription. You can edit existing accounts.";
        }
        if (error is InvalidOperationException)
            return "No matching account is selected, or the account changed. Refresh the list and select an account.";
        if (error is System.Runtime.InteropServices.ExternalException)
            return "Windows clipboard is unavailable. Close clipboard managers or retry.";
        return "Action failed (" + error.GetType().Name +
               "). Verify the input and installed FHG files. No passwords, tokens or 2FA seeds were logged.";
    }

    private void LoadCards(string? focusId = null)
    {
        accounts = AccountVault.Load().ToArray();
        string? candidate = focusId ?? selectedId ?? AccountVault.ActiveId();
        selectedId = accounts.Any(a => a.Id == candidate)
            ? candidate : (accounts.FirstOrDefault(a => a.Id == AccountVault.ActiveId())?.Id ?? accounts.FirstOrDefault()?.Id);
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
            Height = 238, BackColor = selected ? Color.FromArgb(239, 248, 255) : Color.White,
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
            Text = "Sync to original Firefox Authenticator",
            Left = 26, Top = 115, Width = 400, Height = 22,
            LinkColor = Accent, ActiveLinkColor = Mint, Font = SmallFont,
            Cursor = Cursors.Hand
        };
        setupLink.LinkClicked += (_, _) => Safely(() => SyncOriginalAuthenticator(account));
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
        var edit = MakeButton("Edit account", () => EditAccount(account), false, 142);
        edit.Left = 25; edit.Top = 188; edit.Height = 36;
        card.Controls.Add(edit);
        var delete = MakeButton("Delete account", () => DeleteAccount(account), false, 142);
        delete.Left = 180; delete.Top = 188; delete.Height = 36;
        delete.ForeColor = Color.FromArgb(183, 49, 57);
        card.Controls.Add(delete);
        var authenticator = MakeButton("Sync original Authenticator", () => SyncOriginalAuthenticator(account), true, 267);
        authenticator.Left = 335; authenticator.Top = 188; authenticator.Height = 36;
        card.Controls.Add(authenticator);
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

    private bool SyncOriginalAuthenticator(FhgAccount account)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "FHGAuthenticatorSync.exe");
        if (!File.Exists(exe))
            throw new InvalidOperationException("FHG Authenticator Sync helper is missing. Reinstall the complete package.");
        using var process = Process.Start(new ProcessStartInfo(exe) {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = "--account " + account.Id
        }) ?? throw new IOException("Could not start Authenticator Sync helper.");
        if (!process.WaitForExit(20000))
        {
            try { process.Kill(); } catch { }
            throw new IOException("Authenticator Sync timed out.");
        }
        if (process.ExitCode == 0)
        {
            status.Text = "Authenticator by mymindstorm synced in this FHG Firefox profile. Existing OTP entries preserved.";
            return true;
        }
        if (process.ExitCode == 2)
        {
            status.Text = "Close FHG Firefox, then reopen this account; Authenticator storage is currently in use.";
            MessageBox.Show(this,
                "FHG Firefox is using its profile. Close FHG Firefox and select this account again. Your regular Firefox does not need to close.",
                "Authenticator data is busy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        status.Text = "Authenticator Sync could not safely update the original Firefox storage. Existing data was not deleted.";
        MessageBox.Show(this,
            "This Firefox profile uses an unsupported or protected Authenticator storage state. FHG refused to overwrite it.\n\n" +
            "If you enabled a master password inside Authenticator, its encrypted entries cannot be directly mixed with plaintext data.",
            "Authenticator storage needs review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void CopyAuthenticatorSetup(FhgAccount account)
    {
        // Authenticator by mymindstorm explicitly supports importing text
        // containing otpauth:// lines (its published interoperable format).
        // Do NOT edit its signed XPI, Firefox storage or private database.
        // A master secret goes to Windows clipboard only after this click.
        // Old imported records may contain an invalid Base32 seed. Check it
        // before putting a privileged provisioning URI on the clipboard.
        _ = AccountVault.GenerateTotp(account.TotpSeed, DateTimeOffset.UtcNow);
        string seed = account.TotpSeed.Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        string safeLabel = account.Label.Replace(":", "").Replace("\r", "").Replace("\n", "");
        string label = Uri.EscapeDataString(safeLabel);
        string uri = "otpauth://totp/ChatGPT:" + label
            + "?secret=" + Uri.EscapeDataString(seed)
            + "&issuer=ChatGPT&algorithm=SHA1&digits=6&period=30";
        CopySensitive(uri, "Confidential Authenticator setup link", 60);
        status.Text = "2FA link copied. In FHG Firefox: Authenticator > gear icon > Backup > Import Backup > Import Text Backup. Paste and confirm. FHG does not edit the signed extension database.";
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
        // The user explicitly selected the private local account file.
        // Encrypt the vault and sync the ORIGINAL Authenticator by default.
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
        if (!SyncOriginalAuthenticator(account)) return;
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


    private void EditAccount(FhgAccount account)
    {
        using var dialog = new Form
        {
            Text = "Edit FHG account",
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Surface, Font = Font, ClientSize = new Size(710, 424),
            MinimumSize = new Size(690, 455),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            AutoScaleMode = AutoScaleMode.Dpi
        };
        var heading = new Label {
            Left = 24, Top = 14, Width = 650, Height = 36,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Ink, Text = "Edit saved account and login settings"
        };
        dialog.Controls.Add(heading);

        TextBox Field(string name, string value, int y, bool secret, int maxLength)
        {
            dialog.Controls.Add(new Label {
                Left = 24, Top = y, Width = 655, Height = 24, ForeColor = Ink,
                Text = name, Font = SmallBoldFont
            });
            var input = new TextBox {
                Left = 24, Top = y + 25, Width = 652, Height = 30,
                Text = value, MaxLength = maxLength, UseSystemPasswordChar = secret
            };
            dialog.Controls.Add(input);
            return input;
        }

        var username = Field("Account username / email", account.Label, 51, false, 200);
        var password = Field("Password (stored encrypted under Windows user)", account.Password, 117, true, 4096);
        var seed = Field("Authenticator Base32 secret", account.TotpSeed, 183, true, 512);
        var sub = Field("FHG Proxy HTTPS subscription", account.SubscriptionUrl, 249, true, 8192);

        var show = new CheckBox {
            Text = "Temporarily show secret fields", Left = 24, Top = 317,
            Width = 290, Height = 26, ForeColor = Muted
        };
        show.CheckedChanged += (_, _) => {
            password.UseSystemPasswordChar = !show.Checked;
            seed.UseSystemPasswordChar = !show.Checked;
            sub.UseSystemPasswordChar = !show.Checked;
        };
        dialog.Controls.Add(show);
        var cancel = MakeButton("Cancel", () => dialog.Close(), false, 125);
        cancel.Left = 405; cancel.Top = 359;
        dialog.Controls.Add(cancel);
        var save = MakeButton("Save changes", () =>
        {
            AccountVault.UpdateAccount(account.Id, username.Text, password.Text, seed.Text, sub.Text);
            dialog.DialogResult = DialogResult.OK;
        }, true, 145);
        save.Left = 537; save.Top = 359;
        dialog.Controls.Add(save);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ClearOwnedClipboard();
        LoadCards(account.Id);
        SyncOriginalAuthenticator(AccountVault.Load().First(a => a.Id == account.Id));
        status.Text = "Account updated. Authenticator Sync attempted; restart FHG Proxy to apply subscription changes.";
    }

    private void DeleteAccount(FhgAccount account)
    {
        if (AccountVault.ActiveId() == account.Id &&
            Process.GetProcessesByName("FHGProxy").Length > 0)
        {
            MessageBox.Show(this,
                "This account is active in FHG Proxy. Close only FHG Proxy, then try Delete again. Your regular Hiddify is not affected.",
                "Close the active FHG Proxy first", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
            "Remove this account from the encrypted FHG vault?\n\n" +
            "Account: " + account.Label + "\n\n" +
            "Its login credentials and 2FA seed will be removed from FHG's account list.\n" +
            "Existing Firefox profile data is NOT automatically deleted.\n" +
            "This does NOT sign out or revoke sessions on ChatGPT servers.\n\n" +
            "Continue?",
            "Delete saved FHG account", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        ClearOwnedClipboard();
        AccountVault.DeleteAccount(account.Id);
        LoadCards();
        status.Text = "Saved account deleted from FHG vault. Firefox profile and server sessions were not erased.";
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
