package com.fhg.android;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Bundle;
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
import java.util.Locale;

/**
 * Real GeckoView browser, separate cookie/storage jar per selected FHG user.
 * No form submission automation, CAPTCHA solvers, fingerprint spoofing,
 * or direct-fallback network path.
 *
 * Per-app protection gates every session on VPN network capabilities. The
 * device-wide OS Kill Switch must ALSO be configured on Hiddify by the user.
 */
public final class BrowserActivity extends Activity {
    private static GeckoRuntime runtime; // GeckoRuntime is one per process
    private GeckoSession session;
    private GeckoView view;
    private AccountStore store;
    private AccountStore.Account account;
    private ConnectivityManager connectivity;
    private ConnectivityManager.NetworkCallback networkCallback;
    private boolean stoppedForVpn;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(0xff09172c);
        getWindow().setNavigationBarColor(0xff09172c);
        store = new AccountStore(this);
        try {
            String requested = getIntent().getStringExtra("account_id");
            account = store.selected();
            if (account == null || !account.id.equals(requested)) {
                finish(); // another account was selected after browser launch
                return;
            }
            if (!NetworkGate.vpnActive(this)) {
                block("No VPN detected. Connect Hiddify before opening ChatGPT.");
                return;
            }
            buildBrowser();
            watchVpn();
        } catch (Exception e) {
            // No secrets, URLs, or page body appear in logs or errors.
            block("Browser account storage is unavailable. Existing data has not been deleted.");
        }
    }

    private int dp(int x) {
        return (int)(getResources().getDisplayMetrics().density * x + .5f);
    }

    private static String allowedOrigin(String input) {
        if (input == null) return null;
        String host;
        try {
            if (input.contains("://")) {
                URI uri = new URI(input);
                if (!"https".equalsIgnoreCase(uri.getScheme())) return null;
                host = uri.getHost();
            } else host = input;
            if (host == null) return null;
            host = host.toLowerCase(Locale.ROOT);
            if ("auth.openai.com".equals(host) ||
                "chatgpt.com".equals(host)) return "https://" + host;
        } catch (Exception ignored) { }
        return null; // never offer secrets to suffix-alike or http domains
    }

    private Autocomplete.LoginEntry login(String origin) {
        return new Autocomplete.LoginEntry.Builder()
                .origin(origin)
                .username(account.email)
                .password(account.password)
                .build();
    }

    private void buildBrowser() {
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setBackgroundColor(0xff09172c);
        setContentView(layout);

        LinearLayout toolbar = new LinearLayout(this);
        toolbar.setOrientation(LinearLayout.HORIZONTAL);
        toolbar.setPadding(dp(8), dp(6), dp(8), dp(6));
        toolbar.setBackgroundColor(0xff132d4c);
        layout.addView(toolbar, new LinearLayout.LayoutParams(-1, dp(58)));

        Button back = toolButton("‹", () -> {
            if (session != null) session.goBack();
        });
        toolbar.addView(back, new LinearLayout.LayoutParams(dp(46), -1));

        TextView label = new TextView(this);
        label.setText("🔒  ChatGPT  •  " + account.email);
        label.setTextSize(12);
        label.setTextColor(0xffcfe9f4);
        label.setGravity(android.view.Gravity.CENTER_VERTICAL);
        label.setSingleLine(true);
        label.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);
        toolbar.addView(label, new LinearLayout.LayoutParams(0, -1, 1));

        toolbar.addView(toolButton("↻", () -> {
            if (session != null) session.reload();
        }), new LinearLayout.LayoutParams(dp(45), -1));
        toolbar.addView(toolButton("⌂", () -> {
            if (session != null) session.loadUri("https://chatgpt.com/");
        }), new LinearLayout.LayoutParams(dp(45), -1));

        view = new GeckoView(this);
        layout.addView(view, new LinearLayout.LayoutParams(-1, 0, 1));
        if (runtime == null)
            runtime = GeckoRuntime.create(this.getApplicationContext());

        runtime.getSettings().setLoginAutofillEnabled(true);
        runtime.setAutocompleteStorageDelegate(new Autocomplete.StorageDelegate() {
            @Override
            public GeckoResult<Autocomplete.LoginEntry[]> onLoginFetch(String domain) {
                String origin = allowedOrigin(domain);
                if (origin == null)
                    return GeckoResult.fromValue(new Autocomplete.LoginEntry[0]);
                return GeckoResult.fromValue(new Autocomplete.LoginEntry[] { login(origin) });
            }

            @Override
            public GeckoResult<Autocomplete.LoginEntry[]> onLoginFetch() {
                return GeckoResult.fromValue(new Autocomplete.LoginEntry[] {
                    login("https://auth.openai.com"),
                    login("https://chatgpt.com")
                });
            }

            @Override
            public void onLoginSave(Autocomplete.LoginEntry entry) {
                // User already submitted a real official form. Store modified
                // passwords automatically in Android Keystore; no Save prompt.
                if (allowedOrigin(entry.origin) == null ||
                    !account.email.equalsIgnoreCase(entry.username) ||
                    entry.password.isEmpty()) return;
                try {
                    store.updatePassword(account.id, entry.password);
                    account = store.selected();
                } catch (Exception ignored) {
                    // A failed save must not silently destroy the previous key.
                }
            }
        });

        GeckoSessionSettings settings = new GeckoSessionSettings.Builder()
                .contextId(account.id)
                .usePrivateMode(false) // isolated PERSISTENT cookies, not private-mode eviction
                .useTrackingProtection(true)
                .build();

        session = new GeckoSession(settings);
        session.setContentDelegate(new GeckoSession.ContentDelegate() { });
        session.setPromptDelegate(new GeckoSession.PromptDelegate() {
            @Override
            public GeckoResult<GeckoSession.PromptDelegate.PromptResponse> onLoginSave(
                    GeckoSession current,
                    GeckoSession.PromptDelegate.AutocompleteRequest<Autocomplete.LoginSaveOption> request) {
                // Confirm ONLY official first-party login stores. All other
                // origins remain unhandled and receive no account secrets.
                if (request.options.length == 1 &&
                    allowedOrigin(request.options[0].value.origin) != null &&
                    account.email.equalsIgnoreCase(request.options[0].value.username)) {
                    return GeckoResult.fromValue(request.confirm(request.options[0]));
                }
                return GeckoResult.fromValue(request.dismiss());
            }
        });
        session.open(runtime);
        view.setSession(session);
        session.loadUri("https://chatgpt.com/login");
    }

    private Button toolButton(String text, Runnable handler) {
        Button button = new Button(this);
        button.setText(text);
        button.setTextSize(22);
        button.setTextColor(Color.WHITE);
        button.setAllCaps(false);
        button.setBackgroundColor(0xff132d4c);
        button.setOnClickListener(v -> handler.run());
        return button;
    }

    private void watchVpn() {
        connectivity = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
        networkCallback = new ConnectivityManager.NetworkCallback() {
            @Override public void onAvailable(Network network) { recheckVpn(); }
            @Override public void onLost(Network network) { recheckVpn(); }
            @Override public void onCapabilitiesChanged(Network n, NetworkCapabilities capabilities) {
                recheckVpn();
            }
        };
        connectivity.registerDefaultNetworkCallback(networkCallback);
    }

    private void recheckVpn() {
        runOnUiThread(() -> {
            if (!NetworkGate.vpnActive(this) && !stoppedForVpn) {
                stoppedForVpn = true;
                // Close Gecko's network requests immediately, rather than
                // leaving a loaded tab ready for a direct-route fallback.
                if (session != null) {
                    try { session.close(); } catch (Exception ignored) {}
                    session = null;
                }
                block("VPN disconnected. All browser sessions were closed. Reconnect Hiddify.");
            }
        });
    }

    private void block(String message) {
        if (isFinishing()) return;
        new AlertDialog.Builder(this)
            .setTitle("FHG network protection")
            .setMessage(message)
            .setCancelable(false)
            .setPositiveButton("Back to accounts", (dialog, which) -> finish())
            .show();
    }

    @Override protected void onResume() {
        super.onResume();
        if (session != null) recheckVpn();
    }

    @Override protected void onDestroy() {
        if (connectivity != null && networkCallback != null) {
            try { connectivity.unregisterNetworkCallback(networkCallback); }
            catch (Exception ignored) { }
        }
        if (session != null) {
            try { session.close(); } catch (Exception ignored) { }
        }
        super.onDestroy();
    }
}
