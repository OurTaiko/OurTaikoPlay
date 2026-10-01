using UnityEngine;

namespace OurTaiko
{
    [CreateAssetMenu(menuName = "OurTaiko/Song")]
    public sealed class SongDefinition : ScriptableObject
    {
        public TextAsset chart;
        public AudioClip music;
        public string course = "Oni";
        [Tooltip("Positive values delay judgments relative to the music, in milliseconds.")]
        public float audioOffsetMs;
        public float visualOffsetMs;
        [Tooltip("Song-select board colour: the Nijiiro genre frame (0 default ... 9).")]
        [Range(0, 9)] public int genre;
        public TaikoChart Parse() => TjaParser.Parse(chart.text, course);
        public TaikoChart Parse(string requestedCourse) => TjaParser.Parse(chart.text, string.IsNullOrEmpty(requestedCourse) ? course : requestedCourse);
        public SongInfo ReadInfo() => SongInfo.Read(chart.text);
    }
}
