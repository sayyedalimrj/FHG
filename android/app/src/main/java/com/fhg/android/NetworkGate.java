package com.fhg.android;

import android.content.Context;
import android.content.pm.PackageManager;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;

import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * External official Hiddify VPN gate.
 *
 * Unlike RC1-RC8, FHG does NOT create a TUN, use libbox, or start a custom
 * Android VpnService. Hiddify owns connection startup and network switching.
 * We never call a normal app process URLConnection for a protected probe.
 *
 * Browser stays fail-closed until HTTPS 204 succeeds over the exact active
 * VPN Network. A route change, connection loss or stale probe invalidates
 * the grant. Android Lockdown is still user's OS-level responsibility.
 */
final class NetworkGate {
    private static final long FRESH_MS=18_000;
    private static final long PROBE_EVERY_MS=6_000;
    private static final AtomicBoolean IN_FLIGHT=new AtomicBoolean(false);
    private static volatile Network verifiedNetwork;
    private static volatile long verifiedAt;
    private static volatile long lastAttempt;
    private static volatile String phase="WAITING_FOR_HIDDIFY";
    private static final Handler MAIN=new Handler(Looper.getMainLooper());

    private NetworkGate() {}

    private static Network ownVpnNetwork(Context context) {
        try {
            ConnectivityManager manager=(ConnectivityManager)
                context.getSystemService(Context.CONNECTIVITY_SERVICE);
            if(manager==null)return null;
            Network net=manager.getActiveNetwork();
            if(net==null)return null;
            NetworkCapabilities caps=manager.getNetworkCapabilities(net);
            if(caps==null ||
                !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN) ||
                !caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET))
                return null;
            if(Build.VERSION.SDK_INT>=29) {
                int owner=caps.getOwnerUid();
                if(owner>=0) {
                    int trustedUid;
                    try {
                        trustedUid=context.getPackageManager()
                            .getPackageUid(HiddifyBridge.PACKAGE,0);
                    } catch(PackageManager.NameNotFoundException unknown) {
                        return null;
                    }
                    if(owner!=trustedUid)return null;
                } else if(!HiddifyBridge.installed(context)) {
                    // Some OEM ROMs do not populate getOwnerUid(). Require at
                    // least an installed official package and a real OS VPN.
                    return null;
                }
            } else if(!HiddifyBridge.installed(context))return null;
            return net;
        }catch(RuntimeException unavailable){return null;}
    }

    static boolean vpnActive(Context context) {
        Network current=ownVpnNetwork(context);
        if(current==null) {
            verifiedNetwork=null;verifiedAt=0;
            phase="VPN_NOT_ACTIVE";
            return false;
        }
        if(!current.equals(verifiedNetwork)) {
            verifiedNetwork=null;
            verifiedAt=0;
        }
        schedule(context,null);
        return verifiedNetwork!=null && current.equals(verifiedNetwork)
            && android.os.SystemClock.elapsedRealtime()-verifiedAt<FRESH_MS;
    }

    static String status(Context context) {
        if(vpnActive(context))return "VERIFIED";
        return phase;
    }

    static void schedule(Context context,Runnable onFinish) {
        Context app=context.getApplicationContext();
        long now=android.os.SystemClock.elapsedRealtime();
        if(onFinish==null && now-lastAttempt<PROBE_EVERY_MS)return;
        if(!IN_FLIGHT.compareAndSet(false,true))return;
        lastAttempt=now;
        new Thread(()->{
            boolean ok=false;
            Network network=ownVpnNetwork(app);
            if(network!=null)for(String endpoint:new String[]{
                "https://cp.cloudflare.com/generate_204",
                "https://www.gstatic.com/generate_204"}) {
                HttpURLConnection con=null;
                try {
                    con=(HttpURLConnection)network.openConnection(new URL(endpoint));
                    con.setConnectTimeout(4000);
                    con.setReadTimeout(4000);
                    con.setUseCaches(false);
                    con.setInstanceFollowRedirects(false);
                    if(con.getResponseCode()==204) { ok=true;break; }
                }catch(Exception ignored) {
                    // No proxy URLs or credentials in logs.
                }finally { if(con!=null)con.disconnect(); }
            }
            Network current=ownVpnNetwork(app);
            if(ok && network!=null && network.equals(current)) {
                verifiedNetwork=network;
                verifiedAt=android.os.SystemClock.elapsedRealtime();
                phase="VERIFIED";
            }else {
                verifiedAt=0;verifiedNetwork=null;
                phase=network==null?"VPN_NOT_ACTIVE":"VPN_PROBE_FAILED";
            }
            IN_FLIGHT.set(false);
            if(onFinish!=null) MAIN.post(onFinish);
        },"fhg-hiddify-network-probe").start();
    }

    static void reset() {verifiedAt=0;verifiedNetwork=null;phase="WAITING_FOR_HIDDIFY";}
}
