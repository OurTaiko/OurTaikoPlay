using System;
using Newtonsoft.Json.Linq;
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
            // Since 2026-10-23: each played song's counts as JSON ([{good,ok,bad,rolls,score,combo}, …])
            // and the course gauge percentage, for the 自己ベスト under each exam. Null on older rows.
            public string songStats { get; set; }
            public int gaugePercent { get; set; }
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
            // A server's song keeps its id as it is replaced; the played block's content hash tells
            // the versions apart.
            foreach (var song in entry.Songs)
                text.Append(song.Song.ScoreKey).Append('/').Append(SongInfo.CourseName(song.Ref.Difficulty)).Append('/').Append(song.ChartHash ?? "").Append('\n');
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

        // Adds the attempt; returns the course's best before it (null for a first attempt). songs are
        // the played songs' counts in order (the gauge percentage is the course's at the end).
        public Best? Save(string courseKey, string title, int verdict, int score, int maxCombo, double gaugePoints, int arrival,
            IReadOnlyList<DanSongStats> songs = null, int gaugePercent = 0)
        {
            var before = Get(courseKey);
            using var db = Open();
            db.Insert(new Attempt
            {
                courseKey = courseKey, title = title, verdict = verdict, score = score, maxCombo = maxCombo,
                gaugePoints = gaugePoints, arrival = arrival, playedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                songStats = songs == null ? null : StatsJson(songs), gaugePercent = gaugePercent,
            });
            return before;
        }

        static string StatsJson(IReadOnlyList<DanSongStats> songs)
        {
            var list = new JArray();
            foreach (var s in songs)
                list.Add(new JObject { ["good"] = s.Good, ["ok"] = s.Ok, ["bad"] = s.Bad, ["rolls"] = s.Rolls, ["score"] = s.Score, ["combo"] = s.MaxCombo });
            return list.ToString(Newtonsoft.Json.Formatting.None);
        }

        static List<DanSongStats> StatsOf(string json)
        {
            var songs = new List<DanSongStats>();
            if (string.IsNullOrEmpty(json)) return songs;
            try
            {
                foreach (var item in JArray.Parse(json))
                    songs.Add(new DanSongStats
                    {
                        Good = item.Value<int?>("good") ?? 0, Ok = item.Value<int?>("ok") ?? 0, Bad = item.Value<int?>("bad") ?? 0,
                        Rolls = item.Value<int?>("rolls") ?? 0, Score = item.Value<int?>("score") ?? 0, MaxCombo = item.Value<int?>("combo") ?? 0,
                    });
            }
            catch (Newtonsoft.Json.JsonException) { songs.Clear(); }
            return songs;
        }

        // The 自己ベスト of each exam, as the arcade's select screen shows under each condition: one
        // value per song for per-song exams, else one for the course; null where no attempt counted
        // it (the song never reached, or only attempts from before the counts were kept). "Less"
        // exams keep the lowest, the others the highest; the gauge is the course's percentage.
        public int?[][] ExamBests(string courseKey, DanCourse course)
        {
            var bests = new int?[course.Exams.Count][];
            for (int i = 0; i < bests.Length; i++) bests[i] = new int?[course.Exams[i].PerSong ? course.Songs.Count : 1];
            using var db = Open();
            foreach (var row in db.Table<Attempt>().Where(a => a.courseKey == courseKey).ToList())
            {
                if (row.songStats == null) continue;
                var songs = StatsOf(row.songStats);
                for (int i = 0; i < bests.Length; i++)
                {
                    var exam = course.Exams[i];
                    for (int k = 0; k < bests[i].Length; k++)
                    {
                        int? value = ExamValue(exam, songs, row.gaugePercent, exam.PerSong ? k : -1);
                        if (value == null) continue;
                        var best = bests[i][k];
                        bests[i][k] = best == null ? value : exam.Range == DanExamRange.Less ? Math.Min(best.Value, value.Value) : Math.Max(best.Value, value.Value);
                    }
                }
            }
            return bests;
        }

        // What an exam counted in one attempt: over the course (song -1, the songs played) or over
        // one song (null when that song was not reached).
        static int? ExamValue(DanExam exam, List<DanSongStats> songs, int gaugePercent, int song)
        {
            if (exam.Type == DanExamType.Gauge) return gaugePercent;
            if (song >= songs.Count) return null;
            var s = song < 0 ? songs.Aggregate(default(DanSongStats), (sum, x) => sum + x) : songs[song];
            switch (exam.Type)
            {
                case DanExamType.Good: return s.Good;
                case DanExamType.Ok: return s.Ok;
                case DanExamType.Bad: return s.Bad;
                case DanExamType.Hits: return s.Good + s.Ok + s.Rolls;
                case DanExamType.Score: return s.Score;
                case DanExamType.Combo: return s.MaxCombo;
                case DanExamType.Rolls: return s.Rolls;
                default: return null;
            }
        }
    }
}
