package com.fhg.android;

import android.content.Context;
import io.nekohasekai.libbox.Libbox;
import io.nekohasekai.libbox.SetupOptions;

/**
 * Native libbox has process-wide setup state. Initialize once, then ask the
 * ACTUAL embedded engine to validate subscriptions before committing them.
 */
final class VpnEngineSetup {
    private static boolean initialized;
    private VpnEngineSetup(){}

    static synchronized void ensure(Context context) throws Exception {
        if(initialized)return;
        Context app=context.getApplicationContext();
        SetupOptions setup=new SetupOptions();
        setup.setBasePath(app.getNoBackupFilesDir().getAbsolutePath());
        setup.setWorkingPath(app.getFilesDir().getAbsolutePath());
        setup.setTempPath(app.getCacheDir().getAbsolutePath());
        setup.setFixAndroidStack(true);
        setup.setLogMaxLines(500);
        setup.setDebug(false);
        Libbox.setup(setup);
        initialized=true;
    }

    static void validate(Context context, String config) throws Exception {
        ensure(context);
        Libbox.checkConfig(config);
    }
}
