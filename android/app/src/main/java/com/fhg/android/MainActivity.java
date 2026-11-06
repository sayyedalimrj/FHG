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
        attach(vpn, text("ورود به مرورگر بدون VPN مسدود است؛ برای حفاظت کل گوشی Lockdown را در تنظیمات Android فعال کن.", 12, MUTED, false), 72, 3);
        attach(vpn, button("بازکردن Hiddify", 0xff4844a9, this::openHiddify), 50, 12);
        attach(vpn, button("تنظیمات Always-on VPN / Kill Switch", 0xff283f64, this::openVpnSettings), 50, 8);
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
        hint = text("ابتدا VPN را در Hiddify وصل کن.", 12, MUTED, false);
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
        vpnStatus.setText(connected ? "● VPN متصل است — مرورگر اجازه بازشدن دارد" :
                "● اتصال VPN شناسایی نشد — مرورگر مسدود است");
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
            hint.setText(connected ? "VPN فعال است؛ برای ورود روی دکمه بالا بزن." :
                    "برای حفاظت، ابتدا Hiddify را وصل کن و Lockdown را فعال نگه دار.");
        } catch (Exception e) {
            otpDisplay.setText("2FA: unavailable");
        }
    }

    private void pickConfig() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.setType("*/*");
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        startActivityForResult(intent, PICK_CONFIG);
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
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

    private void openHiddify() {
        try {
            AccountStore.Account account = store.selected();
            if (account == null) {
                pickConfig();
                return;
            }
            // The user explicitly authorizes this share action.
            Uri uri = Uri.parse("hiddify://import/" + account.subscription + "#FHG");
            Intent importIntent = new Intent(Intent.ACTION_VIEW, uri);
            importIntent.setPackage("app.hiddify.com");
            startActivity(importIntent);
        } catch (ActivityNotFoundException missing) {
            new AlertDialog.Builder(this).setTitle("Install Hiddify")
                    .setMessage("Install the official Hiddify Android VPN app, then return and import the subscription.")
                    .setPositiveButton("Open Play Store", (d, w) -> {
                        startActivity(new Intent(Intent.ACTION_VIEW,
                                Uri.parse("https://play.google.com/store/apps/details?id=app.hiddify.com")));
                    }).setNegativeButton("Cancel", null).show();
        } catch (Exception error) {
            new AlertDialog.Builder(this).setTitle("Hiddify unavailable")
                    .setMessage("Could not open the official Hiddify import screen.")
                    .setPositiveButton("OK", null).show();
        }
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
                    .setMessage("FHG blocks web browsing while no system VPN is detected. Connect Hiddify and enable Always-on VPN with Block connections without VPN in Android settings.")
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
