using UnityEngine;

namespace OurTaiko
{
    [CreateAssetMenu(menuName = "OurTaiko/Song")]
    public sealed class SongDefinition : ScriptableObject
    {
        public TextAsset chart;
        public AudioClip music;
        [System.NonSerialized] public string audioPath;
        NativeAudioSample preparedAudio;
        public bool HasPreparedAudio => preparedAudio != null && !preparedAudio.IsDisposed;
        public void SetPreparedAudio(NativeAudioSample audio) { preparedAudio?.Dispose(); preparedAudio = audio; }
        public NativeAudioSample TakePreparedAudio() { var audio = preparedAudio; preparedAudio = null; return audio?.IsDisposed == true ? null : audio; }
        void OnDisable() { preparedAudio?.Dispose(); preparedAudio = null; }
        public string course = "Oni";
        [Tooltip("Positive values delay judgments relative to the music, in milliseconds.")]
        public float audioOffsetMs;
        public float visualOffsetMs;
        [Tooltip("Song-select board colour: the Nijiiro genre frame (0 default ... 9).")]
        [Range(0, 9)] public int genre;
        public TaikoChart Parse() => TjaParser.Parse(chart.text, course);
        public TaikoChart Parse(string requestedCourse) => TjaParser.Parse(chart.text, string.IsNullOrEmpty(requestedCourse) ? course : requestedCourse);
        public SongInfo ReadInfo() => SongInfo.Read(chart.text);
        // Display metadata is separate from the parsed chart and score identity.
        public SongInfo ReadDisplayInfo() => SongInfo.Read(chart.text,
            SettingManager.Instance != null ? SettingManager.Instance.Settings.general.Language : "en");
    }
}
