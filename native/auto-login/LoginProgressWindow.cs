using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

// This window does not own the Firefox WebDriver session. It only tells the
// user why FHG is waiting, and allows an EXPLICIT cancel. Closing/minimizing
// this status window never silently ends the login browser.
internal sealed class LoginProgressWindow : IDisposable
{
    private readonly Thread thread;
    private readonly object gate = new();
    private Form? window;
    private Label? statusLabel;
    private string currentText = "Opening https://chatgpt.com/login";
    private int canceled;
    private bool closing;

    internal bool CancelRequested => Volatile.Read(ref canceled) != 0;

    internal LoginProgressWindow()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "FHG login status UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void Run()
    {
        using var form = new Form
        {
            Text = "FHG · ChatGPT login status",
            ClientSize = new Size(560, 230),
            MinimumSize = new Size(540, 265),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            BackColor = Color.FromArgb(246, 249, 253),
            Font = new Font("Segoe UI", 10f),
            ShowInTaskbar = true
        };
        var title = new Label {
            Text = "Complete sign-in in the Firefox window",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 52, 86),
            Left = 22, Top = 18, Width = 520, Height = 34
        };
        var details = new Label {
            Text = "FHG will not close Firefox for a CAPTCHA, password, 2FA or provider step.\n" +
                   "Only a VERIFIED signed-in ChatGPT page will trigger normal Firefox handoff.",
            Left = 23, Top = 67, Width = 515, Height = 59,
            ForeColor = Color.FromArgb(90, 109, 128)
        };
        var current = new Label {
            Text = currentText, Left = 23, Top = 130, Width = 515, Height = 37,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 105, 181)
        };
        var minimize = new Button {
            Text = "Minimize", Left = 295, Top = 183, Width = 104, Height = 34
        };
        var cancel = new Button {
            Text = "Stop sign-in", Left = 409, Top = 183, Width = 127, Height = 34
        };
        minimize.Click += (_, _) => form.WindowState = FormWindowState.Minimized;
        cancel.Click += (_, _) =>
        {
            if (MessageBox.Show(form, "Stop this login attempt and close its Firefox window?\n" +
                "Your existing saved account and browser profile will remain untouched.",
                "Confirm cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                Interlocked.Exchange(ref canceled, 1);
        };
        form.Controls.AddRange(new Control[] { title, details, current, minimize, cancel });
        form.FormClosing += (_, e) =>
        {
            lock (gate)
            {
                if (!closing)
                {
                    e.Cancel = true; // X minimizes; only explicit cancel ends sign-in.
                    form.WindowState = FormWindowState.Minimized;
                }
            }
        };
        lock (gate) { window = form; statusLabel = current; current.Text = currentText; }
        try { Application.Run(form); }
        finally { lock (gate) { window = null; statusLabel = null; } }
    }

    internal void Set(string text)
    {
        lock (gate)
        {
            currentText = text;
            var f = window;
            var label = statusLabel;
            if (f != null && label != null && f.IsHandleCreated && !f.IsDisposed)
            {
                try { f.BeginInvoke(new Action(() => { if (!label.IsDisposed) label.Text = text; })); }
                catch (InvalidOperationException) { }
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            closing = true;
            var f = window;
            if (f != null && f.IsHandleCreated && !f.IsDisposed)
            {
                try { f.BeginInvoke(new Action(() => f.Close())); }
                catch (InvalidOperationException) { }
            }
        }
        if (Thread.CurrentThread != thread) thread.Join(1500);
    }
}
