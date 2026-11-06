package com.fhg.android;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.net.ConnectivityManager;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.VpnService;
import android.os.Build;
import android.os.IBinder;
import android.os.ParcelFileDescriptor;
import android.system.OsConstants;
import android.util.Log;

import java.net.HttpURLConnection;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.net.URL;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

import io.nekohasekai.libbox.BridgeOptions;
import io.nekohasekai.libbox.BridgeSession;
import io.nekohasekai.libbox.CommandServer;
import io.nekohasekai.libbox.CommandServerHandler;
import io.nekohasekai.libbox.ConnectionOwner;
import io.nekohasekai.libbox.InterfaceUpdateListener;
import io.nekohasekai.libbox.Libbox;
import io.nekohasekai.libbox.LocalDNSTransport;
import io.nekohasekai.libbox.NeighborUpdateListener;
import io.nekohasekai.libbox.NetworkInterfaceIterator;
import io.nekohasekai.libbox.OverrideOptions;
import io.nekohasekai.libbox.PlatformInterface;
import io.nekohasekai.libbox.PlatformUser;
import io.nekohasekai.libbox.RoutePrefix;
import io.nekohasekai.libbox.RoutePrefixIterator;
import io.nekohasekai.libbox.SetupOptions;
import io.nekohasekai.libbox.ShellSession;
import io.nekohasekai.libbox.StringIterator;
import io.nekohasekai.libbox.SystemProxyStatus;
import io.nekohasekai.libbox.TunOptions;
import io.nekohasekai.libbox.WIFIState;

/**
 * Actual, strongly-typed libbox 1.14.4 bridge. It MUST compile against the
 * pinned AAR's Java interfaces. Never fake an established VPN or silently
 * fall back to a direct route when libbox fails.
 *
 * Native TUN establishment proves only a VPN interface exists, NOT server
 * reachability. Browser access is gated separately by a tunneled HTTPS test.
 */
