package org.ourtaiko.play;

import android.app.Activity;
import android.os.Build;
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
// The system may still refuse: power saving, heat, or a vendor's own list of games.
public final class RefreshRate implements SurfaceHolder.Callback {
    private static final String TAG = "OurTaikoRefreshRate";
    private static RefreshRate current; // UI thread only
    private static volatile float lastAsked;

    private final Activity activity;
    private float wanted;
    private float asked;
    private SurfaceHolder holder;

    private RefreshRate(Activity activity) { this.activity = activity; }

    // fps: the frames per second the game will draw; 0 or less asks for the fastest the display has.
    public static void request(final Activity activity, final float fps) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (current == null || current.activity != activity) current = new RefreshRate(activity);
                current.wanted = fps;
                current.apply();
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
                .append(", asked for ").append(lastAsked > 0 ? hz(lastAsked) : "nothing")
                .append(", modes");
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
}
