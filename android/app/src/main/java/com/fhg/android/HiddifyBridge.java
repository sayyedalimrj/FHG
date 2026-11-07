package com.fhg.android;

import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;

/**
 * Trusted Android integration with Hiddify's *official* app, rather than
 * re-implementing its Go TUN bridge. No Hiddify source code is copied here.
 *
 * The app's published Deep Link parser accepts
 * hiddify://import/?url=<encoded-subscription-url> and itself requires
 * user confirmation, so this integration cannot silently fetch untrusted
 * remote subscriptions (see upstream Hiddify v5.5.5 link_parsers.dart).
 */
final class HiddifyBridge {
    static final String PACKAGE = "app.hiddify.com";
    static final String RELEASES = "https://github.com/hiddify/hiddify-app/releases/tag/v5.5.5";
    private HiddifyBridge() {}

    static Intent subscriptionIntent(String subscription) {
        Uri url = Uri.parse(subscription);
        if (!"https".equalsIgnoreCase(url.getScheme()) || url.getHost() == null
            || url.getUserInfo() != null || subscription.length()>4096)
            throw new IllegalArgumentException("Invalid HTTPS subscription URL");
        Uri deep = new Uri.Builder().scheme("hiddify").authority("import")
            .path("/").appendQueryParameter("url",subscription).build();
        return new Intent(Intent.ACTION_VIEW,deep)
            .addCategory(Intent.CATEGORY_BROWSABLE)
            .setPackage(PACKAGE)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
    }

    static Intent launchIntent(Context context) {
        Intent launch=context.getPackageManager().getLaunchIntentForPackage(PACKAGE);
        if(launch==null)return null;
        launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        return launch;
    }

    static boolean installed(Context context) {
        return launchIntent(context)!=null;
    }

    static boolean open(Context context) {
        Intent launch=launchIntent(context);
        if (launch==null)return false;
        try { context.startActivity(launch); return true; }
        catch (RuntimeException unavailable) { return false; }
    }

    static boolean importSubscription(Context context,String subscription) {
        try {
            context.startActivity(subscriptionIntent(subscription));
            return true;
        }catch (ActivityNotFoundException unavailable) {
            return false;
        }catch (SecurityException invalidHandler) {
            return false;
        }
    }

    static void openOfficialDownload(Context context) {
        Intent view=new Intent(Intent.ACTION_VIEW,Uri.parse(RELEASES));
        view.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        context.startActivity(view);
    }
}
