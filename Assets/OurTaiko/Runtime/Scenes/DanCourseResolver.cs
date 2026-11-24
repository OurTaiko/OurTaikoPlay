using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Finds a course's songs: local courses among the local songs (file SHA-256, else title and
    // subtitle), a server's courses among that server's songs by id. Each song is played as the 1P
    // seat reads it. Why a course cannot be played is written in the player's language (L10n) for
    // the 段位 select to show.
    public sealed class DanCourseResolver
    {
        readonly IReadOnlyList<SongDefinition> localSongs;
        readonly Func<string, string, SongDefinition> serverSong;
        readonly Dictionary<SongDefinition, SongInfo> infos = new Dictionary<SongDefinition, SongInfo>();
        Dictionary<string, SongDefinition> byHash;

        // serverSong(server base URL, song id) finds a server's song, null when there is none.
        public DanCourseResolver(IReadOnlyList<SongDefinition> localSongs, Func<string, string, SongDefinition> serverSong)
        {
            this.localSongs = localSongs ?? Array.Empty<SongDefinition>();
            this.serverSong = serverSong;
        }

        public DanCourseEntry Resolve(DanCourse course, string path = null)
        {
            var entry = new DanCourseEntry { Course = course, Path = path };
            foreach (var reference in course.Songs)
            {
                string label = reference.Title.Length > 0 ? reference.Title : reference.Sha256.Length > 0 ? reference.Sha256 : reference.Id;
                var song = course.IsLocal ? FindLocal(reference) : serverSong?.Invoke(course.Server, reference.Id);
                string tjaCourse = null, side = "single", chartHash = ""; int level = 0;
                string error = song == null ? L10n.Format("Dan", "resolve.songNotFound", label) : Block(song, reference, label, out tjaCourse, out side, out level, out chartHash);
                if (error != null) { entry.Error = error; entry.Songs.Clear(); return entry; }
                entry.Songs.Add(new DanResolvedSong { Ref = reference, Song = song, Course = tjaCourse, Side = side, Level = level, ChartHash = chartHash });
            }
            return entry;
        }

        // Null when the song has the block, else why it cannot be played (label names the song).
        string Block(SongDefinition song, DanSongRef reference, string label, out string tjaCourse, out string side, out int level, out string chartHash)
        {
            tjaCourse = null; side = "single"; level = 0; chartHash = "";
            string Missing() => L10n.Format("Dan", "resolve.noChart", DifficultyName(reference.Difficulty), label);
            var online = song.onlineChart;
            if (online != null)
            {
                // The server says whether the block the course named is still the one it was set up on.
                if (!reference.Available) return L10n.Format("Dan", "resolve.songChanged", label);
                var block = reference.Side.Length > 0
                    ? online.Blocks.Find(b => b.Course == SongInfo.CourseName(reference.Difficulty) && b.Side == reference.Side)
                    : online.Difficulties[(int)reference.Difficulty];
                if (block == null) return Missing();
                tjaCourse = block.Course; side = block.Side; level = block.Level; chartHash = block.ChartHash;
                return null;
            }
            var course = Info(song)?.Course(reference.Difficulty);
            if (course == null) return Missing();
            tjaCourse = course.Course; side = course.Side; level = course.Level;
            return null;
        }

        // A course's name in the player's language (Common difficulty.*).
        static string DifficultyName(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => L10n.Get("Common", "difficulty.easy"),
            Difficulty.Normal => L10n.Get("Common", "difficulty.normal"),
            Difficulty.Hard => L10n.Get("Common", "difficulty.hard"),
            Difficulty.Oni => L10n.Get("Common", "difficulty.oni"),
            Difficulty.Ura => L10n.Get("Common", "difficulty.ura"),
            _ => SongInfo.CourseName(difficulty),
        };

        SongDefinition FindLocal(DanSongRef reference)
        {
            if (reference.Sha256.Length > 0)
            {
                if (byHash == null)
                {
                    byHash = new Dictionary<string, SongDefinition>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in localSongs)
                        if (!string.IsNullOrEmpty(s.localChartSha256) && !byHash.ContainsKey(s.localChartSha256)) byHash[s.localChartSha256] = s;
                }
                if (byHash.TryGetValue(reference.Sha256, out var song)) return song;
            }
            if (reference.Title.Length == 0) return null;
            return localSongs.FirstOrDefault(s => Info(s) is SongInfo info && info.Title == reference.Title
                && (reference.Subtitle.Length == 0 || info.Subtitle == reference.Subtitle) && info.Has(reference.Difficulty));
        }

        SongInfo Info(SongDefinition song)
        {
            if (infos.TryGetValue(song, out var info)) return info;
            try { info = song.chart != null ? song.ReadInfo() : null; }
            catch (FormatException) { info = null; }
            return infos[song] = info;
        }
    }
}
