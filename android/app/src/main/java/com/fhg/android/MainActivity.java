package com.fhg.android;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;
import java.io.InputStream;
import java.util.List;

/**
 * FHG's secure ChatGPT account and browser workspace.
 *
 * VPN routing is owned exclusively by the unmodified, officially signed
 * Hiddify app. No native VPN engine runs in this process; this is not a
 * repackaged derivative of Hiddify and does not claim to be one.
 * Confirmation of subscription import happens inside Hiddify.
 */
public final class MainActivity extends Activity {
    private static final int PICK_ACCOUNT=42;
    private static final int BG=0xff0b1020, PANEL=0xff151e31;
    private static final int ACCENT=0xff55e1c8, INK=0xfff3f7ff;
    private static final int MUTED=0xffa1b0c8, WARNING=0xffffc383;

    private final Handler ui=new Handler(Looper.getMainLooper());
    private AccountStore vault;
    private Spinner accountPicker;
    private TextView stateText,detail,server,code,notice,profileText;
    private Button vpnButton,openBrowser;
    private boolean refreshingPicker;
    private String alert="";
    private final Runnable tick=new Runnable(){
        @Override public void run(){
            if(!isFinishing()){
                render();
                ui.postDelayed(this,1400);
            }
        }
    };

    private int dp(int v){return (int)(v*getResources().getDisplayMetrics().density+.5f);}
    private GradientDrawable bg(int color,int radius,int outline){
        GradientDrawable d=new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radius));
        if(outline!=0)d.setStroke(dp(1),outline);
        return d;
    }
    private TextView label(String text,int size,int color,boolean heavy){
        TextView v=new TextView(this);
        v.setText(text);
        v.setTextSize(size);
        v.setTextColor(color);
        if(heavy)v.setTypeface(Typeface.create("sans-serif-medium",Typeface.BOLD));
        v.setGravity(Gravity.CENTER_VERTICAL);
        v.setLineSpacing(dp(2),1.03f);
        return v;
    }
    private Button button(String text,int color,Runnable action){
        Button b=new Button(this);
        b.setAllCaps(false);
        b.setText(text);
        b.setTextSize(15);
        b.setTextColor(color==ACCENT?BG:INK);
        b.setBackground(bg(color,16,0));
        b.setMinHeight(dp(54));
        b.setPadding(dp(12),0,dp(12),0);
        b.setOnClickListener(v->action.run());
        return b;
    }
    private LinearLayout card(){
        LinearLayout c=new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(dp(18),dp(20),dp(18),dp(20));
        c.setBackground(bg(PANEL,22,0xff2b3d55));
        return c;
    }
    private void add(LinearLayout parent,View child,int height,int top){
        LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,
            height<0?height:dp(height));
        p.topMargin=dp(top);
        parent.addView(child,p);
    }

    @Override public void onCreate(Bundle state){
        super.onCreate(state);
        getWindow().setStatusBarColor(BG);
        getWindow().setNavigationBarColor(BG);
        vault=new AccountStore(this);
        build();
        reloadAccounts();
        NetworkGate.schedule(this,this::render);
        ui.post(tick);
        if(state==null) {
            try {
                if(vault.load().isEmpty())
                    ui.postDelayed(this::pickAccount,480);
            }catch(Exception error){alert="خزانه حساب‌ها خوانده نشد؛ اطلاعات پاک نشده‌اند.";}
        }
    }
    @Override protected void onResume(){
        super.onResume();
        NetworkGate.schedule(this,this::render);
        ui.postDelayed(this::render,350);
    }
    @Override protected void onDestroy(){
        ui.removeCallbacks(tick);
        super.onDestroy();
    }

    private void build(){
        ScrollView scroll=new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(BG);
        LinearLayout content=new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setLayoutDirection(View.LAYOUT_DIRECTION_RTL);
        content.setPadding(dp(19),dp(24),dp(19),dp(36));
        content.setOnApplyWindowInsetsListener((view,insets)->{
            int top=0,bottom=0;
            if(android.os.Build.VERSION.SDK_INT>=30){
                android.graphics.Insets bars=insets.getInsets(
                    android.view.WindowInsets.Type.systemBars());
                top=bars.top;bottom=bars.bottom;
            }else {
                top=insets.getSystemWindowInsetTop();
                bottom=insets.getSystemWindowInsetBottom();
            }
            view.setPadding(dp(19),dp(20)+top,dp(19),dp(36)+bottom);
            return insets;
        });
        scroll.addView(content);
        setContentView(scroll);

        TextView title=label("FHG  /  Secure Workspace",24,INK,true);
        title.setTextDirection(View.TEXT_DIRECTION_LTR);
        add(content,title,52,4);
        TextView sub=label("VPN با موتور رسمی Hiddify  •  مرورگر اختصاصی FHG",12,MUTED,false);
        add(content,sub,-2,0);

        LinearLayout hero=card();
        add(hero,label("وضعیت اتصال محافظت‌شده",13,MUTED,true),-2,0);
        stateText=label("در حال بررسی…",22,INK,true);
        add(hero,stateText,-2,12);
        detail=label("اتصال صرفاً توسط برنامه رسمی Hiddify انجام می‌شود.",13,MUTED,false);
        add(hero,detail,-2,7);
        vpnButton=button("باز کردن Hiddify و اتصال",ACCENT,this::openHiddify);
        add(hero,vpnButton,56,18);
        openBrowser=button("ورود به مرورگر ChatGPT",0xff2660a3,this::launchBrowser);
        add(hero,openBrowser,56,10);
        add(content,hero,-2,22);

        LinearLayout serverCard=card();
        add(serverCard,label("سرورها و اشتراک",18,INK,true),-2,0);
        server=label("انتخاب سرور در خود Hiddify",13,MUTED,false);
        add(serverCard,server,-2,9);
        add(serverCard,button("افزودن اشتراک در Hiddify",0xff263950,this::sendSubscription),54,15);
        add(serverCard,button("انتخاب لوکیشن / تست سرعت",0xff263950,this::openHiddify),54,9);
        add(content,serverCard,-2,14);

        LinearLayout accounts=card();
        add(accounts,label("حساب‌ها و ورود دومرحله‌ای",18,INK,true),-2,0);
        accountPicker=new Spinner(this);
        accountPicker.setMinimumHeight(dp(53));
        accountPicker.setBackground(bg(0xff1d2b43,13,0xff2b3d55));
        add(accounts,accountPicker,54,12);
        accountPicker.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){
            @Override public void onItemSelected(AdapterView<?> parent,View v,int pos,long id) {
                if(refreshingPicker)return;
                try {
                    List<AccountStore.Account> all=vault.load();
                    if(pos>=0&&pos<all.size()){
                        vault.select(all.get(pos).id);
                        NetworkGate.reset();
                        render();
                    }
                }catch(Exception ignored){}
            }
            @Override public void onNothingSelected(AdapterView<?> parent){}
        });
        profileText=label("حسابی وارد نشده",12,MUTED,false);
        add(accounts,profileText,-2,7);
        add(accounts,button("افزودن حساب از فایل چهارخطی",0xff263950,this::pickAccount),54,13);
        add(accounts,label("کد احراز هویت",12,MUTED,false),-2,18);
        code=label("—",22,INK,true);
        code.setTextDirection(View.TEXT_DIRECTION_LTR);
        add(accounts,code,38,5);
        add(accounts,button("کپی کد 2FA",0xff263950,this::copyCode),50,8);
        add(content,accounts,-2,14);

        notice=label("",13,WARNING,false);
        notice.setVisibility(View.GONE);
        add(content,notice,-2,15);
        add(content,button("تنظیمات VPN اندروید",0xff202e46,this::openVpnSettings),53,17);
        add(content,button("گزارش فنی غیرمحرمانه",0xff202e46,this::shareReport),53,8);
        add(content,label("FHG · Hiddify official backend · beta",11,MUTED,false),48,18);
    }

    private void reloadAccounts(){
        try {
            List<AccountStore.Account> list=vault.load();
            String[] labels=new String[Math.max(1,list.size())];
            if(list.isEmpty())labels[0]="حسابی وارد نشده";
            for(int i=0;i<list.size();i++)labels[i]=list.get(i).email;
            refreshingPicker=true;
            ArrayAdapter<String> adapter=new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_dropdown_item,labels);
            accountPicker.setAdapter(adapter);
            AccountStore.Account selected=vault.selected();
            if(selected!=null)for(int i=0;i<list.size();i++)
                if(selected.id.equals(list.get(i).id)){accountPicker.setSelection(i);break;}
            refreshingPicker=false;
        }catch(Exception failure){alert="خواندن حساب‌ها با مشکل مواجه شد.";}
        render();
    }

    private void render(){
        boolean trusted=NetworkGate.vpnActive(this);
        boolean installed=HiddifyBridge.installed(this);
        String current=NetworkGate.status(this);
        if(trusted) {
            stateText.setText("VPN فعال و مسیر HTTPS تأیید شد");
            stateText.setTextColor(ACCENT);
            detail.setText("مرورگر مجاز است؛ ارتباط به شبکه VPN سیستم متصل است.");
        }else if(!installed) {
            stateText.setText("هیدیفای نصب نیست");
            stateText.setTextColor(WARNING);
            detail.setText("برای اتصال، نسخه رسمی Hiddify را نصب کن.");
        }else if("VPN_PROBE_FAILED".equals(current)) {
            stateText.setText("VPN پیدا شد، اما اتصال تأیید نشد");
            stateText.setTextColor(WARNING);
            detail.setText("در Hiddify سرور دیگری انتخاب کن یا اینترنت را بررسی کن.");
        }else{
            stateText.setText("در انتظار اتصال Hiddify");
            stateText.setTextColor(INK);
            detail.setText("هیدیفای را باز کن، سرور دلخواه را انتخاب و VPN را وصل کن.");
        }
        vpnButton.setText(installed?"باز کردن Hiddify":"نصب Hiddify رسمی");
        openBrowser.setEnabled(trusted);
        openBrowser.setAlpha(trusted?1f:.45f);
        try {
            AccountStore.Account active=vault.selected();
            profileText.setText(active==null?"حسابی وارد نشده":"اشتراک این حساب از فایل وارد شده");
            code.setText(active==null||active.totpSecret.isEmpty()?"کد موجود نیست":
                Totp.now(active.totpSecret)+"  ·  "+Totp.secondsRemaining()+" ثانیه");
        }catch(Exception error){code.setText("کد در دسترس نیست");}
        notice.setVisibility(alert.isEmpty()?View.GONE:View.VISIBLE);
        notice.setText(alert);
    }

    private void openHiddify(){
        if(!HiddifyBridge.open(this)) {
            new AlertDialog.Builder(this)
                .setTitle("نصب Hiddify رسمی")
                .setMessage("موتور VPN این برنامه متعلق به Hiddify رسمی است. برای حفظ امنیت، نسخه رسمی را از GitHub دریافت کن.")
                .setPositiveButton("صفحه دانلود",(d,w)->HiddifyBridge.openOfficialDownload(this))
                .setNegativeButton("انصراف",null).show();
        }else {
            NetworkGate.reset();
        }
    }
    private void sendSubscription(){
        try {
            AccountStore.Account account=vault.selected();
            if(account==null){pickAccount();return;}
            if(!HiddifyBridge.installed(this)){openHiddify();return;}
            if(!HiddifyBridge.importSubscription(this,account.subscription)){
                alert="تحویل اشتراک به Hiddify انجام نشد؛ نسخه رسمی را نصب و به‌روز کن.";
                render();
            }else{
                alert="";
                Toast.makeText(this,"ورود اشتراک را داخل Hiddify تأیید کن.",Toast.LENGTH_LONG).show();
            }
        }catch(Exception e){alert="اشتراک حساب در دسترس نیست.";render();}
    }
    private void pickAccount(){
        Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT);
        i.setType("*/*");
        i.addCategory(Intent.CATEGORY_OPENABLE);
        startActivityForResult(i,PICK_ACCOUNT);
    }
    @Override protected void onActivityResult(int req,int result,Intent data){
        super.onActivityResult(req,result,data);
        if(req!=PICK_ACCOUNT||result!=RESULT_OK||data==null||data.getData()==null)return;
        try(InputStream stream=getContentResolver().openInputStream(data.getData())){
            if(stream==null)throw new IllegalArgumentException();
            vault.importFourLines(stream);
            alert="";
            reloadAccounts();
            // Explicit action initiated by the user: import the new account
            // into official Hiddify. The target app must still confirm it.
            sendSubscription();
        }catch(Exception error) {
            alert="فایل چهارخطی معتبر نبود یا حساب ذخیره نشد؛ فایل اصلی حذف نشده.";
            render();
        }
    }
    private void launchBrowser(){
        if(!NetworkGate.vpnActive(this)){
            NetworkGate.schedule(this,()->{
                if(NetworkGate.vpnActive(this))launchBrowser();
                else new AlertDialog.Builder(this)
                    .setTitle("اتصال تأیید نشده")
                    .setMessage("هیدیفای باید VPN را وصل کند و آزمون امن HTTPS با موفقیت انجام شود.")
                    .setPositiveButton("باز کردن Hiddify",(d,w)->openHiddify())
                    .setNegativeButton("انصراف",null).show();
            });
            return;
        }
        try {
            AccountStore.Account account=vault.selected();
            if(account==null){pickAccount();return;}
            Intent i=new Intent(this,BrowserActivity.class);
            i.putExtra("account_id",account.id);
            startActivity(i);
        }catch(Exception error){alert="مرورگر برای این حساب قابل بازکردن نیست.";render();}
    }
    private void copyCode(){
        try{
            AccountStore.Account a=vault.selected();
            if(a==null||a.totpSecret.isEmpty())return;
            String value=Totp.now(a.totpSecret);
            ClipboardManager cm=(ClipboardManager)getSystemService(CLIPBOARD_SERVICE);
            cm.setPrimaryClip(ClipData.newPlainText("FHG 2FA",value));
            Toast.makeText(this,"کد کپی شد",Toast.LENGTH_SHORT).show();
        }catch(Exception ignored){}
    }
    private void openVpnSettings(){
        try{startActivity(new Intent(Settings.ACTION_VPN_SETTINGS));}
        catch(Exception e){startActivity(new Intent(Settings.ACTION_SETTINGS));}
    }
    private void shareReport(){
        String report="FHG external Hiddify backend\n"+
            "Android: "+android.os.Build.VERSION.SDK_INT+"\n"+
            "Hiddify installed: "+HiddifyBridge.installed(this)+"\n"+
            "VPN network state: "+NetworkGate.status(this)+"\n"+
            "Protected session: "+NetworkGate.vpnActive(this)+"\n"+
            "Native FHG VPN engine: disabled\n"+
            "No credentials or server addresses included.";
        Intent send=new Intent(Intent.ACTION_SEND);
        send.setType("text/plain");
        send.putExtra(Intent.EXTRA_TEXT,report);
        startActivity(Intent.createChooser(send,"گزارش بدون اطلاعات محرمانه"));
    }
}
