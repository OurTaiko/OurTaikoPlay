namespace OurTaiko
{
    // One go at a 段位 course: the course, and its charts as SongLoadingScene parsed them (with the
    // play options applied) for DanPlayScene to take. A chart left null is parsed by the play scene.
    public sealed class DanRun
    {
        public DanCourseEntry Entry { get; }
        public TaikoChart[] Charts { get; }
        // Each song's decoded audio, one per entry (a song played twice is decoded twice).
        public NativeAudioSample[] Audio { get; }
        public int Count => Entry.Songs.Count;

        public DanRun(DanCourseEntry entry)
        {
            Entry = entry;
            Charts = new TaikoChart[entry.Songs.Count];
            Audio = new NativeAudioSample[entry.Songs.Count];
        }

        public NativeAudioSample TakeAudio(int index)
        {
            var audio = Audio[index];
            Audio[index] = null;
            return audio?.IsDisposed == true ? null : audio;
        }

        // Frees what was decoded for songs never reached (back or a fail-out mid-course).
        public void Release()
        {
            for (int i = 0; i < Audio.Length; i++) { Audio[i]?.Dispose(); Audio[i] = null; }
        }

        public SongDefinition Song(int index) => Entry.Songs[index].Song;
        public string Course(int index) => Entry.Songs[index].Course;
    }
}

namespace OurTaiko
{
    // What a finished (or failed-out) course hands to the dan result: the exams as they ended, the
    // verdict, and each song played as a normal result (for the per-song scores).
    public sealed class DanPlayResult
    {
        public DanRun Run;
        public DanProgress Progress;
        public int Verdict;
        public PlayResult[] Songs;
        // Each played song's drum inputs, for its online score.
        public Online.PlayRecord[] Records;
        public int Score, MaxCombo;
        public double GaugePoints;
        public bool AutoPlay;
        // Set once DanResult has recorded it, so showing it again saves nothing twice; the best
        // before this attempt is kept for the celebration and the best-score pill.
        public bool Saved;
        public DanRecordStore.Best? PreviousBest;
        // The server course's attempt upload (FanmadeClient queue key), so showing the result again
        // does not upload it twice.
        public string Upload;
    }
}
