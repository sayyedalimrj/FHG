package com.fhg.android;

import org.junit.Test;
import static org.junit.Assert.*;

/** Privacy regression: exported debug reports must never contain secrets. */
public class DiagnosticReportTest {
    @Test public void aSafeReportIncludesOnlyStateAndVersion() {
        String report=DiagnosticReport.format(35,"15","CONNECTED","NONE",true,true);
        assertTrue(report.contains("Android API: 35"));
        assertTrue(report.contains("VPN state: CONNECTED"));
        assertTrue(report.contains("Encrypted profile present: true"));
        assertFalse(report.contains("password"));
        assertFalse(report.contains("subscription URL: https://"));
    }

    @Test public void rejectAnyUntrustedNativeError() {
        assertEquals("REDACTED",
            DiagnosticReport.safeError("LIBBOX failed on https://secret.example/token"));
        assertEquals("REDACTED",
            DiagnosticReport.safeError("AUTH_ERROR account@example.org"));
        assertEquals("PROXY_NOT_REACHABLE",
            DiagnosticReport.safeError("PROXY_NOT_REACHABLE"));
    }

    @Test public void statusMustBeWhitelisted() {
        String report=DiagnosticReport.format(37,"17",
            "https://secret.example/should-not-leak","PASSWORD=123",true,false);
        assertTrue(report.contains("VPN state: UNKNOWN"));
        assertFalse(report.contains("https://secret.example"));
        assertFalse(report.contains("PASSWORD=123"));
    }
}
