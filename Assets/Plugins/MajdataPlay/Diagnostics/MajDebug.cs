#nullable enable
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace MajdataPlay.Diagnostics
{
    // OurTaiko: the subset of MajdataPlay's logger the vendored FFmpeg player calls, forwarded to
    // Unity's log (from any thread). Debug-level messages (every play, pause and seek) are compiled
    // in only with the OURTAIKO_FFMPEG_DEBUG define.
    public static class MajDebug
    {
        [Conditional("OURTAIKO_FFMPEG_DEBUG")] public static void LogDebug<T>(T obj) => Debug.Log(obj);
        [Conditional("OURTAIKO_FFMPEG_DEBUG")] public static void LogDebug<T>(string tag, T obj) => Debug.Log(Tagged(tag, obj));
        public static void LogInfo<T>(T obj) => Debug.Log(obj);
        public static void LogInfo<T>(string tag, T obj) => Debug.Log(Tagged(tag, obj));
        public static void LogWarning<T>(T obj) => Debug.LogWarning(obj);
        public static void LogWarning<T>(string tag, T obj) => Debug.LogWarning(Tagged(tag, obj));
        public static void LogError<T>(T obj) => Debug.LogError(obj);
        public static void LogError<T>(string tag, T obj) => Debug.LogError(Tagged(tag, obj));

        static string Tagged<T>(string tag, T obj) => "[" + tag + "] " + obj;
    }
}
