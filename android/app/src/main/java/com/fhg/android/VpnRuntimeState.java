package com.fhg.android;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.Binder;
import android.os.Bundle;
import android.os.Process;
import android.os.SystemClock;

/**
 * Same-UID, non-exported IPC boundary between isolated :vpn native engine
 * process and the app/UI main process. Android VpnService has its own Go JNI
 * runtime; a fatal Go panic must not terminate the browser/UI process.
 *
 * Only redacted state codes cross this boundary. These are NOT connectivity
 * credentials, and are NEVER trusted as proof of protection without the
 * Android OS VPN network owner/transport checks (see NetworkGate).
 */
public final class VpnRuntimeState extends ContentProvider {
    private static final String READ = "read";
    private static final String PUBLISH = "publish";
    private static final long HEARTBEAT_TTL_MS = 7000;
    private static volatile Bundle current = empty();
    private static volatile long lastBeat;

    private static Bundle empty() {
        Bundle b = new Bundle();
        b.putString("state","STOPPED");
        b.putString("error","");
        b.putBoolean("up",false);
        b.putBoolean("verified",false);
        return b;
    }
    private static Uri uri(Context c) {
        return Uri.parse("content://" + c.getPackageName() + ".vpnstate");
    }
    @Override public boolean onCreate() { return true; }

    @Override public Bundle call(String method, String arg, Bundle extras) {
        // Exported=false additionally prevents access from other apps;
        // verify Binder UID as defense-in-depth before accepting updates.
        if (Binder.getCallingUid() != Process.myUid()) return empty();
        if (PUBLISH.equals(method)) {
            if (extras == null) return empty();
            String state = extras.getString("state", "FAILED");
            String error = extras.getString("error", "");
            if (state.length()>35 || error.length()>70) return empty();
            Bundle copy = new Bundle();
            copy.putString("state",state);
            copy.putString("error",error);
            copy.putBoolean("up",extras.getBoolean("up",false));
            copy.putBoolean("verified",extras.getBoolean("verified",false));
            synchronized(VpnRuntimeState.class) {
                current=copy;
                lastBeat=SystemClock.elapsedRealtime();
            }
        }
        if (READ.equals(method) || PUBLISH.equals(method)) return fresh();
        return empty();
    }

    private static Bundle fresh() {
        Bundle b = new Bundle(current);
        String status=b.getString("state","STOPPED");
        long age=SystemClock.elapsedRealtime()-lastBeat;
        if (lastBeat==0 || age>HEARTBEAT_TTL_MS) {
            b.putBoolean("up",false);
            b.putBoolean("verified",false);
            if ("CONNECTED".equals(status) || "CHECKING".equals(status)
                    || "RECONNECTING".equals(status) || "STARTING".equals(status)) {
                b.putString("state","FAILED");
                b.putString("error","VPN_NATIVE_PROCESS_EXITED");
            }
        }
        return b;
    }

    static void publish(Context context,String state,String error,
                        boolean up,boolean verified) {
        Bundle payload=new Bundle();
        payload.putString("state",state);
        payload.putString("error",error==null?"":error);
        payload.putBoolean("up",up);
        payload.putBoolean("verified",verified);
        try { context.getContentResolver().call(uri(context),PUBLISH,null,payload); }
        catch (RuntimeException ignored) {
            // Never trust or expose a failed cross-process status update.
            // The UI will fail closed after the last heartbeat expires.
        }
    }

    static Bundle read(Context context) {
        try {
            Bundle result=context.getContentResolver().call(uri(context),READ,null,null);
            return result==null?empty():result;
        } catch(RuntimeException ignored) { return empty(); }
    }
    static String getState(Context context) {
        return read(context).getString("state","STOPPED");
    }
    static String getError(Context context) {
        return read(context).getString("error","");
    }
    static boolean isStarting(Context context) {
        return "STARTING".equals(getState(context));
    }
    static boolean isTunnelUp(Context context) {
        return read(context).getBoolean("up",false);
    }
    static boolean isEstablished(Context context) {
        Bundle b=read(context);
        return b.getBoolean("up",false) && b.getBoolean("verified",false);
    }

    @Override public Cursor query(Uri uri,String[] projection,String selection,
                                   String[] selectionArgs,String sortOrder){return null;}
    @Override public String getType(Uri uri){return null;}
    @Override public Uri insert(Uri uri,ContentValues values){return null;}
    @Override public int delete(Uri uri,String selection,String[] selectionArgs){return 0;}
    @Override public int update(Uri uri,ContentValues values,String selection,
                                String[] selectionArgs){return 0;}
}
