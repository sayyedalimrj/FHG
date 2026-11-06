package com.fhg.android;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import org.mozilla.geckoview.Autocomplete;
import org.mozilla.geckoview.GeckoResult;
import org.mozilla.geckoview.GeckoRuntime;
import org.mozilla.geckoview.GeckoSession;
import org.mozilla.geckoview.GeckoSessionSettings;
import org.mozilla.geckoview.GeckoView;

import java.net.URI;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Locale;

/**
 * A single GeckoView browser task; every account has its own persistent
 * contextId. Official Login Autocomplete is scoped to the CURRENT active
 * account, and no concurrent browser Activity can replace the delegate.
 *
 * The user's clicks and Captcha/2FA are always manual. OAuth pop-ups share
 * the SAME view via a bounded GeckoSession stack (no window explosion).
 */
public final class BrowserActivity extends Activity {
    private static final int DARK = 0xff0b1528;
    private static final int PANEL = 0xff172942;
    private static final int WHITE = 0xfff4f8ff;
    private static final int MUTED = 0xff99b1cb;
    private static GeckoRuntime runtime;

    private final Handler ui = new Handler(Looper.getMainLooper());
    private final ArrayDeque<GeckoSession> parentSessions = new ArrayDeque<>();
    private final ArrayList<GeckoSession> sessions = new ArrayList<>();
    private GeckoSession session;
    private GeckoView view;
    private AccountStore store;
    private AccountStore.Account account;
    private ConnectivityManager connectivity;
    private ConnectivityManager.NetworkCallback networkCallback;
    private boolean stoppedForVpn;
    private boolean alertShown;
    private TextView heading;
    private TextView connectionLabel;

