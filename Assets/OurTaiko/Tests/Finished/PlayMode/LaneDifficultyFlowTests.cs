using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class LaneDifficultyFlowTests
    {
        // Player::draw: lane/lane_difficulty frame = difficulty, so the icon follows the chosen course.
        [UnityTest]
        public IEnumerator LaneIconShowsThePlayedDifficulty(
            [Values("Easy", "Normal", "Hard", "Oni", "Edit")] string course)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            var chart = new System.Text.StringBuilder("TITLE:Lane\nBPM:120\n");
            foreach (string c in new[] { "Easy", "Normal", "Hard", "Oni", "Edit" })
                chart.Append("COURSE:" + c + "\nLEVEL:5\n#START\n1000,\n#END\n");
            song.chart = new TextAsset(chart.ToString());
            SceneSwitcher.EnsureInstance().Select(song, course, false);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session, Is.Not.Null, "The chart should load.");
            Assert.That(play.laneDifficultySprites, Has.Length.EqualTo(5));
            var difficulty = SongInfo.DifficultyOf(course).Value;
            Assert.That(play.laneDifficulty.sprite, Is.SameAs(play.laneDifficultySprites[(int)difficulty]));
            Assert.That(play.laneDifficulty.sprite.name, Is.EqualTo("LaneDifficulty" + (int)difficulty));
        }
    }
}
