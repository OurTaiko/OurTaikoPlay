using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class NoteMojiFlowTests
    {
        static readonly System.Reflection.MethodInfo Render =
            typeof(PlayScene).GetMethod("RenderNotes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        [UnityTest]
        public IEnumerator MojiFollowsItsNoteAboveTheNoteLayer()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Moji\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n11101120,\n50000008,\n1,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
                SceneSwitcher.Instance.Play(song);
                float deadline = Time.realtimeSinceStartup + 20;
                do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene);
                yield return null;
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var notes = play.Session.Chart.Notes;
                string Frame(Transform part) => part.GetComponent<UnityEngine.UI.Image>().sprite.name;

                // ドドドン, ドドカッ (eighths), then 連打ー … ーっ!! and a lone ドン.
                // Only notes on the lane own pooled text views; show each one at its judge time.
                string FrameAt(int i) { Render.Invoke(play, new object[] { notes[i].Time }); return Frame(play.MojiRoot(i).Find("Head")); }
                Assert.That(Enumerable.Range(0, notes.Count).Select(FrameAt),
                    Is.EqualTo(new[] { "Moji1", "Moji1", "Moji0", "Moji1", "Moji1", "Moji3", "Moji7", "Moji0" }));
                Render.Invoke(play, new object[] { 0.0 });
                Canvas.ForceUpdateCanvases();
                var roll = play.MojiRoot(6);
                Assert.That(Frame(roll.Find("Tail")), Is.EqualTo("Moji10"));
                Assert.That(roll.Find("Mid").GetSiblingIndex(), Is.LessThan(roll.Find("Head").GetSiblingIndex()));
                Assert.That(roll.Find("Head").GetSiblingIndex(), Is.LessThan(roll.Find("Tail").GetSiblingIndex()));

                // Second pass of draw_notes: all text above all notes, earlier text over later text.
                Assert.That(play.mojiLayer.parent.GetSiblingIndex(), Is.EqualTo(play.noteLayer.parent.GetSiblingIndex() + 1));
                for (int i = 1; i <= 6; i++)
                    Assert.That(play.MojiRoot(i - 1).GetSiblingIndex(), Is.GreaterThan(play.MojiRoot(i).GetSiblingIndex()));
                for (int i = 1; i <= 6; i++)
                    Assert.That(play.NoteRoot(i - 1).GetSiblingIndex(), Is.GreaterThan(play.NoteRoot(i).GetSiblingIndex()));

                for (int i = 0; i < 6; i++)
                {
                    var note = play.NoteRoot(i); var moji = play.MojiRoot(i);
                    Assert.That(moji.gameObject.activeSelf, Is.True);
                    // skin moji.y=209 vs notes.y=14: 195 between tops, 123 between centres.
                    Assert.That(moji.anchoredPosition, Is.EqualTo(note.anchoredPosition - new Vector2(0, 123)));
                    Assert.That(moji.rect.size, Is.EqualTo(new Vector2(256, 48)));
                }
                SceneFlowTests.Capture("NoteMoji.png");

                // A hit note takes its text with it; a missed one keeps both scrolling.
                Assert.That(play.Session.Hit(false, notes[0].Time), Is.EqualTo(Judgment.Good));
                play.Session.Advance(notes[1].Time + 0.2, false);
                Assert.That(play.Session.Missed[1], Is.True);
                Render.Invoke(play, new object[] { notes[1].Time + 0.2 });
                Assert.That(play.NoteRoot(0) == null && play.MojiRoot(0) == null, Is.True);
                Assert.That(play.NoteRoot(1) != null && play.MojiRoot(1) != null, Is.True);

                // The roll strip spans head to tail: native 8 px plus the roll length.
                var rollNote = notes[6];
                Render.Invoke(play, new object[] { rollNote.Time - 0.2 });
                Canvas.ForceUpdateCanvases();
                roll = play.MojiRoot(6);
                float length = (float)NoteScroll.RollLength(rollNote, play.noteLayer.rect.width - 120);
                Assert.That(roll.Find("Mid").GetComponent<RectTransform>().rect.width, Is.EqualTo(8 + length).Within(0.01));
                Assert.That(((RectTransform)roll.Find("Tail")).anchoredPosition.x, Is.EqualTo(length).Within(0.01));
                Assert.That(roll.anchoredPosition.x, Is.EqualTo(play.NoteRoot(6).anchoredPosition.x).Within(0.01));

                // ドロン hides the text with the note.
                notes[7].Display = false;
                Render.Invoke(play, new object[] { notes[7].Time - 0.5 });
                Assert.That(play.MojiRoot(7), Is.Null);
                play.Back();
                float leavingDeadline = Time.realtimeSinceStartup + 20;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.SongSelectScene)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(leavingDeadline));
                    yield return null;
                }
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
