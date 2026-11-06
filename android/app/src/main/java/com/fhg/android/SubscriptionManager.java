package com.fhg.android;

import android.content.Context;
import android.net.VpnService;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URL;
import java.nio.charset.StandardCharsets;

/**
 * User-supplied HTTPS subscription importer. No HTML-page scraping, arbitrary
 * insecure redirects, credential logging, HTTP, or silent bypass. Multiple
 * format Accept headers cover popular Hiddify/Sing-box/Clash panels.
 */
final class SubscriptionManager {
    private static final int MAX_BYTES = 1_000_000;
    private static final String[] USER_AGENTS = {
        "SFA (sing-box 1.14.4; language en_US)",
        "ClashMeta/1.19.0",
        "HiddifyNext/4.1.1",
        "v2rayNG/1.10.0"
    };
    interface Result {
        void complete(Profile profile);
        void failed(String safeCode);
    }
    static final class Profile {
        final String config;
        final int proxies;
        final String format;
        Profile(String config, String format) throws Exception {
            this.config = config;
            JSONObject root = new JSONObject(config);
            JSONArray out = root.getJSONArray("outbounds");
            int count=0;
            for(int i=0; i<out.length();i++) {
                String t=out.getJSONObject(i).optString("type","");
                if (!"urltest".equals(t) && !"selector".equals(t))count++;
            }
            this.proxies=count;
            this.format=format;
        }
    }
    private SubscriptionManager() {}

    static void fetchAsync(Context context, AccountStore.Account account, Result callback) {
        // Fetch is permitted only after explicit account import / user update.
        new Thread(() -> {
            String error="SUBSCRIPTION_UNAVAILABLE";
            for (String agent : USER_AGENTS) {
                try {
                    String raw = request(account.subscription, agent);
                    String format = identify(raw);
                    String config=VpnConfig.parse(raw);
                    if (config.length()>2_000_000)throw new Exception("CONFIG_TOO_LARGE");
                    Profile parsed=new Profile(config,format);
                    try {
                        VpnEngineSetup.validate(context,config);
                    } catch(Exception invalidEngineConfig) {
                        // Native parser error may contain proxy credentials;
                        // never expose it in logs, UI or callback.
                        throw new Exception("VPN_CONFIG_REJECTED");
                    }
                    // Preserve last working profile if a provider returns bad data.
                    new AccountStore(context).saveVpnConfig(account.id,config);
                    callback.complete(parsed);
                    return;
                } catch(Exception failure) {
                    error=classify(failure);
                    // If a site returned a wrong format, try a second accepted
                    // format header. Connection failures will not be retried
                    // with multiple UAs, to avoid hammering a broken network.
                    if (!error.equals("SUBSCRIPTION_RETURNED_HTML") &&
                        !error.equals("NO_SUPPORTED_PROXY_NODES") &&
                        !error.equals("SUBSCRIPTION_UNKNOWN_FORMAT") &&
                        !error.startsWith("YAML_") &&
                        !error.startsWith("JSON_"))
                        break;
                }
            }
            callback.failed(error);
        },"fhg-subscription-import").start();
    }

    static Profile importLocal(Context context, String accountId, String content) throws Exception {
        String json=VpnConfig.parse(content);
        Profile profile=new Profile(json,identify(content));
        VpnEngineSetup.validate(context,json);
        new AccountStore(context).saveVpnConfig(accountId,json);
        return profile;
    }

    private static String request(String input, String agent) throws Exception {
        URI next = new URI(input.trim());
        for(int redirect=0; redirect<4; redirect++) {
            if (!"https".equalsIgnoreCase(next.getScheme()) ||
                next.getHost()==null || next.getUserInfo()!=null)
                throw new Exception("SUBSCRIPTION_HTTPS_REQUIRED");
            HttpURLConnection con=(HttpURLConnection)next.toURL().openConnection();
            con.setInstanceFollowRedirects(false);
            con.setConnectTimeout(12_000);
            con.setReadTimeout(16_000);
            con.setRequestProperty("User-Agent",agent);
            con.setRequestProperty("Accept","application/json, text/plain, application/yaml, text/yaml, */*");
            try {
                int code=con.getResponseCode();
                if(code>=300 && code<400) {
                    String location=con.getHeaderField("Location");
                    if(location==null)throw new Exception("SUBSCRIPTION_REDIRECT_MISSING");
                    next=next.resolve(location);
                    continue;
                }
                if(code==401 || code==403)throw new Exception("SUBSCRIPTION_ACCESS_DENIED");
                if(code!=200)throw new Exception("SUBSCRIPTION_HTTP_ERROR");
                try(InputStream stream=con.getInputStream()) {
                    ByteArrayOutputStream content=new ByteArrayOutputStream();
                    byte[] buf=new byte[8192];
                    int n;
                    while((n=stream.read(buf))!=-1) {
                        if(content.size()+n>MAX_BYTES) throw new Exception("SUBSCRIPTION_TOO_LARGE");
                        content.write(buf,0,n);
                    }
                    return content.toString(StandardCharsets.UTF_8.name()).replace("\uFEFF","");
                }
            } finally { con.disconnect(); }
        }
        throw new Exception("SUBSCRIPTION_TOO_MANY_REDIRECTS");
    }

    private static String identify(String raw) {
        String clean=raw.trim();
        if(clean.startsWith("{"))return "sing-box JSON";
        if(clean.startsWith("proxies:") || clean.contains("\nproxies:")) return "Clash YAML";
        if(clean.startsWith("<"))return "HTML (not a subscription)";
        if(clean.contains("vless://")||clean.contains("trojan://")||
            clean.contains("vmess://")||clean.contains("ss://")) return "V2Ray links";
        return "encoded subscription";
    }

    static String classify(Exception e) {
        String s=e.getMessage();
        if (s!=null) {
            String[] safe={
                "SUBSCRIPTION_RETURNED_HTML","SUBSCRIPTION_ACCESS_DENIED",
                "SUBSCRIPTION_TOO_LARGE","SUBSCRIPTION_HTTPS_REQUIRED",
                "SUBSCRIPTION_HTTP_ERROR","SUBSCRIPTION_TOO_MANY_REDIRECTS",
                "SUBSCRIPTION_REDIRECT_MISSING","SUBSCRIPTION_UNKNOWN_FORMAT",
                "NO_SUPPORTED_PROXY_NODES","JSON_HAS_NO_SUPPORTED_PROXY",
                "JSON_NO_OUTBOUNDS","YAML_NO_PROXIES","YAML_REMOTE_PROVIDER_UNSUPPORTED",
                "NO_SUPPORTED_YAML_NODES","VPN_CONFIG_REJECTED"
            };
            for(String code:safe)if(s.contains(code))return code;
        }
        if(e instanceof javax.net.ssl.SSLException)return "SUBSCRIPTION_TLS_ERROR";
        if(e instanceof java.net.UnknownHostException)return "SUBSCRIPTION_DNS_ERROR";
        if(e instanceof java.net.SocketTimeoutException)return "SUBSCRIPTION_TIMEOUT";
        if(e instanceof java.net.ConnectException)return "SUBSCRIPTION_CONNECTION_ERROR";
        return "SUBSCRIPTION_INVALID_FORMAT_OR_NETWORK";
    }
}
