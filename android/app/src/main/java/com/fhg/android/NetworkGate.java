package com.fhg.android;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;

/**
 * Enforces the *app's* own verified tunnel plus Android's VPN transport.
 * A VPN app may see the underlying network as its process default, so testing
 * only getActiveNetwork() would incorrectly report failure on some devices.
 * This check does not replace Always-on VPN + OS Lockdown for other apps.
 */
final class NetworkGate {
    private NetworkGate() {}

    static Network activeFhgVpnNetwork(Context context) {
        if (!VpnRuntimeState.isEstablished(context)) return null;
        return ownVpnNetwork(context);
    }

    // Available during the initial connectivity probe, before VERIFIED=true.
    static Network ownVpnNetwork(Context context) {
        ConnectivityManager cm = (ConnectivityManager)
                context.getSystemService(Context.CONNECTIVITY_SERVICE);
        if (cm == null) return null;
        try {
            for (Network n : cm.getAllNetworks()) {
                NetworkCapabilities caps = cm.getNetworkCapabilities(n);
                if (caps == null ||
                    !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN) ||
                    !caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET))
                    continue;
                // Only one Android VPN is permitted for a user profile and
                // our own libbox verifies the TUN. Owner UID can further
                // discriminate another VPN on Android 10+.
                if (android.os.Build.VERSION.SDK_INT >= 30) {
                    int owner = caps.getOwnerUid();
                    // Some OEM ROMs return INVALID_UID (-1) even for an
                    // owned VPN. Native TUN_UP + one VPN/user is still
                    // mandatory; reject any positively identified other owner.
                    if (owner >= 0 && owner != android.os.Process.myUid())
                        continue;
                }
                return n;
            }
        } catch (RuntimeException ignored) {}
        return null;
    }

    static boolean vpnActive(Context context) {
        return activeFhgVpnNetwork(context) != null;
    }
}
