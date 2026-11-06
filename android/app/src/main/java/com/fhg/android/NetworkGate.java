package com.fhg.android;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;

/**
 * Browser-level fail-closed gate. This is NOT a replacement for Android's
 * system VPN Lockdown. The user must enable Always-on VPN and
 * Block connections without VPN for Hiddify in Android system settings.
 * Every web session is closed when the active VPN is lost.
 */
final class NetworkGate {
    private NetworkGate() {}
    static boolean vpnActive(Context context) {
        ConnectivityManager cm = (ConnectivityManager) context.getSystemService(
                Context.CONNECTIVITY_SERVICE);
        if (cm == null) return false;
        Network network = cm.getActiveNetwork();
        if (network == null) return false;
        NetworkCapabilities caps = cm.getNetworkCapabilities(network);
        return caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
                && caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET);
    }
}
