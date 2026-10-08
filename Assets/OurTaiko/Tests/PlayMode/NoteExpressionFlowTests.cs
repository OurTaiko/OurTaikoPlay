using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OurTaiko.Tests
{
    public sealed class NoteExpressionFlowTests
    {
        static readonly MethodInfo Render = typeof(PlayScene).GetMethod("RenderNotes", BindingFlags.Instance | BindingFlags.NonPublic);
        SongDefinition song;
        PlayScene play;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (play != null) Object.Destroy(play.gameObject);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            if (song != null) { Object.Destroy(song.chart); Object.Destroy(song); }
        }

        [UnityTest] public IEnumerator SinglePlayExpressions() => Check(false);
        [UnityTest] public IEnumerator PracticeExpressions() => Check(true);

        IEnumerator Check(bool practice)
        {
            song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Expressions\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:5,5\n#START\n"
                + new string('1', 50) + ",\n1,\n2,\n3,\n4,\n5008,\n6008,\n7008,\n9008,\n#BPMCHANGE 240\n1,\n#END");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            SceneSwitcher.Instance.PracticeMode = practice;
            SceneSwitcher.Instance.Play(song);
            string scene = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
            float deadline = Time.realtimeSinceStartup + 25;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
            play = Object.FindFirstObjectByType<PlayScene>();
            if (!play.IsPaused) play.TogglePause();
            play.pausePanel.SetActive(false);
            var notes = play.Session.Chart.Notes;
            void Draw(double time) => Render.Invoke(play, new object[] { time });
            Sprite Head(int index) => play.NoteRoot(index).Find("Head").GetComponent<Image>().sprite;

            Draw(2.25);
            Assert.That(Head(50), Is.SameAs(play.noteSprites[1]), "Before 50 combo the face stays neutral.");
            for (int i = 0; i < 50; i++) Assert.That(play.Session.Hit(false, notes[i].Time), Is.EqualTo(Judgment.Good));
            Assert.That(play.Session.Combo, Is.EqualTo(50));
            // The same render time must update after a judgment changes the combo.
            Draw(2.25);
            Assert.That(Head(50), Is.SameAs(play.alternateNoteSprites[1]));
            for (int i = 50; i < 58; i++)
            {
                int kind = (int)notes[i].Kind;
                Draw(notes[i].Time + 0.125);
                Assert.That(Head(i), Is.SameAs(play.noteSprites[kind]));
                Draw(notes[i].Time + 0.25);
                Assert.That(Head(i), Is.SameAs(play.alternateNoteSprites[kind]));
                if (kind != 9) Assert.That(play.alternateNoteSprites[kind], Is.Not.SameAs(play.noteSprites[kind]));
                else Assert.That(play.alternateNoteSprites[kind], Is.SameAs(play.noteSprites[kind]), "Nijiiro kusudama repeats the same crop.");
                if (kind == 5 || kind == 6)
                {
                    Assert.That(play.NoteRoot(i).Find("RollBody").GetComponent<Image>().sprite, Is.SameAs(play.rollBodySprites[kind - 5]));
                    Assert.That(play.NoteRoot(i).Find("RollTail").GetComponent<Image>().sprite, Is.SameAs(play.rollTailSprites[kind - 5]));
                }
            }
            Draw(notes[58].Time + 0.125);
            Assert.That(Head(58), Is.SameAs(play.alternateNoteSprites[1]), "The new BPM applies at its command.");
            // Seek back and reuse a pooled view: phase depends only on chart time.
            Draw(2.25);
            var pausedSprite = Head(50);
            Draw(2.25);
            Assert.That(Head(50), Is.SameAs(pausedSprite), "Repeated sampling at a frozen chart time holds the frame.");
            Draw(2.1);
            Assert.That(Head(50), Is.SameAs(play.noteSprites[1]));
            play.Session.Advance(2.2, false);
            Assert.That(play.Session.Combo, Is.Zero);
            Draw(2.25);
            Assert.That(Head(50), Is.SameAs(play.noteSprites[1]), "A miss restores neutral expressions immediately.");
        }
    }
}