    private final Runnable vpnGuard = new Runnable() {
        @Override public void run() {
            if (!isFinishing()) {
                recheckVpn();
                ui.postDelayed(this, 1200);
            }
        }
    };

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(DARK);
        getWindow().setNavigationBarColor(DARK);
        store = new AccountStore(this);
        if (!loadAccount(getIntent())) return;
        if (!NetworkGate.vpnActive(this)) {
            block("اتصال VPN امن FHG برقرار نیست. ابتدا به برنامه برگرد و VPN را وصل کن.");
            return;
        }
        buildBrowser();
        watchVpn();
        ui.post(vpnGuard);
    }

    private boolean loadAccount(Intent intent) {
        try {
            String requested = intent == null ? null : intent.getStringExtra("account_id");
            AccountStore.Account current = store.selected();
            if (current == null || !current.id.equals(requested)) {
                finish();
                return false;
            }
            account = current;
            return true;
        } catch (Exception failure) {
            block("خزانه حساب در دسترس نیست. اطلاعات قدیمی حذف نشده‌اند.");
            return false;
        }
    }

    // Single-task Activity receives new intents rather than creating a
    // second GeckoRuntime delegate that might leak another account's secrets.
    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String previous = account == null ? "" : account.id;
        if (!loadAccount(intent)) return;
        if (!previous.equals(account.id)) {
            shutdownBrowser();
            stoppedForVpn=false;
            if (NetworkGate.vpnActive(this)) buildBrowser();
            else block("VPN برای حساب جدید متصل نیست.");
        } else if (session != null) {
            session.loadUri("https://chatgpt.com/");
        }
    }

    private int dp(int px) {
        return (int) (getResources().getDisplayMetrics().density * px + .5f);
    }

    private static GradientDrawable rounded(int color, int radius) {
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(color);
        bg.setCornerRadius(radius);
        return bg;
    }

    private Button toolButton(String content, String description, Runnable action) {
        Button b = new Button(this);
        b.setText(content);
        b.setContentDescription(description);
        b.setTextSize(19);
        b.setAllCaps(false);
        b.setTextColor(WHITE);
        b.setBackground(rounded(PANEL, dp(12)));
        b.setMinWidth(dp(48));
        b.setMinHeight(dp(48));
        b.setOnClickListener(v -> action.run());
        return b;
    }

    private void buildBrowser() {
        LinearLayout page = new LinearLayout(this);
        page.setOrientation(LinearLayout.VERTICAL);
        page.setBackgroundColor(DARK);
        page.setPadding(dp(8), dp(8), dp(8), dp(4));
        setContentView(page);

        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setPadding(dp(3), dp(2), dp(3), dp(2));
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setBackground(rounded(PANEL, dp(16)));
        page.addView(bar, new LinearLayout.LayoutParams(-1, dp(64)));

        bar.addView(toolButton("‹", "بازگشت در مرورگر", this::goBack),
                new LinearLayout.LayoutParams(dp(50), dp(50)));
        LinearLayout labels = new LinearLayout(this);
        labels.setOrientation(LinearLayout.VERTICAL);
        labels.setPadding(dp(9), 0, dp(8), 0);
        bar.addView(labels, new LinearLayout.LayoutParams(0, dp(52), 1));
        heading = new TextView(this);
        heading.setText("ChatGPT   /   FHG");
        heading.setTextSize(14);
        heading.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        heading.setTextColor(WHITE);
        heading.setSingleLine(true);
        heading.setGravity(Gravity.CENTER_VERTICAL);
        labels.addView(heading, new LinearLayout.LayoutParams(-1, 0, 1));
        connectionLabel = new TextView(this);
        connectionLabel.setText("🔒  " + account.email);
        connectionLabel.setTextColor(MUTED);
        connectionLabel.setTextSize(11);
        connectionLabel.setSingleLine(true);
        connectionLabel.setEllipsize(android.text.TextUtils.TruncateAt.END);
        labels.addView(connectionLabel, new LinearLayout.LayoutParams(-1, 0, 1));
        bar.addView(toolButton("↻", "بارگذاری دوباره صفحه", () -> {
            if (session != null) session.reload();
        }), new LinearLayout.LayoutParams(dp(50), dp(50)));
        bar.addView(toolButton("⌂", "صفحه اصلی ChatGPT", () -> {
            if (session != null) session.loadUri("https://chatgpt.com/");
        }), new LinearLayout.LayoutParams(dp(50), dp(50)));

        view = new GeckoView(this);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, 0, 1);
        lp.topMargin=dp(8);
        page.addView(view, lp);

        if (runtime == null)
            runtime = GeckoRuntime.create(getApplicationContext());
        runtime.getSettings().setLoginAutofillEnabled(true);
        installSecureAutofill();
        session = makeSession(true);
        view.setSession(session);
        session.loadUri("https://chatgpt.com/auth/login");
    }

    private GeckoSession makeSession(boolean openImmediately) {
        GeckoSessionSettings settings = new GeckoSessionSettings.Builder()
            .contextId(account.id).usePrivateMode(false)
            .useTrackingProtection(false).build(); // Cloudflare/MFA requires normal provider cookies; this is a ChatGPT-dedicated isolated context.
        GeckoSession child = new GeckoSession(settings);
        child.setNavigationDelegate(new GeckoSession.NavigationDelegate() {
            @Override public GeckoResult<GeckoSession> onNewSession(
                    GeckoSession current, String uri) {
                // Important for Google/Microsoft sign-in and manual challenges,
                // but do not create extra Android Activities / loose windows.
                if (sessions.size() >= 4 || !NetworkGate.vpnActive(BrowserActivity.this))
                    return null;
                GeckoSession newSession = makeSession(false);
                // Mozilla's onNewSession contract requires an UNOPENED
                // session. Gecko itself opens it upon completion.
                parentSessions.push(current);
                session = newSession;
                view.releaseSession();
                view.setSession(newSession);
                return GeckoResult.fromValue(newSession);
            }
        });
        child.setContentDelegate(new GeckoSession.ContentDelegate() {
            @Override public void onCloseRequest(GeckoSession closing) {
                if (closing == session && !parentSessions.isEmpty()) {
                    GeckoSession parent = parentSessions.pop();
                    session = parent;
                    view.releaseSession();
                    view.setSession(parent);
                    closeChild(closing);
                } else if (closing == session) {
                    finish();
                }
            }
        });
        child.setPromptDelegate(new GeckoSession.PromptDelegate() {
            @Override public GeckoResult<GeckoSession.PromptDelegate.PromptResponse>
                onLoginSave(GeckoSession current,
                    GeckoSession.PromptDelegate.AutocompleteRequest<Autocomplete.LoginSaveOption> request) {
                if (request.options != null && request.options.length == 1) {
                    Autocomplete.LoginEntry input = request.options[0].value;
                    if (allowedOrigin(input.origin) != null &&
                        account != null &&
                        account.email.equalsIgnoreCase(input.username)) {
                        return GeckoResult.fromValue(request.confirm(request.options[0]));
                    }
                }
                return GeckoResult.fromValue(request.dismiss());
            }
        });
        if (openImmediately) child.open(runtime);
        sessions.add(child);
        return child;
    }

    private static String allowedOrigin(String input) {
        if (input == null) return null;
        try {
            String hostname;
            if (input.contains("://")) {
                URI u = new URI(input);
                if (!"https".equalsIgnoreCase(u.getScheme()) ||
                    u.getUserInfo() != null ||
                    (u.getPort() != -1 && u.getPort() != 443))
                    return null;
                hostname = u.getHost();
            } else hostname = input;
            if (hostname == null) return null;
            String host = hostname.toLowerCase(Locale.ROOT);
            if ("auth.openai.com".equals(host) ||
                "auth0.openai.com".equals(host) ||
                "setup.auth.openai.com".equals(host) ||
                "chatgpt.com".equals(host))
                return "https://" + host;
        } catch (Exception ignored) {}
        return null;
    }

    private Autocomplete.LoginEntry login(String origin) {
        return new Autocomplete.LoginEntry.Builder()
            .origin(origin).username(account.email).password(account.password)
            .build();
    }

    private void installSecureAutofill() {
        runtime.setAutocompleteStorageDelegate(new Autocomplete.StorageDelegate() {
            @Override public GeckoResult<Autocomplete.LoginEntry[]> onLoginFetch(String domain) {
                String origin = allowedOrigin(domain);
                if (origin == null || account == null)
                    return GeckoResult.fromValue(new Autocomplete.LoginEntry[0]);
                return GeckoResult.fromValue(new Autocomplete.LoginEntry[]{login(origin)});
            }
            @Override public GeckoResult<Autocomplete.LoginEntry[]> onLoginFetch() {
                if (account == null)
                    return GeckoResult.fromValue(new Autocomplete.LoginEntry[0]);
                return GeckoResult.fromValue(new Autocomplete.LoginEntry[]{
                    login("https://chatgpt.com"), login("https://auth.openai.com"),
                    login("https://auth0.openai.com"), login("https://setup.auth.openai.com")});
            }
            @Override public void onLoginSave(Autocomplete.LoginEntry entry) {
                if (account == null || entry == null ||
                    allowedOrigin(entry.origin) == null ||
                    !account.email.equalsIgnoreCase(entry.username) ||
                    entry.password == null || entry.password.isEmpty())return;
                try {
                    // Saves the real password the user typed and submitted on
                    // an official site, not an invented automation result.
                    store.updatePassword(account.id, entry.password);
                    AccountStore.Account updated = store.selected();
                    if (updated != null && updated.id.equals(account.id)) account = updated;
                } catch(Exception ignored){}
            }
        });
    }

    private void goBack() {
        if (session == null) return;
        if (!parentSessions.isEmpty()) {
            GeckoSession close = session;
            session = parentSessions.pop();
            view.releaseSession();
            view.setSession(session);
            closeChild(close);
        } else session.goBack();
    }

    private void closeChild(GeckoSession child) {
        sessions.remove(child);
        try {child.close();}catch(Exception ignored){}
    }

    private void watchVpn() {
        connectivity=(ConnectivityManager)getSystemService(CONNECTIVITY_SERVICE);
        if(connectivity == null)return;
        networkCallback=new ConnectivityManager.NetworkCallback() {
            @Override public void onAvailable(Network network) { recheckVpn(); }
            @Override public void onLost(Network network) { recheckVpn(); }
            @Override public void onCapabilitiesChanged(Network n, NetworkCapabilities c) {
                recheckVpn();
            }
        };
        connectivity.registerDefaultNetworkCallback(networkCallback);
    }

    private void recheckVpn() {
        ui.post(() -> {
            if (!NetworkGate.vpnActive(this) && !stoppedForVpn) {
                stoppedForVpn = true;
                shutdownBrowser();
                block("اتصال VPN تأییدشده قطع شده است. مرورگر برای جلوگیری از اتصال مستقیم بسته شد.");
            }
        });
    }

    private void shutdownBrowser() {
        if (view != null) {
            try {view.releaseSession();}catch(Exception ignored){}
        }
        for (GeckoSession current : new ArrayList<>(sessions)) closeChild(current);
        sessions.clear();
        parentSessions.clear();
        session=null;
    }

    private void block(String message) {
        if (isFinishing() || alertShown) return;
        alertShown = true;
        new AlertDialog.Builder(this)
            .setTitle("حفاظت شبکه FHG")
            .setMessage(message)
            .setCancelable(false)
            .setPositiveButton("بازگشت", (dialog, which) -> finish())
            .show();
    }

    @Override protected void onResume() {
        super.onResume();
        if (account != null) {
            try {
                AccountStore.Account selected = store.selected();
                if (selected == null || !selected.id.equals(account.id)) {
                    shutdownBrowser();
                    finish();
                    return;
                }
            } catch(Exception e) { shutdownBrowser();finish();return; }
        }
        if (session != null) recheckVpn();
    }

    @Override protected void onDestroy() {
        ui.removeCallbacksAndMessages(null);
        if (connectivity != null && networkCallback != null) {
            try { connectivity.unregisterNetworkCallback(networkCallback); }
            catch(Exception ignored){}
        }
        shutdownBrowser();
        // Avoid retaining the previous account's credentials via the
        // process-global GeckoRuntime delegate across Activity destruction.
        if (runtime != null) runtime.setAutocompleteStorageDelegate(null);
        super.onDestroy();
    }
}
