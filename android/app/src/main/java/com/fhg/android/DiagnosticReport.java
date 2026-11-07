package com.fhg.android;

import android.content.Context;
import android.os.Build;

/**
 * User-triggered, LOCAL diagnostic report. No credential, email, UUID,
 * subscription URL, server address, IP, hostname, cookies or logs.
 */
final class DiagnosticReport {
    private DiagnosticReport() {}

    static String collect(Context context, boolean profilePresent) {
        String result=format(Build.VERSION.SDK_INT, Build.VERSION.RELEASE,
            VpnRuntimeState.getState(context),
            safeError(VpnRuntimeState.getError(context)),
            profilePresent, NetworkGate.vpnActive(context));
        android.content.SharedPreferences prefs=context.getSharedPreferences(
            "fhg-vpn-state",Context.MODE_PRIVATE);
        return result+"Previous startup unfinished: "+
            prefs.getBoolean("startup_pending",false)+"\n"+
            "Last startup phase: "+
            safeError(prefs.getString("last_stage","NONE"))+"\n";
    }

    static String safeError(String error) {
        if (error == null) return "NONE";
        if (!error.matches("[A-Z0-9_]{0,48}")) return "REDACTED";
        return error.isEmpty() ? "NONE" : error;
    }

    static String format(int sdk, String osVersion, String state,
                         String error, boolean profilePresent, boolean network) {
        // No arbitrary user-supplied strings appear without whitelisting.
        if (state == null || !state.matches("[A-Z_]{1,20}")) state="UNKNOWN";
        if (osVersion == null || !osVersion.matches("[0-9.]{1,20}"))
            osVersion="unknown";
        return "FHG Secure / VPN diagnostics\n"
            + "Release: 1.0.0-rc.8\n"
            + "Android API: " + sdk + "\n"
            + "Android version: " + osVersion + "\n"
            + "ABI: arm64-v8a\n"
            + "VPN state: " + state + "\n"
            + "VPN error: " + safeError(error) + "\n"
            + "Encrypted profile present: " + profilePresent + "\n"
            + "Verified VPN transport: " + network + "\n"
            + "Note: this report excludes credentials and subscription URLs.\n";
    }
}
