package com.fhg.android;

import org.json.JSONArray;
import org.json.JSONObject;
import java.net.URI;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.LinkedHashMap;

/**
 * A deliberately narrow, fail-closed sing-box subscription adapter.
 * Supports raw sing-box JSON and commonly-used Hiddify-style VLESS,
 * Trojan, VMess and Shadowsocks links; unsupported protocols are
 * reported rather than silently routed DIRECT.
 *
 * Import is explicitly initiated by the user and contains secret proxy
 * credentials. The resulting JSON is stored encrypted by AccountStore.
 */
final class VpnConfig {
    private VpnConfig() {}

    private static final int MAX_NODES = 64;
    private static final int MAX_CHARS = 1000000;
    private static final String PROBE = "https://cp.cloudflare.com/";

    private static String decode(String value) {
        try {
            // URLDecoder.decode(String, Charset) was added to Android API 33.
            // This legacy overload works all the way back to our API 26 floor.
            return URLDecoder.decode(value.replace("+", "%2B"), "UTF-8");
        } catch (java.io.UnsupportedEncodingException impossible) {
            throw new IllegalStateException("UTF-8 unavailable");
        }
    }

    private static Map<String,String> params(String query) {
        Map<String,String> fields = new LinkedHashMap<>();
        if (query == null) return fields;
        for (String pair : query.split("&")) {
            String[] kv = pair.split("=",2);
            if (kv.length == 2) fields.put(decode(kv[0]),decode(kv[1]));
        }
        return fields;
    }

    private static String base64(String value) {
        String s = value.replaceAll("\\s+", "");
        try {
            byte[] b = Base64.getUrlDecoder().decode(s);
            return new String(b,StandardCharsets.UTF_8);
        } catch (IllegalArgumentException e) {
            byte[] b = Base64.getDecoder().decode(s);
            return new String(b,StandardCharsets.UTF_8);
        }
    }

    private static void validEndpoint(String address, int port) {
        if (address == null || address.length() < 1 || address.length() > 255 ||
            address.contains("/") || address.contains("@") || port < 1 || port > 65535)
            throw new IllegalArgumentException("Invalid outbound endpoint");
    }

    static String parse(String original) throws Exception {
        if (original == null || original.length() > MAX_CHARS || original.isBlank())
            throw new Exception("EMPTY_OR_OVERSIZED_SUBSCRIPTION");
        String clean = original.trim().replace("\\r\\n","\\n");
        if (clean.startsWith("<!DOCTYPE") || clean.startsWith("<html") ||
            clean.startsWith("<HTML") || clean.startsWith("<!doctype"))
            throw new Exception("SUBSCRIPTION_RETURNED_HTML");
        if (clean.startsWith("{")) return parseJsonSubscription(clean);
        if (clean.startsWith("proxies:") || clean.contains("\\nproxies:"))
            return build(ClashSubscription.parse(clean));
        // Base64 response is typical of V2Ray/Hiddify panel subscriptions.
        if (!clean.contains("://") && !clean.contains("proxies:")) {
            try { clean = base64(clean).trim(); }
            catch (Exception e) { throw new Exception("SUBSCRIPTION_UNKNOWN_FORMAT"); }
        }
        if (clean.startsWith("{")) return parseJsonSubscription(clean);
        if (clean.startsWith("proxies:") || clean.contains("\\nproxies:"))
            return build(ClashSubscription.parse(clean));
        List<JSONObject> outbounds = new ArrayList<>();
        int row = 0;
        for (String line : clean.split("\\n")) {
            String link = line.trim();
            if (link.isEmpty() || link.startsWith("#")) continue;
            if (++row > MAX_NODES) break;
            JSONObject outbound;
            try {
                if (link.startsWith("vless://")) outbound = parseVless(link);
                else if (link.startsWith("trojan://")) outbound = parseTrojan(link);
                else if (link.startsWith("vmess://")) outbound = parseVmess(link);
                else if (link.startsWith("ss://")) outbound = parseShadowsocks(link);
                else if (link.startsWith("hy2://") || link.startsWith("hysteria2://"))
                    outbound = parseHysteria2(link);
                else if (link.startsWith("tuic://")) outbound = parseTuic(link);
                else continue;
            } catch (Exception unsupportedNode) {
                // One bad link must not discard every valid proxy in a provider's
                // subscription. If none work, the importer reports an error.
                continue;
            }
            // Keep provider-advertised location names for the server picker.
            // These are display labels, never URLs or credentials.
            int hash=link.indexOf('#');
            if (hash>=0 && hash+1<link.length()) {
                try { outbound.put("tag",decode(link.substring(hash+1))); }
                catch (RuntimeException ignored) {}
            }
            outbounds.add(outbound);
        }
        if (outbounds.isEmpty())
            throw new Exception("NO_SUPPORTED_PROXY_NODES");
        return build(outbounds);
    }

