package com.fhg.android;

import java.util.Arrays;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** RFC 6238 TOTP for the user's existing Base32 2FA seed. */
final class Totp {
    private Totp() {}

    static void validate(String base32) throws Exception {
        byte[] bytes = decode(base32);
        if (bytes.length < 10 || bytes.length > 128)
            throw new Exception("2FA secret must be Base32 and 10-128 bytes");
        Arrays.fill(bytes, (byte) 0);
    }

    private static byte[] decode(String base32) throws Exception {
        String clean = base32.toUpperCase(java.util.Locale.ROOT)
                .replace(" ", "").replace("-", "").replace("=", "");
        java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
        int bits = 0, accumulator = 0;
        for (int i = 0; i < clean.length(); i++) {
            char c = clean.charAt(i);
            int digit = c >= 'A' && c <= 'Z' ? c - 'A' :
                    c >= '2' && c <= '7' ? c - '2' + 26 : -1;
            if (digit < 0) throw new Exception("Invalid Base32 TOTP character");
            accumulator = (accumulator << 5) | digit;
            bits += 5;
            if (bits >= 8) {
                bits -= 8;
                out.write((accumulator >>> bits) & 0xff);
            }
        }
        return out.toByteArray();
    }

    static String now(String seed) throws Exception {
        return codeAt(seed, System.currentTimeMillis());
    }

    static String codeAt(String seed, long unixMillis) throws Exception {
        byte[] key = decode(seed);
        try {
            long counter = (unixMillis / 1000L) / 30L;
            byte[] time = new byte[8];
            for (int i = 7; i >= 0; i--) {
                time[i] = (byte)(counter & 255);
                counter >>>= 8;
            }
            Mac mac = Mac.getInstance("HmacSHA1");
            mac.init(new SecretKeySpec(key, "HmacSHA1"));
            byte[] digest = mac.doFinal(time);
            int offset = digest[digest.length - 1] & 0x0f;
            int code = ((digest[offset] & 0x7f) << 24) |
                ((digest[offset + 1] & 255) << 16) |
                ((digest[offset + 2] & 255) << 8) |
                (digest[offset + 3] & 255);
            return String.format(java.util.Locale.US,"%06d",code % 1000000);
        } finally {
            Arrays.fill(key,(byte)0);
        }
    }

    static int secondsRemaining() {
        return 30 - (int)((System.currentTimeMillis() / 1000) % 30);
    }
}
