using TMPro;
using UnityEngine;

namespace OurTaiko
{
    public sealed class LaunchMenu : MonoBehaviour
    {
        public SongDefinition[] songs;
        public TMP_Text selection, mode;
        public UnityEngine.UI.Button playButton, songButton, autoButton, songSelectButton;
        int selected;
        bool auto;
        void Awake()
        {
            SceneSwitcher.EnsureInstance();
            playButton.onClick.AddListener(Play);
            songButton.onClick.AddListener(NextSong);
            autoButton.onClick.AddListener(ToggleAuto);
            if (songSelectButton != null) songSelectButton.onClick.AddListener(OpenSongSelect);
            Refresh();
        }
        void Update()
        {
            if (SceneSwitcher.Instance == null || SceneSwitcher.Instance.IsInputBlocked) return;
            if (InputManager.GetKeyDown(InputKey.Confirm)) Play();
            if (InputManager.GetKeyDown(InputKey.NextSong)) NextSong();
            if (InputManager.GetKeyDown(InputKey.ToggleAuto)) ToggleAuto();
            if (InputManager.GetKeyDown(InputKey.SongSelect)) OpenSongSelect();
        }
        void OpenSongSelect() => SceneSwitcher.EnsureInstance().SwitchScene(SceneSwitcher.SongSelectScene);
        void Play() => SceneSwitcher.EnsureInstance().Play(songs[selected], auto);
        void NextSong() { if (SceneSwitcher.EnsureInstance().IsInputBlocked) return; selected = (selected + 1) % songs.Length; Refresh(); }
        void ToggleAuto() { if (SceneSwitcher.EnsureInstance().IsInputBlocked) return; auto = !auto; Refresh(); }
        void Refresh()
        {
            var chart = songs[selected].Parse();
            selection.text = $"{chart.Title}\n<size=24>{chart.Course.ToUpperInvariant()}  /  LV.{chart.Level}  /  {chart.Bpm:0.##} BPM</size>";
            mode.text = auto ? "AUTO PLAY  /  ON" : "AUTO PLAY  /  OFF";
        }
    }
}