    private static String parseJsonSubscription(String text) throws Exception {
        JSONObject root = new JSONObject(text);
        JSONArray out = root.optJSONArray("outbounds");
        if (out == null) throw new Exception("JSON_NO_OUTBOUNDS");
        List<JSONObject> accepted = new ArrayList<>();
        for (int i=0; i<out.length() && accepted.size()<MAX_NODES; i++) {
            JSONObject item=out.optJSONObject(i);
            if (item==null)continue;
            String type=item.optString("type","");
            if (!("vless".equals(type) || "vmess".equals(type) ||
                "trojan".equals(type) || "shadowsocks".equals(type) ||
                "hysteria2".equals(type) || "tuic".equals(type)))
                continue;
            JSONObject copy=new JSONObject(item.toString());
            copy.put("tag","node-"+(accepted.size()+1));
            accepted.add(copy);
        }
        // Never trust provider route rules, direct outbounds or bypass policies.
        // Keep only recognized secure upstreams and construct a clean TUN.
        if (accepted.isEmpty()) throw new Exception("JSON_HAS_NO_SUPPORTED_PROXY");
        return build(accepted);
    }

    private static JSONObject parseHysteria2(String link) throws Exception {
        URI u = new URI(link.replaceFirst("^hy2:", "hysteria2:"));
        validEndpoint(u.getHost(), u.getPort());
        Map<String,String> q=params(u.getRawQuery());
        JSONObject out=new JSONObject().put("type","hysteria2")
            .put("server",u.getHost()).put("server_port",u.getPort())
            .put("password",decode(u.getRawUserInfo()));
        out.put("tls",new JSONObject().put("enabled",true)
            .put("server_name",q.getOrDefault("sni",u.getHost())));
        return out;
    }

    private static JSONObject parseTuic(String link) throws Exception {
        URI u=new URI(link);
        validEndpoint(u.getHost(),u.getPort());
        String pair=decode(u.getRawUserInfo());
        int sep=pair.indexOf(':');
        if(sep<0)throw new Exception("TUIC credentials missing");
        Map<String,String> q=params(u.getRawQuery());
        return new JSONObject().put("type","tuic").put("server",u.getHost())
            .put("server_port",u.getPort()).put("uuid",pair.substring(0,sep))
            .put("password",pair.substring(sep+1))
            .put("congestion_control",q.getOrDefault("congestion_control","bbr"))
            .put("tls",new JSONObject().put("enabled",true)
                .put("server_name",q.getOrDefault("sni",u.getHost())));
    }

    private static JSONObject tls(String server, Map<String,String> q) throws Exception {
        String security = q.getOrDefault("security","tls");
        if (security.equals("none")) return null;
        JSONObject tls = new JSONObject().put("enabled",true);
        tls.put("server_name", q.getOrDefault("sni", server));
        String fp = q.getOrDefault("fp","");
        if (!fp.isBlank()) tls.put("utls",new JSONObject().put("enabled",true).put("fingerprint",fp));
        if (security.equals("reality")) {
            if (!q.containsKey("pbk")) throw new Exception("Reality public key is missing");
            tls.put("reality", new JSONObject().put("enabled",true)
                    .put("public_key",q.get("pbk")).put("short_id",q.getOrDefault("sid","")));
        } else if (!security.equals("tls")) throw new Exception("Unknown TLS security mode");
        if (q.containsKey("alpn"))
            tls.put("alpn", new JSONArray(q.get("alpn").split(",")));
        return tls;
    }

    private static void transport(JSONObject item, Map<String,String> q) throws Exception {
        String type = q.getOrDefault("type","tcp");
        if (type.equals("tcp")) return;
        if (type.equals("ws")) {
            JSONObject tr = new JSONObject().put("type","ws")
                .put("path",q.getOrDefault("path","/"));
            if (q.containsKey("host")) tr.put("headers",new JSONObject().put("Host",q.get("host")));
            item.put("transport",tr);
        } else if (type.equals("grpc")) {
            item.put("transport",new JSONObject().put("type","grpc")
                .put("service_name",q.getOrDefault("serviceName","")));
        } else if (type.equals("httpupgrade")) {
            item.put("transport",new JSONObject().put("type","httpupgrade")
                .put("path",q.getOrDefault("path","/"))
                .put("host",q.getOrDefault("host","")));
        } else throw new Exception("Unsupported transport: "+type);
    }

