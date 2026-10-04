using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class ScoreRankTests
    {
        [TestCase(-1, 0)] [TestCase(499999, 0)] [TestCase(500000, 1)]
        [TestCase(599999, 1)] [TestCase(600000, 2)] [TestCase(699999, 2)] [TestCase(700000, 3)]
        [TestCase(799999, 3)] [TestCase(800000, 4)] [TestCase(899999, 4)] [TestCase(900000, 5)]
        [TestCase(949999, 5)] [TestCase(950000, 6)] [TestCase(999999, 6)]
        [TestCase(1000000, 7)] [TestCase(int.MaxValue, 7)]
        public void ScoreThresholds(int score, int rank) => Assert.That(ScoreRank.FromScore(score), Is.EqualTo(rank));

        [TestCase(false)] [TestCase(true)]
        public void RankPrecedesCrownEvenOnFailedGauge(bool clear)
        {
            var sequence = new ResultSequence(new PlayResult { Score = 950000, IsClear = clear });
            sequence.Update(1300); sequence.Update(2300);
            Assert.That(sequence.CrownAtMs - sequence.RankAtMs, Is.EqualTo(ResultSequence.RankDurationMs).Within(.001));
            var cues = sequence.Update(sequence.RankAtMs.Value).Select(c => c.Cue).ToArray();
            Assert.That(cues, Has.Member(ResultCue.ScoreRank));
            Assert.That(cues, Has.No.Member(ResultCue.Crown));
            Assert.That(sequence.Update(sequence.RankAtMs.Value + 1).Select(c => c.Cue), Has.No.Member(ResultCue.ScoreRank));
            cues = sequence.Update(sequence.CrownAtMs.Value).Select(c => c.Cue).ToArray();
            Assert.That(cues.Contains(ResultCue.Crown), Is.EqualTo(clear));
        }

        [TestCase(false)] [TestCase(true)]
        public void SkipSettlesRankWithoutReplayingItsSound(bool duringRank)
        {
            var sequence = new ResultSequence(new PlayResult { Score = 950000, IsClear = true });
            sequence.Update(1300); sequence.Update(2300);
            if (duringRank) sequence.Update(sequence.RankAtMs.Value + 200);
            Assert.That(sequence.Skip(), Is.True);
            var cues = sequence.Update(sequence.Now + 1).Select(c => c.Cue).ToArray();
            Assert.That(cues, Has.No.Member(ResultCue.ScoreRank));
            Assert.That(sequence.RankAtMs, Is.LessThanOrEqualTo(sequence.Now));
            Assert.That(sequence.RevealEndMs, Is.GreaterThan(0));
        }

        [Test]
        public void SevenSharedPrefabsAndAllAnimationFramesKeepTheSelectedRank()
        {
            const string folder = "Assets/OurTaiko/Generated/ScoreRank/";
            Assert.That(AssetDatabase.FindAssets("t:Prefab", new[] { folder.TrimEnd('/') }).Length, Is.EqualTo(7));
            Assert.That(AssetDatabase.IsValidFolder("Assets/OurTaiko/Art/song_select/yellow_box/score_rank"), Is.False);
            var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Result.unity");
                var view = Object.FindFirstObjectByType<ResultScene>().view.scoreRank;
                var shapes = new[] { "s74", "s76", "s78", "s80", "s82", "s84", "s86" };
                for (int rank = 1; rank <= 7; rank++)
                {
                    Assert.That(PrefabUtility.IsPartOfPrefabAsset(view.icons[rank - 1]), Is.True);
                    foreach (double seconds in new[] { .2, .5, 1.0, 1.98 })
                    {
                        view.Show(rank, Difficulty.Oni, seconds);
                        var visible = view.animationView.layers.Where(i => i.enabled && i.color.a > 0 && i.sprite != null).ToArray();
                        Assert.That(visible.Any(i => i.sprite.name == shapes[rank - 1]), Is.True, $"Rank {rank} at {seconds}");
                        Assert.That(visible.Where(i => shapes.Contains(i.sprite.name)).All(i => i.sprite.name == shapes[rank - 1]), Is.True);
                        Assert.That(visible.All(i => !i.raycastTarget), Is.True);
                    }
                }
                view.Show(0);
                Assert.That(view.animationView.layers.All(i => !i.enabled), Is.True);
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SongSelect.unity");
                var select = Object.FindFirstObjectByType<SongSelectScene>();
                foreach (var display in select.view.songBoards.Select(b => b.scoreRank).Concat(select.view.cards.Select(c => c.scoreRank)))
                {
                    for (int rank = 1; rank <= 7; rank++)
                    {
                        display.Show(rank);
                        Assert.That(display.image.enabled, Is.True);
                        Assert.That(display.image.sprite.name, Is.EqualTo(shapes[rank - 1]));
                    }
                    display.Show(0);
                    Assert.That(display.image.enabled, Is.False);
                }
            }
            finally
            {
                if (setup.Any(s => s.isLoaded)) UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup);
                else UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            }
        }
    }
}
