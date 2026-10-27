using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // A note coming in from the left (negative SCROLL) is not cut at the lane's left edge: like
    // OpenTaiko it is drawn until it leaves the screen and slides under the player cover.
    public sealed class LaneLeftEdgeFlowTests
    {
        [UnityTest]
        public IEnumerator NotesFromTheLeftAreDrawnPastTheLaneEdge()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Left Edge\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n0,\n#SCROLL -1\n1,\n#END");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            while (SceneSwitcher.Instance.IsInputBlocked) yield return null;
            SceneSwitcher.Instance.Play(song);
            float deadline = Time.realtimeSinceStartup + 20;
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene)
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
            yield return null;
            var play = Object.FindAnyObjectByType<PlayScene>();
            if (!play.IsPaused) play.TogglePause();
            play.pausePanel.SetActive(false);
            var lane = play.Lane1;
            int index = lane.Session.Chart.Notes.FindIndex(n => n.Kind == NoteKind.Don);
            double hit = lane.Session.Chart.Notes[index].Time;
            // Before the hit the note is left of the judge, moving right; find a frame where its
            // centre is left of the lane's edge (x < 0 in the note layer).
            bool drawnPastEdge = false;
            for (double t = hit - 0.6; t < hit && !drawnPastEdge; t += 0.005)
            {
                lane.RenderNotes(t, false, false);
                var root = lane.NoteRoot(index);
                if (root != null && root.anchoredPosition.x < 0) drawnPastEdge = true;
            }
            Assert.That(drawnPastEdge, Is.True, "A note half past the lane's left edge is still drawn.");
            var layer = play.noteLayer.GetComponentInParent<LaneSpriteLayer>();
            yield return null;
            Assert.That(layer.Material.GetVector("_ClipRect").x, Is.LessThan(-1e5f), "Nothing clips the lane's left edge.");
            Assert.That(play.noteLayer.parent.parent.Find("LaneForeground").GetComponent<Canvas>().sortingOrder, Is.GreaterThan(10),
                "The player cover is drawn over the notes.");
            Object.Destroy(song);
        }
    }
}
