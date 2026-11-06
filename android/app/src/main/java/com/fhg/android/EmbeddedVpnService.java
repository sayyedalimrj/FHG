package com.fhg.android;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.LinkProperties;
import android.net.VpnService;
import android.os.Build;
import android.os.IBinder;
import android.os.ParcelFileDescriptor;
import android.util.Log;
import android.os.Process;

import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.net.NetworkInterface;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Integrated sing-box TUN runtime. ALL internet packet handling is delegated
 * to the pinned GPLv3 libbox AAR; this Java bridge does NOT implement VPN
 * cryptography or pretend that a VpnService Builder forwards packets.
 *
 * Reflection is used deliberately to isolate gomobile API compatibility and
 * fail with a visible error if any essential interface is missing.
 * No silent direct-network fallback is possible from this service.
 */
public final class EmbeddedVpnService extends VpnService {
    public static final String ACTION_CONNECT = "com.fhg.android.START_VPN";
    public static final String ACTION_DISCONNECT = "com.fhg.android.STOP_VPN";
    private static final String CHANNEL = "fhg-vpn";
    private static final int NOTIFICATION = 1702;
    private static final String TAG = "FHG-VPN";
    private static final AtomicBoolean ESTABLISHED = new AtomicBoolean(false);
    private static final AtomicBoolean STARTING = new AtomicBoolean(false);
    private Object server;
    private Object handler;
    private ParcelFileDescriptor tunnel;
    private Thread worker;
    private volatile boolean stopping;

    static boolean isEstablished() { return ESTABLISHED.get(); }
    static boolean isStarting() { return STARTING.get(); }

    private static Object invoke(Object target, String method, Object... args) throws Exception {
        Method chosen = null;
        for (Method candidate: target.getClass().getMethods()) {
            if (!candidate.getName().equals(method) ||
                candidate.getParameterCount() != args.length) continue;
            chosen = candidate; break;
        }
        if (chosen == null) throw new NoSuchMethodException(method);
        try { return chosen.invoke(target,args); }
        catch (java.lang.reflect.InvocationTargetException error) {
            Throwable cause = error.getCause();
            if (cause instanceof Exception) throw (Exception) cause;
            throw error;
        }
    }

    private static Object invokeStatic(Class<?> type, String method, Object... args) throws Exception {
        for(Method candidate:type.getMethods()) {
            if(candidate.getName().equals(method) &&
               candidate.getParameterCount()==args.length) {
                try { return candidate.invoke(null,args); }
                catch (java.lang.reflect.InvocationTargetException error) {
                    Throwable cause=error.getCause();
                    if(cause instanceof Exception) throw (Exception)cause;
                    throw error;
                }
            }
        }
        throw new NoSuchMethodException(type.getName()+"."+method);
    }

    private static int numeric(Object value) {
        return value instanceof Number ? ((Number)value).intValue() : 0;
    }

    private static String value(Object value) { return value == null ? "" : String.valueOf(value); }

    @Override public void onCreate() {
        super.onCreate();
        if (Build.VERSION.SDK_INT>=26) {
            NotificationChannel channel=new NotificationChannel(
                CHANNEL,"FHG protected VPN",NotificationManager.IMPORTANCE_LOW);
            NotificationManager nm=(NotificationManager)getSystemService(NOTIFICATION_SERVICE);
            nm.createNotificationChannel(channel);
        }
    }

    private Notification notification(String state) {
        Intent open=new Intent(this,MainActivity.class);
        PendingIntent pending=PendingIntent.getActivity(this,0,open,
            PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this,CHANNEL)
            .setContentTitle("FHG • Integrated VPN")
            .setContentText(state)
            .setSmallIcon(android.R.drawable.stat_sys_download_done)
            .setContentIntent(pending)
            .setOngoing(true)
            .build();
    }

    @Override public int onStartCommand(Intent intent,int flags,int id) {
        String action = intent==null ? ACTION_CONNECT : intent.getAction();
        if(ACTION_DISCONNECT.equals(action)){
            stopTunnel();
            stopSelf();
            return START_NOT_STICKY;
        }
        if(!ACTION_CONNECT.equals(action)) return START_NOT_STICKY;
        if(prepare(this)!=null) {
            ESTABLISHED.set(false);
            stopSelf();
            return START_NOT_STICKY;
        }
        if(!STARTING.compareAndSet(false,true)) return START_STICKY;
        stopping=false;
        ESTABLISHED.set(false);
        startForeground(NOTIFICATION,notification("Starting encrypted sing-box tunnel…"));
        worker = new Thread(this::startTunnel,"fhg-libbox-start");
        worker.start();
        return START_STICKY; // Android Always-on can restart the service
    }

