using System;

namespace OurTaiko.Online
{
    // The level (AudioLevel) each server song's full audio measured when it was decoded for play,
    // by the audio's content hash. A song's preview is a separate 15-second excerpt that measures
    // louder or quieter than the whole song; once the song has been played, its preview takes the
    // whole song's level from here so both sound the same. One row (AudioLevelRecord) per audio in
    // the scores database; a row from another LoudnessMeter version is treated as missing.
    public sealed class AudioLevelStore
    {
        readonly GameDatabase database;
        readonly object sync = new object();

        public AudioLevelStore(string path) : this(new GameDatabase(path)) { }
        public AudioLevelStore(GameDatabase database) => this.database = database;

        public void Save(string hash, AudioLevel level)
        {
            if (!Json.HexId(hash ?? "", 64)) return;
            var entry = new AudioLevelRecord
            {
                Hash = hash, Meter = AudioLevel.MeterVersion, Peak = level.Peak,
                Loudness = double.IsNaN(level.Loudness) ? (double?)null : level.Loudness, Measured = DateTime.UtcNow.Ticks,
            };
            lock (sync) { using var db = database.Open(); db.InsertOrReplace(entry); }
        }

        public bool TryGet(string hash, out AudioLevel level)
        {
            level = AudioLevel.Unmeasured;
            if (!Json.HexId(hash ?? "", 64)) return false;
            AudioLevelRecord entry;
            lock (sync) { using var db = database.Open(); entry = db.Find<AudioLevelRecord>(hash); }
            if (entry == null || entry.Meter != AudioLevel.MeterVersion) return false;
            level = new AudioLevel(entry.Peak, entry.Loudness ?? double.NaN);
            return true;
        }
    }
}
