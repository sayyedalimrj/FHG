// FHG optional first-run setup. The input is UNTRUSTED and contains account secrets.
// Never write the account password, TOTP seed or subscription URL to logs,
// command-line arguments, release artifacts, telemetry or the browser profile.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal sealed record FirstRunRequest(string SubscriptionFile, string ReceiptFile, string Label);

internal static class FirstRunWizard
{
    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FHG", "InitialSetup");

    public static bool Completed => File.Exists(Path.Combine(StateDir, "setup-complete.flag"));

    public static FirstRunRequest? AskForLocalFile()
    {
        using var filePicker = new OpenFileDialog
        {
            Title = "FHG first-run setup — select your four-line LOCAL text file",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true,
            RestoreDirectory = true
        };
        if (filePicker.ShowDialog() != DialogResult.OK) return null;
        try
        {
            var file = new FileInfo(filePicker.FileName);
            if (file.Length <= 0 || file.Length > 16384)
                throw new FormatException("The local input file must be under 16 KB.");

            // Four lines in this exact order, never echo values:
            // 1 label/login, 2 password, 3 base32 TOTP seed, 4 HTTPS subscription.
            var lines = File.ReadAllLines(file.FullName, Encoding.UTF8);
            try
            {
                if (lines.Length != 4)
                    throw new FormatException("Exactly four lines are required.");
                string username = lines[0].Trim();
                string subscription = lines[3].Trim();
                if (string.IsNullOrWhiteSpace(username) || username.Length > 200 ||
                    string.IsNullOrWhiteSpace(lines[1]) ||
                    string.IsNullOrWhiteSpace(lines[2]))
                    throw new FormatException("Username, password and authenticator key cannot be blank.");
                if (!Uri.TryCreate(subscription, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps ||
                    uri.UserInfo.Length != 0 ||
                    uri.Host.Length == 0 ||
                    subscription.Length > 8192)
                    throw new FormatException("Subscription must be an absolute HTTPS URL.");

                var confirmation = MessageBox.Show(
                    "The selected file contains your login password and your TOTP master key.\n\n" +
                    "FHG will use ONLY the subscription URL to configure FHG Proxy. " +
                    "No ChatGPT password or TOTP seed will be saved, uploaded or injected into login forms.\n\n" +
                    "After the proxy has passed a real HTTPS check, Firefox will open ChatGPT " +
                    "for you to sign in and add the authenticator key in the trusted add-on UI.\n\n" +
                    "Continue? Keep the original four-line file safe; remove it yourself " +
                    "after your account recovery methods are verified.",
                    "FHG — security confirmation",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes) return null;

                Directory.CreateDirectory(StateDir);
                string name = "provision-" + Guid.NewGuid().ToString("N");
                string subscriptionPath = Path.Combine(StateDir, name + ".txt");
                // Only the sub URL is temporarily written to the current user's profile.
                File.WriteAllText(subscriptionPath, subscription + "\n", new UTF8Encoding(false));
                File.SetAttributes(subscriptionPath, FileAttributes.Hidden);
                return new FirstRunRequest(subscriptionPath, subscriptionPath + ".status", username);
            }
            finally
            {
                Array.Clear(lines);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show("Setup file rejected: " + e.Message,
                "FHG initial setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    public static void MarkCompleted(FirstRunRequest request)
    {
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(Path.Combine(StateDir, "setup-complete.flag"),
            "FHG local setup completed " + DateTimeOffset.Now.ToString("O"));
        CleanupTicket(request);
    }

    public static void CleanupTicket(FirstRunRequest request)
    {
        try { if (File.Exists(request.SubscriptionFile)) File.Delete(request.SubscriptionFile); }
        catch { /* refuse to erase unrelated files */ }
        try { if (File.Exists(request.ReceiptFile)) File.Delete(request.ReceiptFile); }
        catch { }
    }
}