    private void startTunnel() {
        try {
            AccountStore store=new AccountStore(this);
            AccountStore.Account account=store.selected();
            if(account==null) throw new Exception("No active account");
            String json=store.loadVpnConfig(account.id);
            // Reject configs that were corrupted or tampered with since import.
            VpnConfig.parse(json);
            ClassLoader loader=getClassLoader();
            Class<?> libbox=Class.forName("io.nekohasekai.libbox.Libbox",true,loader);
            Class<?> setupType=Class.forName("io.nekohasekai.libbox.SetupOptions",true,loader);
            Object setup=setupType.getConstructor().newInstance();
            invoke(setup,"setBasePath",getNoBackupFilesDir().getAbsolutePath());
            invoke(setup,"setWorkingPath",getFilesDir().getAbsolutePath());
            invoke(setup,"setTempPath",getCacheDir().getAbsolutePath());
            invokeStatic(libbox,"setup",setup);
            Class<?> platform=Class.forName("io.nekohasekai.libbox.PlatformInterface",true,loader);
            Class<?> callbacks=Class.forName("io.nekohasekai.libbox.CommandServerHandler",true,loader);
            handler=Proxy.newProxyInstance(loader,new Class<?>[]{platform,callbacks},
                (proxy, method, args) -> platformInvoke(method,args));
            Class<?> serverType=Class.forName("io.nekohasekai.libbox.CommandServer",true,loader);
            server=serverType.getConstructor(callbacks,platform).newInstance(handler,handler);
            invoke(server,"start");
            Class<?> optionsType=Class.forName("io.nekohasekai.libbox.OverrideOptions",true,loader);
            Object options=optionsType.getConstructor().newInstance();
            // Never enable autoRedirect or a per-app exception; use full-device
            // TUN and protect only actual proxy dialer sockets.
            invoke(server,"startOrReloadService",json,options);
            STARTING.set(false);
            if (tunnel == null || !ESTABLISHED.get())
                throw new IllegalStateException("sing-box did not establish TUN");
            updateNotification("VPN established • sing-box TUN active");
        } catch (Throwable failure) {
            // Exception messages may include config URLs, credentials or IP.
            // Never log them or present them in a world-readable notification.
            Log.e(TAG,"Embedded VPN unavailable: "+failure.getClass().getSimpleName());
            stopTunnel();
            stopSelf();
        } finally {
            STARTING.set(false);
        }
    }

    private void updateNotification(String state) {
        ((NotificationManager)getSystemService(NOTIFICATION_SERVICE))
            .notify(NOTIFICATION,notification(state));
    }

    private Object platformInvoke(Method method,Object[] input) throws Exception {
        String m=method.getName();
        Object[] args=input==null?new Object[0]:input;
        switch(m) {
            case "toString": return "FHG Android VPN platform";
            case "hashCode": return System.identityHashCode(this);
            case "equals": return args.length>0 && args[0]==this;
            case "usePlatformAutoDetectInterfaceControl": return true;
            case "autoDetectInterfaceControl":
                if(args.length>0 && !protect(numeric(args[0])))
                    throw new SecurityException("Dialer socket protect failed");
                return null;
            case "openTun": return openTun(args[0]);
            case "usePlatformShell":
            case "usePlatformAutoRedirect":
            case "usePlatformBridge":
            case "includeAllNetworks":
            case "underNetworkExtension": return false;
            case "clearDNSCache":
            case "registerMyInterface":
            case "closeNeighborMonitor":
            case "startNeighborMonitor":
            case "startDefaultInterfaceMonitor":
            case "closeDefaultInterfaceMonitor":
            case "sendNotification":
            case "cancelNotification":
            case "writeDebugMessage": return null;
            case "getInterfaces":
                return interfaceIterator(method.getReturnType());
            case "readWIFIState":
            case "localDNSTransport":
            case "getSystemProxyStatus":
            case "lookupUser":
            case "lookupFSSHHostKey":
            case "lookupFSSHHostKeys":
                return null;
            case "underNetworkExtension2": return false;
            case "findConnectionOwner":
                return null;
            case "usePlatformAutoDetectInterfaceControlV2": return true;
            case "setSystemProxyEnabled": return null;
            case "connectSSHAgent":
            case "triggerNativeCrash": return -1;
            case "serviceReload": return null;
            case "serviceStop":
                stopSelf();
                return null;
            default:
                if(method.getReturnType()==Boolean.TYPE) return false;
                if(method.getReturnType()==Integer.TYPE) return 0;
                if(method.getReturnType()==Long.TYPE) return 0L;
                return null;
        }
    }

