using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new WorkspaceWindow());
    }
}

internal sealed class WorkspaceWindow : Form
{
    private static readonly Color Navy = Color.FromArgb(15, 31, 51);
    private static readonly Color Ink = Color.FromArgb(26, 45, 65);
    private static readonly Color Muted = Color.FromArgb(104, 122, 142);
    private static readonly Color Cloud = Color.FromArgb(245, 248, 252);
    private static readonly Color Blue = Color.FromArgb(28, 106, 206);
    private static readonly Color Mint = Color.FromArgb(20, 153, 126);
    private static readonly Color Border = Color.FromArgb(218, 229, 238);
    private static readonly Font NameFont = new("Segoe UI", 12.5f, FontStyle.Bold);
    private static readonly Font CodeFont = new("Consolas", 22f, FontStyle.Bold);
    private readonly FlowLayoutPanel cards = new() {
        Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,
        WrapContents=false,AutoScroll=true,
        BackColor=Cloud,Padding=new Padding(18,18,18,10)
    };
    private readonly Label subtitle = new() {Font=new Font("Segoe UI",9.5f),ForeColor=Color.FromArgb(168,193,214)};
    private readonly Label connection = new() {ForeColor=Muted,Font=new Font("Segoe UI",10f,FontStyle.Bold)};
    private readonly Label info = new() {ForeColor=Muted,Font=new Font("Segoe UI",9.4f)};
    private readonly Label hint = new() {ForeColor=Muted,Font=new Font("Segoe UI",9.5f)};
    private readonly Dictionary<string,(Label code,Label seconds)> clocks = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };
    private string? selectedId;
    private string? clipboardOwner;
    private DateTimeOffset clearClipboardAt;
    private FhgAccount[] accounts = Array.Empty<FhgAccount>();
    private bool checking;
    private readonly Panel top = new() { Height = 102, Dock=DockStyle.Top,BackColor=Navy };
    private readonly Panel actions = new() { Height=84,Dock=DockStyle.Top,BackColor=Color.White };
    private readonly Panel bottom = new() {Height=90,Dock=DockStyle.Bottom,BackColor=Color.White};
    private readonly Button launch = MakeButton("Start workspace", 186, true);
    private readonly Button openProxy = MakeButton("Open FHG Proxy", 162, false);

    public WorkspaceWindow()
    {
        Text="FHG | Account & Proxy Workspace";
        StartPosition=FormStartPosition.CenterScreen;
        Size=new Size(1055,755);
        MinimumSize=new Size(840,605);
        BackColor=Cloud;Font=new Font("Segoe UI",10f);
        AutoScaleMode=AutoScaleMode.Dpi;
        Controls.Add(cards);Controls.Add(bottom);Controls.Add(actions);Controls.Add(top);

        top.Controls.Add(new Label {
            Text="◆",Left=28,Top=25,Width=58,Height=53,
            Font=new Font("Segoe UI",31f,FontStyle.Bold),
            ForeColor=Color.FromArgb(60,225,210),TextAlign=ContentAlignment.MiddleCenter
        });
        top.Controls.Add(new Label {
            Text="FHG  /  WORKSPACE",Left=103,Top=20,Width=570,Height=45,
            ForeColor=Color.White,Font=new Font("Segoe UI",21f,FontStyle.Bold)
        });
        subtitle.SetBounds(106,70,740,26);top.Controls.Add(subtitle);
        var tag=new Label {
            Text="PRIVATE  •  ISOLATED",Anchor=AnchorStyles.Right|AnchorStyles.Top,
            Left=834,Top=35,Width=182,Height=32,ForeColor=Color.FromArgb(103,239,210),
            BackColor=Color.FromArgb(22,65,79),TextAlign=ContentAlignment.MiddleCenter,
            Font=new Font("Segoe UI",8.5f,FontStyle.Bold)
        };
        top.Controls.Add(tag);
        top.Resize+=(_,_)=>tag.Left=top.ClientSize.Width-tag.Width-25;

        actions.Controls.Add(launch);launch.SetBounds(24,18,186,48);
        openProxy.SetBounds(225,18,170,48);actions.Controls.Add(openProxy);
        var add=MakeButton("+ Add account",156,false);add.SetBounds(410,18,156,48);
        actions.Controls.Add(add);
        var edit=MakeButton("Edit selected",145,false);edit.SetBounds(580,18,145,48);
        actions.Controls.Add(edit);
        var remove=MakeButton("Remove",112,false);remove.SetBounds(740,18,112,48);
        actions.Controls.Add(remove);
        launch.Click+=(_,_)=>Safely(()=>StartSelected(false));
        openProxy.Click+=(_,_)=>Safely(()=>StartSelected(true));
        add.Click+=(_,_)=>Safely(AddAccount);
        edit.Click+=(_,_)=>Safely(()=>EditAccount(Selected()));
        remove.Click+=(_,_)=>Safely(()=>DeleteAccount(Selected()));

        connection.SetBounds(24,8,850,27);bottom.Controls.Add(connection);
        var check=MakeButton("Test connection",145,false);
        check.SetBounds(22,44,145,34);bottom.Controls.Add(check);
        var files=MakeButton("Account files",125,false);
        files.SetBounds(180,44,125,34);bottom.Controls.Add(files);
        var copyUser=MakeButton("Copy email",124,false);
        copyUser.SetBounds(317,44,124,34);bottom.Controls.Add(copyUser);
        var copyPass=MakeButton("Copy password",144,false);
        copyPass.SetBounds(453,44,144,34);bottom.Controls.Add(copyPass);
        hint.SetBounds(612,47,400,30);bottom.Controls.Add(hint);
        check.Click+=async(_,_)=>await CheckConnection();
        files.Click+=(_,_)=>Safely(OpenAccountFolder);
        copyUser.Click+=(_,_)=>Safely(()=>CopySecret(Selected().Label,"Email",45));
        copyPass.Click+=(_,_)=>Safely(()=>CopySecret(Selected().Password,"Password",30));
        bottom.Resize+=(_,_)=>{hint.Width=Math.Max(155,bottom.ClientSize.Width-620);};

        cards.Resize+=(_,_)=>ResizeCards();
        refreshTimer.Tick+=(_,_)=>{
            UpdateCodes();
            if (clipboardOwner != null && DateTimeOffset.UtcNow >= clearClipboardAt)
                ClearOwnedClipboard();
        };
        refreshTimer.Start();
        FormClosed+=(_,_)=>ClearOwnedClipboard();
        LoadAccounts();
        _=CheckConnection();
    }

    private static Button MakeButton(string text,int width,bool primary)
    {
        var b=new Button {
            Text=text,Size=new Size(width,40),FlatStyle=FlatStyle.Flat,
            Font=new Font("Segoe UI",9.4f,FontStyle.Bold),Cursor=Cursors.Hand,
            BackColor=primary?Blue:Color.White,ForeColor=primary?Color.White:Ink,
            UseVisualStyleBackColor=false,TabStop=true
        };
        b.FlatAppearance.BorderColor=primary?Blue:Border;
        b.FlatAppearance.BorderSize=1;
        return b;
    }

    private void Safely(Action action)
    {
        try { action(); }
        catch (FormatException) {
            Alert("Account data is invalid. Check the email, password, Base32 2FA key and HTTPS subscription.");
        }
        catch (Exception ex) {
            Alert("FHG could not complete the action ("+ex.GetType().Name+
                  "). Existing account and Firefox data were not intentionally deleted.");
        }
    }

    private void Alert(string message)
    {
        info.Text=message;
        MessageBox.Show(this,message,"FHG Workspace",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }

    private void LoadAccounts(string? focus=null)
    {
        accounts=AccountVault.Load();
        selectedId=focus??selectedId??AccountVault.ActiveId();
        if(!accounts.Any(a=>a.Id==selectedId)) selectedId=
            accounts.FirstOrDefault(a=>a.Id==AccountVault.ActiveId())?.Id??accounts.FirstOrDefault()?.Id;
        cards.SuspendLayout();
        cards.Controls.Clear();
        clocks.Clear();
        foreach(var account in accounts) {
            var c=BuildCard(account);
            cards.Controls.Add(c);
        }
        if(accounts.Length==0) {
            cards.Controls.Add(new Label{
                Text="No saved accounts. Choose Add account to select your four-line local file.",
                Width=740,Height=100,ForeColor=Muted,
                TextAlign=ContentAlignment.MiddleCenter,Font=NameFont
            });
        }
        subtitle.Text=$"{accounts.Length} encrypted account(s)   •   Separate Firefox profiles   •   SOCKS5 127.0.0.1:12345";
        // Keep Add account accessible even when the vault is empty.
        foreach(var button in actions.Controls.OfType<Button>())
            button.Enabled=accounts.Length>0 || button.Text=="+ Add account";
        ResizeCards();
        cards.ResumeLayout();
        UpdateCodes();
    }

    private Control BuildCard(FhgAccount account)
    {
        bool chosen=account.Id==selectedId, active=account.Id==AccountVault.ActiveId();
        var c=new Panel {
            Height=201,Width=Math.Max(710,cards.ClientSize.Width-49),
            BackColor=chosen?Color.FromArgb(235,247,255):Color.White,
            Margin=new Padding(2,0,2,13),Tag=account.Id,
            BorderStyle=BorderStyle.FixedSingle
        };
        c.Controls.Add(new Panel {
            BackColor=active?Mint:Blue,Width=5,Height=199,Left=0,Top=0
        });
        var label=new Label {
            Text=account.Label,Left=22,Top=20,Width=550,Height=31,
            Font=NameFont,ForeColor=Ink,AutoEllipsis=true
        };
        c.Controls.Add(label);
        var state=new Label {
            Text=active?"● ACTIVE":"○ SAVED",Left=23,Top=56,Width=180,Height=26,
            ForeColor=active?Mint:Muted,Font=new Font("Segoe UI",9f,FontStyle.Bold)
        };
        c.Controls.Add(state);
        string host;
        try{ host=new Uri(account.SubscriptionUrl).Host; }
        catch {host="Subscription saved";}
        c.Controls.Add(new Label {
            Text="Proxy subscription: "+host+"    •    Private Firefox session",
            Left=23,Top=87,Width=630,Height=25,ForeColor=Muted,
            Font=new Font("Segoe UI",8.9f)
        });
        var code=new Label {
            Text="------",Left=c.Width-290,Top=20,Width=180,Height=43,
            Font=CodeFont,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Ink,
            Anchor=AnchorStyles.Right|AnchorStyles.Top
        };
        var seconds=new Label {
            Text="",Left=c.Width-105,Top=33,Width=97,Height=30,
            Font=new Font("Segoe UI",8.3f),ForeColor=Muted,
            Anchor=AnchorStyles.Right|AnchorStyles.Top
        };
        c.Controls.Add(code);c.Controls.Add(seconds);
        clocks[account.Id]=(code,seconds);

        var use=MakeButton(active?"Start ChatGPT":"Use & start",155,true);
        use.SetBounds(22,133,155,43);c.Controls.Add(use);
        var proxy=MakeButton("Proxy settings",145,false);
        proxy.SetBounds(190,133,145,43);c.Controls.Add(proxy);
        var edit=MakeButton("Edit",92,false);edit.SetBounds(348,133,92,43);c.Controls.Add(edit);
        var del=MakeButton("Delete",92,false);del.SetBounds(453,133,92,43);
        del.ForeColor=Color.FromArgb(190,53,58);c.Controls.Add(del);
        var otp=MakeButton("Copy 2FA",135,false);otp.SetBounds(558,133,135,43);c.Controls.Add(otp);

        use.Click+=(_,_)=>Safely(()=>Start(account,false));
        proxy.Click+=(_,_)=>Safely(()=>Start(account,true));
        edit.Click+=(_,_)=>Safely(()=>EditAccount(account));
        del.Click+=(_,_)=>Safely(()=>DeleteAccount(account));
        otp.Click+=(_,_)=>Safely(()=>CopySecret(
            AccountVault.GenerateTotp(account.TotpSeed,DateTimeOffset.UtcNow),"2FA code",22));
        c.Click+=(_,_)=>Choose(account.Id);
        label.Click+=(_,_)=>Choose(account.Id);
        state.Click+=(_,_)=>Choose(account.Id);
        return c;
    }

    private void Choose(string id) { if(id!=selectedId) LoadAccounts(id); }
    private void ResizeCards()
    {
        int width=Math.Max(706,cards.ClientSize.Width-51);
        foreach(Control c in cards.Controls) if(c is Panel) c.Width=width;
    }
    private FhgAccount Selected() =>
        accounts.FirstOrDefault(a=>a.Id==selectedId) ??
            throw new InvalidOperationException("Select an account first.");

    private void UpdateCodes()
    {
        int remaining=30-(int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds()%30);
        foreach(var a in accounts)
        {
            if(!clocks.TryGetValue(a.Id,out var view))continue;
            try { view.code.Text=AccountVault.GenerateTotp(a.TotpSeed,DateTimeOffset.UtcNow); }
            catch {view.code.Text="Invalid key";}
            view.seconds.Text=remaining+"s remaining";
        }
    }

    private void StartSelected(bool proxyOnly)=>Start(Selected(),proxyOnly);
    private void Start(FhgAccount account,bool proxyOnly)
    {
        string exe=Path.Combine(AppContext.BaseDirectory,"FHG.exe");
        if(!File.Exists(exe))throw new IOException("FHG launcher missing");
        ClearOwnedClipboard();
        AccountVault.SetActive(account.Id);
        selectedId=account.Id;
        LoadAccounts(account.Id);
        var info=new ProcessStartInfo(exe) {
            WorkingDirectory=AppContext.BaseDirectory,UseShellExecute=false
        };
        if(proxyOnly)info.ArgumentList.Add("--proxy-only");
        using var started=Process.Start(info);
        if(started is null)throw new IOException("Could not start FHG");
        this.info.Text=proxyOnly
            ? "Opening the dedicated FHG Proxy with the selected subscription."
            : "Starting automatic subscription, secure connection check, Authenticator and ChatGPT sign-in.";
    }

    private void AddAccount()
    {
        using var picker=new OpenFileDialog{
            Filter="Four-line FHG account (*.txt)|*.txt|All files (*.*)|*.*",
            Title="Select your local account file",CheckFileExists=true
        };
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        var a=AccountVault.ImportFourLineFile(picker.FileName);
        LoadAccounts(a.Id);
        Start(a,false);
    }

    private void EditAccount(FhgAccount account)
    {
        using var dialog=new Form {
            Text="Edit account",StartPosition=FormStartPosition.CenterParent,
            Font=Font,BackColor=Cloud,ClientSize=new Size(650,456),
            FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false
        };
        var fields=new List<TextBox>();
        string[] labels={"Email / account name","Password","2FA Base32 secret","HTTPS proxy subscription URL"};
        string[] values={account.Label,account.Password,account.TotpSeed,account.SubscriptionUrl};
        for(int i=0;i<4;i++)
        {
            dialog.Controls.Add(new Label{
                Text=labels[i],Left=24,Top=17+i*91,Width=585,Height=22,
                ForeColor=Ink,Font=new Font("Segoe UI",9.5f,FontStyle.Bold)
            });
            var field=new TextBox{
                Left=24,Top=45+i*91,Width=596,Height=34,
                Text=values[i],UseSystemPasswordChar=i>0
            };
            fields.Add(field);dialog.Controls.Add(field);
        }
        var show=new CheckBox{Text="Show sensitive fields",Left=26,Top=379,Width=220,Height=28,ForeColor=Muted};
        show.CheckedChanged+=(_,_)=>{for(int i=1;i<4;i++)fields[i].UseSystemPasswordChar=!show.Checked;};
        dialog.Controls.Add(show);
        var save=MakeButton("Save changes",154,true);
        save.SetBounds(468,398,154,38);
        save.Click+=(_,_)=>{
            try {
                AccountVault.UpdateAccount(account.Id,fields[0].Text,fields[1].Text,
                    fields[2].Text,fields[3].Text);
                dialog.DialogResult=DialogResult.OK;
            }
            catch(FormatException) {
                MessageBox.Show(dialog,"Invalid account name, password, 2FA key, or HTTPS subscription.",
                    "Check account fields",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        dialog.Controls.Add(save);
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        LoadAccounts(account.Id);
        info.Text="Saved. Start workspace to apply any changed subscription and refresh Authenticator.";
    }

    private void DeleteAccount(FhgAccount account)
    {
        if(MessageBox.Show(this,
            "Remove the selected credentials from the encrypted FHG vault?\n\n"+
            "Existing Firefox profiles and online sessions will NOT be deleted.",
            "Remove saved account",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        ClearOwnedClipboard();
        AccountVault.DeleteAccount(account.Id);
        LoadAccounts();
        info.Text="Saved credentials removed. Firefox profile preserved.";
    }

    private void CopySecret(string value,string title,int seconds)
    {
        if(string.IsNullOrEmpty(value))return;
        Clipboard.SetText(value);
        clipboardOwner=value;clearClipboardAt=DateTimeOffset.UtcNow.AddSeconds(seconds);
        info.Text=title+" copied locally. Clipboard clears automatically after "+seconds+" seconds.";
    }
    private void ClearOwnedClipboard()
    {
        var text=clipboardOwner;clipboardOwner=null;
        if(text==null)return;
        try{
            if(Clipboard.ContainsText() &&
                string.Equals(Clipboard.GetText(),text,StringComparison.Ordinal))
                Clipboard.Clear();
        }catch{}
    }

    private void OpenAccountFolder()
    {
        Directory.CreateDirectory(AccountVault.Root);
        Process.Start(new ProcessStartInfo("explorer.exe",AccountVault.Root){UseShellExecute=true});
        info.Text="The account vault is encrypted for this Windows user. Do not share its contents.";
    }

    private async Task CheckConnection()
    {
        if(checking||IsDisposed)return;
        checking=true;connection.Text="● Checking FHG SOCKS5 connection…";connection.ForeColor=Muted;
        try {
            using var handler=new SocketsHttpHandler{
                Proxy=new WebProxy("socks5://127.0.0.1:12345"),UseProxy=true,
                AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(4)
            };
            using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(9)};
            using var response=await client.GetAsync("https://example.com/",
                HttpCompletionOption.ResponseHeadersRead);
            if(IsDisposed)return;
            bool good=(int)response.StatusCode>=200&&(int)response.StatusCode<400;
            connection.Text=good?"● FHG Proxy connected — HTTPS through SOCKS5:12345 verified":
                "● FHG Proxy responded but HTTPS check did not pass";
            connection.ForeColor=good?Mint:Color.FromArgb(180,94,42);
        }catch {
            if(!IsDisposed){
                connection.Text="○ FHG Proxy is offline or not passing HTTPS. Open FHG Proxy to connect.";
                connection.ForeColor=Color.FromArgb(179,84,55);
            }
        }finally{checking=false;}
    }
}