public final class EmbeddedVpnService extends VpnService
        implements PlatformInterface, CommandServerHandler {
    public static final String ACTION_CONNECT = "com.fhg.android.START_VPN";
    public static final String ACTION_DISCONNECT = "com.fhg.android.STOP_VPN";
    private static final String CHANNEL = "fhg-vpn";
    private static final int NOTIFICATION_ID = 1702;
    private static final AtomicBoolean TUN_UP = new AtomicBoolean(false);
    private static final AtomicBoolean VERIFIED = new AtomicBoolean(false);
    private static final AtomicBoolean STARTING = new AtomicBoolean(false);
    private static volatile String state = "STOPPED";
    private static volatile String error = "";
    private static final String TAG = "FHG-VPN";

    private CommandServer commandServer;
    private ParcelFileDescriptor tunFd;
    private volatile boolean stopping;
    private ConnectivityManager cm;
    private ConnectivityManager.NetworkCallback networkCallback;
    private InterfaceUpdateListener interfaceListener;
    private Thread startWorker;
    private Thread verifyWorker;

    public static boolean isEstablished() { return VERIFIED.get() && TUN_UP.get(); }
    public static boolean isStarting() { return STARTING.get(); }
    public static String getState() { return state; }
    public static String getError() { return error; }

    private void status(String newState, String newError) {
        state = newState;
        error = newError;
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationManager nm = getSystemService(NotificationManager.class);
            if (nm != null) nm.notify(NOTIFICATION_ID, notification(newState));
        }
    }

    @Override public void onCreate() {
        super.onCreate();
        cm = (ConnectivityManager)getSystemService(CONNECTIVITY_SERVICE);
        NotificationManager nm = getSystemService(NotificationManager.class);
        if (nm != null && Build.VERSION.SDK_INT >= 26)
            nm.createNotificationChannel(new NotificationChannel(
                    CHANNEL, "FHG protected tunnel", NotificationManager.IMPORTANCE_LOW));
    }

    private Notification notification(String current) {
        PendingIntent open = PendingIntent.getActivity(this, 0,
            new Intent(this, MainActivity.class),
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder n = Build.VERSION.SDK_INT >= 26
            ? new Notification.Builder(this, CHANNEL)
            : new Notification.Builder(this);
        return n.setContentTitle("FHG VPN")
            .setContentText(current.equals("CONNECTED") ? "Protected tunnel verified" :
                current.equals("STARTING") ? "Starting VPN tunnel" :
                current.equals("CHECKING") ? "Verifying proxy connectivity" :
                "VPN disconnected / not ready")
            .setSmallIcon(android.R.drawable.stat_sys_download_done)
            .setOngoing(true).setContentIntent(open).build();
    }

    @Override public int onStartCommand(Intent intent, int flags, int id) {
        String action = intent == null ? ACTION_CONNECT : intent.getAction();
        if (ACTION_DISCONNECT.equals(action)) {
            stopTunnel(); stopSelf(); return START_NOT_STICKY;
        }
        if (!ACTION_CONNECT.equals(action) && action != null) return START_NOT_STICKY;
        if (VpnService.prepare(this) != null) {
            status("FAILED", "VPN_PERMISSION_REQUIRED");
            stopSelf(); return START_NOT_STICKY;
        }
        if (!STARTING.compareAndSet(false, true)) return START_STICKY;
        VERIFIED.set(false);
        TUN_UP.set(false);
        stopping = false;
        try {
            startForeground(NOTIFICATION_ID, notification("STARTING"));
            status("STARTING", "");
            startWorker = new Thread(this::runEngine, "fhg-libbox-tunnel");
            startWorker.start();
        } catch (RuntimeException e) {
            STARTING.set(false); status("FAILED", "FOREGROUND_SERVICE_ERROR");
            stopSelf(); return START_NOT_STICKY;
        }
        return START_STICKY;
    }

    private void runEngine() {
        try {
            AccountStore.Account active = new AccountStore(this).selected();
            if (active == null) throw new IllegalStateException("NO_ACTIVE_ACCOUNT");
            String config = new AccountStore(this).loadVpnConfig(active.id);

            // Process-wide libbox setup is shared with offline profile
            // validation, so reconnect cannot reinitialize its Go runtime.
            VpnEngineSetup.validate(this, config);
            commandServer = new CommandServer(this, this);
            commandServer.start();
            OverrideOptions options = new OverrideOptions();
            options.setAutoRedirect(false);
            commandServer.startOrReloadService(config, options);
            if (!TUN_UP.get()) throw new IllegalStateException("TUN_NOT_ESTABLISHED");
            STARTING.set(false);
            status("CHECKING", "");
            verifyWorker = new Thread(this::verifyConnectivity, "fhg-tunnel-health");
            verifyWorker.start();
        } catch (Throwable failure) {
            // Do NOT log the exception message, configs may contain secrets.
            String category = "LIBBOX_START_ERROR";
            if (failure instanceof java.io.FileNotFoundException)
                category = "PROFILE_MISSING";
            else if (failure instanceof IllegalStateException &&
                    "TUN_NOT_ESTABLISHED".equals(failure.getMessage()))
                category = "TUN_NOT_ESTABLISHED";
            Log.e(TAG, category + " / " + failure.getClass().getSimpleName());
            status("FAILED", category);
            stopTunnel();
            stopSelf();
        } finally {
            STARTING.set(false);
        }
    }

    private void verifyConnectivity() {
        // An established TUN by itself is not evidence of a usable proxy.
        // The HTTPS probe goes through normal Android routing, never protect().
        int attempts = 0;
        while (!stopping && attempts++ < 12) {
            HttpURLConnection connection = null;
            try {
                Thread.sleep(1800);
                if (!TUN_UP.get()) return;
                connection = (HttpURLConnection)new URL(
                    "https://cp.cloudflare.com/generate_204").openConnection();
                connection.setConnectTimeout(6500);
                connection.setReadTimeout(6500);
                connection.setUseCaches(false);
                connection.setInstanceFollowRedirects(false);
                int response = connection.getResponseCode();
                if (response == 204 && !stopping && TUN_UP.get()) {
                    VERIFIED.set(true);
                    status("CONNECTED", "");
                    return;
                }
            } catch (Exception ignored) {
                // Report only that the server cannot yet be reached.
            } finally {
                if (connection != null) connection.disconnect();
            }
            status("CHECKING", "PROXY_NOT_REACHABLE");
        }
        if (!stopping) {
            VERIFIED.set(false);
            status("FAILED", "PROXY_NOT_REACHABLE");
            stopTunnel();
            stopSelf();
        }
    }

    @Override public void autoDetectInterfaceControl(int fd) throws Exception {
        if (!protect(fd)) throw new SecurityException("PROXY_SOCKET_NOT_PROTECTED");
    }
    @Override public boolean usePlatformAutoDetectInterfaceControl() { return true; }
    @Override public boolean includeAllNetworks() { return false; }
    @Override public boolean underNetworkExtension() { return false; }
    @Override public boolean usePlatformBridge() { return false; }
    @Override public boolean usePlatformShell() { return false; }

    @Override public int openTun(TunOptions options) throws Exception {
        if (VpnService.prepare(this) != null)
            throw new SecurityException("VPN_PERMISSION_REQUIRED");
        Builder builder = new Builder().setSession("FHG protected VPN");
        int mtu = options.getMTU();
        builder.setMtu(mtu >= 1280 && mtu <= 9000 ? mtu : 1500);
        int ipv4 = putAddresses(builder, options.getInet4Address());
        int ipv6 = putAddresses(builder, options.getInet6Address());
        if (ipv4 == 0 && ipv6 == 0)
            throw new IllegalStateException("INVALID_TUN_ADDRESSES");
        if (options.getAutoRoute()) {
            // Full tunnel. Do not allowBypass or exclude any package.
            if (ipv4 > 0) builder.addRoute("0.0.0.0", 0);
            if (ipv6 > 0) builder.addRoute("::", 0);
        }
        if (!Libbox.DNSModeDisabled.equals(options.getDNSMode().getValue())) {
            StringIterator dns = options.getDNSServerAddress();
            int count = 0;
            while (dns != null && dns.hasNext()) {
                String address = dns.next();
                if (!address.isEmpty()) { builder.addDnsServer(address); count++; }
            }
            if (count == 0) builder.addDnsServer("1.1.1.1");
        }
        // No proxy or bypass exclusions. Android Lockdown can enforce device
        // wide blocking whenever the service isn't connected.
        tunFd = builder.establish();
        if (tunFd == null) throw new IllegalStateException("ANDROID_TUN_REJECTED");
        TUN_UP.set(true);
        return tunFd.getFd();
    }

    private int putAddresses(Builder builder, RoutePrefixIterator routes) {
        int count = 0;
        while (routes != null && routes.hasNext()) {
            RoutePrefix route = routes.next();
            builder.addAddress(route.address(), route.prefix());
            count++;
        }
        return count;
    }

    // Interface detection uses physical underlying Wi-Fi/Cellular networks,
    // not our VPN interface, so sing-box cannot recursively dial itself.
    @Override public NetworkInterfaceIterator getInterfaces() throws Exception {
        List<io.nekohasekai.libbox.NetworkInterface> entries = new ArrayList<>();
        if (cm != null) for (Network network : cm.getAllNetworks()) {
            NetworkCapabilities cap = cm.getNetworkCapabilities(network);
            LinkProperties props = cm.getLinkProperties(network);
            if (cap == null || props == null ||
                cap.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) continue;
            String name = props.getInterfaceName();
            if (name == null || name.isEmpty()) continue;
            NetworkInterface physical = NetworkInterface.getByName(name);
            if (physical == null || !physical.isUp()) continue;
            io.nekohasekai.libbox.NetworkInterface info =
                new io.nekohasekai.libbox.NetworkInterface();
            info.setName(name);
            info.setIndex(physical.getIndex());
            info.setMTU(physical.getMTU());
            info.setFlags(OsConstants.IFF_UP | OsConstants.IFF_RUNNING);
            info.setType(cap.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
                ? Libbox.InterfaceTypeWIFI
                : cap.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)
                    ? Libbox.InterfaceTypeCellular : Libbox.InterfaceTypeOther);
            info.setMetered(!cap.hasCapability(
                NetworkCapabilities.NET_CAPABILITY_NOT_METERED));
            List<String> addresses = new ArrayList<>();
            for (java.net.InterfaceAddress ia : physical.getInterfaceAddresses()) {
                if (ia.getAddress() != null)
                    addresses.add(ia.getAddress().getHostAddress() + "/" + ia.getNetworkPrefixLength());
            }
            info.setAddresses(strings(addresses));
            List<String> dns = new ArrayList<>();
            for (InetAddress addr : props.getDnsServers()) dns.add(addr.getHostAddress());
            info.setDNSServer(strings(dns));
            List<String> gateways = new ArrayList<>();
            for (android.net.RouteInfo route : props.getRoutes()) {
                if (route.isDefaultRoute() && route.getGateway() != null)
                    gateways.add(route.getGateway().getHostAddress());
            }
            info.setGateway(strings(gateways));
            entries.add(info);
        }
        return new NetworkInterfaceIterator() {
            int index = 0;
            @Override public boolean hasNext() { return index < entries.size(); }
            @Override public io.nekohasekai.libbox.NetworkInterface next() { return entries.get(index++); }
        };
    }

    private static StringIterator strings(List<String> entries) {
        return new StringIterator() {
            int pos = 0;
            @Override public boolean hasNext() { return pos < entries.size(); }
            @Override public String next() { return entries.get(pos++); }
            @Override public int len() { return entries.size(); }
        };
    }

    @Override public void startDefaultInterfaceMonitor(InterfaceUpdateListener listener) {
        interfaceListener = listener;
        if (cm == null) return;
        networkCallback = new ConnectivityManager.NetworkCallback() {
            @Override public void onAvailable(Network n) { notifyUnderlying(); }
            @Override public void onLost(Network n) { notifyUnderlying(); }
            @Override public void onCapabilitiesChanged(Network n, NetworkCapabilities c) { notifyUnderlying(); }
        };
        cm.registerDefaultNetworkCallback(networkCallback);
        notifyUnderlying();
    }

    private void notifyUnderlying() {
        InterfaceUpdateListener listener = interfaceListener;
        if (listener == null || cm == null) return;
        for (Network n : cm.getAllNetworks()) {
            NetworkCapabilities c = cm.getNetworkCapabilities(n);
            LinkProperties p = cm.getLinkProperties(n);
            if (c == null || p == null || c.hasTransport(NetworkCapabilities.TRANSPORT_VPN))
                continue;
            try {
                NetworkInterface ni = NetworkInterface.getByName(p.getInterfaceName());
                if (ni != null && ni.isUp()) {
                    listener.updateDefaultInterface(p.getInterfaceName(), ni.getIndex(),
                            !c.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED),
                            false);
                    return;
                }
            } catch (Exception ignored) { }
        }
    }

    @Override public void closeDefaultInterfaceMonitor(InterfaceUpdateListener listener) {
        if (cm != null && networkCallback != null)
            try { cm.unregisterNetworkCallback(networkCallback); } catch (Exception ignored) {}
        networkCallback = null;
        interfaceListener = null;
    }

    @Override public void clearDNSCache() {}
    @Override public void registerMyInterface(String name) {}
    @Override public void sendNotification(io.nekohasekai.libbox.Notification notification) {}
    @Override public void cancelNotification(String identifier, int typeId) {}
    @Override public void startNeighborMonitor(NeighborUpdateListener listener) {}
    @Override public void closeNeighborMonitor(NeighborUpdateListener listener) {}
    @Override public LocalDNSTransport localDNSTransport() { return null; }
    @Override public WIFIState readWIFIState() { return null; }
    @Override public ConnectionOwner findConnectionOwner(int protocol, String sourceIp,
            int sourcePort, String destIp, int destPort) { return null; }
    @Override public void checkPlatformShell() { throw new UnsupportedOperationException("ROOT_DISABLED"); }
    @Override public ShellSession openShellSession(PlatformUser user, String cmd,
            StringIterator environment, String term, int rows, int cols) {
        throw new UnsupportedOperationException("SHELL_DISABLED");
    }
    @Override public BridgeSession createBridge(BridgeOptions options) {
        throw new UnsupportedOperationException("BRIDGE_DISABLED");
    }
    @Override public PlatformUser lookupUser(String id) { return null; }
    @Override public String lookupSFTPServer() { return ""; }
    @Override public String readSystemSSHHostKey() { return ""; }
    @Override public String tailscaleHostname() { return ""; }

    // CommandServerHandler methods: backend never silently toggles a system
    // proxy or gets a shell without the user's explicit permission.
    @Override public int connectSSHAgent() { return -1; }
    @Override public SystemProxyStatus getSystemProxyStatus() { return null; }
    @Override public void setSystemProxyEnabled(boolean value) {}
    @Override public void serviceReload() {}
    @Override public void serviceStop() { stopTunnel(); stopSelf(); }
    @Override public void triggerNativeCrash() {}
    @Override public void writeDebugMessage(String message) {}

    private synchronized void stopTunnel() {
        if (stopping) return;
        stopping = true;
        VERIFIED.set(false);
        TUN_UP.set(false);
        STARTING.set(false);
        closeDefaultInterfaceMonitor(interfaceListener);
        if (commandServer != null) {
            try { commandServer.closeService(); } catch (Exception ignored) {}
            try { commandServer.close(); } catch (Exception ignored) {}
            commandServer = null;
        }
        if (tunFd != null) {
            try { tunFd.close(); } catch (Exception ignored) {}
            tunFd = null;
        }
        if (!"FAILED".equals(state)) status("STOPPED", "");
        stopForeground(STOP_FOREGROUND_REMOVE);
    }

    @Override public void onRevoke() { stopTunnel(); stopSelf(); super.onRevoke(); }
    @Override public void onDestroy() { stopTunnel(); super.onDestroy(); }

    @Override public IBinder onBind(Intent intent) { return super.onBind(intent); }
}
