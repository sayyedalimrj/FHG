package com.fhg.android;

import org.json.JSONObject;
import org.yaml.snakeyaml.Yaml;
import org.yaml.snakeyaml.LoaderOptions;
import org.yaml.snakeyaml.constructor.SafeConstructor;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/** Strict conversion for widely deployed Clash/Clash.Meta subscription YAML. */
final class ClashSubscription {
    private ClashSubscription() {}
    private static String str(Map<?, ?> map, String key, String fallback) {
        Object v = map.get(key);
        return v == null ? fallback : String.valueOf(v);
    }
    private static int number(Map<?, ?> map, String key, int fallback) {
        Object v = map.get(key);
        if(v == null) return fallback;
        try { return Integer.parseInt(String.valueOf(v)); }
        catch(Exception e) { return fallback; }
    }
    private static boolean yes(Map<?, ?> map, String key) {
        Object v = map.get(key);
        return Boolean.TRUE.equals(v) || "true".equalsIgnoreCase(String.valueOf(v));
    }
    private static Map<?, ?> map(Map<?, ?> node, String key) {
        Object raw = node.get(key);
        return raw instanceof Map ? (Map<?, ?>)raw : java.util.Collections.emptyMap();
    }
    private static void endpoint(String host, int port) throws Exception {
        if(host.length() == 0 || host.length() > 255 || host.contains("/") ||
            port < 1 || port > 65535) throw new Exception("Invalid proxy endpoint");
    }

    static List<JSONObject> parse(String input) throws Exception {
        LoaderOptions options = new LoaderOptions();
        options.setMaxAliasesForCollections(10);
        options.setNestingDepthLimit(40);
        options.setCodePointLimit(1_000_000);
        Object decoded = new Yaml(new SafeConstructor(options)).load(input);
        if (!(decoded instanceof Map))
            throw new Exception("YAML_ROOT_INVALID");
        Map<?, ?> root = (Map<?, ?>)decoded;
        Object raw = root.get("proxies");
        if (!(raw instanceof List)) {
            if (root.containsKey("proxy-providers"))
                throw new Exception("YAML_REMOTE_PROVIDER_UNSUPPORTED");
            throw new Exception("YAML_NO_PROXIES");
        }
        ArrayList<JSONObject> out = new ArrayList<>();
        int total=0;
        for (Object element : (List<?>)raw) {
            if (!(element instanceof Map)) continue;
            if (++total > 64) break;
            Map<?, ?> p=(Map<?, ?>)element;
            String type=str(p,"type","").toLowerCase(Locale.ROOT);
            String host=str(p,"server","");
            int port=number(p,"port",0);
            if (!("vless".equals(type) || "vmess".equals(type) ||
                  "trojan".equals(type) || "ss".equals(type) ||
                  "hysteria2".equals(type) || "tuic".equals(type)))
                continue;
            endpoint(host,port);
            JSONObject node=new JSONObject().put("type","ss".equals(type)?"shadowsocks":type)
                .put("server",host).put("server_port",port)
                .put("tag",str(p,"name",""));
            switch(type) {
                case "vless":
                    node.put("uuid",str(p,"uuid",""));
                    String flow=str(p,"flow","");
                    if (!flow.isEmpty()) node.put("flow",flow);
                    break;
                case "vmess":
                    node.put("uuid",str(p,"uuid",""));
                    node.put("security",str(p,"cipher","auto"));
                    node.put("alter_id",number(p,"alterId",0));
                    break;
                case "trojan":
                case "hysteria2":
                case "tuic":
                    node.put("password",str(p,"password",""));
                    if("tuic".equals(type))node.put("uuid",str(p,"uuid",""));
                    break;
                case "ss":
                    node.put("method",str(p,"cipher",""));
                    node.put("password",str(p,"password",""));
                    break;
            }
            if (!"ss".equals(type)) {
                boolean useTls = "trojan".equals(type) || "hysteria2".equals(type) ||
                    "tuic".equals(type) || yes(p,"tls") ||
                    p.containsKey("reality-opts");
                if (useTls) {
                    JSONObject tls=new JSONObject().put("enabled",true)
                        .put("server_name",str(p,"servername",str(p,"sni",host)));
                    Map<?, ?> reality=map(p,"reality-opts");
                    if (!reality.isEmpty()) {
                        String pub=str(reality,"public-key","");
                        if (pub.isEmpty()) throw new Exception("Reality public key missing");
                        tls.put("reality",new JSONObject().put("enabled",true)
                            .put("public_key",pub)
                            .put("short_id",str(reality,"short-id","")));
                    }
                    String fingerprint=str(p,"client-fingerprint","");
                    if(!fingerprint.isEmpty())tls.put("utls",new JSONObject()
                        .put("enabled",true).put("fingerprint",fingerprint));
                    node.put("tls",tls);
                }
            }
            String transport=str(p,"network","tcp").toLowerCase(Locale.ROOT);
            if(!"ss".equals(type) && !"hysteria2".equals(type) &&
                !"tuic".equals(type) && !"tcp".equals(transport)) {
                if("ws".equals(transport)) {
                    Map<?, ?> ws=map(p,"ws-opts");
                    JSONObject t=new JSONObject().put("type","ws")
                        .put("path",str(ws,"path","/"));
                    Object headers=ws.get("headers");
                    if(headers instanceof Map) {
                        String h=str((Map<?, ?>)headers,"Host","");
                        if(!h.isEmpty())t.put("headers",new JSONObject().put("Host",h));
                    }
                    node.put("transport",t);
                } else if ("grpc".equals(transport)) {
                    node.put("transport",new JSONObject().put("type","grpc")
                        .put("service_name",str(map(p,"grpc-opts"),"grpc-service-name","")));
                } else continue;
            }
            out.add(node);
        }
        if (out.isEmpty()) throw new Exception("NO_SUPPORTED_YAML_NODES");
        return out;
    }
}
