package com.fhg.android;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
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
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import java.io.InputStream;
import java.util.List;

public final class MainActivity extends Activity {
    private static final int PICK_CONFIG = 41;
    private static final int PICK_VPN_CONFIG = 42;
    private static final int VPN_PERMISSION = 43;
    private static final int NAVY = 0xff0b1528;
    private static final int SURFACE = 0xff12233d;
    private static final int TEAL = 0xff45dec6;
    private static final int MUTED = 0xff8da6c4;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private AccountStore store;
    private LinearLayout root;
    private Spinner accountSpinner;
    private TextView vpnStatus, accountStatus, otpDisplay, hint;
    private List<AccountStore.Account> accounts;
    private boolean firstRunPickerShown;

    @Override public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().setStatusBarColor(NAVY);
        getWindow().setNavigationBarColor(NAVY);
        store = new AccountStore(this);
        draw();
        refresh();
        handler.postDelayed(new Runnable() {
            @Override public void run() {
                if (!isFinishing()) {
                    refreshStatus();
                    handler.postDelayed(this, 1000);
                }
            }
        }, 1000);
        if (savedInstanceState == null) {
            try {
                if (store.load().isEmpty()) {
                    firstRunPickerShown = true;
                    handler.postDelayed(this::pickConfig, 600);
                }
            } catch (Exception ignored) {
                // Never silently reset a corrupted or locked keystore vault.
            }
        }
    }

    private int dp(float n) { return (int)(getResources().getDisplayMetrics().density * n + .5f); }

    private GradientDrawable background(int color, int radiusDp) {
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(color);
        bg.setCornerRadius(dp(radiusDp));
        return bg;
    }

    private TextView text(String label, int sp, int color, boolean bold) {
        TextView view = new TextView(this);
        view.setText(label);
        view.setTextColor(color);
        view.setTextSize(sp);
        if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        view.setGravity(Gravity.CENTER_VERTICAL);
        return view;
    }

    private void attach(LinearLayout container, View child, int height, int margin) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                height < 0 ? height : dp(height));
        lp.setMargins(0, dp(margin), 0, 0);
        container.addView(child, lp);
    }

    private Button button(String caption, int color, Runnable action) {
        Button button = new Button(this);
        button.setAllCaps(false);
        button.setText(caption);
        button.setTextSize(15);
        button.setTextColor(Color.WHITE);
        button.setBackground(background(color, 12));
        button.setOnClickListener(v -> action.run());
        return button;
    }

    private LinearLayout card() {
        LinearLayout block = new LinearLayout(this);
        block.setPadding(dp(16), dp(15), dp(16), dp(18));
        block.setOrientation(LinearLayout.VERTICAL);
        block.setBackground(background(SURFACE, 18));
        return block;
    }

    private void draw() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(NAVY);
        root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(18), dp(25), dp(18), dp(25));
        root.setBackgroundColor(NAVY);
        scroll.addView(root);
        setContentView(scroll);

        TextView title = text("FHG  •  ANDROID", 25, Color.WHITE, true);
        attach(root, title, 40, 0);
        TextView subtitle = text("مرورگر جداگانه، حساب‌های محفوظ و اتصال تحت VPN", 12, MUTED, false);
        attach(root, subtitle, 32, 0);

        LinearLayout vpn = card();
        attach(vpn, text("●  وضعیت مسیر اینترنت", 17, TEAL, true), 33, 0);
        vpnStatus = text("در حال بررسی...", 14, Color.WHITE, false);
        attach(vpn, vpnStatus, 49, 2);
        attach(vpn, text("VPN داخلی FHG با موتور sing-box. مرورگر تنها با تونل همین برنامه باز می‌شود.", 12, MUTED, false), 72, 3);
        attach(vpn, button("▶  اتصال VPN داخلی / AUTO", 0xff4844a9, this::startVpn), 50, 12);
        attach(vpn, button("↓  دریافت اشتراک چهارخطی و انتخاب سرور", 0xff215e87, this::confirmBootstrap), 50, 8);
        attach(vpn, button("📁  کانفیگ sing-box از فایل (آفلاین)", 0xff345f77, this::pickVpnConfig), 50, 8);
        attach(vpn, button("■  قطع اتصال VPN داخلی", 0xff73415f, this::disconnectVpn), 50, 8);
        attach(vpn, button("فعال‌سازی Always-on / Block without VPN برای FHG", 0xff283f64, this::openVpnSettings), 50, 8);
        attach(root, vpn, -2, 23);

        LinearLayout account = card();
        attach(account, text("●  حساب‌ها / ACCOUNTS", 17, TEAL, true), 31, 0);
        accountSpinner = new Spinner(this);
        accountSpinner.setBackground(background(0xff1a3553, 10));
        attach(account, accountSpinner, 50, 8);
        accountSpinner.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener() {
            @Override public void onNothingSelected(android.widget.AdapterView<?> parent) {}
            @Override public void onItemSelected(android.widget.AdapterView<?> parent, View view, int position, long id) {
                if (accounts != null && position >= 0 && position < accounts.size()) {
                    store.select(accounts.get(position).id);
                    refreshStatus();
                }
            }
        });
        accountStatus = text("", 12, MUTED, false);
        attach(account, accountStatus, 44, 5);
        attach(account, button("📁  واردکردن فایل چهارخطی حساب", 0xff0e7b87, this::pickConfig), 48, 4);
        otpDisplay = text("2FA: —", 21, Color.WHITE, true);
        attach(account, otpDisplay, 48, 10);
        attach(account, button("کپی کد 2FA", 0xff314864, this::copyTotp), 46, 2);
        attach(root, account, -2, 16);

        LinearLayout browser = card();
        attach(browser, text("●  مرورگر خصوصی / FIREFOX GECKO", 17, TEAL, true), 37, 0);
        attach(browser, text("ورود دستی به ChatGPT، تکمیل خودکار ایمیل و رمز از خزانه امن و جداسازی نشست هر حساب.", 13, MUTED, false), 70, 0);
        attach(browser, button("🌐  ورود به ChatGPT", 0xff157eaa, this::openBrowser), 56, 8);
        hint = text("ابتدا VPN داخلی FHG را وصل کن.", 12, MUTED, false);
        attach(browser, hint, 45, 4);
        attach(root, browser, -2, 16);

        TextView footer = text("FHG Android • build 0.1 beta • Browser uses GeckoView • Hiddify manages the VPN tunnel", 10, MUTED, false);
        attach(root, footer, 70, 13);
    }

    private void refresh() {
        try {
            accounts = store.load();
            String[] names = accounts.stream().map(a -> a.email).toArray(String[]::new);
            ArrayAdapter<String> adapter = new ArrayAdapter<>(this,
                    android.R.layout.simple_spinner_dropdown_item,
                    names.length == 0 ? new String[] { "No accounts yet" } : names);
            accountSpinner.setAdapter(adapter);
            AccountStore.Account selected = store.selected();
            if (selected != null) {
                for (int i = 0; i < accounts.size(); i++) {
                    if (accounts.get(i).id.equals(selected.id)) {
                        accountSpinner.setSelection(i);
                        break;
                    }
                }
            }
        } catch (Exception e) {
            accounts = java.util.Collections.emptyList();
            accountStatus.setText("خزانه رمز در دسترس نیست؛ داده‌ها پاک نشده‌اند.");
            new AlertDialog.Builder(this).setTitle("Protected vault")
                    .setMessage("The encrypted vault cannot be opened. The app will not reset it or replace existing secrets.")
                    .setPositiveButton("OK", null).show();
        }
        refreshStatus();
    }

    private void refreshStatus() {
        boolean connected = NetworkGate.vpnActive(this);
        vpnStatus.setText(connected ? "● تونل داخلی FHG فعال است — مرورگر مجاز" :
                (EmbeddedVpnService.isStarting() ? "● تونل sing-box در حال اتصال است…" :
                "● تونل داخلی FHG غیرفعال است — مرورگر مسدود"));
        vpnStatus.setTextColor(connected ? TEAL : 0xffffaa70);
        try {
            AccountStore.Account current = store.selected();
            if (current == null) {
                accountStatus.setText("فایل چهارخطی را وارد کن؛ اولین اجرا انتخاب فایل را باز می‌کند.");
                otpDisplay.setText("2FA: —");
                return;
            }
            accountStatus.setText("حساب انتخاب‌شده: " + current.email +
                    "  •  نشست خصوصی جداگانه");
            if (current.totpSecret.isEmpty()) {
                otpDisplay.setText("2FA: not configured");
            } else {
                otpDisplay.setText("2FA: " + Totp.now(current.totpSecret) +
                        "    (" + Totp.secondsRemaining() + "s)");
            }
            hint.setText(connected ? "تونل داخلی فعال است؛ وارد ChatGPT شو." :
                    "برای حفاظت، تونل داخلی FHG را وصل کن و Lockdown را فعال نگه دار.");
        } catch (Exception e) {
            otpDisplay.setText("2FA: unavailable");
        }
    }

    private void pickVpnConfig() {
        Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.setType("*/*");
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        startActivityForResult(intent,PICK_VPN_CONFIG);
    }

    private void pickConfig() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.setType("*/*");
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        startActivityForResult(intent, PICK_CONFIG);
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == VPN_PERMISSION) {
            if (resultCode == RESULT_OK) launchVpnService();
            else explain("VPN permission denied", "No tunnel was started and FHG browser remains blocked.");
            return;
        }
        if (requestCode == PICK_VPN_CONFIG && resultCode == RESULT_OK && data != null) {
            importVpnFile(data.getData());
            return;
        }
        if (requestCode != PICK_CONFIG || resultCode != RESULT_OK || data == null) return;
        try (InputStream stream = getContentResolver().openInputStream(data.getData())) {
            if (stream == null) throw new Exception("No file contents");
            store.importFourLines(stream);
            refresh();
            new AlertDialog.Builder(this).setTitle("Account imported")
                    .setMessage("Secrets are stored encrypted in the Android Keystore. No file is uploaded.")
                    .setPositiveButton("OK", null).show();
        } catch (Exception e) {
            new AlertDialog.Builder(this).setTitle("Import failed")
                    .setMessage("Check the four-line file format. Existing accounts remain unchanged.")
                    .setPositiveButton("OK", null).show();
        }
    }


    private void explain(String title,String message) {
        new AlertDialog.Builder(this).setTitle(title).setMessage(message)
            .setPositiveButton("OK",null).show();
    }

    private void confirmBootstrap() {
        try {
            AccountStore.Account selected = store.selected();
            if (selected == null) { pickConfig(); return; }
            // First import may be before any VPN tunnel can exist. This is
            // explicit user consent to a single HTTPS bootstrap request;
            // no hidden direct reconnect or transparent HTTP fallback.
            new AlertDialog.Builder(this).setTitle("Download proxy subscription")
                .setMessage("This initial subscription download may use the current connection before FHG VPN starts. Your private HTTPS subscription URL is sent only to its configured server. Continue?")
                .setPositiveButton("Download once", (d,w) -> downloadSubscription(selected))
                .setNegativeButton("Cancel",null).show();
        } catch (Exception e) { explain("Vault unavailable","Unable to read selected account."); }
    }

    private void downloadSubscription(AccountStore.Account selected) {
        new Thread(() -> {
            try {
                String target = selected.subscription;
                if(!target.startsWith("https://")) throw new IllegalArgumentException("HTTPS subscription required");
                String text = null;
                for(int redirect=0;redirect<=2;redirect++) {
                    java.net.URL url = new java.net.URL(target);
                    if(!"https".equalsIgnoreCase(url.getProtocol())) throw new SecurityException("HTTPS only");
                    java.net.HttpURLConnection conn=(java.net.HttpURLConnection)url.openConnection();
                    conn.setConnectTimeout(12000);
                    conn.setReadTimeout(16000);
                    conn.setInstanceFollowRedirects(false);
                    conn.setRequestProperty("User-Agent","FHG-Android/0.2");
                    try {
                        int status=conn.getResponseCode();
                        if(status>=300 && status<400 && redirect<2) {
                            String location=conn.getHeaderField("Location");
                            java.net.URI resolved=new java.net.URI(target).resolve(location);
                            if(!"https".equalsIgnoreCase(resolved.getScheme()))
                                throw new SecurityException("Cross-scheme redirect refused");
                            target=resolved.toString();
                            continue;
                        }
                        if(status!=200)throw new IllegalStateException("Subscription HTTP status "+status);
                        try(InputStream input=conn.getInputStream()) {
                            java.io.ByteArrayOutputStream bounded=new java.io.ByteArrayOutputStream();
                            byte[] buf=new byte[8192]; int n;
                            while((n=input.read(buf))>=0) {
                                if(bounded.size()+n>1000000) throw new Exception("Subscription is too large");
                                bounded.write(buf,0,n);
                            }
                            text=bounded.toString("UTF-8");
                        }
                        break;
                    } finally { conn.disconnect(); }
                }
                if(text==null)throw new Exception("HTTPS redirect limit");
                String config=VpnConfig.parse(text);
                store.saveVpnConfig(selected.id,config);
                runOnUiThread(() -> explain("Subscription ready",
                    "Embedded sing-box config encrypted successfully. Press Connect FHG VPN. Unsupported nodes were not given a direct bypass."));
            } catch(Exception e) {
                runOnUiThread(() -> explain("Import failed",
                    "Subscription unsupported or unavailable. Import a local sing-box JSON configuration or a VLESS/Trojan/VMess/SS subscription. No direct VPN fallback was enabled."));
            }
        },"fhg-bootstrap-subscription").start();
    }

    private void importVpnFile(Uri uri) {
        new Thread(() -> {
            try {
                AccountStore.Account selected=store.selected();
                if(selected==null)throw new Exception("No account");
                byte[] bytes;
                try(InputStream input=getContentResolver().openInputStream(uri)) {
                    if(input==null)throw new Exception("Unreadable file");
                    java.io.ByteArrayOutputStream buffer=new java.io.ByteArrayOutputStream();
                    byte[] tmp=new byte[8192]; int n;
                    while((n=input.read(tmp))>=0) {
                        if(buffer.size()+n>1000000)throw new Exception("Config too large");
                        buffer.write(tmp,0,n);
                    }
                    bytes=buffer.toByteArray();
                }
                String config=VpnConfig.parse(new String(bytes,java.nio.charset.StandardCharsets.UTF_8));
                java.util.Arrays.fill(bytes,(byte)0);
                store.saveVpnConfig(selected.id,config);
                runOnUiThread(() -> explain("VPN configuration imported",
                    "The configuration is saved encrypted and ready for the internal VPN."));
            } catch(Exception e) {
                runOnUiThread(() -> explain("VPN import failed",
                    "Import a supported subscription or safe sing-box JSON config; existing account data is preserved."));
            }
        },"fhg-import-config").start();
    }

    private void startVpn() {
        try {
            AccountStore.Account selected=store.selected();
            if(selected==null){pickConfig();return;}
            store.loadVpnConfig(selected.id);
        } catch(java.io.FileNotFoundException missing) {
            new AlertDialog.Builder(this).setTitle("VPN configuration missing")
                .setMessage("Download the subscription from the existing four-line account file or import a sing-box JSON config first.")
                .setPositiveButton("Download subscription",(d,w)->confirmBootstrap())
                .setNeutralButton("Import JSON",(d,w)->pickVpnConfig())
                .setNegativeButton("Cancel",null).show();
            return;
        } catch(Exception e) {
            explain("Encrypted VPN config unavailable","The saved configuration could not be decrypted. It has NOT been deleted.");
            return;
        }
        Intent consent=android.net.VpnService.prepare(this);
        if(consent!=null)startActivityForResult(consent,VPN_PERMISSION);
        else launchVpnService();
    }

    private void launchVpnService() {
        Intent start=new Intent(this,EmbeddedVpnService.class);
        start.setAction(EmbeddedVpnService.ACTION_CONNECT);
        try {
            if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(start);
            else startService(start);
            android.widget.Toast.makeText(this,"Starting internal VPN…",
                android.widget.Toast.LENGTH_SHORT).show();
        } catch(Exception e) {
            explain("VPN service unavailable","The secure tunnel could not start. Browser remains blocked.");
        }
    }

    private void disconnectVpn() {
        Intent stop=new Intent(this,EmbeddedVpnService.class);
        stop.setAction(EmbeddedVpnService.ACTION_DISCONNECT);
        startService(stop);
        handler.postDelayed(this::refreshStatus,500);
    }

    private void openVpnSettings() {
        try {
            startActivity(new Intent(Settings.ACTION_VPN_SETTINGS));
        } catch (Exception e) {
            startActivity(new Intent(Settings.ACTION_SETTINGS));
        }
    }

    private void copyTotp() {
        try {
            AccountStore.Account current = store.selected();
            if (current == null || current.totpSecret.isEmpty()) return;
            String otp = Totp.now(current.totpSecret);
            ClipboardManager clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
            clipboard.setPrimaryClip(ClipData.newPlainText("FHG 2FA", otp));
            android.widget.Toast.makeText(this,"2FA copied",android.widget.Toast.LENGTH_SHORT).show();
        } catch (Exception ignored) {
            android.widget.Toast.makeText(this,"2FA unavailable",android.widget.Toast.LENGTH_SHORT).show();
        }
    }

    private void openBrowser() {
        try {
            AccountStore.Account current = store.selected();
            if (current == null) {
                pickConfig();
                return;
            }
            if (!NetworkGate.vpnActive(this)) {
                new AlertDialog.Builder(this).setTitle("VPN required")
                    .setMessage("FHG blocks web browsing while no system VPN is detected. Connect the integrated FHG VPN and enable Always-on VPN with Block connections without VPN for FHG in Android settings.")
                    .setPositiveButton("VPN settings", (d,w) -> openVpnSettings())
                    .setNegativeButton("Cancel", null).show();
                return;
            }
            Intent browser = new Intent(this, BrowserActivity.class);
            browser.putExtra("account_id", current.id);
            startActivity(browser);
        } catch (Exception error) {
            new AlertDialog.Builder(this).setMessage("Account vault unavailable; browser not opened.")
                    .setPositiveButton("OK", null).show();
        }
    }
}
