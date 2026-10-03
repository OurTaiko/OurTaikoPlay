using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SongBestScoreTests
    {
        [UnityTest]
        public IEnumerator SavedWindowShowsBestRotatesCoursesAndKeepsAuthoredLayout()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(.5f);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            var song = select.FocusedSong;
            var info = song.ReadInfo();
            var courses = new[] { new CourseInfo { Difficulty = Difficulty.Oni }, new CourseInfo { Difficulty = Difficulty.Ura } };
            var view = select.view.bestScore;
            Assert.That(view, Is.Not.Null);
            Assert.That(view.group.alpha, Is.Zero, "No record yet.");
            Assert.That(view.group.blocksRaycasts, Is.False);
            var position = ((RectTransform)view.transform).anchoredPosition;
            ScoreStore.Shared.Save(new PlayResult { ChartKey = song.name, Difficulty = courses[0].Difficulty, Score = 1002540, Good = 853, Ok = 14, Bad = 2, Rolls = 35 });
            ScoreStore.Shared.Save(new PlayResult { ChartKey = song.name, Difficulty = courses[1].Difficulty, Score = 987650 });
            yield return null;
            Assert.That(view.group.alpha, Is.EqualTo(1), "The focused song shows its best before choosing a difficulty.");
            Assert.That(view.judgments.Text(0), Is.EqualTo("853"));
            Assert.That(view.judgments.Text(1), Is.EqualTo("14"));
            Assert.That(view.judgments.Text(2), Is.EqualTo("2"));
            Assert.That(view.judgments.Text(3), Is.EqualTo("35"));
            TestCapture.Capture("SongSelectBestScoreList.png");
            select.Right();
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(view.group.alpha, Is.Zero, "Changing to an unplayed song clears the previous record.");
            select.Left();
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(view.group.alpha, Is.EqualTo(1));
            select.Confirm();
            for (int i = 0; i < 120 && select.CourseFade < 1; i++) yield return null;
            yield return null;
            Assert.That(view.group.alpha, Is.EqualTo(1).Within(.01));
            Assert.That(view.DisplayedScore, Is.EqualTo(1002540).Or.EqualTo(987650));
            TestCapture.Capture("SongSelectBestScore.png");
            select.enabled = false; // Drive the clock deterministically for the rotation checks.
            view.cycleSeconds = .5f;
            ScoreStore.Shared.Save(new PlayResult { ChartKey = song.name, Difficulty = courses[0].Difficulty, Score = 1002540, Good = 853, Ok = 14, Bad = 2, Rolls = 35 });
            if (!info.Has(Difficulty.Ura)) info.Courses.Add(courses[1]);
            view.Show(song, info, 1, 100);
            Assert.That(view.DisplayedScore, Is.EqualTo(1002540));
            view.Show(song, info, 1, 100.6);
            Assert.That(view.DisplayedScore, Is.EqualTo(987650));
            Assert.That(view.digits.Count(d => d.enabled), Is.EqualTo(6));
            Assert.That(((RectTransform)view.transform).anchoredPosition, Is.EqualTo(position));
            var other = ScriptableObject.CreateInstance<SongDefinition>();
            other.name = "BestScorePriorityTest";
            try
            {
                var all = new SongInfo();
                foreach (var d in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard, Difficulty.Oni, Difficulty.Ura })
                    all.Courses.Add(new CourseInfo { Difficulty = d });
                void Save(Difficulty d, int score) => ScoreStore.Shared.Save(new PlayResult { ChartKey = other.name, Difficulty = d, Score = score });
                view.Show(other, all, 1, 200);
                Assert.That(view.HasRecord, Is.False);
                Save(Difficulty.Easy, 999999); Save(Difficulty.Normal, 200); Save(Difficulty.Hard, 100);
                view.Show(other, all, 1, 201);
                Assert.That(view.DisplayedDifficulty, Is.EqualTo(Difficulty.Hard), "Fallback uses difficulty, not numeric score.");
                view.Show(other, all, 1, 202);
                Assert.That(view.DisplayedDifficulty, Is.EqualTo(Difficulty.Hard), "Lower courses never rotate.");
                Save(Difficulty.Ura, 50);
                view.Show(other, all, 1, 203);
                Assert.That(view.DisplayedDifficulty, Is.EqualTo(Difficulty.Ura));
                view.Show(other, all, 1, 204);
                Assert.That(view.DisplayedDifficulty, Is.EqualTo(Difficulty.Ura));
                Save(Difficulty.Oni, 40);
                view.Show(other, all, 1, 205);
                Assert.That(view.DisplayedDifficulty, Is.EqualTo(Difficulty.Oni));
                view.Show(other, all, 1, 205.6);
                Assert.That(view.DisplayedDifficulty, Is.EqualTo(Difficulty.Ura));
            }
            finally { Object.Destroy(other); }
            view.Show(null, null, 1, 206);
            Assert.That(view.group.alpha, Is.Zero, "Folders and Back have no score window.");
            view.Show(song, info, 0, 102);
            Assert.That(view.group.alpha, Is.Zero);
        }
    }
}
