using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

namespace OurTaiko
{
    // Ordered from the most to the least verbose: a minimum level keeps itself and those after it.
    public enum LogLevel { Debug, Info, Warning, Error, Off }

    // The game's log. Each message has a level and a tag (the subsystem: "Audio", "Online"…);
    // messages below MinimumLevel are dropped. The rest go to Unity's console (Debug and Info as
    // Log, Warning, Error) as "[Tag] message", and, once LogFile has started, to a log file under
    // GamePaths.Root/Logs with a timestamp, the level and the thread. Callable from any thread.
    public static class Log
    {
        static int minimum = (int)DefaultLevel;
        public static LogLevel MinimumLevel
        {
            get => (LogLevel)Volatile.Read(ref minimum);
            set => Volatile.Write(ref minimum, (int)value);
        }

        // The Editor and development builds show Debug messages until the settings say otherwise.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public const LogLevel DefaultLevel = LogLevel.Debug;
#else
        public const LogLevel DefaultLevel = LogLevel.Info;
#endif

        public static bool Enabled(LogLevel level) => level < LogLevel.Off && level >= MinimumLevel;

        public static void Debug(string tag, string message, UnityEngine.Object context = null) => Write(LogLevel.Debug, tag, message, context);
        public static void Info(string tag, string message, UnityEngine.Object context = null) => Write(LogLevel.Info, tag, message, context);
        public static void Warn(string tag, string message, UnityEngine.Object context = null) => Write(LogLevel.Warning, tag, message, context);
        public static void Error(string tag, string message, UnityEngine.Object context = null) => Write(LogLevel.Error, tag, message, context);

        // Error level; the console keeps Unity's clickable stack trace, the file gets ToString().
        public static void Exception(string tag, Exception error, UnityEngine.Object context = null)
        {
            if (error == null || !Enabled(LogLevel.Error)) return;
            LogFile.Write(LogLevel.Error, tag, error.ToString());
            Forward(() => UnityEngine.Debug.LogException(error, context));
        }

        public static void Write(LogLevel level, string tag, string message, UnityEngine.Object context = null)
        {
            if (!Enabled(level)) return;
            LogFile.Write(level, tag, message);
            string text = string.IsNullOrEmpty(tag) ? message : "[" + tag + "] " + message;
            Forward(() =>
            {
                switch (level)
                {
                    case LogLevel.Warning: UnityEngine.Debug.LogWarning(text, context); break;
                    case LogLevel.Error: UnityEngine.Debug.LogError(text, context); break;
                    default: UnityEngine.Debug.Log(text, context); break;
                }
            });
        }

        // Unity's own messages (plugins, uncaught exceptions) reach the file through its callback;
        // ours are already there, so the callback skips what this thread is forwarding.
        [ThreadStatic] static bool forwarding;
        internal static bool IsForwarding => forwarding;

        static void Forward(Action log)
        {
            forwarding = true;
            try { log(); }
            finally { forwarding = false; }
        }

        public static LogLevel Parse(string name, LogLevel fallback)
            => Enum.TryParse(name, true, out LogLevel level) && Enum.IsDefined(typeof(LogLevel), level) ? level : fallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => MinimumLevel = DefaultLevel;
    }

    // The session's log file: Logs/ourtaiko-<start time>.log, written by a background thread so
    // a slow disk never stalls a frame. The newest KeptFiles sessions are kept.
    public static class LogFile
    {
        public const string FolderName = "Logs";
        public const int KeptFiles = 10;

        static readonly object gate = new object();
        static BlockingCollection<string> queue;
        static Thread writer;

        public static string Path { get; private set; }
        public static bool IsRunning => queue != null;

        // Starts before the first scene of a player or Play mode; EditMode tests have no file.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartForPlayer()
        {
            try { Start(System.IO.Path.Combine(GamePaths.Root, FolderName)); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("[Log] No log file: " + error.Message); }
        }

        public static void Start(string folder)
        {
            lock (gate)
            {
                if (queue != null) return;
                Directory.CreateDirectory(folder);
                Prune(folder, KeptFiles - 1);
                Path = System.IO.Path.Combine(folder, "ourtaiko-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                var stream = new StreamWriter(new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                queue = new BlockingCollection<string>();
                var lines = queue;
                writer = new Thread(() => Drain(lines, stream)) { IsBackground = true, Name = "OurTaiko log" };
                writer.Start();
                Application.logMessageReceivedThreaded += FromUnity;
                Application.quitting += Stop;
            }
            Log.Info("Log", $"OurTaiko {Application.version} on {Application.platform} ({SystemInfo.operatingSystem}); level {Log.MinimumLevel}");
        }

        public static void Stop()
        {
            Thread finished;
            lock (gate)
            {
                if (queue == null) return;
                Application.logMessageReceivedThreaded -= FromUnity;
                Application.quitting -= Stop;
                queue.CompleteAdding();
                queue = null;
                finished = writer;
                writer = null;
            }
            finished?.Join(2000);
        }

        internal static void Write(LogLevel level, string tag, string message)
        {
            var lines = queue;
            if (lines == null) return;
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {Name(level)} [{Thread.CurrentThread.ManagedThreadId}] {tag}: {message}";
            try { lines.Add(line); }
            catch (InvalidOperationException) { }  // Stopped meanwhile.
        }

        static void FromUnity(string message, string stackTrace, LogType type)
        {
            if (Log.IsForwarding) return;
            LogLevel level = type == LogType.Log ? LogLevel.Info : type == LogType.Warning ? LogLevel.Warning : LogLevel.Error;
            if (!Log.Enabled(level)) return;
            if (level == LogLevel.Error && !string.IsNullOrEmpty(stackTrace)) message += "\n" + stackTrace.TrimEnd();
            Write(level, "Unity", message);
        }

        static string Name(LogLevel level) => level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warning => "WARN ",
            _ => "ERROR",
        };

        static void Drain(BlockingCollection<string> lines, StreamWriter stream)
        {
            using (stream)
            {
                foreach (string line in lines.GetConsumingEnumerable())
                {
                    stream.WriteLine(line);
                    if (lines.Count == 0) stream.Flush();
                }
            }
        }

        // Leaves the newest `keep` session files (names sort by start time).
        public static void Prune(string folder, int keep)
        {
            IEnumerable<string> old = Directory.GetFiles(folder, "ourtaiko-*.log").OrderByDescending(f => f, StringComparer.Ordinal).Skip(Math.Max(0, keep));
            foreach (string file in old)
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
