using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SQLite;

namespace OurTaiko
{
    // Every 段位 attempt played for real (not under オート), one row each, in the same database as
    // the song scores (YataiDON dan_scores). A course is known by its key: its id, the server that
    // hands it out, each song's chart and course, and its exams, so changing any of them starts a
    // new record. Best() is the course's best verdict, score and number of songs reached.
    public sealed class DanRecordStore
    {
        [UnityEngine.Scripting.Preserve, Table("DanAttempts")]
        public sealed class Attempt
        {
            [PrimaryKey, AutoIncrement] public int id { get; set; }
            [Indexed] public string courseKey { get; set; }
            public string title { get; set; }
            public int verdict { get; set; }
            public int score { get; set; }
            public int maxCombo { get; set; }
            public double gaugePoints { get; set; }
            public int arrival { get; set; }
            public long playedAt { get; set; }
        }

        public readonly struct Best
        {
            public readonly int Verdict, Score, Arrival, Attempts;
            public Best(int verdict, int score, int arrival, int attempts) { Verdict = verdict; Score = score; Arrival = arrival; Attempts = attempts; }
        }

        static DanRecordStore shared;
        readonly string path;
        public static DanRecordStore Shared
        {
            get => shared ??= new DanRecordStore(Path.Combine(GamePaths.Root, "scores.sqlite3"));
            set => shared = value;
        }

        public DanRecordStore(string path)
        {
            this.path = path;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var db = Open();
            db.CreateTable<Attempt>();
        }

        SQLiteConnection Open() => new SQLiteConnection(path) { BusyTimeout = TimeSpan.FromSeconds(5) };

        public static string Key(DanCourseEntry entry)
        {
            var text = new StringBuilder();
            text.Append(entry.Course.Id).Append('\n').Append(entry.Course.Server ?? "").Append('\n');
            // A server's song keeps its id as it is replaced; its TJA hash tells the versions apart.
            foreach (var song in entry.Songs)
                text.Append(song.Song.ScoreKey).Append('/').Append(SongInfo.CourseName(song.Ref.Difficulty)).Append('/').Append(song.Song.onlineChart?.TjaHash ?? "").Append('\n');
            foreach (var exam in entry.Course.Exams)
            {
                text.Append(exam.Type).Append(',').Append(exam.Range).Append(',').Append(exam.Course.Red).Append(',').Append(exam.Course.Gold);
                if (exam.PerSong) foreach (var border in exam.Songs) text.Append(';').Append(border is DanBorder b ? b.Red + "/" + b.Gold : "-");
                text.Append('\n');
            }
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        public Best? Get(string courseKey)
        {
            using var db = Open();
            var rows = db.Table<Attempt>().Where(a => a.courseKey == courseKey).ToList();
            if (rows.Count == 0) return null;
            return new Best(rows.Max(a => a.verdict), rows.Max(a => a.score), rows.Max(a => a.arrival), rows.Count);
        }

        // Adds the attempt; returns the course's best before it (null for a first attempt).
        public Best? Save(string courseKey, string title, int verdict, int score, int maxCombo, double gaugePoints, int arrival)
        {
            var before = Get(courseKey);
            using var db = Open();
            db.Insert(new Attempt
            {
                courseKey = courseKey, title = title, verdict = verdict, score = score, maxCombo = maxCombo,
                gaugePoints = gaugePoints, arrival = arrival, playedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            return before;
        }
    }
}
