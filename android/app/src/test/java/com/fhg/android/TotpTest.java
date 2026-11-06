package com.fhg.android;

import org.junit.Test;
import static org.junit.Assert.*;

public class TotpTest {
    private static final String RFC_SHA1_SEED = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    @Test public void rfc6238Sha1T59s() throws Exception {
        assertEquals("287082", Totp.codeAt(RFC_SHA1_SEED, 59000L));
    }

    @Test public void rfc6238Sha1T1111111109s() throws Exception {
        assertEquals("081804", Totp.codeAt(RFC_SHA1_SEED, 1111111109000L));
    }

    @Test public void invalidBase32MustFail() throws Exception {
        try {
            Totp.validate("BAD!SECRET");
            fail("Invalid seed should be rejected");
        } catch (Exception expected) { }
    }
}
