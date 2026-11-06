package com.fhg.android;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.net.VpnService;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/**
 * One-screen VPN-first UX: select account -> automatic subscription import ->
 * one Android VPN consent -> verified tunnel -> GeckoView.
 *
 * Never claims to install Android system Lockdown (the user must enable it).
 * No WebDriver, synthetic clicks, or online authentication automation.
 */
public final class MainActivity extends Activity {
    private static final int PICK_ACCOUNT=41;
    private static final int PICK_PROXY_FILE=42;
    private static final int VPN_CONSENT=43;
    private static final int BG=0xff0a1426;
    private static final int SURFACE=0xff17263e;
    private static final int ACCENT=0xff24c8b6;
    private static final int INK=0xfff4f8ff;
    private static final int MUTED=0xffa4b4cb;
    private static final int WARNING=0xffffb16e;

    private AccountStore vault;
    private final Handler main=new Handler(Looper.getMainLooper());
    private LinearLayout root, advancedPanel;
    private Spinner spinner;
    private TextView status, caption, accountLabel, profileLabel, otp, inlineError;
    private ProgressBar pending;
    private Button power, browser, advanced;
    private List<AccountStore.Account> accounts=new ArrayList<>();
    private boolean importing;
    private boolean connectAfterImport;
    private boolean syncingSpinner;
    private boolean expanded;
    private String lastImportError="";
    private String lastImportedProfile="";
    private boolean created;

