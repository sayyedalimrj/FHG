using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

// Use the existing Mozilla-signed Spoof Timezone addon by Joue (MPL-2.0).
// A per-FHG Firefox policy enforces its signed local XPI; this never changes
// the system timezone, regular Firefox, Hiddify or user clock.
internal static class TimezonePolicy
{
    internal const string ExtensionId = "{55f61747-c3d3-4425-97f9-dfc19a0be23c}";
    internal const string IanaZone = "Europe/Berlin";

    internal static bool Ensure(string firefoxDir)
    {
        string distribution = Path.Combine(firefoxDir, "distribution");
        string xpi = Path.Combine(distribution, "fhg-timezone.xpi");
        string shaPath = xpi + ".sha256";
        string policyPath = Path.Combine(distribution, "policies.json");
        if (!File.Exists(xpi) || !File.Exists(shaPath) || !File.Exists(policyPath))
            return false;

        string expected = File.ReadAllText(shaPath)
            .Split(new[]{' ', '\t', '\r', '\n'}, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(xpi)));
        if (expected.Length != 64 ||
            !expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
            return false;
        // Do not accidentally install a malicious/unrelated XPI from an
        // unverified path. Firefox itself validates Mozilla's XPI signature.
        using (var zip = System.IO.Compression.ZipFile.OpenRead(xpi))
        {
            var manifest = zip.GetEntry("manifest.json");
            if (manifest == null) return false;
            using var stream = manifest.Open();
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            string? id = root.GetProperty("browser_specific_settings")
                .GetProperty("gecko").GetProperty("id").GetString();
            string? version = root.GetProperty("version").GetString();
            if (!string.Equals(id, ExtensionId, StringComparison.OrdinalIgnoreCase) ||
                version != "0.5.0")
                return false;
        }

        JsonNode? node = JsonNode.Parse(File.ReadAllText(policyPath));
        if (node?["policies"]?["ExtensionSettings"] is not JsonObject extensions ||
            extensions["authenticator@mymindstorm"] is not JsonObject)
            return false;
        string location = new Uri(xpi).AbsoluteUri;
        if (extensions[ExtensionId] is JsonObject prior &&
            prior["installation_mode"]?.GetValue<string>() == "force_installed" &&
            prior["install_url"]?.GetValue<string>() == location)
            return true;
        extensions[ExtensionId] = new JsonObject {
            ["installation_mode"] = "force_installed",
            ["install_url"] = location
        };
        File.WriteAllText(policyPath, node.ToJsonString(new JsonSerializerOptions {
            WriteIndented = true
        }));
        return true;
    }
}
