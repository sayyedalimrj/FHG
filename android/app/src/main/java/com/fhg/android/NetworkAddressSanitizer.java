package com.fhg.android;

import java.net.InetAddress;

/**
 * Go libbox calls netip.MustParsePrefix on every platform network-interface
 * address. Android Inet6Address.getHostAddress() may append "%wlan0" (or
 * another scoped zone), which is valid as a host address but NOT in netip
 * prefixes. Passing it to libbox causes an uncaught Go panic / SIGABRT.
 *
 * This boundary converts the Android interface address to a strict, unscoped
 * CIDR prefix before it ever crosses the JNI/gomobile bridge.
 */
final class NetworkAddressSanitizer {
    private NetworkAddressSanitizer() {}

    static String cidr(InetAddress address, int prefixLength) {
        if (address == null) throw new IllegalArgumentException("Missing interface address");
        return cidrFromHostAddress(address.getHostAddress(), prefixLength);
    }

    static String cidrFromHostAddress(String host, int prefixLength) {
        if (host == null || host.isEmpty())
            throw new IllegalArgumentException("Empty interface address");
        // Scoped IPv6 link-local values such as fe80::1%wlan0/64 cannot be
        // parsed by Go netip.MustParsePrefix. Keep only the address.
        int scope = host.indexOf('%');
        if (scope >= 0) host = host.substring(0, scope);
        final boolean ipv6 = host.indexOf(':') >= 0;
        final int max = ipv6 ? 128 : 32;
        if (host.isEmpty() || prefixLength < 0 || prefixLength > max)
            throw new IllegalArgumentException("Invalid interface prefix length");
        return host + "/" + prefixLength;
    }
}
