package com.fhg.android;

import org.junit.Test;
import static org.junit.Assert.*;

import java.net.Inet6Address;
import java.net.InetAddress;

public class NetworkAddressSanitizerTest {
    @Test public void scopedWifiIpv6MustNeverReachGoPrefixParser() {
        assertEquals("fe80::1/64",
            NetworkAddressSanitizer.cidrFromHostAddress("fe80::1%wlan0",64));
        assertEquals("fe80::1/64",
            NetworkAddressSanitizer.cidrFromHostAddress("fe80::1%42",64));
        assertEquals("2001:db8::4/128",
            NetworkAddressSanitizer.cidrFromHostAddress("2001:db8::4",128));
    }

    @Test public void realInet6AddressWithScopeIsSanitized() throws Exception {
        byte[] data=InetAddress.getByName("fe80::1").getAddress();
        Inet6Address scoped=Inet6Address.getByAddress(null,data,42);
        assertTrue(scoped.getHostAddress().contains("%"));
        String cidr=NetworkAddressSanitizer.cidr(scoped,64);
        assertFalse(cidr.contains("%"));
        assertTrue(cidr.endsWith("/64"));
    }

    @Test public void ipv4IsUnchanged() throws Exception {
        assertEquals("192.0.2.3/24",
            NetworkAddressSanitizer.cidr(InetAddress.getByName("192.0.2.3"),24));
    }

    @Test public void invalidPrefixesAreRejectedBeforeNativeCode() {
        for(int bad:new int[]{-1,129,256}){
            try { NetworkAddressSanitizer.cidrFromHostAddress("fe80::1%wlan0",bad); fail(); }
            catch(IllegalArgumentException expected) {}
        }
        try { NetworkAddressSanitizer.cidrFromHostAddress("192.0.2.3",33); fail(); }
        catch(IllegalArgumentException expected) {}
    }
}
