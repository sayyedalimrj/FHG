package com.fhg.android;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

import java.util.ArrayList;
import java.util.UUID;

/**
 * DEBUG-ONLY integration fixture, absent from signed release APK.
 * No user credentials or real subscription URLs are copied into tests.
 * The VPN startup runs against the exact native Go tunnel with an unreachable
 * synthetic proxy, enough to exercise TUN/NetworkInterfaces/JNI without
 * contacting any real user's server.
 */
public final class DebugVpnSmokeReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent intent) {
        if ("com.fhg.android.DEBUG_STATUS".equals(intent.getAction())) {
            android.os.Bundle state=VpnRuntimeState.read(context);
            Log.i("FHG-SMOKE","STATE="+state.getString("state","STOPPED")
                +" UP="+state.getBoolean("up",false)
                +" VERIFIED="+state.getBoolean("verified",false)
                +" ERROR="+state.getString("error",""));
            return;
        }
        if (!"com.fhg.android.DEBUG_SEED".equals(intent.getAction()))
            return;
        PendingResult pending=goAsync();
        new Thread(() -> {
            try {
                AccountStore store=new AccountStore(context);
                if (!store.load().isEmpty()) {
                    Log.w("FHG-SMOKE","REFUSING_TO_REPLACE_EXISTING_PROFILES");
                    return;
                }
                String id=UUID.randomUUID().toString();
                ArrayList<AccountStore.Account> existing=new ArrayList<>();
                existing.add(new AccountStore.Account(id,
                    "smoke@example.invalid","test-only-no-real-user","",
                    "https://example.invalid/no-network-access"));
                store.save(existing);
                store.select(id);
                String profile=VpnConfig.parse(
                    "trojan://smoke-secret@192.0.2.23:443?security=tls#Test%20Server");
                store.saveVpnConfig(id,profile);
                context.getSharedPreferences("fhg-smoke-test",Context.MODE_PRIVATE)
                    .edit().putBoolean("connect_once",true).commit();
                Log.i("FHG-SMOKE","SEEDED_SYNTHETIC_PROFILE");
            } catch(Exception failure) {
                Log.e("FHG-SMOKE","SEED_FAILED_TYPE_"+failure.getClass().getSimpleName());
            } finally {
                pending.finish();
            }
        },"fhg-test-seed").start();
    }
}
