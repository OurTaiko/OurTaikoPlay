package org.ourtaiko.play;

import android.app.Activity;
import android.app.Application;
import android.content.Intent;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.Display;
import android.view.Surface;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;

import java.util.Locale;

// Asks Android for a display refresh rate; DisplayRefreshRate.cs calls it. Android 15 holds an app
// whose category is "game" at 60 Hz until its surface asks for more (Surface.setFrameRate), and
// several vendors' systems leave the display at 60 Hz for an app that prefers no display mode
// (preferredDisplayModeId). Application.targetFrameRate alone does neither, so both are done here.
//
// Xiaomi's systems (MIUI, HyperOS) ignore both for an app they count as a game and take a game's
// rate from a private broadcast that their game service (Joyose) sends to PowerKeeper; on a
// Xiaomi phone this sends it too, each time the game comes back and every 30 seconds while it is
// in front, in case PowerKeeper restarted. It is undocumented and may stop working with an update.
//
// The system may still refuse any of these: power saving, heat, or a vendor's own list of games.
public final class RefreshRate implements SurfaceHolder.Callback, Application.ActivityLifecycleCallbacks {
    private static final String TAG = "OurTaikoRefreshRate";
    private static final String MIUI_ACTION = "com.xiaomi.joyose.OVERRIDE_GAME_FRESHRATE";
    private static final String MIUI_RECEIVER = "com.miui.powerkeeper";
    private static final long MIUI_BEAT_MS = 30000;
    private static final boolean XIAOMI = "xiaomi".equalsIgnoreCase(Build.MANUFACTURER);

    private static RefreshRate current; // UI thread only
    private static volatile float lastAsked;
    private static volatile int lastMiui;

    private final Activity activity;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable miuiBeat = new Runnable() {
        @Override public void run() {
            tellMiui();
            handler.postDelayed(this, MIUI_BEAT_MS);
        }
    };
    private float wanted;
    private float asked;
    private SurfaceHolder holder;
    private boolean paused;

    private RefreshRate(Activity activity) {
        this.activity = activity;
        activity.getApplication().registerActivityLifecycleCallbacks(this);
    }

