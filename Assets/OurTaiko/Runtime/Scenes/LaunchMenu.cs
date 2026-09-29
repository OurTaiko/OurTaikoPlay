using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    public sealed class LaunchMenu : MonoBehaviour
    {
        public SongDefinition[] songs;
        public TMP_Text selection, mode;
        public UnityEngine.UI.Button playButton, songButton, autoButton;
        int selected;
        bool auto;
        void Awake()
        {
            SceneSwitcher.EnsureInstance();
            playButton.onClick.AddListener(Play);
            songButton.onClick.AddListener(NextSong);
            autoButton.onClick.AddListener(ToggleAuto);
            Refresh();
        }
        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.enterKey.wasPressedThisFrame) Play();
            if (keyboard.tabKey.wasPressedThisFrame) NextSong();
            if (keyboard.aKey.wasPressedThisFrame) ToggleAuto();
        }
        void Play() => SceneSwitcher.Instance.Play(songs[selected], auto);
        void NextSong() { selected = (selected + 1) % songs.Length; Refresh(); }
        void ToggleAuto() { auto = !auto; Refresh(); }
        void Refresh()
        {
            var chart = songs[selected].Parse();
            selection.text = $"{chart.Title}\n<size=24>{chart.Course.ToUpperInvariant()}  /  LV.{chart.Level}  /  {chart.Bpm:0.##} BPM</size>";
            mode.text = auto ? "AUTO PLAY  /  ON" : "AUTO PLAY  /  OFF";
        }
    }
}
