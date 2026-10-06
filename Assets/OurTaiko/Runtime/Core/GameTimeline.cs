using System.Diagnostics;
using UnityEngine;

namespace OurTaiko
{
    // One time source for the application. UI keeps running while a SongClock is paused.
    // Frame values never change within a Unity frame; live time is only for audio scheduling
    // and elapsed-time measurements, never for judging individual input events.
    public static class GameTimeline
    {
        static long origin = Stopwatch.GetTimestamp();
        static int sampledFrame = -1;
        static double frameTime;

        public static double Realtime => (Stopwatch.GetTimestamp() - origin) / (double)Stopwatch.Frequency;
        // BASS has no scheduled start; AudioBus starts playback when this clock reaches the time.
        public static double AudioNow => Realtime;
        public static double FrameTime
        {
            get
            {
                // Editor previews must not create the runtime audio engine.
                if (!Application.isPlaying) return Time.realtimeSinceStartupAsDouble;
                UpdateFrame();
                return frameTime;
            }
        }
        // Song progress in the playback clock's domain; the same value as FrameTime with BASS.
        public static double AudioFrameTime
        {
            get { UpdateFrame(); return frameTime; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            origin = Stopwatch.GetTimestamp();
            sampledFrame = -1;
            frameTime = 0;
        }

        internal static void UpdateFrame()
        {
            if (sampledFrame == Time.frameCount) return;
            sampledFrame = Time.frameCount;
            frameTime = Realtime;
        }
    }
}
