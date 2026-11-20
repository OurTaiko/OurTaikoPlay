using System;
using UnityEngine;

namespace OurTaiko
{
    // Asks the phone's system for a display refresh rate, which Application.targetFrameRate alone
    // does not do. Android 15 holds an app whose category is "game" at 60 Hz until its surface asks
    // for more, and several vendors' systems leave the display at 60 Hz for an app that states no
    // preference; Plugins/Android/RefreshRate.java asks both ways and keeps asking as the surface
    // is recreated. The system may still refuse (power saving, heat, a vendor's list of games):
    // the log lines here say what the display did. On iOS it only reads the fastest rate the screen
    // has, for Unlimited (Plugins/iOS/OurTaikoDisplay.mm). Does nothing elsewhere.
    public static class DisplayRefreshRate
    {
        const string Tag = "Display";
        static int requested = int.MinValue, requestedRate;
        static bool reportPending;

        // fps: the frames per second the game wants; 0 asks for the fastest the display has.
        // Returns the refresh rate of the display mode asked for, the rate the game should draw at:
        // a cap that does not divide the display's rate (120 on a 90 or 144 Hz display) would be
        // divided down by frame pacing. Off Android, or when the request fails, returns fallback.
        public static int Request(int fps, int fallback)
        {
#if UNITY_IOS && !UNITY_EDITOR
            // iOS needs no request: it draws up to the screen's rate by itself. Unlimited is the
            // fastest the screen has.
            if (fps != 0) return fallback;
            int fastest = OurTaikoDisplay_MaximumFramesPerSecond();
            return fastest > 0 ? fastest : fallback;
#else
            if (fps == requested) return requestedRate;
            requested = fps;
            requestedRate = fallback;
            Call((plugin, activity) =>
            {
                Log.Info(Tag, $"Asking for {Describe(fps)}. Before: {plugin.CallStatic<string>("state", activity)}");
                float rate = plugin.CallStatic<float>("request", activity, (float)fps);
                if (rate > 0) requestedRate = Mathf.RoundToInt(rate);
                reportPending = true;
            });
            return requestedRate;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int OurTaikoDisplay_MaximumFramesPerSecond();
#endif

        // Logs once what the display did with the last request. The switch is not immediate, so
        // this is called some time after it: SceneSwitcher does on each scene change.
        public static void Report()
        {
            if (!reportPending) return;
            reportPending = false;
            Call((plugin, activity) =>
                Log.Info(Tag, $"After asking for {Describe(requested)}: {plugin.CallStatic<string>("state", activity)}"));
        }

        static string Describe(int fps) => fps > 0 ? $"{fps} Hz" : "the fastest refresh rate";

        // A platform check at run time, not #if, so that the Editor compiles this on every target.
        static void Call(Action<AndroidJavaClass, AndroidJavaObject> action)
        {
            if (Application.platform != RuntimePlatform.Android) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var plugin = new AndroidJavaClass("org.ourtaiko.play.RefreshRate");
                action(plugin, activity);
            }
            catch (Exception e)
            {
                Log.Warn(Tag, $"The refresh rate request failed: {e.Message}");
            }
        }
    }
}
