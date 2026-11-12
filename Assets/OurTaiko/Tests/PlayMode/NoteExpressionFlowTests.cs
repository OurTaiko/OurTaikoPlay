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

        [UnityTest, Category("SpriteRendering")] public IEnumerator SinglePlayExpressions() => Check(false);
        [UnityTest, Category("SpriteRendering")] public IEnumerator PracticeExpressions() => Check(true);

        IEnumerator Load(bool practice, string body)
        {
            song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Expressions\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:5,5\n#START\n" + body + "\n#END");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            SceneSwitcher.Instance.PracticeMode = practice;
            yield return TestPlay.Open(song);
            string scene = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
            float deadline = Time.realtimeSinceStartup + 25;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
            play = Object.FindAnyObjectByType<PlayScene>();
            if (!play.IsPaused) play.TogglePause();
            play.pausePanel.SetActive(false);
        }

        void Draw(double time) => Render.Invoke(play, new object[] { time });
        Sprite Head(int index) => play.NoteRoot(index).Find("Head").GetComponent<LaneSprite>().sprite;
        // Big notes rest open-mouthed: their first crop is open, their second closed. Every note
        // opens its mouth on the same steps.
        static bool RestsOpen(int kind) => kind is 3 or 4 or 6 or 16;
        Sprite Mouth(int kind, bool open) => open == RestsOpen(kind) ? play.noteSprites[kind] : play.alternateNoteSprites[kind];

        IEnumerator Check(bool practice)
        {
            yield return Load(practice, new string('1', 50) + ",\n1,\n2,\n3,\n4,\n5008,\n6008,\n7008,\n9008,\n#BPMCHANGE 240\n1,");
            var notes = play.Session.Chart.Notes;

            Draw(2.0);
            Assert.That(Head(50), Is.SameAs(play.noteSprites[1]), "Before 10 combo the face stays neutral.");
            for (int i = 0; i < 50; i++) Assert.That(play.Session.Hit(false, notes[i].Time), Is.EqualTo(Judgment.Good));
            Assert.That(play.Session.Combo, Is.EqualTo(50));
            // The same render time must update after a judgment changes the combo.
            Draw(2.0);
            Assert.That(Head(50), Is.SameAs(play.alternateNoteSprites[1]));
            for (int i = 50; i < 58; i++)
            {
                int kind = (int)notes[i].Kind;
                // 50 combo: sixteenths, a quarter of a second per beat at 120 BPM.
                Draw(notes[i].Time + 0.125);
                Assert.That(Head(i), Is.SameAs(Mouth(kind, false)), $"kind {kind} closed");
                Draw(notes[i].Time + 0.0625);
                Assert.That(Head(i), Is.SameAs(Mouth(kind, true)), $"kind {kind} open");
                if (kind != 9) Assert.That(play.alternateNoteSprites[kind], Is.Not.SameAs(play.noteSprites[kind]));
                else Assert.That(play.alternateNoteSprites[kind], Is.SameAs(play.noteSprites[kind]), "Nijiiro kusudama repeats the same crop.");
                if (kind == 5 || kind == 6)
                {
                    Assert.That(play.NoteRoot(i).Find("RollBody").GetComponent<LaneSprite>().sprite, Is.SameAs(play.rollBodySprites[kind - 5]));
                    Assert.That(play.NoteRoot(i).Find("RollTail").GetComponent<LaneSprite>().sprite, Is.SameAs(play.rollTailSprites[kind - 5]));
                }
            }
            Draw(notes[58].Time + 0.05);
            Assert.That(Head(58), Is.SameAs(play.alternateNoteSprites[1]), "The new BPM applies at its command.");
            // Seek back and reuse a pooled view: phase depends only on chart time.
            Draw(2.25);
            var pausedSprite = Head(50);
            Draw(2.25);
            Assert.That(Head(50), Is.SameAs(pausedSprite), "Repeated sampling at a frozen chart time holds the frame.");
            Draw(0.49);
            Assert.That(Head(50), Is.SameAs(play.noteSprites[1]), "10 combo (note 10, beat 0.72) starts the faces on beat 1.");
            play.Session.Advance(2.2, false);
            Assert.That(play.Session.Combo, Is.Zero);
            Draw(2.25);
            Assert.That(Head(50), Is.SameAs(play.noteSprites[1]), "A miss restores neutral expressions immediately.");
            Draw(notes[52].Time + 0.125);
            Assert.That(Head(52), Is.SameAs(play.noteSprites[3]), "A big don rests angry and open-mouthed.");
        }

        // From 100 combo don, ka, the small roll and the balloon switch between the angry face and
        // the closed one every sixteenth; big notes have no other angry face and keep opening and
        // closing their mouths in step with them.
        [UnityTest, Category("SpriteRendering")]
        public IEnumerator AngryFacesFromOneHundredCombo()
        {
            yield return Load(false, new string('1', 100) + ",\n1,\n2,\n5008,\n7008,\n3,");
            var notes = play.Session.Chart.Notes;
            for (int i = 0; i < 99; i++) Assert.That(play.Session.Hit(false, notes[i].Time), Is.EqualTo(Judgment.Good));
            Draw(notes[100].Time + 0.0625);
            Assert.That(Head(100), Is.SameAs(play.alternateNoteSprites[1]), "99 combo still opens the mouth on sixteenths.");
            Assert.That(play.Session.Hit(false, notes[99].Time), Is.EqualTo(Judgment.Good));
            Assert.That(play.Session.Combo, Is.EqualTo(100));
            for (int i = 100; i < 105; i++)
            {
                int kind = (int)notes[i].Kind;
                bool hasAngry = kind is 1 or 2 or 5 or 7;
                Draw(notes[i].Time + 0.0625);
                Assert.That(Head(i), Is.SameAs(hasAngry ? play.angryNoteSprites[kind] : Mouth(kind, true)), $"kind {kind} on the sixteenth");
                if (hasAngry) Assert.That(play.angryNoteSprites[kind], Is.Not.Null.And.Not.SameAs(play.alternateNoteSprites[kind]));
                Draw(notes[i].Time + 0.125);
                Assert.That(Head(i), Is.SameAs(Mouth(kind, false)), $"kind {kind} off the sixteenth");
            }
        }
    }
}