    private static JSONObject parseVless(String link) throws Exception {
        URI uri = new URI(link);
        String host=uri.getHost();
        int port=uri.getPort();
        validEndpoint(host,port);
        String uuid=uri.getUserInfo();
        if (uuid==null || !uuid.matches("[a-fA-F0-9-]{36}"))
            throw new Exception("VLESS user ID invalid");
        Map<String,String> q=params(uri.getRawQuery());
        JSONObject node=new JSONObject().put("type","vless").put("server",host)
            .put("server_port",port).put("uuid",uuid);
        JSONObject tls=tls(host,q);
        if (tls!=null) node.put("tls",tls);
        if (q.containsKey("flow")) node.put("flow",q.get("flow"));
        transport(node,q);
        return node;
    }

    private static JSONObject parseTrojan(String link) throws Exception {
        URI uri=new URI(link);
        String host=uri.getHost();
        int port=uri.getPort();
        validEndpoint(host,port);
        Map<String,String> q=params(uri.getRawQuery());
        JSONObject node=new JSONObject().put("type","trojan").put("server",host)
             .put("server_port",port).put("password",decode(uri.getRawUserInfo()));
        JSONObject tls=tls(host,q);
        if (tls!=null) node.put("tls",tls);
        transport(node,q);
        return node;
    }

    private static JSONObject parseVmess(String link) throws Exception {
        JSONObject x=new JSONObject(base64(link.substring("vmess://".length()).split("#")[0]));
        String host=x.optString("add");
        int port=Integer.parseInt(x.optString("port","0"));
        validEndpoint(host,port);
        JSONObject node=new JSONObject().put("type","vmess").put("server",host)
            .put("server_port",port).put("uuid",x.getString("id"))
            .put("security",x.optString("scy","auto")).put("alter_id",x.optInt("aid",0));
        node.put("tag",x.optString("ps",""));
        String network=x.optString("net","tcp");
        Map<String,String> q=new LinkedHashMap<>();
        q.put("type",network);
        q.put("path",x.optString("path","/"));
        q.put("host",x.optString("host",""));
        q.put("security",x.optString("tls","").equals("tls") ? "tls" : "none");
        q.put("sni",x.optString("sni",host));
        if(x.has("fp"))q.put("fp",x.optString("fp"));
        JSONObject tls=tls(host,q);
        if(tls!=null)node.put("tls",tls);
        transport(node,q);
        return node;
    }

    private static JSONObject parseShadowsocks(String link) throws Exception {
        String tail=link.substring(5).split("#")[0].split("\\?")[0];
        String address;
        String credential;
        if (tail.contains("@")) {
            String[] pair=tail.split("@",2);
            credential=pair[0].contains(":") ? pair[0] : base64(pair[0]);
            address=pair[1];
        } else {
            String combined=base64(tail);
            int at=combined.lastIndexOf('@');
            if(at<=0)throw new Exception("Invalid Shadowsocks URI");
            credential=combined.substring(0,at);
            address=combined.substring(at+1);
        }
        int colon=address.lastIndexOf(':');
        if(colon<=0)throw new Exception("Invalid Shadowsocks address");
        String host=address.substring(0,colon).replace("[","").replace("]","");
        int port=Integer.parseInt(address.substring(colon+1));
        validEndpoint(host,port);
        int colonCred=credential.indexOf(':');
        if(colonCred<=0)throw new Exception("Invalid Shadowsocks credentials");
        return new JSONObject().put("type","shadowsocks").put("server",host)
            .put("server_port",port).put("method",decode(credential.substring(0,colonCred)))
            .put("password",decode(credential.substring(colonCred+1)));
    }

