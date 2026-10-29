using System;
using System.Collections;
using System.IO;
using System.Linq;
using MajdataPlay.FFmpeg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The background movie decoded by the vendored FFmpeg player in the real play scenes: an 8 s
    // 320×180 30 fps H.264 test pattern (Fixtures/Movie~, kept out of the asset import).
    public sealed class BackgroundMovieFlowTests
    {
        static string Fixture(string name) => Path.GetFullPath("Assets/OurTaiko/Tests/Fixtures/Movie~/" + name);
        const string Chart = "TITLE:Movie\nBPM:120\nMOVIEOFFSET:1\nCOURSE:Oni\nLEVEL:1\n#START\n0010,\n0010,\n0010,\n0010,\n0010,\n0010,\n0010,\n0010,\n#END";

        [UnityTest]
        public IEnumerator MovieStartsAtMovieOffsetAndFollowsTheSongClock()
        {
            PlayScene play = null; BackgroundMovieView movie = null;
            yield return Play(Fixture("testsrc.mp4"), false, false, (p, m) => { play = p; movie = m; });
            try
            {
                Assert.That(movie.HasMovie, Is.True);
                Assert.That(movie.stageArt.Length, Is.EqualTo(9));
                Assert.That(movie.stageArt.All(g => g.alpha == 0), Is.True, "the stage art is hidden");
                Assert.That(movie.laneBackgrounds.All(g => Mathf.Approximately(g.alpha, .4f)), Is.True);
                Assert.That(movie.blackout.enabled, Is.True);
                // Before MOVIEOFFSET: black, the first frame waiting.
                yield return Wait(() => movie.player.IsPrepared);
                Assert.That(play.SongTime, Is.LessThan(1));
                Assert.That(movie.IsShowing, Is.False);
                yield return Wait(() => play.SongTime >= 2);
                Assert.That(movie.IsShowing, Is.True);
                Assert.That(movie.screen.texture, Is.Not.Null);
                Assert.That(movie.screen.rectTransform.sizeDelta, Is.EqualTo(new Vector2(1920, 1080)));
                // Give the rate correction a second to pull in the start.
                yield return Wait(() => play.SongTime >= 3);
                Assert.That(movie.player.State, Is.EqualTo(VideoPlaybackState.Playing));
                Assert.That(movie.player.TimeSeconds, Is.EqualTo(play.SongTime - 1).Within(.06));
                TestCapture.Capture("BackgroundMovie.png");
                // Pausing the song pauses the movie where it is.
                play.TogglePause(); yield return null; yield return null;
                Assert.That(movie.player.State, Is.EqualTo(VideoPlaybackState.Paused));
                double paused = movie.player.TimeSeconds;
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(movie.player.TimeSeconds, Is.EqualTo(paused).Within(1e-6));
                play.Resume();
                yield return Wait(() => !play.IsPaused);
                // The song clock settles on the restarted audio (about 0.1 s); the movie catches up.
                yield return new WaitForSecondsRealtime(1.5f);
                Assert.That(movie.player.TimeSeconds, Is.EqualTo(play.SongTime - 1).Within(.06));
                // After its 8 s: the last frame stays.
                yield return Wait(() => play.SongTime >= 9.5 || play.IsFinished);
                Assert.That(movie.player.State, Is.EqualTo(VideoPlaybackState.Ended));
                Assert.That(movie.IsShowing, Is.True);
            }
            finally { Cleanup(); }
        }

        [UnityTest]
        public IEnumerator DoublePlayShowsTheSongsMovieBehindBothLanes()
        {
            BackgroundMovieView movie = null; PlayScene play = null;
            yield return Play(Fixture("testsrc.mp4"), false, true, (p, m) => { play = p; movie = m; });
            try
            {
                Assert.That(play.IsTwoPlayer, Is.True);
                Assert.That(movie.laneBackgrounds.Length, Is.EqualTo(6));
                yield return Wait(() => play.SongTime >= 2);
                Assert.That(movie.IsShowing, Is.True);
            }
            finally { Cleanup(); }
        }

        [UnityTest]
        public IEnumerator SettingOffPracticeAndBrokenFilesKeepTheStageArt()
        {
            BackgroundMovieView movie = null;
            yield return Play(Fixture("testsrc.mp4"), false, false, (_, m) => movie = m, new GameSettings { display = new DisplaySettings { backgroundMovie = false } });
            try { Assert.That(movie.HasMovie, Is.False); Assert.That(movie.stageArt.All(g => g.alpha == 1), Is.True); }
            finally { Cleanup(); }
            yield return null;

            PlayScene play = null;
            yield return Play(Fixture("testsrc.mp4"), true, false, (p, m) => { play = p; movie = m; });
            try { Assert.That(play.IsPractice, Is.True); Assert.That(play.movie, Is.Null, "PracticeScene has no movie view"); }
            finally { Cleanup(); }
            yield return null;

            LogAssert.ignoreFailingMessages = true;
            try
            {
                yield return Play(Fixture("broken.mp4"), false, false, (_, m) => movie = m);
                yield return Wait(() => !movie.HasMovie);
                Assert.That(movie.stageArt.All(g => g.alpha == 1), Is.True);
                Assert.That(movie.laneBackgrounds.All(g => g.alpha == 1), Is.True);
                Assert.That(movie.blackout.enabled, Is.False);
            }
            finally { Cleanup(); LogAssert.ignoreFailingMessages = false; }
        }

        static SongDefinition song;

        static IEnumerator Play(string moviePath, bool practice, bool twoPlayers, Action<PlayScene, BackgroundMovieView> started, GameSettings settings = null)
        {
            SettingManager.EnsureInstance().UseUnsaved(settings ?? new GameSettings());
            song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset(Chart);
            song.music = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
            song.moviePath = moviePath;
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
            var switcher = SceneSwitcher.Instance;
            switcher.PracticeMode = practice;
            switcher.TwoPlayers = twoPlayers;
            switcher.Play(song, "Oni", true, twoPlayers ? "Oni" : null, true);
            yield return Wait(() => !switcher.IsInputBlocked && SceneManager.GetActiveScene().name == switcher.SelectedPlayScene);
            var play = Object.FindAnyObjectByType<PlayScene>();
            yield return Wait(() => play.Session != null);
            started(play, play.movie != null ? play.movie : Object.FindAnyObjectByType<BackgroundMovieView>());
        }

        static void Cleanup()
        {
            var play = Object.FindAnyObjectByType<PlayScene>();
            if (play != null) Object.Destroy(play.gameObject);
            if (SceneSwitcher.Instance != null)
            {
                SceneSwitcher.Instance.TwoPlayers = false; SceneSwitcher.Instance.PracticeMode = false;
                Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
            if (song != null) { Object.Destroy(song.chart); Object.Destroy(song); song = null; }
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
        }

        static IEnumerator Wait(Func<bool> condition)
        {
            double end = Time.realtimeSinceStartupAsDouble + 25;
            while (!condition()) { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(end)); yield return null; }
        }
    }
}
