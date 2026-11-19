using System;
using UnityEngine;

namespace OurTaiko
{
    // Asks the phone's system for a display refresh rate, which Application.targetFrameRate alone
    // does not do. Android 15 holds an app whose category is "game" at 60 Hz until its surface asks
    // for more, and several vendors' systems leave the display at 60 Hz for an app that states no
    // preference; Plugins/Android/RefreshRate.java asks both ways and keeps asking as the surface
    // is recreated. The system may still refuse (power saving, heat, a vendor's list of games):
    // the log lines here say what the display did. Does nothing off Android.
    public static class DisplayRefreshRate
    {
        const string Tag = "Display";
        static int requested = int.MinValue;
        static bool reportPending;

        internal static int CurrentTarget
        {
            get
            {
                var rate = Screen.currentResolution.refreshRateRatio;
                return rate.numerator > 0 && rate.denominator > 0 ? Mathf.Max(1, Mathf.RoundToInt((float)rate.value)) : 60;
            }
        }

        // fps: the frames per second the game will draw; 0 asks for the fastest the display has.
        public static void Request(int fps)
        {
            if (fps == requested) return;
            requested = fps;
            Call((plugin, activity) =>
            {
                Log.Info(Tag, $"Asking for {Describe(fps)}. Before: {plugin.CallStatic<string>("state", activity)}");
                plugin.CallStatic("request", activity, (float)fps);
                reportPending = true;
            });
        }

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
