using System;
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
    private readonly ListBox list = new()
    {
        Font = new Font("Segoe UI", 11f),
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false
    };
    private readonly Label hint = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 10f),
        ForeColor = Color.FromArgb(55, 69, 84),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label otp = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 15f, FontStyle.Bold),
        ForeColor = Color.FromArgb(0, 125, 120),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly System.Windows.Forms.Timer otpTimer = new() { Interval = 1000 };
    private FhgAccount[] accounts = Array.Empty<FhgAccount>();
    private string? shownOtpId;

    public AccountsWindow()
    {
        Text = "FHG Accounts";
        Size = new Size(740, 545);
        MinimumSize = new Size(640, 465);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(246, 248, 252);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24),
            ColumnCount = 1, RowCount = 5, BackColor = BackColor
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(layout);

        var title = new Label {
            Text = "Accounts & subscriptions",
            Font = new Font("Segoe UI Semibold", 18),
            ForeColor = Color.FromArgb(20, 39, 60),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(title, 0, 0);
        hint.Text = "Separate Firefox profile per account • Encrypted archive in your Windows user profile";
        layout.Controls.Add(hint, 0, 1);

        var panel = new Panel {
            Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16)
        };
        panel.Controls.Add(list);
        layout.Controls.Add(panel, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 0),
            WrapContents = true, AutoScroll = true
        };
        actions.Controls.Add(Action("Add account file", Add, 154));
        actions.Controls.Add(Action("Activate & open", ActivateAccount, 156));
        actions.Controls.Add(Action("Edit subscription", EditSubscription, 159));
        actions.Controls.Add(Action("Open proxy", OpenProxy, 115));
        actions.Controls.Add(Action("Show 2FA code", ShowOtp, 136));
        layout.Controls.Add(actions, 0, 3);

        var bottom = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        bottom.Controls.Add(new Label {
            Text = "Switching isolates sessions; it does not revoke old ChatGPT sessions.",
            Dock = DockStyle.Fill, ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.5f)
        }, 0, 0);
        bottom.Controls.Add(otp, 1, 0);
        layout.Controls.Add(bottom, 0, 4);

        otpTimer.Tick += (_, _) => RefreshOtpDisplay();
        list.SelectedIndexChanged += (_, _) => { shownOtpId = null; otp.Text = ""; };
        LoadAccounts();
        // The first-run experience is a file picker, not a proxy dashboard
        // or a chain of redundant configuration dialogs.
        Shown += (_, _) => {
            if (accounts.Length == 0)
                BeginInvoke(new Action(Add));
        };
    }

    private Button Action(string label, Action command, int width)
    {
        var b = new Button {
            Text = label, Width = width, Height = 36, FlatStyle = FlatStyle.Flat,
            BackColor = Color.White, ForeColor = Color.FromArgb(19, 51, 83),
            Margin = new Padding(0, 3, 9, 3), Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(183, 203, 224);
        b.Click += (_, _) => {
            try { command(); }
            catch (Exception e) {
                MessageBox.Show(this, "FHG action failed: " + e.Message,
                    "FHG Accounts", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        return b;
    }

    private void LoadAccounts(string? preferred = null)
    {
        accounts = AccountVault.Load().ToArray();
        string? active = AccountVault.ActiveId();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var a in accounts) list.Items.Add(a.Label + (a.Id == active ? "    ✓ Active" : "    • Archived"));
        list.EndUpdate();
        if (accounts.Length != 0)
        {
            int index = Array.FindIndex(accounts, a => a.Id == (preferred ?? active));
            list.SelectedIndex = index >= 0 ? index : 0;
        }
        hint.Text = accounts.Length + " local account(s) • DPAPI-protected archive • FHG runs without Administrator";
    }

    private FhgAccount Selected()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= accounts.Length)
            throw new InvalidOperationException("Select an account first.");
        return accounts[list.SelectedIndex];
    }

    private void Add()
    {
        using var dialog = new OpenFileDialog {
            Filter = "Four-line text file|*.txt|Any file|*.*",
            Title = "Choose the LOCAL credentials + subscription file",
            CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this,
            "Your file contains a password, TOTP master key and subscription credential.\n\n" +
            "Save them ENCRYPTED for this Windows user with DPAPI? " +
            "No password or TOTP key will be sent to FHG Proxy or added to Firefox.\n\n" +
            "Keep account recovery codes elsewhere and delete the original plaintext file yourself.",
            "Protect account data", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        var account = AccountVault.ImportFourLineFile(dialog.FileName);
        LoadAccounts(account.Id);
        // User already confirmed secure local import. Automatically request
        // subscription activation; never bounce them back to old manual UI.
        ActivateAccount();
    }

    private void ActivateAccount()
    {
        var a = Selected();
        var prior = AccountVault.ActiveId();
        bool proxyRunning = Process.GetProcessesByName("FHGProxy").Length != 0;
        if (prior != a.Id && proxyRunning)
        {
            MessageBox.Show(this,
                "Close the existing FHG Proxy FIRST. It owns local ports and may belong to another account.\n\n" +
                "Original Hiddify may remain open. FHG will not force-kill anything.",
                "Switch safely", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Isolate the new Firefox session using its own profile. Other sessions
        // stay archived rather than being deleted or remotely revoked.
        AccountVault.SetActive(a.Id);
        LoadAccounts(a.Id);
        // Same account with its own already-running FHG Proxy only needs to
        // reopen its isolated Firefox session. No redundant subscription import.
        string? ticket = null;
        try
        {
            var starter = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "FHG.exe")) {
                WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false
            };
            if (!proxyRunning)
            {
                ticket = AccountVault.MakeSubscriptionTicket(a);
                starter.Environment["FHG_SETUP_SUB_TICKET"] = ticket;
            }
            if (Process.Start(starter) is null)
                throw new InvalidOperationException("Could not launch FHG.");
            hint.Text = "Opening: " + a.Label + " — Firefox remains guarded and requires a working SOCKS5 proxy.";
        }
        catch
        {
            if (ticket != null) { try { File.Delete(ticket); } catch {} }
            throw;
        }
    }

    private void OpenProxy()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "FHG.exe");
        Process.Start(new ProcessStartInfo(exe, "--proxy-only") {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false
        });
    }

    private void EditSubscription()
    {
        var a = Selected();
        using var pop = new Form {
            Text = "Update subscription for " + a.Label, Width = 570, Height = 185,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var input = new TextBox { Left = 20, Top = 30, Width = 515, Text = a.SubscriptionUrl };
        var ok = new Button { Text = "Save", Left = 420, Top = 80, Width = 115, DialogResult = DialogResult.OK };
        pop.Controls.Add(input); pop.Controls.Add(ok); pop.AcceptButton = ok;
        if (pop.ShowDialog(this) != DialogResult.OK) return;
        AccountVault.UpdateSubscription(a.Id, input.Text.Trim());
        LoadAccounts(a.Id);
        MessageBox.Show(this,
            "Subscription updated in encrypted local archive. To apply it, close FHG Proxy and click Activate & open.",
            "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowOtp()
    {
        var a = Selected();
        if (MessageBox.Show(this,
            "Display the current 6-digit authentication code on this screen?\n\n" +
            "Do not share codes, TOTP secrets or the plain-text input file.",
            "Show one-time code", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        shownOtpId = a.Id;
        otpTimer.Start();
        RefreshOtpDisplay();
    }

    private void RefreshOtpDisplay()
    {
        if (shownOtpId == null) return;
        var a = accounts.FirstOrDefault(x => x.Id == shownOtpId);
        if (a == null) { otpTimer.Stop(); otp.Text = ""; return; }
        int left = 30 - (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30);
        otp.Text = AccountVault.GenerateTotp(a.TotpSeed, DateTimeOffset.UtcNow) + "  " + left + "s";
    }
}