    // fps: the frames per second the game will draw; 0 or less asks for the fastest the display has.
    public static void request(final Activity activity, final float fps) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (current == null || current.activity != activity) {
                    if (current != null) current.release();
                    current = new RefreshRate(activity);
                }
                current.wanted = fps;
                current.apply();
                if (!current.paused) current.startMiuiBeat();
            }
        });
    }

    // The display as this app sees it, for the game's log. Callable from any thread.
    public static String state(Activity activity) {
        try {
            Display display = activity.getWindowManager().getDefaultDisplay();
            StringBuilder text = new StringBuilder();
            text.append(Build.MANUFACTURER).append(' ').append(Build.MODEL)
                .append(", Android API ").append(Build.VERSION.SDK_INT)
                .append(": mode ").append(describe(display.getMode()))
                .append(", the app is given ").append(hz(display.getRefreshRate()))
                .append(", asked for ").append(lastAsked > 0 ? hz(lastAsked) : "nothing");
            if (lastMiui > 0) text.append(" (and ").append(lastMiui).append(" Hz from Xiaomi's PowerKeeper)");
            text.append(", modes");
            for (Display.Mode mode : display.getSupportedModes()) text.append(' ').append(describe(mode));
            return text.toString();
        } catch (RuntimeException e) {
            return "unknown (" + e + ")";
        }
    }

    private void apply() {
        try {
            Window window = activity.getWindow();
            Display.Mode mode = pick(activity.getWindowManager().getDefaultDisplay(), wanted);
            asked = lastAsked = mode.getRefreshRate();
            WindowManager.LayoutParams attributes = window.getAttributes();
            if (attributes.preferredDisplayModeId != mode.getModeId()) {
                attributes.preferredDisplayModeId = mode.getModeId();
                window.setAttributes(attributes);
            }
            if (holder == null) {
                SurfaceView view = findSurfaceView(window.getDecorView());
                if (view == null) Log.w(TAG, "no SurfaceView to ask through");
                else { holder = view.getHolder(); holder.addCallback(this); }
            }
            applyToSurface();
            tellMiui();
            Log.i(TAG, "asked for " + describe(mode));
        } catch (RuntimeException e) {
            Log.w(TAG, "the request failed", e);
        }
    }

    // The request belongs to the surface, and Android destroys the surface whenever the game
    // leaves the screen: ask again on every new one.
    private void applyToSurface() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R || holder == null || asked <= 0) return;
        Surface surface = holder.getSurface();
        if (surface == null || !surface.isValid()) return;
        try {
            surface.setFrameRate(asked, Surface.FRAME_RATE_COMPATIBILITY_DEFAULT);
        } catch (RuntimeException e) {
            Log.w(TAG, "setFrameRate failed", e);
        }
    }

    // Xiaomi only: tells PowerKeeper the rate this game wants, as Joyose would. Elsewhere nothing
    // receives it, so it is not sent.
    private void tellMiui() {
        if (!XIAOMI || asked <= 0) return;
        int rate = Math.round(asked);
        try {
            Intent intent = new Intent(MIUI_ACTION).setPackage(MIUI_RECEIVER);
            intent.putExtra("override_pkg_name", activity.getPackageName());
            intent.putExtra("override_freshrate", rate);
            activity.sendBroadcast(intent);
            if (lastMiui != rate) Log.i(TAG, "told PowerKeeper " + rate + " Hz");
            lastMiui = rate;
        } catch (RuntimeException e) {
            Log.w(TAG, "the PowerKeeper broadcast failed", e);
        }
    }

    private void startMiuiBeat() {
        handler.removeCallbacks(miuiBeat);
        if (XIAOMI) handler.postDelayed(miuiBeat, MIUI_BEAT_MS);
    }

    private void release() {
        handler.removeCallbacks(miuiBeat);
        if (holder != null) holder.removeCallback(this);
        activity.getApplication().unregisterActivityLifecycleCallbacks(this);
    }

    // At the current resolution: the slowest mode that is not slower than wanted, or the fastest
    // one when none reaches it (or when wanted is 0).
    private static Display.Mode pick(Display display, float wanted) {
        Display.Mode now = display.getMode(), best = now;
        for (Display.Mode mode : display.getSupportedModes()) {
            if (mode.getPhysicalWidth() != now.getPhysicalWidth() || mode.getPhysicalHeight() != now.getPhysicalHeight()) continue;
            boolean reaches = reaches(mode, wanted), bestReaches = reaches(best, wanted);
            if (reaches != bestReaches) { if (reaches) best = mode; }
            else if (reaches ? mode.getRefreshRate() < best.getRefreshRate() : mode.getRefreshRate() > best.getRefreshRate()) best = mode;
        }
        return best;
    }

    private static boolean reaches(Display.Mode mode, float wanted) {
        return wanted > 0 && mode.getRefreshRate() >= wanted - 0.5f;
    }

    private static SurfaceView findSurfaceView(View view) {
        if (view instanceof SurfaceView) return (SurfaceView) view;
        if (!(view instanceof ViewGroup)) return null;
        ViewGroup group = (ViewGroup) view;
        for (int i = 0; i < group.getChildCount(); i++) {
            SurfaceView found = findSurfaceView(group.getChildAt(i));
            if (found != null) return found;
        }
        return null;
    }

    private static String describe(Display.Mode mode) {
        return mode.getPhysicalWidth() + "x" + mode.getPhysicalHeight() + "@" + hz(mode.getRefreshRate());
    }

    private static String hz(float rate) { return String.format(Locale.ROOT, "%.2f Hz", rate); }

    @Override public void surfaceCreated(SurfaceHolder holder) { applyToSurface(); }
    @Override public void surfaceChanged(SurfaceHolder holder, int format, int width, int height) { applyToSurface(); }
    @Override public void surfaceDestroyed(SurfaceHolder holder) { }

    @Override public void onActivityResumed(Activity resumed) {
        if (resumed != activity) return;
        paused = false;
        tellMiui();
        startMiuiBeat();
    }

    @Override public void onActivityPaused(Activity pausedActivity) {
        if (pausedActivity != activity) return;
        paused = true;
        handler.removeCallbacks(miuiBeat);
    }

    @Override public void onActivityDestroyed(Activity destroyed) {
        if (destroyed != activity) return;
        release();
        if (current == this) current = null;
    }

    @Override public void onActivityCreated(Activity created, Bundle state) { }
    @Override public void onActivityStarted(Activity started) { }
    @Override public void onActivityStopped(Activity stopped) { }
    @Override public void onActivitySaveInstanceState(Activity saved, Bundle state) { }
}
