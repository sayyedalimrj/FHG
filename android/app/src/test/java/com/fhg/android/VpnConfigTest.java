package com.fhg.android;

import org.json.JSONObject;
import org.json.JSONArray;
import org.junit.Test;
import static org.junit.Assert.*;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

public class VpnConfigTest {
    @Test public void vlessRealityUsesEncryptedDNSAndNoDirectFallback() throws Exception {
        String link="vless://123e4567-e89b-12d3-a456-426614174000@edge.example.org:443?security=reality&pbk=validPublicKey&sid=abcdef&fp=chrome&type=ws&path=%2Fws&host=cdn.example.org#Frankfurt";
        JSONObject root=new JSONObject(VpnConfig.parse(link));
        assertEquals("tun",root.getJSONArray("inbounds").getJSONObject(0).getString("type"));
        JSONObject node=root.getJSONArray("outbounds").getJSONObject(0);
        assertEquals("vless",node.getString("type"));
        assertTrue(node.getJSONObject("tls").getJSONObject("reality").getBoolean("enabled"));
        assertEquals("ws",node.getJSONObject("transport").getString("type"));
        assertTrue(root.getJSONObject("route").getBoolean("auto_detect_interface"));
        assertTrue(root.getJSONObject("route").getString("final").startsWith("node-1"));
        assertEquals(root.getJSONObject("route").getString("final"),root.getJSONObject("dns").getJSONArray("servers").getJSONObject(0).getString("detour"));
        assertFalse(root.toString().contains("direct"));
    }

    @Test public void twoTrojanNodesCreateAutomaticUrlTest() throws Exception {
        String sub="trojan://secret@a.example.org:443?security=tls#A\n"+
                "trojan://secret2@b.example.org:443?security=tls#B";
        JSONObject obj=new JSONObject(VpnConfig.parse(sub));
        JSONArray outs=obj.getJSONArray("outbounds");
        assertEquals(3,outs.length());
        assertEquals("urltest",outs.getJSONObject(2).getString("type"));
        assertEquals("fhg-proxy",obj.getJSONObject("route").getString("final"));
    }

    @Test public void selectingLocationChangesBothProxyRouteAndDnsDetour() throws Exception {
        String config=VpnConfig.parse("trojan://pass@a.example.org:443#Germany%20Frankfurt\n" +
            "trojan://pass@b.example.org:443#Finland%20Helsinki");
        java.util.List<String> tags=VpnConfig.selectableTags(config);
        assertEquals(2,tags.size());
        assertTrue(tags.get(0).contains("Germany Frankfurt"));
        assertTrue(tags.get(1).contains("Finland Helsinki"));
        String chosen=VpnConfig.withSelected(config,tags.get(1));
        JSONObject root=new JSONObject(chosen);
        assertEquals(tags.get(1),VpnConfig.selectedTag(chosen));
        assertEquals(tags.get(1),root.getJSONObject("dns").getJSONArray("servers")
            .getJSONObject(0).getString("detour"));
        assertEquals(3,root.getJSONArray("outbounds").length());
        assertFalse(root.toString().contains("direct"));
        assertEquals("fhg-proxy",VpnConfig.selectedTag(
            VpnConfig.withSelected(chosen,"fhg-proxy")));
    }

    @Test public void maliciousOrUnknownLocationCannotAlterRoute() throws Exception {
        String json=VpnConfig.parse("trojan://pass@a.example.org:443#Germany");
        try { VpnConfig.withSelected(json,"direct"); fail("direct selection must be rejected"); }
        catch(IllegalArgumentException expected) {}
        try { VpnConfig.withSelected(json,"fhg-proxy"); fail("auto absent for one server"); }
        catch(IllegalArgumentException expected) {}
    }

    @Test public void base64SubscriptionIsAccepted() throws Exception {
        String input="trojan://pw@vpn.example.org:443#Sample";
        String encoded=Base64.getEncoder().encodeToString(input.getBytes(StandardCharsets.UTF_8));
        JSONObject obj=new JSONObject(VpnConfig.parse(encoded));
        assertEquals("trojan",obj.getJSONArray("outbounds").getJSONObject(0).getString("type"));
    }

