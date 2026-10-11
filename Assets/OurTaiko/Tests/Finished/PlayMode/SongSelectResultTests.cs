using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SongSelectResultTests
    {
        // Checks real audio output timing; CI containers have no audio device.
        [UnityTest, Category("AudioDevice")]
        public IEnumerator SongListCourseSelectPlayResultAndBack()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var switcher = SceneSwitcher.Instance;
            switcher.LastDifficulty = -1;
            switcher.SwitchScene(SceneSwitcher.SongSelectScene);
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            Assert.That(select.Manager.Songs.Length, Is.EqualTo(3));
            Assert.That(select.Manager.FocusedSong.name, Is.EqualTo(TestSongs.TripleHelix));
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.Browsing));
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(select.IsPreviewPlaying, Is.True, "The focused song previews from DEMOSTART once its board is open.");
            Capture("SongSelectBrowse.png");

            select.Manager.Right();
            Assert.That(select.Manager.FocusedSong.name, Is.EqualTo(TestSongs.Calibration));
            Assert.That(select.IsPreviewPlaying, Is.False);
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("SongSelectMoving.png");
            yield return new WaitForSecondsRealtime(0.6f);
            select.Manager.Left();
            yield return new WaitForSecondsRealtime(0.9f);
            Assert.That(select.Manager.FocusedSong.name, Is.EqualTo(TestSongs.TripleHelix));

            select.Manager.Confirm();
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Back), "last_difficulty -1 starts on もどる.");
            select.Manager.Right();
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Back), "Input waits for the course panel fade.");
            yield return WaitUntil(() => select.CourseFade >= 1);
            select.Manager.Right(); select.Manager.Right();
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Easy));
            select.Manager.Right(); select.Manager.Right(); select.Manager.Right();
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Oni));
            Capture("SongSelectCourse.png");
            for (int i = 0; i < 10; i++) select.Manager.Right();
            Assert.That(select.Manager.Cursor.IsUra && select.Manager.Cursor.Selected == Difficulty.Ura, Is.True);
            yield return new WaitForSecondsRealtime(0.6f);
            Capture("SongSelectUraChange.png");
            yield return new WaitForSecondsRealtime(1f);
            Capture("SongSelectUra.png");
            for (int i = 0; i < 4; i++) select.Manager.Left();
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Modifier));
            select.Manager.Confirm();
            Assert.That(select.Manager.IsOptionPanelOpen && select.Manager.OptionMenu.Current == OptionRow.Auto, Is.True);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("SongSelectOptions.png");
            select.Manager.Right();
            Assert.That(select.Manager.AutoPlay, Is.True);
            select.Manager.Left();
            Assert.That(select.Manager.AutoPlay, Is.False);
            select.Manager.Right();
            for (int i = 0; i < OptionMenu.Rows.Length; i++) select.Manager.Confirm();
            Assert.That(select.Manager.OptionMenu.IsConfirmed, Is.True);
            select.Manager.Left();
            Assert.That(select.Manager.AutoPlay, Is.True, "A closing panel ignores ka.");
            yield return WaitUntil(() => !select.Manager.IsOptionPanelOpen);
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Modifier));
            select.Manager.Left(); select.Manager.Confirm();
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.Browsing));
            yield return new WaitForSecondsRealtime(0.6f);
            select.Manager.Confirm();
            yield return WaitUntil(() => select.CourseFade >= 1);
            Assert.That(select.Manager.Cursor.IsUra, Is.True, "The column keeps the Ura side when re-entering.");
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Back));
            for (int i = 0; i < 5; i++) select.Manager.Right();
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Ura));
            // TRIPLE HELIX's Edit chart uses letter-extension notes the parser rejects; flip back to Oni.
            for (int i = 0; i < 10; i++) select.Manager.Right();
            Assert.That((select.Manager.Cursor.Selected, select.Manager.Cursor.IsUra), Is.EqualTo((Difficulty.Oni, false)));
            yield return new WaitForSecondsRealtime(1.6f);
            select.Manager.Confirm();
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.Decided));
            yield return WaitForScene(SceneSwitcher.GameScene, 30);
            Assert.That(switcher.SelectedCourse, Is.EqualTo("Oni"));
            Assert.That(switcher.AutoPlay && switcher.LastDifficulty == (int)Difficulty.Oni, Is.True);
            Assert.That(switcher.ReturnScene, Is.EqualTo(SceneSwitcher.SongSelectScene));

            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session.Chart.Course, Is.EqualTo("Oni"));
            if (!play.IsPaused) play.TogglePause();
            play.pausePanel.SetActive(false);
            play.Session.Advance(play.Session.Chart.Duration + 1, true);
            play.SendMessage("Finish");
            yield return WaitForScene(SceneSwitcher.ResultScene);
            var result = Object.FindFirstObjectByType<ResultScene>();
            Assert.That(result.Result.Bad == 0 && result.Result.Ok == 0, Is.True);
            Assert.That(result.Result.GaugeState, Is.EqualTo(GaugeState.Full));
            Assert.That(ScoreStore.Shared.Get(TestSongs.TripleHelix, Difficulty.Oni), Is.Null, "Auto play is never saved.");
            yield return WaitUntil(() => result.Sequence.GaugeShown >= 30);
            Capture("ResultGaugeFilling.png");
            yield return WaitUntil(() => result.Sequence.RowsLanded >= 3);
            Assert.That(result.Sequence.CanSkip, Is.True);
            result.Don();
            Assert.That(result.Sequence.Skipped, Is.True);
            yield return WaitUntil(() => result.Sequence.RevealEndMs > 0);
            yield return new WaitForSecondsRealtime(2.2f);
            Capture("ResultFullCombo.png");
            Assert.That(result.Sequence.CanAdvance, Is.True, "A settled result has no extra exit delay.");
            result.Don();
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            Assert.That(Object.FindFirstObjectByType<SongSelectScene>().Manager.FocusedSong.name, Is.EqualTo(TestSongs.TripleHelix));
            Object.Destroy(switcher.gameObject);
        }

        [UnityTest]
        public IEnumerator FailedRunShowsDarkGaugeAndMissMessage()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var failed = new PlayResult
            {
                ChartKey = TestSongs.Calibration, Title = "Input Calibration", Course = "Hard", Difficulty = Difficulty.Hard, Level = 1,
                Score = 123450, Good = 40, Ok = 12, Bad = 30, MaxCombo = 17, Rolls = 3, GaugePoints = 2500,
            };
            Assert.That(failed.Message, Is.EqualTo(ResultMessage.Miss));
            SceneSwitcher.Instance.ShowResult(failed);
            yield return WaitForScene(SceneSwitcher.ResultScene);
            var result = Object.FindFirstObjectByType<ResultScene>();
            yield return WaitUntil(() => result.Sequence.RevealEndMs > 0, 20);
            Assert.That(result.Sequence.GaugeShown, Is.EqualTo(12));
            Assert.That(result.Sequence.CrownAtMs.HasValue, Is.True);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("ResultFailed.png");
            Object.Destroy(SceneSwitcher.Instance.gameObject);
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds = 20)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
        }

        static IEnumerator WaitForScene(string scene, float seconds = 20)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }

        static void Capture(string name)
        {
            Canvas canvas = null;
            foreach (var candidate in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == SceneManager.GetActiveScene()) canvas = candidate;
            var camera = Camera.main;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            var previous = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                scaler.SendMessage("Handle"); Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                Directory.CreateDirectory("TestResults"); File.WriteAllBytes("TestResults/" + name, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
                scaler.SendMessage("Handle"); Canvas.ForceUpdateCanvases();
                Object.Destroy(texture); Object.Destroy(target);
            }
        }
    }
}
