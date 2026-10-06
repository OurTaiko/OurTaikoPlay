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
            Assert.That(select.songs.Length, Is.EqualTo(3));
            Assert.That(select.FocusedSong.name, Is.EqualTo(TestSongs.TripleHelix));
            Assert.That(select.Phase, Is.EqualTo(SongSelectScene.State.Browsing));
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(select.IsPreviewPlaying, Is.True, "The focused song previews from DEMOSTART once its board is open.");
            Capture("SongSelectBrowse.png");

            select.Right();
            Assert.That(select.FocusedSong.name, Is.EqualTo(TestSongs.Calibration));
            Assert.That(select.IsPreviewPlaying, Is.False);
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("SongSelectMoving.png");
            yield return new WaitForSecondsRealtime(0.6f);
            select.Left();
            yield return new WaitForSecondsRealtime(0.9f);
            Assert.That(select.FocusedSong.name, Is.EqualTo(TestSongs.TripleHelix));

            select.Confirm();
            Assert.That(select.Phase, Is.EqualTo(SongSelectScene.State.CourseSelect));
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Back), "last_difficulty -1 starts on もどる.");
            select.Right();
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Back), "Input waits for the course panel fade.");
            yield return WaitUntil(() => select.CourseFade >= 1);
            select.Right(); select.Right();
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Easy));
            select.Right(); select.Right(); select.Right();
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Oni));
            Capture("SongSelectCourse.png");
            for (int i = 0; i < 10; i++) select.Right();
            Assert.That(select.Cursor.IsUra && select.Cursor.Selected == Difficulty.Ura, Is.True);
            yield return new WaitForSecondsRealtime(0.6f);
            Capture("SongSelectUraChange.png");
            yield return new WaitForSecondsRealtime(1f);
            Capture("SongSelectUra.png");
            for (int i = 0; i < 4; i++) select.Left();
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Modifier));
            select.Confirm();
            Assert.That(select.IsOptionPanelOpen && select.OptionMenu.Current == OptionRow.Auto, Is.True);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("SongSelectOptions.png");
            select.Right();
            Assert.That(select.AutoPlay, Is.True);
            select.Left();
            Assert.That(select.AutoPlay, Is.False);
            select.Right();
            for (int i = 0; i < OptionMenu.Rows.Length; i++) select.Confirm();
            Assert.That(select.OptionMenu.IsConfirmed, Is.True);
            select.Left();
            Assert.That(select.AutoPlay, Is.True, "A closing panel ignores ka.");
            yield return WaitUntil(() => !select.IsOptionPanelOpen);
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Modifier));
            select.Left(); select.Confirm();
            Assert.That(select.Phase, Is.EqualTo(SongSelectScene.State.Browsing));
            yield return new WaitForSecondsRealtime(0.6f);
            select.Confirm();
            yield return WaitUntil(() => select.CourseFade >= 1);
            Assert.That(select.Cursor.IsUra, Is.True, "The column keeps the Ura side when re-entering.");
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Back));
            for (int i = 0; i < 5; i++) select.Right();
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Ura));
            // TRIPLE HELIX's Edit chart uses letter-extension notes the parser rejects; flip back to Oni.
            for (int i = 0; i < 10; i++) select.Right();
            Assert.That((select.Cursor.Selected, select.Cursor.IsUra), Is.EqualTo((Difficulty.Oni, false)));
            yield return new WaitForSecondsRealtime(1.6f);
            select.Confirm();
            Assert.That(select.Phase, Is.EqualTo(SongSelectScene.State.Decided));
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
            Assert.That(result.Sequence.CanAdvance, Is.False);
            result.Don();
            Assert.That(result.IsLeaving, Is.False, "Advancing waits for WaitEffectEnd + WaitNextScene.");
            yield return WaitUntil(() => result.Sequence.CanAdvance, 15);
            result.Don();
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            Assert.That(Object.FindFirstObjectByType<SongSelectScene>().FocusedSong.name, Is.EqualTo(TestSongs.TripleHelix));
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
