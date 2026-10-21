using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class DanSelectFlowTests
    {
        static string DanFolder => Path.Combine(TestSongs.Root, DanCourseLoader.LocalFolder);

        [SetUp]
        public void WriteCourses()
        {
            Directory.CreateDirectory(DanFolder);
            string helix;
            using (var sha = SHA256.Create())
                helix = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(TestSongs.ChartPath(TestSongs.TripleHelix)))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Path.Combine(DanFolder, "1 first.otdan.json"),
                "{\"format\":1,\"id\":\"local:test-1\",\"title\":\"十段\",\"color\":3,\"songs\":["
                + "{\"chart\":{\"sha256\":\"" + helix + "\",\"course\":\"Oni\"}},"
                + "{\"chart\":{\"course\":\"Hard\"},\"title\":\"Input Calibration\",\"hidden\":true},"
                + "{\"chart\":{\"course\":\"Oni\"},\"title\":\"Branch Training\"}],"
                + "\"exams\":[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[100]},"
                + "{\"type\":\"ok\",\"range\":\"less\",\"value\":[[30,15],null,[20,10]]},"
                + "{\"type\":\"bad\",\"range\":\"less\",\"value\":[9,5]},"
                + "{\"type\":\"combo\",\"range\":\"more\",\"value\":[300]}]}");
            File.WriteAllText(Path.Combine(DanFolder, "2 missing.otdan.json"),
                "{\"format\":1,\"id\":\"local:test-2\",\"title\":\"名人\",\"color\":5,\"songs\":[{\"chart\":{\"course\":\"Oni\"},\"title\":\"Nowhere\"}],"
                + "\"exams\":[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[100]}]}");
            File.WriteAllText(Path.Combine(DanFolder, "3 broken.otdan.json"), "{");
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(DanFolder)) Directory.Delete(DanFolder, true);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
        }

        [UnityTest]
        public IEnumerator EntrysBoardOpensTheCourseSelectAndItsConfirmation()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var entry = Object.FindFirstObjectByType<EntryScene>();
            Assert.That(entry.Modes.Select(m => m.Title), Is.EqualTo(new[] { "演奏ゲーム", "段位道場", "練習モード", "ゲーム設定" }));
            Assert.That(entry.Modes[1].Scene, Is.EqualTo(SceneSwitcher.DanSelectScene));
            yield return new WaitForSecondsRealtime(0.2f);
            entry.Don();
            yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 3);
            yield return new WaitForSecondsRealtime(1f);
            entry.Ka(1);
            yield return new WaitForSecondsRealtime(0.8f);
            TestCapture.Capture("EntryDanBoard.png");
            entry.Don();
            yield return WaitForScene(SceneSwitcher.DanSelectScene);

            var scene = Object.FindFirstObjectByType<DanSelectScene>();
            var view = scene.view;
            Assert.That(scene.Courses.Count, Is.EqualTo(3));
            Assert.That(scene.Courses[0].IsPlayable, Is.True, scene.Courses[0].Error);
            Assert.That(scene.Courses[1].Error, Is.EqualTo("Song not found: Nowhere"));
            Assert.That(scene.Courses[2].IsPlayable, Is.False);
            yield return new WaitForSecondsRealtime(0.6f);

            // The first course: its chip under the cursor, its songs and exams on the board.
            Assert.That(view.chips.Count(c => c.root.gameObject.activeSelf), Is.EqualTo(3));
            Assert.That(view.cursor.rectTransform.anchoredPosition.x, Is.EqualTo(DanSelectScene.RibbonCenter).Within(0.01f));
            Assert.That(view.contents.alpha, Is.EqualTo(1));
            Assert.That(view.horiName.text, Is.EqualTo("十段"));
            Assert.That(view.songs[0].title.text, Is.EqualTo("TRIPLE HELIX"));
            Assert.That(view.songs[1].title.text, Is.EqualTo("？？？"), "Hidden songs keep their title back.");
            Assert.That(view.songs[2].title.text, Is.EqualTo("Branch Training"));
            Assert.That(view.gauge.gameObject.activeSelf, Is.True);
            Assert.That(view.gaugeCaption.text, Is.EqualTo("100 ％以上"));
            Assert.That(view.examRows.Select(r => r.root.gameObject.activeSelf), Is.EqualTo(new[] { true, true, true }));
            Assert.That(view.examRows[0].captions.Select(c => c.text), Is.EqualTo(new[] { "30 未満", "―", "20 未満" }));
            Assert.That(view.examRows[1].segments.Count(s => s.gameObject.activeSelf), Is.EqualTo(1), "A course-wide exam has one segment.");
            Assert.That(view.examRows[1].captions[0].text, Is.EqualTo("9 未満"));
            Assert.That(view.error.gameObject.activeSelf, Is.False);
            TestCapture.Capture("DanSelect.png");

            // Right ka: the course whose song is missing is greyed and does not open.
            scene.Ka(1);
            Assert.That(scene.Flow.Selected, Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(view.error.gameObject.activeSelf, Is.True);
            Assert.That(view.error.text, Is.EqualTo("Song not found: Nowhere"));
            Assert.That(view.exams.gameObject.activeSelf, Is.False);
            TestCapture.Capture("DanSelectMissingSong.png");
            scene.Don();
            Assert.That(scene.Flow.State, Is.EqualTo(DanSelectFlow.Phase.Browsing));

            // Back to the first course, then the confirmation.
            scene.Ka(-1);
            yield return new WaitForSecondsRealtime(0.3f);
            scene.Don();
            Assert.That(scene.Flow.State, Is.EqualTo(DanSelectFlow.Phase.Confirm));
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(view.confirm.gameObject.activeSelf, Is.True);
            Assert.That(view.confirm.alpha, Is.EqualTo(1));
            Assert.That(view.noOutline.enabled, Is.True);
            scene.Ka(-1);
            Assert.That(scene.Flow.Cursor, Is.EqualTo(DanSelectFlow.Choice.Yes));
            yield return null;
            Assert.That(view.yesOutline.enabled && !view.noOutline.enabled, Is.True);
            TestCapture.Capture("DanSelectConfirm.png");

            // オプション opens the 演奏オプション panel; confirming every row slides it out again.
            scene.Ka(-1);
            scene.Don();
            Assert.That(scene.Flow.State, Is.EqualTo(DanSelectFlow.Phase.Options));
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(view.options.gameObject.activeSelf, Is.True);
            TestCapture.Capture("DanSelectOptions.png");
            for (int i = 0; i < OptionMenu.Rows.Length; i++) scene.Options.Confirm();
            yield return WaitUntil(() => scene.Flow.State == DanSelectFlow.Phase.Confirm, 2);
            Assert.That(view.options.gameObject.activeSelf, Is.False);

            // いいえ (a back) closes the confirmation; back again leaves for Entry.
            scene.Back();
            Assert.That(scene.Flow.State, Is.EqualTo(DanSelectFlow.Phase.Browsing));
            yield return null;
            Assert.That(view.confirm.gameObject.activeSelf, Is.False);
            scene.Back();
            yield return WaitForScene(SceneSwitcher.EntryScene);
        }

        [UnityTest]
        public IEnumerator TheBoardIsGreyedAndCannotBePickedWith2PIn()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var entry = Object.FindFirstObjectByType<EntryScene>();
            yield return new WaitForSecondsRealtime(0.2f);
            entry.Don();
            entry.Join2P();
            yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 3);
            yield return new WaitForSecondsRealtime(1f);
            entry.Ka(1);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(entry.view.boards[1].closed.color.r, Is.LessThan(0.5f));
            entry.Don();
            Assert.That(entry.Flow.IsSelected, Is.False);
        }

        [UnityTest]
        public IEnumerator ATapManyChipsAwaySlidesTheRibbonAllTheWay()
        {
            for (int i = 4; i <= 7; i++)
                File.WriteAllText(Path.Combine(DanFolder, i + " extra.otdan.json"),
                    "{\"format\":1,\"id\":\"local:extra-" + i + "\",\"title\":\"" + i + "段\",\"color\":" + (i % 7) + ","
                    + "\"songs\":[{\"chart\":{\"course\":\"Oni\"},\"title\":\"TRIPLE HELIX\"}],\"exams\":[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[100]}]}");
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.DanSelectScene);
            yield return null;
            var scene = Object.FindFirstObjectByType<DanSelectScene>();
            var view = scene.view;
            Assert.That(scene.Courses.Count, Is.EqualTo(7));
            yield return new WaitForSecondsRealtime(0.3f);
            // The chip three to the right of the selected one.
            var target = view.chips.First(c => c.root.gameObject.activeSelf
                && Mathf.Abs(c.root.anchoredPosition.x - (DanSelectScene.RibbonCenter + 3 * DanSelectScene.RibbonSpacing)) < 0.5f);
            target.hit.Clicked();
            Assert.That(scene.Flow.Selected, Is.EqualTo(3));
            Assert.That(scene.Flow.State, Is.EqualTo(DanSelectFlow.Phase.Browsing), "A tap on another chip only moves to it.");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(view.cursor.rectTransform.anchoredPosition.x, Is.EqualTo(DanSelectScene.RibbonCenter).Within(0.01f));
            Assert.That(target.root.anchoredPosition.x, Is.EqualTo(DanSelectScene.RibbonCenter).Within(0.01f));
            Assert.That(view.horiName.text, Is.EqualTo("4段"));
            // Left past the first course wraps to the last.
            for (int i = 0; i < 4; i++) scene.Ka(-1);
            Assert.That(scene.Flow.Selected, Is.EqualTo(6));
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(view.cursor.rectTransform.anchoredPosition.x, Is.EqualTo(DanSelectScene.RibbonCenter).Within(0.01f));
            Assert.That(SceneSwitcher.Instance.TwoPlayers, Is.False);
        }

        [UnityTest]
        public IEnumerator TwoPCannotJoinOnceTheBoardIsPicked()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var entry = Object.FindFirstObjectByType<EntryScene>();
            yield return new WaitForSecondsRealtime(0.2f);
            entry.Don();
            yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 3);
            yield return new WaitForSecondsRealtime(1f);
            entry.Ka(1);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(entry.view.boards[1].closed.color.r, Is.EqualTo(1), "Not greyed with 1P alone.");
            entry.Don();
            Assert.That(entry.Flow.IsSelected, Is.True);
            entry.Join2P();
            Assert.That(entry.Joined2P, Is.False);
            yield return WaitForScene(SceneSwitcher.DanSelectScene);
            Assert.That(SceneSwitcher.Instance.TwoPlayers, Is.False);
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
        }

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
