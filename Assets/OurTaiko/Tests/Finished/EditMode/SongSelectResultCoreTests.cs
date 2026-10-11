using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class SongSelectResultCoreTests
    {
        static LumenClip Timeline(string name)
            => LumenClip.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/OurTaiko/Animations/" + name + ".txt").text);

        [Test]
        public void LumenTablesSampleLinearlyAndClamp()
        {
            var board = Timeline("song_board");
            Assert.That(board.Label("select_on"), Is.EqualTo(5));
            Assert.That(board.Label("select_off"), Is.EqualTo(30));
            Assert.That(board.Get("instance_board_center", 8, "sy"), Is.EqualTo(0.2168).Within(1e-9));
            Assert.That(board.Get("instance_board_center", 10.5, "sy"), Is.EqualTo(0.2168 + (1 - 0.2168) / 2).Within(1e-9));
            Assert.That(board.Get("instance_board_center", 99, "sy"), Is.EqualTo(0.2168).Within(1e-9), "Frames past the last row clamp.");
            Assert.That(board.Get("song_kanban_info", 15.5, "a"), Is.EqualTo(0.75).Within(1e-9));
            Assert.That(board.Get("missing", 1, "a"), Is.Null);
            var clouds = Timeline("result_bg");
            Assert.That((clouds.First, clouds.Last), Is.EqualTo((0.0, 719.0)));
            Assert.That(clouds.Get("#69@4", 30, "tx"), Is.EqualTo(1240 + (1214.6 - 1240) / 2).Within(1e-9));
            Assert.That(Timeline("result_bg_fuji").Get("huji_1p_l_mc/#84@0", 500, "sy"), Is.EqualTo(0.9).Within(0.001));
            Assert.That(Timeline("crown_mc").Label("end"), Is.EqualTo(120));
        }

        [Test]
        public void SongInfoListsCoursesLevelsAndBranches()
        {
            var triple = SongInfo.Read(File.ReadAllText("Assets/OurTaiko/Tests/Shared/Songs~/1 TripleHelix.tja"));
            Assert.That(triple.Title, Is.EqualTo("TRIPLE HELIX"));
            Assert.That(triple.Subtitle, Is.EqualTo("Yonokid"));
            Assert.That(triple.DemoStart, Is.EqualTo(120.001));
            Assert.That(triple.Courses.Select(c => (c.Difficulty, c.Level)),
                Is.EqualTo(new[] { (Difficulty.Easy, 2), (Difficulty.Normal, 4), (Difficulty.Hard, 6), (Difficulty.Oni, 9), (Difficulty.Ura, 10) }));
            Assert.That(triple.Course(Difficulty.Ura).Course, Is.EqualTo("Edit"));
            var branch = SongInfo.Read(File.ReadAllText("Assets/OurTaiko/Tests/Shared/Songs~/3 BranchTraining.tja"));
            Assert.That(branch.Course(Difficulty.Oni).IsBranching, Is.True);
            Assert.That(SongInfo.Read("TITLE:x\nCOURSE:3\nLEVEL:7\n#START\n1,\n#END").Course(Difficulty.Oni).Level, Is.EqualTo(7));
        }

        [Test]
        public void DifficultyCursorFollowsTheOriginalSongSelectRules()
        {
            var all = new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard, Difficulty.Oni, Difficulty.Ura };
            Assert.That(new DifficultyCursor(all, false, -1).Selected, Is.EqualTo(Difficulty.Back));
            Assert.That(new DifficultyCursor(all, false, (int)Difficulty.Ura).Selected, Is.EqualTo(Difficulty.Oni));
            Assert.That(new DifficultyCursor(new[] { Difficulty.Hard }, false, 0).Selected, Is.EqualTo(Difficulty.Hard), "Nothing at or below the last course falls back to the first.");
            Assert.That(new DifficultyCursor(new[] { Difficulty.Ura }, false, 3).IsUra, Is.True, "A standalone Edit is always visible.");

            var cursor = new DifficultyCursor(all, false, (int)Difficulty.Easy);
            cursor.Left(); Assert.That(cursor.Selected, Is.EqualTo(Difficulty.Modifier));
            cursor.Left(); Assert.That(cursor.Selected, Is.EqualTo(Difficulty.Back));
            cursor.Left(); Assert.That(cursor.Selected, Is.EqualTo(Difficulty.Back));
            cursor.Right(); cursor.Right(); cursor.Right(); cursor.Right(); cursor.Right();
            Assert.That(cursor.Selected, Is.EqualTo(Difficulty.Oni));
            for (int i = 0; i < 9; i++) Assert.That(cursor.Right(), Is.False);
            Assert.That(cursor.Right(), Is.True, "The tenth press flips Oni to Edit.");
            Assert.That((cursor.Selected, cursor.IsUra), Is.EqualTo((Difficulty.Ura, true)));
            Assert.That(cursor.Visible, Is.EqualTo(new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard, Difficulty.Ura }));

            var hardOnly = new DifficultyCursor(new[] { Difficulty.Easy, Difficulty.Hard }, false, 1);
            Assert.That(hardOnly.Selected, Is.EqualTo(Difficulty.Easy));
            hardOnly.Right(); hardOnly.Right();
            Assert.That(hardOnly.Selected, Is.EqualTo(Difficulty.Hard), "The last course does not wrap.");
        }

        [Test]
        public void ResultCrownGaugeAndMessageFollowTheResultScreen()
        {
            var result = new PlayResult { Good = 10, GaugePoints = 10000, IsClear = true, IsGaugeFull = true };
            Assert.That((result.ResultCrown, result.StoredCrown, result.Message, result.Cells),
                Is.EqualTo((Crown.DonderfulCombo, Crown.DonderfulCombo, ResultMessage.Perfect, 50)));
            result = new PlayResult { Good = 10, Ok = 2, GaugePoints = 4000 };
            Assert.That((result.ResultCrown, result.StoredCrown, result.Message), Is.EqualTo((Crown.None, Crown.FullCombo, ResultMessage.Near)));
            result = new PlayResult { Bad = 1, GaugePoints = 8100, IsClear = true };
            Assert.That((result.ResultCrown, result.GaugeState, result.Message, result.Cells), Is.EqualTo((Crown.Clear, GaugeState.Cleared, ResultMessage.Success, 40)));
            Assert.That(new PlayResult { GaugePoints = 3000 }.Message, Is.EqualTo(ResultMessage.Miss));
        }

        [Test]
        public void ResultSequenceUsesTheCabinetFrameCounts()
        {
            var result = new PlayResult { Good = 5, Bad = 1, GaugePoints = 4000, IsClear = false };
            var sequence = new ResultSequence(result);
            double start = ResultSequence.FadeInEndMs + ResultSequence.WaitGaugeMs;
            Assert.That(sequence.Update(start).Count, Is.Zero);
            Assert.That(sequence.Update(start + 1).Select(c => c.Cue), Is.EqualTo(new[] { ResultCue.CountLoopStart }));
            Assert.That(sequence.Update(start + 5 * ResultSequence.GaugeCellMs + 1).Count, Is.Zero);
            Assert.That(sequence.GaugeShown, Is.EqualTo(5));
            double filled = start + 20 * ResultSequence.GaugeCellMs + 1;
            Assert.That(sequence.Update(filled).Select(c => c.Cue), Is.EqualTo(new[] { ResultCue.CountLoopStop }));
            Assert.That(sequence.GaugeShown, Is.EqualTo(20));
            double scoreStart = filled + ResultSequence.WaitScoreMs + 1;
            sequence.Update(scoreStart);
            Assert.That(sequence.CrownAtMs, Is.EqualTo(scoreStart + ResultSequence.ScoreToCrownMs));
            Assert.That(sequence.MessageAtMs, Is.EqualTo(sequence.CrownAtMs), "A failed gauge skips the Crown state.");
            for (int row = 0; row < ResultSequence.Rows; row++)
            {
                var cues = sequence.Update(scoreStart + row * ResultSequence.RowMs + 1);
                Assert.That(cues.Single(), Is.EqualTo((ResultCue.RowLanded, row)));
            }
            Assert.That(sequence.Update(scoreStart + 4 * ResultSequence.RowMs + ResultSequence.ScoreMs - 1).Count, Is.Zero);
            Assert.That(sequence.Update(scoreStart + 4 * ResultSequence.RowMs + ResultSequence.ScoreMs + 1).Single().Cue, Is.EqualTo(ResultCue.ScoreLanded));
            var message = sequence.Update(sequence.MessageAtMs.Value);
            Assert.That(message.Select(c => c.Cue), Is.EqualTo(new[] { ResultCue.Message }), "No crown and no success backdrop for a failed gauge.");
            Assert.That(sequence.RevealEndMs, Is.EqualTo(scoreStart + ResultSequence.ScoreToCrownMs));
            Assert.That(sequence.Skip(), Is.False);
            sequence.Update(sequence.RevealEndMs + ResultSequence.WaitEffectEndMs + ResultSequence.WaitNextSceneMs);
            Assert.That(sequence.CanAdvance && !sequence.ShouldAutoAdvance, Is.True);
        }

        [Test]
        public void ResultSequenceSkipLandsEverythingAtOnce()
        {
            var result = new PlayResult { Good = 9, GaugePoints = 10000, IsClear = true, IsGaugeFull = true, Score = 1000, PreviousBest = 10 };
            var sequence = new ResultSequence(result);
            sequence.Update(ResultSequence.FadeInEndMs - 10);
            Assert.That(sequence.Skip(), Is.False, "The fade-in still blocks input.");
            double now = ResultSequence.FadeInEndMs + ResultSequence.EnableSkipMs + 1;
            sequence.Update(now);
            Assert.That(sequence.Skip(), Is.True);
            var cues = sequence.Update(now + 1).Select(c => c.Cue).ToList();
            Assert.That(sequence.GaugeShown, Is.EqualTo(50));
            Assert.That(cues.Contains(ResultCue.HighScore) && !cues.Contains(ResultCue.RowLanded), Is.True);
            cues.AddRange(sequence.Update(now + 2).Select(c => c.Cue));
            Assert.That(cues.Contains(ResultCue.Crown) && cues.Contains(ResultCue.Message) && cues.Contains(ResultCue.SuccessBackground), Is.True);
            Assert.That(sequence.RainbowStartMs.HasValue && sequence.RevealEndMs > 0, Is.True);
        }

        [Test]
        public void ScoreStoreKeepsBestScoreAndCrownButNotAutoPlay()
        {
            string path = Path.Combine(Application.temporaryCachePath, "editmode-scores.sqlite3");
            if (File.Exists(path)) File.Delete(path);
            try
            {
                var store = new ScoreStore(path);
                var first = new PlayResult { ChartKey = "Song", Difficulty = Difficulty.Hard, Score = 500, Bad = 0, Ok = 3 };
                store.Save(first);
                Assert.That(first.PreviousBest, Is.Zero);
                var second = new PlayResult { ChartKey = "Song", Difficulty = Difficulty.Hard, Score = 400, Bad = 2, IsClear = true };
                store.Save(second);
                Assert.That(second.PreviousBest, Is.EqualTo(500));
                store.Save(new PlayResult { ChartKey = "Song", Difficulty = Difficulty.Hard, Score = 999999, AutoPlay = true });
                var reloaded = new ScoreStore(path).Get("Song", Difficulty.Hard);
                Assert.That((reloaded.score, reloaded.crown), Is.EqualTo((500, Crown.FullCombo)));
                Assert.That(new ScoreStore(path).Get("Song", Difficulty.Oni), Is.Null);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