    private final Runnable poll=new Runnable(){
        @Override public void run() {
            if(!isFinishing()) {
                update();
                main.postDelayed(this,1000);
            }
        }
    };

    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().setStatusBarColor(BG);
        getWindow().setNavigationBarColor(BG);
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR & 0);
        vault=new AccountStore(this);
        draw();
        loadAccounts();
        main.post(poll);
        if(saved==null) {
            try {
                if(vault.load().isEmpty())
                    main.postDelayed(this::pickAccount,500);
                else {
                    AccountStore.Account account=vault.selected();
                    if(account!=null && !vault.hasVpnConfig(account.id))
                        main.postDelayed(()->importSubscription(account,false),650);
                }
            } catch(Exception ignored) {}
        }
    }

    private int dp(int n){return (int)(getResources().getDisplayMetrics().density*n+.5f);}
    private GradientDrawable shape(int color,int radius){
        GradientDrawable d=new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radius));
        return d;
    }
    private TextView label(String value,int size,int color,boolean bold){
        TextView t=new TextView(this);
        t.setText(value);
        t.setTextSize(size);
        t.setTextColor(color);
        if(bold)t.setTypeface(Typeface.DEFAULT,Typeface.BOLD);
        t.setGravity(Gravity.CENTER_VERTICAL);
        return t;
    }
    private void add(LinearLayout parent,View view,int height,int top){
        LinearLayout.LayoutParams params=new LinearLayout.LayoutParams(-1,
            height<0?height:dp(height));
        params.topMargin=dp(top);
        parent.addView(view,params);
    }
    private LinearLayout card(){
        LinearLayout c=new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(dp(18),dp(17),dp(18),dp(19));
        c.setBackground(shape(SURFACE,20));
        return c;
    }
    private Button action(String value,int color,Runnable callback){
        Button b=new Button(this);
        b.setAllCaps(false);
        b.setText(value);
        b.setTextColor(Color.WHITE);
        b.setTextSize(15);
        b.setTypeface(Typeface.DEFAULT,Typeface.BOLD);
        b.setBackground(shape(color,13));
        b.setOnClickListener(v->callback.run());
        return b;
    }
    private void draw(){
        ScrollView scroll=new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(BG);
        root=new LinearLayout(this);
        root.setLayoutDirection(View.LAYOUT_DIRECTION_RTL);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(18),dp(22),dp(18),dp(30));
        scroll.addView(root);
        setContentView(scroll);

        add(root,label("FHG  /  ANDROID",27,INK,true),39,0);
        add(root,label("مرورگر خصوصی و VPN یکپارچه",14,MUTED,false),30,0);

        LinearLayout connection=card();
        add(connection,label("اتصال امن",21,INK,true),36,0);
        status=label("در حال بررسی وضعیت…",16,ACCENT,true);
        add(connection,status,43,7);
        caption=label("ابتدا یک حساب وارد کن. پروفایل VPN خودکار نصب می‌شود.",13,MUTED,false);
        caption.setMaxLines(3);
        add(connection,caption,-2,0);
        pending=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);
        pending.setIndeterminate(true);
        pending.setVisibility(View.GONE);
        add(connection,pending,7,17);
        power=action("اتصال امن",0xff0a8f83,this::onPower);
        add(connection,power,55,16);
        profileLabel=label("پروفایل: نصب نشده",13,MUTED,false);
        add(connection,profileLabel,38,10);
        inlineError=label("",12,WARNING,false);
        inlineError.setMaxLines(4);
        inlineError.setVisibility(View.GONE);
        add(connection,inlineError,-2,6);
        add(root,connection,-2,22);

        LinearLayout profile=card();
        add(profile,label("حساب کاربری",19,INK,true),35,0);
        spinner=new Spinner(this);
        spinner.setBackground(shape(0xff223652,10));
        add(profile,spinner,50,4);
        spinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){
            @Override public void onNothingSelected(AdapterView<?> parent){}
            @Override public void onItemSelected(AdapterView<?> p,View v,int pos,long id){
                if(syncingSpinner||accounts.isEmpty()||pos>=accounts.size())return;
                String next=accounts.get(pos).id;
                try {
                    AccountStore.Account current=vault.selected();
                    if(current!=null&&!current.id.equals(next)){
                        disconnect();
                        vault.select(next);
                        lastImportError="";
                        lastImportedProfile="";
                        if(!vault.hasVpnConfig(next))
                            importSubscription(accounts.get(pos),false);
                    }
                }catch(Exception ignored){}
                update();
            }
        });
        accountLabel=label("حسابی وارد نشده",12,MUTED,false);
        add(profile,accountLabel,38,5);
        add(profile,action("افزودن حساب از فایل چهارخطی",0xff254e67,this::pickAccount),50,6);
        otp=label("کد 2FA: —",22,INK,true);
        add(profile,otp,44,9);
        add(profile,action("کپی کد 2FA",0xff32465c,this::copyOtp),45,0);
        add(root,profile,-2,14);

        LinearLayout browsing=card();
        add(browsing,label("مرورگر اختصاصی",19,INK,true),36,0);
        add(browsing,label("ورود به ChatGPT دستی است؛ اطلاعات حساب فقط برای دامنه‌های رسمی تکمیل می‌شود.",13,MUTED,false),-2,2);
        browser=action("ورود به ChatGPT",0xff1773aa,this::openBrowser);
        add(browsing,browser,55,17);
        add(root,browsing,-2,14);

        advanced=action("تنظیمات پیشرفته  ▾",0xff27364e,()->{
            expanded=!expanded;
            advancedPanel.setVisibility(expanded?View.VISIBLE:View.GONE);
            advanced.setText(expanded?"بستن تنظیمات  ▴":"تنظیمات پیشرفته  ▾");
        });
        add(root,advanced,49,14);
        advancedPanel=card();
        advancedPanel.setVisibility(View.GONE);
        add(advancedPanel,action("اصلاح لینک اشتراک",0xff295577,this::editSubscription),48,0);
        add(advancedPanel,action("به‌روزرسانی اشتراک",0xff295577,()->{
            try{
                AccountStore.Account a=vault.selected();
                if(a==null){pickAccount();return;}
                importSubscription(a,false);
            }catch(Exception e){error("ACCOUNT_VAULT_ERROR");}
        }),48,0);
        add(advancedPanel,action("ورود کانفیگ از فایل JSON / YAML",0xff295577,this::pickProxyFile),48,8);
        add(advancedPanel,action("تنظیمات Always-on VPN و Kill Switch",0xff295577,this::openVpnSettings),48,8);
        add(advancedPanel,action("قطع اتصال VPN",0xff734760,this::disconnect),48,8);
        add(advancedPanel,label("نکته: دانلود اولیه اشتراک HTTPS ممکن است قبل از اتصال VPN انجام شود. برای محافظت کل گوشی، گزینه Block connections without VPN را در تنظیمات اندروید فعال کن.",12,MUTED,false),-2,16);
        add(root,advancedPanel,-2,7);
        add(root,label("FHG Android 0.2.3  •  GeckoView  +  sing-box",11,MUTED,false),35,18);
    }

    private void loadAccounts(){
        try{
            accounts=vault.load();
            String[] names=new String[accounts.size()];
            for(int i=0;i<names.length;i++)names[i]=accounts.get(i).email;
            if(names.length==0)names=new String[]{"حسابی وارد نشده"};
            syncingSpinner=true;
            ArrayAdapter<String> adapter=new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_dropdown_item,names);
            spinner.setAdapter(adapter);
            AccountStore.Account chosen=vault.selected();
            if(chosen!=null)for(int i=0;i<accounts.size();i++)
                if(chosen.id.equals(accounts.get(i).id)){spinner.setSelection(i);break;}
            syncingSpinner=false;
        }catch(Exception failure){
            error("ACCOUNT_VAULT_ERROR");
        }
        update();
    }

    private void update(){
        boolean connected=NetworkGate.vpnActive(this);
        String state=EmbeddedVpnService.getState();
        boolean working=importing||EmbeddedVpnService.isStarting()||state.equals("CHECKING");
        pending.setVisibility(working?View.VISIBLE:View.GONE);
        status.setText(connected?"متصل • ترافیک VPN بررسی شد":
            importing?"در حال دریافت و نصب خودکار پروفایل…":
            state.equals("CHECKING")?"تونل ساخته شد؛ در حال آزمون اینترنت…":
            state.equals("STARTING")?"در حال راه‌اندازی موتور VPN…":
            state.equals("FAILED")?"VPN متصل نیست":"آماده اتصال");
        status.setTextColor(connected?ACCENT:working?0xffffd180:INK);
        caption.setText(connected?"VPN و مسیر HTTPS آزمون شده‌اند؛ مرورگر آماده است.":
            importing?"اشتراک با HTTPS در حال نصب خودکار است؛ دانلود اولیه ممکن است قبل از VPN انجام شود.":
            "تا هنگام تأیید اتصال، دسترسی مرورگر مسدود می‌ماند.");
        power.setText(connected?"قطع اتصال":"اتصال VPN");
        power.setEnabled(!working);
        power.setAlpha(working?.55f:1f);
        browser.setEnabled(connected);
        browser.setAlpha(connected?1f:.48f);
        String failure=lastImportError;
        if(failure.isEmpty() && state.equals("FAILED"))failure=EmbeddedVpnService.getError();
        if(!failure.isEmpty()){
            inlineError.setText("نیاز به بررسی: "+humanError(failure));
            inlineError.setVisibility(View.VISIBLE);
        } else inlineError.setVisibility(View.GONE);
        try{
            AccountStore.Account account=vault.selected();
            if(account==null){
                accountLabel.setText("فایل چهارخطی حساب را انتخاب کن");
                profileLabel.setText("پروفایل: نصب نشده");
                otp.setText("2FA: —");
            } else {
                accountLabel.setText(account.email);
                if(!vault.hasVpnConfig(account.id))
                    profileLabel.setText("پروفایل: هنوز نصب نشده");
                else if(!lastImportedProfile.isEmpty())
                    profileLabel.setText(lastImportedProfile);
                else {
                    JSONObject config=new JSONObject(vault.loadVpnConfig(account.id));
                    JSONArray outs=config.getJSONArray("outbounds");
                    int n=0; for(int i=0;i<outs.length();i++)
                        if(!"urltest".equals(outs.getJSONObject(i).optString("type")))n++;
                    profileLabel.setText("پروفایل نصب‌شده  •  "+n+" سرور");
                }
                otp.setText(account.totpSecret.isEmpty()?"2FA: —":
                    "2FA: "+Totp.now(account.totpSecret)+"  ("+
                    Totp.secondsRemaining()+" ثانیه)");
            }
        }catch(Exception e){
            accountLabel.setText("خزانه اطلاعات در دسترس نیست");
        }
    }

    private static String humanError(String code){
        switch(code){
            case "SUBSCRIPTION_RETURNED_HTML":return "لینک صفحه وب برگردانده، نه اشتراک خام؛ لینک مستقیم Subscription را وارد کن.";
            case "SUBSCRIPTION_ACCESS_DENIED":return "سرویس اشتراک، دسترسی را رد کرد (401/403).";
            case "SUBSCRIPTION_DNS_ERROR":return "آدرس سرویس اشتراک پیدا نشد (DNS).";
            case "SUBSCRIPTION_TLS_ERROR":return "گواهی امنیتی HTTPS اشتراک معتبر نیست.";
            case "SUBSCRIPTION_TIMEOUT":return "دریافت اشتراک زمان‌بر شد یا سرور پاسخ نمی‌دهد.";
            case "NO_SUPPORTED_PROXY_NODES":return "اشتراک سرور سازگار پیدا نکرد.";
            case "JSON_HAS_NO_SUPPORTED_PROXY":return "فایل JSON سرور قابل‌پشتیبانی ندارد.";
            case "YAML_REMOTE_PROVIDER_UNSUPPORTED":return "این Clash YAML فقط به proxy-provider دیگری اشاره می‌کند.";
            case "PROXY_NOT_REACHABLE":return "اینترفیس VPN برقرار شد ولی خروجی HTTPS از تونل پاسخ نداد.";
            case "VPN_PERMISSION_REQUIRED":return "باید یک‌بار مجوز VPN اندروید را تأیید کنی.";
            case "VPN_CONFIG_REJECTED":return "این پروفایل با موتور sing-box نصب‌شده سازگار نیست؛ فایل یا نوع اشتراک را بررسی کن.";
            case "LIBBOX_START_ERROR":return "موتور sing-box پیکربندی را اجرا نکرد؛ پروفایل یا شبکه نیاز به بررسی دارد.";
            case "PROFILE_MISSING":return "پروفایل VPN برای حساب انتخاب‌شده نصب نشده.";
            case "FOREGROUND_SERVICE_ERROR":return "اندروید اجرای سرویس VPN در پس‌زمینه را رد کرد.";
            default:return "اتصال یا اشتراک معتبر نیست ("+code+").";
        }
    }

    private void error(String code){ lastImportError=code; update(); }

    private void pickAccount(){
        Intent file=new Intent(Intent.ACTION_OPEN_DOCUMENT);
        file.setType("*/*");
        file.addCategory(Intent.CATEGORY_OPENABLE);
        startActivityForResult(file,PICK_ACCOUNT);
    }
    private void pickProxyFile(){
        Intent file=new Intent(Intent.ACTION_OPEN_DOCUMENT);
        file.setType("*/*");
        file.addCategory(Intent.CATEGORY_OPENABLE);
        startActivityForResult(file,PICK_PROXY_FILE);
    }
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);
        if(request==VPN_CONSENT){
            if(result==RESULT_OK)startVpnService();
            else error("VPN_PERMISSION_REQUIRED");
            return;
        }
        if(result!=RESULT_OK||data==null||data.getData()==null)return;
        if(request==PICK_ACCOUNT){
            try(InputStream stream=getContentResolver().openInputStream(data.getData())){
                if(stream==null)throw new IllegalArgumentException();
                AccountStore.Account account=vault.importFourLines(stream);
                loadAccounts();
                // Required user behavior: automatically install the subscription
                // and open Android's VPN consent after the four-line file.
                importSubscription(account,true);
            }catch(Exception failure){error("ACCOUNT_IMPORT_INVALID");}
        }else if(request==PICK_PROXY_FILE)importLocal(data.getData());
    }

    private void importSubscription(AccountStore.Account account,boolean connectWhenReady){
        if(importing)return;
        if(EmbeddedVpnService.isEstablished()){
            disconnect();
        }
        importing=true;
        connectAfterImport=connectWhenReady;
        lastImportError="";
        update();
        SubscriptionManager.fetchAsync(this,account,new SubscriptionManager.Result(){
            @Override public void complete(SubscriptionManager.Profile p){
                runOnUiThread(()->{
                    importing=false;
                    lastImportedProfile="پروفایل نصب شد  •  "+p.proxies+" سرور  •  "+p.format;
                    lastImportError="";
                    update();
                    if(connectAfterImport)connectVpn();
                });
            }
            @Override public void failed(String reason){
                runOnUiThread(()->{importing=false;error(reason);});
            }
        });
    }

    private void editSubscription(){
        try{
            AccountStore.Account a=vault.selected();
            if(a==null){pickAccount();return;}
            EditText input=new EditText(this);
            input.setSingleLine(true);
            input.setInputType(android.text.InputType.TYPE_CLASS_TEXT |
                android.text.InputType.TYPE_TEXT_VARIATION_URI);
            input.setText(a.subscription);
            input.setSelectAllOnFocus(true);
            LinearLayout wrapper=new LinearLayout(this);
            wrapper.setPadding(dp(18),dp(4),dp(18),0);
            wrapper.addView(input,new LinearLayout.LayoutParams(-1,-2));
            new AlertDialog.Builder(this).setTitle("لینک مستقیم اشتراک HTTPS")
                .setMessage("اگر صفحه وب دریافت می‌شود، لینک مستقیم Subscription پنل را وارد کن.")
                .setView(wrapper)
                .setPositiveButton("ذخیره و نصب", (dialog,which)->{
                    try{
                        vault.updateSubscription(a.id,input.getText().toString());
                        loadAccounts();
                        importSubscription(vault.selected(),true);
                    }catch(Exception e){error("SUBSCRIPTION_HTTPS_REQUIRED");}
                }).setNegativeButton("انصراف",null).show();
        }catch(Exception e){error("ACCOUNT_VAULT_ERROR");}
    }

    private void importLocal(Uri uri){
        new Thread(()->{
            try(InputStream stream=getContentResolver().openInputStream(uri)){
                if(stream==null)throw new Exception("No local file");
                ByteArrayOutputStream data=new ByteArrayOutputStream();
                byte[] buf=new byte[4096];int len;
                while((len=stream.read(buf))!=-1){
                    if(data.size()+len>1_000_000)throw new Exception("Too large");
                    data.write(buf,0,len);
                }
                AccountStore.Account account=vault.selected();
                if(account==null)throw new Exception("No account");
                SubscriptionManager.Profile p=SubscriptionManager.importLocal(this,
                    account.id,data.toString("UTF-8"));
                runOnUiThread(()->{
                    lastImportedProfile="پروفایل نصب شد  •  "+p.proxies+" سرور";
                    lastImportError="";
                    update();
                    connectVpn();
                });
            }catch(Exception e){
                runOnUiThread(()->error("LOCAL_PROFILE_INVALID"));
            }
        },"fhg-local-profile").start();
    }

    private void onPower(){
        if(NetworkGate.vpnActive(this))disconnect(); else connectVpn();
    }
    private void connectVpn(){
        try{
            AccountStore.Account account=vault.selected();
            if(account==null){pickAccount();return;}
            if(!vault.hasVpnConfig(account.id)){
                importSubscription(account,true);
                return;
            }
            Intent permission=VpnService.prepare(this);
            if(permission!=null)startActivityForResult(permission,VPN_CONSENT);
            else startVpnService();
        }catch(Exception failure){error("ACCOUNT_VAULT_ERROR");}
    }
    private void startVpnService(){
        Intent connect=new Intent(this,EmbeddedVpnService.class);
        connect.setAction(EmbeddedVpnService.ACTION_CONNECT);
        try{
            if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(connect);
            else startService(connect);
        }catch(Exception e){error("FOREGROUND_SERVICE_ERROR");}
    }
    private void disconnect(){
        Intent stop=new Intent(this,EmbeddedVpnService.class);
        stop.setAction(EmbeddedVpnService.ACTION_DISCONNECT);
        try{startService(stop);}catch(Exception ignored){}
        lastImportError="";
        main.postDelayed(this::update,600);
    }
    private void openVpnSettings(){
        try{startActivity(new Intent(Settings.ACTION_VPN_SETTINGS));}
        catch(Exception e){startActivity(new Intent(Settings.ACTION_SETTINGS));}
    }
    private void openBrowser(){
        if(!NetworkGate.vpnActive(this)){
            new AlertDialog.Builder(this).setTitle("VPN لازم است")
                .setMessage("ابتدا VPN داخل FHG باید متصل شود و آزمون اینترنت را بگذراند.")
                .setPositiveButton("اتصال VPN",(d,w)->connectVpn())
                .setNegativeButton("انصراف",null).show();
            return;
        }
        try{
            AccountStore.Account account=vault.selected();
            if(account==null){pickAccount();return;}
            Intent browser=new Intent(this,BrowserActivity.class);
            browser.putExtra("account_id",account.id);
            startActivity(browser);
        }catch(Exception e){error("ACCOUNT_VAULT_ERROR");}
    }
    private void copyOtp(){
        try{
            AccountStore.Account account=vault.selected();
            if(account==null||account.totpSecret.isEmpty())return;
            String code=Totp.now(account.totpSecret);
            ClipboardManager clipboard=(ClipboardManager)getSystemService(CLIPBOARD_SERVICE);
            clipboard.setPrimaryClip(ClipData.newPlainText("FHG TOTP",code));
            Toast.makeText(this,"کد کپی شد",Toast.LENGTH_SHORT).show();
        }catch(Exception ignored){}
    }
    @Override public void onResume(){super.onResume(); if(created)loadAccounts(); created=true;}
    @Override protected void onDestroy(){
        main.removeCallbacksAndMessages(null);
        super.onDestroy();
    }
}
