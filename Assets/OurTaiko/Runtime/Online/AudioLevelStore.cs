using System;
using SQLite;

namespace OurTaiko.Online
{
    // The level (AudioLevel) each server song's full audio measured when it was decoded for play,
    // by the audio's content hash. A song's preview is a separate 15-second excerpt that measures
    // louder or quieter than the whole song; once the song has been played, its preview takes the
    // whole song's level from here so both sound the same. One row per audio in the scores
    // database; a row from another LoudnessMeter version is treated as missing.
    public sealed class AudioLevelStore
    {
        [UnityEngine.Scripting.Preserve, Table("AudioLevels")]
        public sealed class Entry
        {
            [PrimaryKey] public string Hash { get; set; }
            public int Meter { get; set; }
            public float Peak { get; set; }
            // Integrated LUFS; null when unmeasurable (silent or under 400 ms).
            public double? Loudness { get; set; }
            public long Measured { get; set; }
        }

        readonly string path;
        readonly object sync = new object();

        public AudioLevelStore(string path)
        {
            this.path = path;
            using var db = Open();
            db.CreateTable<Entry>();
        }

        SQLiteConnection Open() => new SQLiteConnection(path) { BusyTimeout = TimeSpan.FromSeconds(5) };

        public void Save(string hash, AudioLevel level)
        {
            if (!Json.HexId(hash ?? "", 64)) return;
            var entry = new Entry
            {
                Hash = hash, Meter = AudioLevel.MeterVersion, Peak = level.Peak,
                Loudness = double.IsNaN(level.Loudness) ? (double?)null : level.Loudness, Measured = DateTime.UtcNow.Ticks,
            };
            lock (sync) { using var db = Open(); db.InsertOrReplace(entry); }
        }

        public bool TryGet(string hash, out AudioLevel level)
        {
            level = AudioLevel.Unmeasured;
            if (!Json.HexId(hash ?? "", 64)) return false;
            Entry entry;
            lock (sync) { using var db = Open(); entry = db.Find<Entry>(hash); }
            if (entry == null || entry.Meter != AudioLevel.MeterVersion) return false;
            level = new AudioLevel(entry.Peak, entry.Loudness ?? double.NaN);
            return true;
        }
    }
}
