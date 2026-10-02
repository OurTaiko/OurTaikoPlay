using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class NoteArcFlowTests
    {
        static readonly System.Reflection.FieldInfo HitKa =
            typeof(PlayScene).GetField("hitKa", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        [UnityTest]
        public IEnumerator HitNotesFlyToSoulBadgeAndBurst()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Arc\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:2,2\n#START\n1234,\n5008,\n7008,\n9008,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
                SceneSwitcher.Instance.Play(song);
                float deadline = Time.realtimeSinceStartup + 20;
                do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene);
                yield return null;
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var arcs = play.noteArcs; var session = play.Session;
                Assert.That(arcs, Is.Not.Null);
                // Player::draw paints the gauge before draw_overlays, so the arcs fly over it.
                Assert.That(arcs.transform.GetSiblingIndex(), Is.EqualTo(play.soulGauge.transform.GetSiblingIndex() + 1));
                string Sprite(int i) => arcs.ArcRoot(i).GetComponent<UnityEngine.UI.Image>().sprite.name;

                // Bad does not fly; good/ok notes fly as themselves.
                Assert.That(session.Hit(false, -0.09), Is.EqualTo(Judgment.Bad));
                Assert.That(arcs.ActiveCount, Is.Zero);
                session.Hit(true, 0.5); session.Hit(false, 1.0); session.Hit(true, 1.55);
                // Drumroll hits fly a small note of the drum that was hit.
                HitKa.SetValue(play, true); session.Hit(true, 2.5);
                HitKa.SetValue(play, false); session.Hit(false, 2.6);
                // A balloon flies once, when it pops; a kusudama never does.
                session.Hit(false, 4.1);
                Assert.That(arcs.ActiveCount, Is.EqualTo(5));
                session.Hit(false, 4.2);
                session.Hit(false, 6.1); session.Hit(false, 6.2);
                Assert.That(session.Resolved[6], Is.True);
                Assert.That(Enumerable.Range(0, arcs.ActiveCount).Select(Sprite),
                    Is.EqualTo(new[] { 2, 3, 4, 2, 1, 7 }.Select(k => play.noteSprites[k].name)));
                // Later arcs draw over earlier ones.
                for (int i = 1; i < arcs.ActiveCount; i++)
                    Assert.That(arcs.ArcRoot(i).GetSiblingIndex(), Is.GreaterThan(arcs.ArcRoot(i - 1).GetSiblingIndex()));

                // Start on the judge circle (lane-local 618,110) and land on the soul badge.
                var viewport = (RectTransform)arcs.transform.parent;
                Vector2 Local(Transform t) => viewport.InverseTransformPoint(t.position);
                Vector2 Centre(RectTransform r) => viewport.InverseTransformPoint(r.TransformPoint(r.rect.center));
                var lane = (RectTransform)play.noteLayer.parent.parent;
                double start = play.SongTime - SceneSwitcher.Instance.SelectedSong.audioOffsetMs / 1000.0;
                arcs.ShowTime(start);
                Assert.That(Vector2.Distance(Local(arcs.ArcRoot(0)), (Vector2)viewport.InverseTransformPoint(lane.TransformPoint(new Vector3(618, -110)))), Is.LessThan(0.1f));
                arcs.ShowTime(start + NoteArcPath.Duration / 2);
                Assert.That(Local(arcs.ArcRoot(0)).y, Is.EqualTo(Local(lane).y + 299).Within(1));
                var soul = (RectTransform)play.soulGauge.transform.Find("Soul");
                arcs.ShowTime(start + NoteArcPath.Duration - 1e-6);
                Assert.That(Vector2.Distance(Local(arcs.ArcRoot(0)), Centre(soul)), Is.LessThan(0.1f));
                var effect = arcs.gaugeHitEffect;
                Assert.That(effect.IsPlaying, Is.False);
                Assert.That(effect.burst.enabled || effect.note.enabled, Is.False);
                double landed = start + NoteArcPath.Duration;
                arcs.ShowTime(landed);
                Assert.That(arcs.ActiveCount, Is.Zero);
                Assert.That(arcs.GetComponentsInChildren<UnityEngine.UI.Image>().Length, Is.Zero, "Finished arcs are hidden.");

                // GaugeHitEffect: the last note to land replaces the burst, above the arcs.
                Assert.That(effect.transform.GetSiblingIndex(), Is.EqualTo(arcs.transform.GetSiblingIndex() + 1));
                Assert.That(effect.IsPlaying && effect.IsBig, Is.True);
                Assert.That(effect.note.sprite, Is.SameAs(play.noteSprites[7]));
                Assert.That(effect.burst.transform.GetSiblingIndex(), Is.LessThan(effect.note.transform.GetSiblingIndex()));
                Assert.That(Vector2.Distance(Centre(effect.burst.rectTransform), Centre(soul)), Is.LessThan(0.1f));
                Assert.That(Vector2.Distance(Centre(effect.note.rectTransform), Centre(soul)), Is.LessThan(0.1f));
                Assert.That(effect.burst.sprite.name, Is.EqualTo("GaugeHitEffect0"));
                Assert.That(effect.burst.rectTransform.rect.width, Is.EqualTo(232 * 0.8f).Within(0.01f));
                Assert.That((Color32)effect.burst.color, Is.EqualTo(new Color32(253, 249, 0, 255)));
                arcs.ShowTime(landed + 0.2);
                Assert.That(effect.burst.sprite.name, Is.EqualTo("GaugeHitEffect2"));
                Assert.That(effect.burst.rectTransform.rect.width, Is.EqualTo(232 * (0.8f + 0.7f * 0.08333f / 0.266f)).Within(0.01f));
                Assert.That((Color32)effect.burst.color, Is.EqualTo(new Color32(230, 41, 55, 255)));
                Assert.That(effect.note.color.a, Is.EqualTo(1));
                arcs.ShowTime(landed + 0.34);
                Assert.That(effect.note.color.a, Is.EqualTo(1 - 0.04f / 0.083f).Within(0.001f));
                Assert.That(effect.burst.color.a, Is.EqualTo(effect.note.color.a).Within(0.003f));
                arcs.ShowTime(landed + effect.GetComponent<ClipSampler>().clip.length);
                Assert.That(effect.IsPlaying || effect.burst.enabled || effect.note.enabled, Is.False);

                // A stream in flight over a burst 200 ms old, for the record.
                arcs.Spawn(play.noteSprites[3], true, start - NoteArcPath.Duration - 0.2);
                for (int i = 6; i >= 0; i--) arcs.Spawn(play.noteSprites[i % 2 == 0 ? 1 : 2], false, start - i * 0.07);
                arcs.ShowTime(start);
                Assert.That(arcs.ActiveCount, Is.EqualTo(7));
                Assert.That(effect.note.sprite, Is.SameAs(play.noteSprites[3]));
                SceneFlowTests.Capture("NoteArc.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
