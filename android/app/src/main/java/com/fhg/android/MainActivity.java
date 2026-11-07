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
    private static final int BG=0xff0B1020;
    private static final int SURFACE=0xff151E31;
    private static final int ACCENT=0xff55E1C8;
    private static final int INK=0xffF3F7FF;
    private static final int MUTED=0xffA1B0C8;
    private static final int WARNING=0xffFFC383;

    private AccountStore vault;
    private final Handler main=new Handler(Looper.getMainLooper());
    private LinearLayout root, advancedPanel;
    private Spinner spinner;
    private TextView status, caption, accountLabel, profileLabel, otp, inlineError, serverLabel;
    private ProgressBar pending;
    private TextView connectionPill;
    private boolean lastPowerStyleConnected=false;
    private Button power, browser, advanced, recovery, serverPicker;
    private List<AccountStore.Account> accounts=new ArrayList<>();
    private boolean importing;
    private volatile int importRevision;
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
        // A process that died during native startup leaves only a redacted
        // phase marker. Never pretend it was a successful VPN connection.
        if(getSharedPreferences("fhg-vpn-state",MODE_PRIVATE)
                .getBoolean("startup_pending",false)
                && !VpnRuntimeState.isStarting(this)) {
            lastImportError="VPN_PREVIOUS_START_INTERRUPTED";
            update();
        }
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
        if(color==SURFACE)d.setStroke(dp(1),0xff2B3D55);
        return d;
    }
    private TextView label(String value,int size,int color,boolean bold){
        TextView t=new TextView(this);
        t.setText(value);
        t.setTextSize(size);
        t.setTextColor(color);
        if(bold)t.setTypeface(Typeface.create("sans-serif-medium",Typeface.BOLD));
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
        c.setPadding(dp(19),dp(18),dp(19),dp(19));
        c.setBackground(shape(SURFACE,24));
        return c;
    }
    private Button action(String value,int color,Runnable callback){
        Button b=new Button(this);
        b.setAllCaps(false);
        b.setText(value);
        b.setTextColor(color==ACCENT?BG:INK);
        b.setTextSize(14);
        b.setGravity(Gravity.CENTER);
        b.setPadding(dp(12),0,dp(12),0);
        b.setTypeface(Typeface.DEFAULT,Typeface.BOLD);
        b.setMinHeight(dp(48));
        b.setContentDescription(value);
        b.setBackground(new android.graphics.drawable.RippleDrawable(
            android.content.res.ColorStateList.valueOf(0x44ffffff),
            shape(color,16),null));
        b.setOnClickListener(v->callback.run());
        return b;
    }

    private static final int RAISED=0xff1D2B43;
    private static final int BORDER=0xff2B3D55;
    private static final int SOFT_ACCENT=0xff123D3C;

    private GradientDrawable rounded(int color,int radius,int stroke) {
        GradientDrawable d=new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radius));
        if(stroke!=0)d.setStroke(dp(1),stroke);
        return d;
    }
    private GradientDrawable gradient(int start,int end,int radius) {
        GradientDrawable d=new GradientDrawable(GradientDrawable.Orientation.TL_BR,
            new int[]{start,end});
        d.setCornerRadius(dp(radius));
        d.setStroke(dp(1),BORDER);
        return d;
    }
    private android.graphics.drawable.RippleDrawable touchBackground(int color,int radius) {
        return new android.graphics.drawable.RippleDrawable(
            android.content.res.ColorStateList.valueOf(0x29ffffff),
            rounded(color,radius,0),
            rounded(0xffffffff,radius,0));
    }
    private TextView overline(String value) {
        TextView t=label(value,12,MUTED,true);
        t.setLetterSpacing(.015f);
        return t;
    }
    private void sectionHeading(LinearLayout parent,String value,String meta) {
        LinearLayout r=new LinearLayout(this);
        r.setOrientation(LinearLayout.HORIZONTAL);
        r.setGravity(Gravity.CENTER_VERTICAL);
        r.addView(label(value,17,INK,true),new LinearLayout.LayoutParams(0,dp(35),1));
        r.addView(label(meta,11,MUTED,false),new LinearLayout.LayoutParams(-2,dp(35)));
        add(parent,r,35,0);
    }
    private void draw(){
        ScrollView scroll=new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setClipToPadding(false);
        scroll.setBackgroundColor(BG);
        root=new LinearLayout(this);
        root.setLayoutDirection(View.LAYOUT_DIRECTION_RTL);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20),dp(25),dp(20),dp(36));
        root.setOnApplyWindowInsetsListener((view,insets)->{
            int top,bottom,left,right;
            if(android.os.Build.VERSION.SDK_INT>=30) {
                android.graphics.Insets bars=insets.getInsets(
                    android.view.WindowInsets.Type.systemBars() |
                    android.view.WindowInsets.Type.displayCutout());
                top=bars.top;bottom=bars.bottom;left=bars.left;right=bars.right;
            } else {
                top=insets.getSystemWindowInsetTop();
                bottom=insets.getSystemWindowInsetBottom();
                left=insets.getSystemWindowInsetLeft();
                right=insets.getSystemWindowInsetRight();
            }
            view.setPadding(dp(20)+left,dp(25)+top,dp(20)+right,dp(36)+bottom);
            return insets;
        });
        scroll.addView(root);
        setContentView(scroll);

        LinearLayout header=new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        TextView mark=label("F",25,0xff06211f,true);
        mark.setGravity(Gravity.CENTER);
        mark.setBackground(gradient(0xff7DF4DB,0xff3FC5B6,15));
        mark.setContentDescription("FHG Secure");
        header.addView(mark,new LinearLayout.LayoutParams(dp(52),dp(52)));
        LinearLayout brand=new LinearLayout(this);
        brand.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams brandParams=new LinearLayout.LayoutParams(0,-2,1);
        brandParams.setMarginStart(dp(12));
        header.addView(brand,brandParams);
        TextView name=label("FHG Secure",22,INK,true);
        name.setTextDirection(View.TEXT_DIRECTION_LTR);
        add(brand,name,-2,0);
        TextView tagline=label("PRIVATE WORKSPACE  /  VPN",10,MUTED,false);
        tagline.setTextDirection(View.TEXT_DIRECTION_LTR);
        tagline.setLetterSpacing(.055f);
        add(brand,tagline,-2,1);
        TextView version=label("RC7",11,ACCENT,true);
        version.setGravity(Gravity.CENTER);
        version.setBackground(rounded(SOFT_ACCENT,50,0xff25665F));
        header.addView(version,new LinearLayout.LayoutParams(dp(48),dp(30)));
        add(root,header,58,0);

        LinearLayout connection=card();
        connection.setPadding(dp(21),dp(22),dp(21),dp(22));
        connection.setBackground(gradient(0xff1C3048,0xff121E31,25));
        LinearLayout top=new LinearLayout(this);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setGravity(Gravity.CENTER_VERTICAL);
        top.addView(overline("وضعیت اتصال"),new LinearLayout.LayoutParams(0,dp(32),1));
        connectionPill=label("قطع",12,MUTED,true);
        connectionPill.setGravity(Gravity.CENTER);
        connectionPill.setPadding(dp(14),0,dp(14),0);
        connectionPill.setBackground(rounded(RAISED,99,BORDER));
        top.addView(connectionPill,new LinearLayout.LayoutParams(-2,dp(30)));
        add(connection,top,34,0);
        status=label("آماده اتصال",23,INK,true);
        status.setMaxLines(2);
        add(connection,status,-2,12);
        caption=label("پس از تأیید VPN، مرورگر آماده می‌شود.",13,MUTED,false);
        caption.setMaxLines(3);
        caption.setLineSpacing(dp(3),1f);
        add(connection,caption,-2,7);
        pending=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);
        pending.setIndeterminate(true);
        pending.getIndeterminateDrawable().setTint(ACCENT);
        pending.setVisibility(View.GONE);
        add(connection,pending,4,17);
        power=action("اتصال امن",ACCENT,this::onPower);
        power.setTextSize(17);
        add(connection,power,60,22);
        inlineError=label("",13,WARNING,false);
        inlineError.setLineSpacing(dp(3),1f);
        inlineError.setMaxLines(5);
        inlineError.setVisibility(View.GONE);
        add(connection,inlineError,-2,12);
        recovery=action("بررسی دوباره و به‌روزرسانی",RAISED,()->{
            try{
                AccountStore.Account a=vault.selected();
                if(a==null)pickAccount(); else importSubscription(a,true);
            }catch(Exception ignored){error("ACCOUNT_VAULT_ERROR");}
        });
        recovery.setVisibility(View.GONE);
        add(connection,recovery,52,11);
        add(root,connection,-2,24);

        LinearLayout locations=card();
        sectionHeading(locations,"مسیر اتصال","SERVER");
        add(locations,overline("لوکیشن انتخاب‌شده"),-2,8);
        serverLabel=label("در انتظار دریافت سرورها",18,INK,true);
        serverLabel.setMaxLines(2);
        serverLabel.setEllipsize(android.text.TextUtils.TruncateAt.END);
        serverLabel.setContentDescription("سرور VPN انتخاب‌شده");
        add(locations,serverLabel,-2,4);
        serverPicker=action("انتخاب یا تغییر سرور",RAISED,this::chooseServer);
        add(locations,serverPicker,52,13);
        profileLabel=label("هنوز پروفایلی نصب نشده",12,MUTED,false);
        profileLabel.setMaxLines(2);
        add(locations,profileLabel,-2,13);
        add(root,locations,-2,14);

        LinearLayout profile=card();
        sectionHeading(profile,"حساب‌ها","ACCOUNT");
        add(profile,overline("حساب فعال"),-2,9);
        spinner=new Spinner(this);
        spinner.setBackground(rounded(RAISED,13,BORDER));
        spinner.setMinimumHeight(dp(54));
        spinner.setContentDescription("انتخاب حساب فعال FHG");
        add(profile,spinner,54,9);
        spinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){
            @Override public void onNothingSelected(AdapterView<?> parent){}
            @Override public void onItemSelected(AdapterView<?> p,View v,int pos,long id){
                if(syncingSpinner||accounts.isEmpty()||pos>=accounts.size())return;
                String next=accounts.get(pos).id;
                try {
                    AccountStore.Account current=vault.selected();
                    if(current!=null&&!current.id.equals(next)){
                        importRevision++; // Invalidate pending HTTP callback for previous account.
                        importing=false;
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
        accountLabel.setMaxLines(2);
        accountLabel.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);
        accountLabel.setTextDirection(View.TEXT_DIRECTION_LTR);
        add(profile,accountLabel,-2,8);
        add(profile,action("افزودن حساب از فایل",RAISED,this::pickAccount),52,13);
        View separator=new View(this);
        separator.setBackgroundColor(BORDER);
        add(profile,separator,1,17);
        add(profile,overline("احراز هویت دومرحله‌ای"),-2,15);
        LinearLayout otpRow=new LinearLayout(this);
        otpRow.setOrientation(LinearLayout.HORIZONTAL);
        otpRow.setGravity(Gravity.CENTER_VERTICAL);
        otp=label("—",20,INK,true);
        otp.setSingleLine(true);
        otp.setEllipsize(android.text.TextUtils.TruncateAt.END);
        otp.setTextDirection(View.TEXT_DIRECTION_LTR);
        otpRow.addView(otp,new LinearLayout.LayoutParams(0,dp(52),1));
        Button copy=action("کپی کد",RAISED,this::copyOtp);
        otpRow.addView(copy,new LinearLayout.LayoutParams(dp(112),dp(49)));
        add(profile,otpRow,54,4);
        add(root,profile,-2,14);

        LinearLayout browsing=card();
        sectionHeading(browsing,"مرورگر اختصاصی","BROWSER");
        TextView detail=label("ورود به ChatGPT فقط پس از تأیید اتصال امن VPN فعال می‌شود.",13,MUTED,false);
        detail.setLineSpacing(dp(3),1f);
        add(browsing,detail,-2,6);
        browser=action("باز کردن ChatGPT",0xff2765B4,this::openBrowser);
        add(browsing,browser,56,17);
        add(root,browsing,-2,14);

        advanced=action("تنظیمات بیشتر  ▾",RAISED,()->{
            expanded=!expanded;
            advancedPanel.setVisibility(expanded?View.VISIBLE:View.GONE);
            advanced.setText(expanded?"بستن تنظیمات  ▴":"تنظیمات بیشتر  ▾");
        });
        add(root,advanced,54,16);
        advancedPanel=card();
        advancedPanel.setVisibility(View.GONE);
        sectionHeading(advancedPanel,"ابزارها و تنظیمات","TOOLS");
        add(advancedPanel,action("ویرایش لینک اشتراک",RAISED,this::editSubscription),52,9);
        add(advancedPanel,action("به‌روزرسانی سرورها",RAISED,()->{
            try {
                AccountStore.Account a=vault.selected();
                if(a==null)pickAccount(); else importSubscription(a,false);
            }catch(Exception e){error("ACCOUNT_VAULT_ERROR");}
        }),52,9);
        add(advancedPanel,action("وارد کردن JSON / YAML",RAISED,this::pickProxyFile),52,9);
        add(advancedPanel,action("Always-on VPN و Kill Switch",RAISED,this::openVpnSettings),52,9);
        add(advancedPanel,action("گزارش فنی بدون اطلاعات محرمانه",RAISED,this::shareDiagnostics),52,9);
        add(advancedPanel,action("قطع VPN",0xff56333C,this::disconnect),52,9);
        TextView safety=label("برای محافظت کل دستگاه، گزینه Block connections without VPN را از تنظیمات اندروید فعال کن.",12,MUTED,false);
        safety.setLineSpacing(dp(3),1f);
        add(advancedPanel,safety,-2,14);
        add(root,advancedPanel,-2,10);
        TextView footer=label("FHG SECURE  ·  ANDROID RC7  ·  ARM64",11,MUTED,false);
        footer.setGravity(Gravity.CENTER);
        footer.setTextDirection(View.TEXT_DIRECTION_LTR);
        footer.setLetterSpacing(.04f);
        add(root,footer,46,17);
    }

    private void loadAccounts(){
        try{
            accounts=vault.load();
            String[] names=new String[accounts.size()];
            for(int i=0;i<names.length;i++)names[i]=accounts.get(i).email;
            if(names.length==0)names=new String[]{"حسابی وارد نشده"};
            syncingSpinner=true;
            ArrayAdapter<String> adapter=new ArrayAdapter<String>(this,
                android.R.layout.simple_spinner_dropdown_item,names){
                private TextView row(String value,boolean dropDown){
                    TextView t=label(value,14,INK,false);
                    t.setBackground(shape(dropDown?0xff233b57:0xff223652,dp(10)));
                    t.setPadding(dp(14),dp(12),dp(14),dp(12));
                    t.setMinHeight(dp(52));
                    t.setSingleLine(true);
                    t.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);
                    t.setTextDirection(View.TEXT_DIRECTION_LTR);
                    return t;
                }
                @Override public View getView(int position,View convertView,
                    android.view.ViewGroup parent) {
                    return row(getItem(position),false);
                }
                @Override public View getDropDownView(int position,View convertView,
                    android.view.ViewGroup parent) {
                    return row(getItem(position),true);
                }
            };
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
        String state=VpnRuntimeState.getState(this);
        boolean reconnecting=state.equals("RECONNECTING");
        boolean tunnelOpen=VpnRuntimeState.isTunnelUp(this);
        boolean working=importing||VpnRuntimeState.isStarting(this)||state.equals("CHECKING");
        pending.setVisibility(working?View.VISIBLE:View.GONE);
        status.setText(connected?"اتصال امن برقرار است":
            importing?"در حال دریافت و نصب خودکار پروفایل…":
            state.equals("CHECKING")?"در حال بررسی اتصال…":
            state.equals("STARTING")?"در حال اتصال…":
            reconnecting?"در حال بازیابی اتصال…":
            state.equals("FAILED")?"اتصال برقرار نشد":"آماده اتصال");
        status.setTextColor(connected?ACCENT:working||reconnecting?0xffffd180:INK);
        status.setContentDescription("وضعیت VPN: "+status.getText());
        caption.setText(connected?"مسیر HTTPS از شبکه VPN تأیید شده؛ مرورگر آماده است.":
            importing?"اشتراک با HTTPS در حال نصب خودکار است؛ دانلود اولیه ممکن است قبل از VPN انجام شود.":
            "تا زمان تأیید VPN، مرورگر برای محافظت از اطلاعات غیرفعال می‌ماند.");
        power.setText(tunnelOpen?"قطع اتصال VPN":"اتصال امن");
        if(tunnelOpen!=lastPowerStyleConnected){
            lastPowerStyleConnected=tunnelOpen;
            power.setBackground(touchBackground(tunnelOpen?RAISED:ACCENT,16));
            power.setTextColor(tunnelOpen?INK:BG);
        }
        connectionPill.setText(connected?"متصل":
            reconnecting?"بازیابی":working?"در حال اتصال":
            state.equals("FAILED")?"نیاز به بررسی":"قطع");
        int pillColor=connected?SOFT_ACCENT:
            state.equals("FAILED")?0xff422D2A:
            working||reconnecting?0xff433625:RAISED;
        int pillText=connected?ACCENT:
            state.equals("FAILED")?WARNING:
            working||reconnecting?0xffF6D29A:MUTED;
        connectionPill.setTextColor(pillText);
        connectionPill.setBackground(rounded(pillColor,99,0));
        power.setEnabled(!working);
        power.setAlpha(working?.55f:1f);
        power.setContentDescription(tunnelOpen?"قطع تونل VPN FHG":"اتصال تونل VPN FHG");
        browser.setEnabled(connected);
        browser.setAlpha(connected?1f:.48f);
        String failure=lastImportError;
        if(failure.isEmpty() && state.equals("FAILED"))failure=VpnRuntimeState.getError(this);
        if(!failure.isEmpty()){
            inlineError.setText("نیاز به بررسی: "+humanError(failure));
            inlineError.setVisibility(View.VISIBLE);
        } else inlineError.setVisibility(View.GONE);
        recovery.setVisibility(!failure.isEmpty()&&!working?View.VISIBLE:View.GONE);
        try{
            AccountStore.Account account=vault.selected();
            if(account==null){
                serverPicker.setEnabled(false);
                serverPicker.setAlpha(.55f);
                serverLabel.setText("حسابی انتخاب نشده");
                accountLabel.setText("فایل چهارخطی حساب را انتخاب کن");
                profileLabel.setText("پروفایل: نصب نشده");
                otp.setText("2FA: —");
            } else {
                accountLabel.setText(account.email);
                if(!vault.hasVpnConfig(account.id)) {
                    profileLabel.setText("هنوز پروفایلی نصب نشده");
                    serverLabel.setText("ابتدا اشتراک را نصب کن");
                    serverPicker.setEnabled(false);
                    serverPicker.setAlpha(.55f);
                }
                else {
                    serverPicker.setEnabled(!importing&&!VpnRuntimeState.isStarting(this));
                    serverPicker.setAlpha(serverPicker.isEnabled()?1f:.55f);
                    String existing=vault.loadVpnConfig(account.id);
                    String choice=VpnConfig.selectedTag(existing);
                    serverLabel.setText(displayServer(choice));
                    if(!lastImportedProfile.isEmpty())
                        profileLabel.setText(lastImportedProfile);
                    else {
                    JSONObject config=new JSONObject(existing);
                    JSONArray outs=config.getJSONArray("outbounds");
                    int n=0; for(int i=0;i<outs.length();i++)
                        if(!"urltest".equals(outs.getJSONObject(i).optString("type")))n++;
                    profileLabel.setText(n+" سرور در اشتراک آماده است");
                    }
                }
                otp.setText(account.totpSecret.isEmpty()?"کد فعال نیست":
                    Totp.now(account.totpSecret)+"   ·   "+
                    Totp.secondsRemaining()+"s");
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
            case "PROXY_NOT_REACHABLE":return "اینترفیس VPN برقرار شد ولی آزمون HTTPS اختصاصیِ شبکه VPN موفق نبود.";
            case "SERVER_SELECTION_FAILED":return "انتخاب این سرور با موتور VPN سازگار نیست یا توقف اتصال قبلی کامل نشده؛ سرور دیگری انتخاب کن.";
            case "SERVER_LIST_UNAVAILABLE":return "فهرست سرورها باز نشد؛ یک بار اشتراک را به‌روز کن.";
            case "VPN_PERMISSION_REQUIRED":return "باید یک‌بار مجوز VPN اندروید را تأیید کنی.";
            case "VPN_CONFIG_REJECTED":return "این پروفایل با موتور sing-box نصب‌شده سازگار نیست؛ فایل یا نوع اشتراک را بررسی کن.";
            case "LIBBOX_START_ERROR":return "موتور VPN در شروع خطا داد. گزارش فنی را بگیر و در صورت تکرار، لاگ adb را بررسی کن.";
            case "VPN_NATIVE_PROCESS_EXITED":return "موتور داخلی VPN هنگام اتصال متوقف شد. نشست مرورگر مسدود است؛ گزارش مرحله قبلی شروع را بررسی کن.";
            case "VPN_PREVIOUS_START_INTERRUPTED":return "اجرای قبلی در زمان شروع VPN ناتمام مانده؛ احتمال کرش native یا توقف سیستم وجود دارد. گزارش فنی را بگیر.";
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
        if(VpnRuntimeState.isTunnelUp(this)) disconnect();
        importing=true;
        final int generation=++importRevision;
        lastImportError="";
        update();
        SubscriptionManager.fetchAsync(this,account,new SubscriptionManager.Result(){
            @Override public void complete(SubscriptionManager.Profile p){
                runOnUiThread(()->{
                    if(isFinishing()||isDestroyed()||generation!=importRevision)return;
                    importing=false;
                    lastImportedProfile=p.proxies+" سرور  ·  "+p.format;
                    lastImportError="";
                    update();
                    if(connectWhenReady){
                        try {
                            AccountStore.Account active=vault.selected();
                            if(active!=null && active.id.equals(account.id))
                                connectVpn();
                        }catch(Exception ignored){}
                    }
                });
            }
            @Override public void failed(String reason){
                runOnUiThread(()->{
                    if(isFinishing()||isDestroyed()||generation!=importRevision)return;
                    importing=false;error(reason);
                });
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
                    lastImportedProfile=p.proxies+" سرور آماده است";
                    lastImportError="";
                    update();
                    connectVpn();
                });
            }catch(Exception e){
                runOnUiThread(()->error("LOCAL_PROFILE_INVALID"));
            }
        },"fhg-local-profile").start();
    }

    private static String displayServer(String tag) {
        if("fhg-proxy".equals(tag))return "خودکار (بررسی سرعت سرورها)";
        if(tag==null||tag.isEmpty())return "نامشخص";
        int separator=tag.indexOf(" · ");
        return separator>=0?tag.substring(separator+3):tag;
    }

    private void chooseServer(){
        try {
            AccountStore.Account account=vault.selected();
            if(account==null){pickAccount();return;}
            if(!vault.hasVpnConfig(account.id)){importSubscription(account,false);return;}
            String current=vault.loadVpnConfig(account.id);
            List<String> choices=new ArrayList<>();
            JSONObject json=new JSONObject(current);
            JSONArray out=json.getJSONArray("outbounds");
            for(int i=0;i<out.length();i++){
                JSONObject item=out.getJSONObject(i);
                if("urltest".equals(item.optString("type")) &&
                    "fhg-proxy".equals(item.optString("tag")))choices.add("fhg-proxy");
            }
            choices.addAll(VpnConfig.selectableTags(current));
            if(choices.isEmpty()){error("NO_SUPPORTED_PROXY_NODES");return;}
            String[] labels=new String[choices.size()];
            int selected=0;
            String active=VpnConfig.selectedTag(current);
            for(int i=0;i<choices.size();i++){
                labels[i]=displayServer(choices.get(i));
                if(choices.get(i).equals(active))selected=i;
            }
            new AlertDialog.Builder(this)
                .setTitle("انتخاب سرور یا لوکیشن  •  "+VpnConfig.selectableTags(current).size()+" سرور")
                .setSingleChoiceItems(labels,selected,(dialog,index)->{
                    dialog.dismiss();
                    String next=choices.get(index);
                    if(next.equals(active))return;
                    applyServer(account.id,next);
                })
                .setNegativeButton("انصراف",null).show();
        }catch(Exception e){error("SERVER_LIST_UNAVAILABLE");}
    }

    private void applyServer(String accountId,String selected) {
        final int revision=++importRevision;
        final boolean restart=VpnRuntimeState.isTunnelUp(this) ||
            VpnRuntimeState.isStarting(this);
        importing=true;
        lastImportError="";
        update();
        // Stop the existing tunnel before replacing its encrypted config.
        if(restart)disconnect();
        new Thread(()->{
            String failure="";
            try{
                if(restart){
                    for(int i=0;i<30 && (VpnRuntimeState.isTunnelUp(this) ||
                        VpnRuntimeState.isStarting(this));i++)Thread.sleep(100);
                    Thread.sleep(400);
                    if(VpnRuntimeState.isTunnelUp(this) ||
                        VpnRuntimeState.isStarting(this))throw new Exception("VPN_STOP_TIMEOUT");
                }
                AccountStore.Account active=vault.selected();
                if(active==null||!active.id.equals(accountId)||revision!=importRevision)return;
                String original=vault.loadVpnConfig(accountId);
                String modified=VpnConfig.withSelected(original,selected);
                VpnEngineSetup.validate(this,modified);
                if(revision!=importRevision)return;
                vault.saveVpnConfig(accountId,modified);
            }catch(Exception e){failure="SERVER_SELECTION_FAILED";}
            final String result=failure;
            runOnUiThread(()->{
                if(revision!=importRevision||isFinishing()||isDestroyed())return;
                importing=false;
                lastImportError=result;
                lastImportedProfile="";
                update();
                if(result.isEmpty()&&restart)connectVpn();
            });
        },"fhg-proxy-selector").start();
    }

    private void onPower(){
        if(VpnRuntimeState.isTunnelUp(this)||NetworkGate.vpnActive(this))
            disconnect();
        else connectVpn();
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
    private void shareDiagnostics(){
        boolean profile=false;
        try {
            AccountStore.Account a=vault.selected();
            profile=a!=null&&vault.hasVpnConfig(a.id);
        }catch(Exception ignored){}
        String report=DiagnosticReport.collect(this,profile);
        Intent share=new Intent(Intent.ACTION_SEND);
        share.setType("text/plain");
        share.putExtra(Intent.EXTRA_SUBJECT,"FHG Secure — VPN diagnostics");
        share.putExtra(Intent.EXTRA_TEXT,report);
        startActivity(Intent.createChooser(share,"اشتراک گزارش بدون رمز و اشتراک"));
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
        importRevision++; // Ignore any late subscription download response.
        main.removeCallbacksAndMessages(null);
        super.onDestroy();
    }
}