    /**
     * Android network interface snapshots for sing-box interface selection.
     * The native library decides the preferred outgoing interface. The
     * strict TUN rule and protected sockets remain mandatory.
     */
    private Object interfaceIterator(Class<?> iteratorClass) throws Exception {
        List<Object> values=new ArrayList<>();
        Enumeration<NetworkInterface> all=NetworkInterface.getNetworkInterfaces();
        Class<?> classType=Class.forName("io.nekohasekai.libbox.NetworkInterface");
        while(all!=null && all.hasMoreElements()) {
            NetworkInterface ni=all.nextElement();
            if(ni.isLoopback() || !ni.isUp())continue;
            Object data=classType.getConstructor().newInstance();
            invoke(data,"setName",ni.getName());
            invoke(data,"setIndex",ni.getIndex());
            try { invoke(data,"setMtu",ni.getMTU()); } catch(Exception ignored){}
            values.add(data);
        }
        final int[] offset={0};
        return Proxy.newProxyInstance(getClassLoader(),new Class<?>[]{iteratorClass},
            (proxy,method,args)->{
                if(method.getName().equals("hasNext"))return offset[0]<values.size();
                if(method.getName().equals("next"))return values.get(offset[0]++);
                if(method.getName().equals("len"))return values.size();
                return null;
            });
    }

    private int openTun(Object options) throws Exception {
        if(prepare(this)!=null)throw new SecurityException("VPN permission missing");
        Builder builder=new Builder().setSession("FHG sing-box");
        int mtu=numeric(invoke(options,"getMtu"));
        builder.setMtu(mtu>=1280&&mtu<=9000?mtu:1500);
        int ipv4=addAddresses(builder,invoke(options,"getInet4Address"));
        int ipv6=addAddresses(builder,invoke(options,"getInet6Address"));
        if(ipv4==0 && ipv6==0)
            throw new IllegalStateException("sing-box TUN has no address");
        // No allowBypass(), no disallowed applications and NO direct fallback.
        builder.addRoute("0.0.0.0",0);
        if(ipv6>0) builder.addRoute("::",0);
        try {
            Object dns=invoke(options,"getDnsServerAddress");
            while((Boolean)invoke(dns,"hasNext"))
                builder.addDnsServer(value(invoke(dns,"next")));
        } catch(Exception ignored) {
            builder.addDnsServer("1.1.1.1");
        }
        tunnel=builder.establish();
        if(tunnel==null)throw new IllegalStateException("Android rejected TUN");
        ESTABLISHED.set(true);
        return tunnel.getFd();
    }

    private int addAddresses(Builder builder,Object iterator) throws Exception {
        int count=0;
        while((Boolean)invoke(iterator,"hasNext")) {
            Object cidr=invoke(iterator,"next");
            String ip=value(invoke(cidr,"getAddress"));
            int prefix=numeric(invoke(cidr,"getPrefix"));
            builder.addAddress(ip,prefix);
            count++;
        }
        return count;
    }

    private synchronized void stopTunnel() {
        if(stopping) return;
        stopping=true;
        ESTABLISHED.set(false);
        STARTING.set(false);
        if(server!=null) {
            try { invoke(server,"closeService"); } catch(Exception ignored){}
            try { invoke(server,"close"); } catch(Exception ignored){}
            server=null;
        }
        if(tunnel!=null) {
            try { tunnel.close(); } catch(Exception ignored){}
            tunnel=null;
        }
        stopForeground(STOP_FOREGROUND_REMOVE);
    }

    @Override public void onRevoke() {
        stopTunnel();
        stopSelf();
        super.onRevoke();
    }

    @Override public void onDestroy() {
        stopTunnel();
        super.onDestroy();
    }

    @Override public IBinder onBind(Intent intent) {
        return super.onBind(intent);
    }
}