    @Test public void directConfigIsRejected() {
        String direct="{\"inbounds\":[{\"type\":\"tun\"}],\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"}],\"route\":{\"final\":\"direct\"}}";
        try { VpnConfig.parse(direct); fail("Direct fallback must be rejected"); }
        catch(Exception expected) {}
    }

    @Test public void hysteria2NowSupported() throws Exception {
        JSONObject root=new JSONObject(VpnConfig.parse(
            "hysteria2://secret@host.example.org:443?sni=example.org"));
        JSONObject node=root.getJSONArray("outbounds").getJSONObject(0);
        assertEquals("hysteria2", node.getString("type"));
        assertEquals("example.org", node.getJSONObject("tls").getString("server_name"));
    }

    @Test public void unsupportedProtocolStillFailsClosed() {
        try { VpnConfig.parse("wireguard://secret@host.example.org:443"); fail("Unsupported protocol"); }
        catch(Exception expected) {}
    }

    @Test public void clashYamlVlessRealityParses() throws Exception {
        String yaml="proxies:\n" +
            "  - name: Frankfurt\n" +
            "    type: vless\n" +
            "    server: edge.example.org\n" +
            "    port: 443\n" +
            "    uuid: 123e4567-e89b-12d3-a456-426614174000\n" +
            "    tls: true\n" +
            "    network: ws\n" +
            "    ws-opts:\n" +
            "      path: /chat\n" +
            "      headers:\n" +
            "        Host: cdn.example.org\n" +
            "    reality-opts:\n" +
            "      public-key: publickeyexample\n";
        JSONObject root = new JSONObject(VpnConfig.parse(yaml));
        JSONObject node = root.getJSONArray("outbounds").getJSONObject(0);
        assertEquals("vless",node.getString("type"));
        assertEquals("ws",node.getJSONObject("transport").getString("type"));
        assertEquals("publickeyexample",node.getJSONObject("tls")
            .getJSONObject("reality").getString("public_key"));
    }

    @Test public void singboxProfileWithoutTunIsInstalledAsProtectedTun() throws Exception {
        String input="{\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"},"+
            "{\"type\":\"trojan\",\"tag\":\"proxy\",\"server\":\"edge.example.org\","+
            "\"server_port\":443,\"password\":\"test\"}],"+
            "\"route\":{\"final\":\"direct\"}}";
        JSONObject root=new JSONObject(VpnConfig.parse(input));
        assertEquals("tun",root.getJSONArray("inbounds").getJSONObject(0).getString("type"));
        assertTrue(root.getJSONObject("route").getString("final").startsWith("node-1"));
        assertFalse(root.toString().contains("direct"));
    }

    @Test public void htmlSubscriptionRejectedClearly() {
        try { VpnConfig.parse("<!DOCTYPE html><html></html>"); fail("HTML must not be treated as a profile"); }
        catch(Exception expected) {
            assertTrue(expected.getMessage().contains("SUBSCRIPTION_RETURNED_HTML"));
        }
    }

    @Test public void noDirectHttpBootstrap() {
        try { VpnConfig.parse("http://config.example.org"); fail("HTTP scheme is not a VPN node"); }
        catch(Exception expected) {}
    }
    @Test public void emitFixtureForNativeSingboxParser() throws Exception {
        String destination=System.getenv("FHG_CONFIG_FIXTURE_DIR");
        if(destination==null||destination.isEmpty())return;
        java.nio.file.Path dir=java.nio.file.Paths.get(destination);
        java.nio.file.Files.createDirectories(dir);
        java.util.LinkedHashMap<String,String> formats=new java.util.LinkedHashMap<>();
        formats.put("trojan",
            "trojan://test-password@edge.example.org:443?security=tls&sni=edge.example.org");
        formats.put("vless",
            "vless://123e4567-e89b-12d3-a456-426614174000@edge.example.org:443?security=tls&sni=edge.example.org&type=ws&path=%2Fws");
        formats.put("hysteria2",
            "hysteria2://test-password@edge.example.org:443?sni=edge.example.org");
        formats.put("tuic",
            "tuic://123e4567-e89b-12d3-a456-426614174000:password@edge.example.org:443?sni=edge.example.org");
        formats.put("shadowsocks",
            "ss://YWVzLTEyOC1nY206dGVzdHBhc3N3b3Jk@edge.example.org:8388#test");
        formats.put("vmess",
            "vmess://"+java.util.Base64.getEncoder().encodeToString((
                "{\"add\":\"edge.example.org\",\"port\":443,"+
                "\"id\":\"123e4567-e89b-12d3-a456-426614174000\","+
                "\"net\":\"ws\",\"path\":\"/v\","+
                "\"host\":\"cdn.example.org\",\"tls\":\"tls\"}")
                    .getBytes(java.nio.charset.StandardCharsets.UTF_8)));
        formats.put("clash-yaml",String.join("\n",
            "proxies:","  - name: Frankfurt","    type: vless",
            "    server: edge.example.org","    port: 443",
            "    uuid: 123e4567-e89b-12d3-a456-426614174000",
            "    tls: true","    network: ws","    ws-opts:","      path: /chat"));
        formats.put("singbox-json",
            "{\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"},"+
            "{\"type\":\"trojan\",\"tag\":\"proxy\","+
            "\"server\":\"edge.example.org\",\"server_port\":443,"+
            "\"password\":\"test-password\"}]," +
            "\"route\":{\"final\":\"direct\"}}");
        for (java.util.Map.Entry<String,String> entry:formats.entrySet()) {
            String json=VpnConfig.parse(entry.getValue());
            java.nio.file.Files.writeString(
                dir.resolve(entry.getKey()+"-generated.json"),json);
        }
    }

}
