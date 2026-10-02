using System;
using System.Collections;
using UnityEngine;

namespace OurTaiko
{
    // Shown behind the parked rainbow curtain (arcade loading_song frame 55): parses the TJA with
    // the play options applied and loads the song audio into memory, then hands both to
    // SinglePlayScene, whose load opens the curtain.
    public sealed class SongLoadingScene : MonoBehaviour
    {
        public SongDefinition defaultSong;
        [Tooltip("The title and hints stay up at least this long, however fast the song loads.")]
        [Min(0)] public float minimumSeconds = 2;

        public bool IsLoaded { get; private set; }

        IEnumerator Start()
        {
            var switcher = SceneSwitcher.EnsureInstance();
            var song = switcher.SelectedSong != null ? switcher.SelectedSong : defaultSong;
            string course = switcher.SelectedSong != null ? switcher.SelectedCourse : null;
            if (!switcher.IsCovered)
            {
                // Entered directly (Play mode on this scene): show the parked curtain at once.
                switcher.ShowSongOnCurtain(song);
                switcher.ParkCurtain();
            }
            float started = Time.realtimeSinceStartup;

            // GameScreen::init_tja, moved behind the curtain: the parse and Player::reset_chart's options.
            try { switcher.SetPreparedChart(song, course, PlayScene.PrepareChart(song, course)); }
            catch (Exception error) { Debug.LogException(error, this); }  // SinglePlayScene parses again and shows it.
            var clip = song != null ? song.music : null;
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            while (clip != null && clip.loadState == AudioDataLoadState.Loading) yield return null;
            if (clip != null && clip.loadState == AudioDataLoadState.Failed) Debug.LogError("Could not load song audio: " + clip.name, this);
            IsLoaded = true;

            while (Time.realtimeSinceStartup - started < minimumSeconds || switcher.IsSwitching) yield return null;
            switcher.SwitchScene(SceneSwitcher.GameScene);
        }
    }
}