    private static String build(List<JSONObject> nodes) throws Exception {
        JSONObject root=new JSONObject();
        root.put("log",new JSONObject().put("level","warn"));
        JSONArray inputs=new JSONArray().put(new JSONObject()
            .put("type","tun").put("tag","fhg-tun")
            .put("address",new JSONArray().put("172.19.0.1/30").put("fdfe:dcba:9876::1/126"))
            .put("auto_route",true).put("strict_route",true)
            .put("stack","system"));
        root.put("inbounds",inputs);
        JSONArray outs=new JSONArray();
        JSONArray names=new JSONArray();
        for(int i=0;i<nodes.size();i++){
            JSONObject n=nodes.get(i);
            // Every imported format must have a unique tag. Clash YAML
            // does not include native sing-box "tag" fields.
            String description=n.optString("tag","").replaceAll("[\\p{Cntrl}]"," ")
                .replaceAll("\\s+"," ").trim();
            if(description.length()>64)description=description.substring(0,64).trim();
            String tag="node-"+(i+1)+(description.isEmpty()?"":" · "+description);
            n.put("tag",tag);
            outs.put(n);
            names.put(tag);
        }
        if(nodes.size()>1){
            outs.put(new JSONObject().put("type","urltest").put("tag","fhg-proxy")
                .put("outbounds",names).put("url",PROBE).put("interval","5m"));
        }
        root.put("outbounds",outs);
        // Start conservatively with one upstream. The 21-node URL-test
        // remains an explicit UI option, not mandatory at VPN boot.
        String finalTag=nodes.get(0).getString("tag");
        root.put("route",new JSONObject().put("final",finalTag)
            .put("auto_detect_interface",true));
        root.put("dns",new JSONObject()
            .put("servers",new JSONArray().put(new JSONObject()
                .put("type","https").put("tag","encrypted")
                .put("server","1.1.1.1").put("detour",finalTag)))
            .put("final","encrypted").put("strategy","prefer_ipv4"));
        return root.toString();
    }

    /** Server tags are derived from provider labels but contain no credentials. */
    static List<String> selectableTags(String json) throws Exception {
        JSONObject root=new JSONObject(json);
        JSONArray out=root.getJSONArray("outbounds");
        ArrayList<String> tags=new ArrayList<>();
        for(int i=0;i<out.length();i++) {
            JSONObject item=out.getJSONObject(i);
            String type=item.optString("type","");
            String tag=item.optString("tag","");
            if(tag.startsWith("node-") && ("vless".equals(type) ||
                "vmess".equals(type) || "trojan".equals(type) ||
                "shadowsocks".equals(type) || "hysteria2".equals(type) ||
                "tuic".equals(type))) tags.add(tag);
        }
        return tags;
    }

    static String selectedTag(String json) throws Exception {
        return new JSONObject(json).getJSONObject("route").getString("final");
    }

    /** A route switch never changes provider credentials or permits DIRECT. */
    static String withSelected(String json,String selected) throws Exception {
        JSONObject root=new JSONObject(json);
        JSONArray out=root.getJSONArray("outbounds");
        List<String> allowed=selectableTags(json);
        boolean automatic=false;
        for(int i=0;i<out.length();i++) {
            JSONObject node=out.getJSONObject(i);
            if("urltest".equals(node.optString("type")) &&
                "fhg-proxy".equals(node.optString("tag"))) automatic=true;
        }
        if(!allowed.contains(selected) &&
            !(automatic && "fhg-proxy".equals(selected)))
            throw new IllegalArgumentException("INVALID_SERVER_SELECTION");
        root.getJSONObject("route").put("final",selected);
        JSONObject dns=root.getJSONObject("dns");
        JSONArray servers=dns.getJSONArray("servers");
        for(int i=0;i<servers.length();i++) {
            JSONObject server=servers.getJSONObject(i);
            if("encrypted".equals(server.optString("tag")))
                server.put("detour",selected);
        }
        return root.toString();
    }

    private static String validateRawJson(String raw) throws Exception {
        JSONObject root=new JSONObject(raw);
        JSONArray in=root.optJSONArray("inbounds");
        JSONArray out=root.optJSONArray("outbounds");
        JSONObject route=root.optJSONObject("route");
        if(in==null || out==null || route==null || out.length()==0)
            throw new Exception("Full sing-box configuration requires inbounds, outbounds and route.");
        boolean tun=false;
        for(int i=0;i<in.length();i++)
            if("tun".equals(in.getJSONObject(i).optString("type"))) tun=true;
        if(!tun)throw new Exception("A real sing-box TUN inbound is required.");
        for(int i=0;i<out.length();i++){
            String type=out.getJSONObject(i).optString("type","");
            if("direct".equals(type) || "block".equals(type))
                throw new Exception("No direct or block outbound allowed in strict FHG mode.");
        }
        if(route.optString("final").isBlank())
            throw new Exception("A secure proxy final outbound is required.");
        route.put("auto_detect_interface",true);
        // Disable dangerous direct-route exceptions in imported configs.
        if(route.has("rules") && route.getJSONArray("rules").length()>0)
            throw new Exception("Raw configs with route exceptions cannot be verified; import a normal subscription instead.");
        return root.toString();
    }
}
