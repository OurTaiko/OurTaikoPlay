using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class HitFaceFlowTests
    {
        [UnityTest]
        public IEnumerator FaceAppearsOnlyForGoodAndOk()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            // Dons at 0 and 0.5 s, big don at 1 s; roll 2–3 s; don at 4 s.
            song.chart = new TextAsset("TITLE:Hit Face\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1130,\n5080,\n1000,\n#END");
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
                var face = play.hitFace;
                var ring = play.hitRing;
                Assert.That(face.image.enabled || ring.image.enabled, Is.False);

                // The session is driven directly; the view is timed from the frozen song clock.
                double now = play.SongTime;

                // A timed-out note is 不可 and draws no face.
                play.Session.Advance(0.2, false);
                Assert.That(play.Session.Missed[0], Is.True);
                face.ShowTime(now);
                ring.ShowTime(now);
                Assert.That(face.IsPlaying || face.image.enabled || ring.IsPlaying, Is.False, "A miss must not show the face or ring.");

                // A hit judged 不可 draws no face either.
                Assert.That(play.Session.Hit(false, 0.6), Is.EqualTo(Judgment.Bad));
                face.ShowTime(now);
                ring.ShowTime(now);
                Assert.That(face.IsPlaying || face.image.enabled || ring.IsPlaying || ring.image.enabled, Is.False, "不可 must not show the face or ring.");

                // 良 on a big note uses the big face and follows animation 28.
                Assert.That(play.Session.Hit(false, 1.0), Is.EqualTo(Judgment.Good));
                face.ShowTime(now + 0.1);
                Assert.That(face.image.enabled, Is.True);
                Assert.That(face.image.sprite, Is.SameAs(face.goodBig));
                Assert.That(face.image.color.a, Is.EqualTo(1).Within(0.001));
                ring.ShowTime(now + 0.1);
                Assert.That(ring.image.enabled, Is.True);
                Assert.That(ring.image.sprite.name, Is.EqualTo("HitRing_outer_good_big3"));
                Assert.That(ring.image.color.a, Is.EqualTo(1).Within(0.001));
                ring.ShowTime(now + 0.201);
                Assert.That(ring.image.enabled, Is.False, "The ring is gone after 200 ms.");
                face.ShowTime(now + 0.36);
                Assert.That(face.image.enabled, Is.False, "The face is removed after 350 ms.");

                // Roll hits draw no face.
                Assert.That(play.Session.Hit(false, 2.5), Is.EqualTo(Judgment.Roll));
                face.ShowTime(now + 0.36);
                ring.ShowTime(now + 0.36);
                Assert.That(face.IsPlaying || face.image.enabled || ring.IsPlaying || ring.image.enabled, Is.False, "Roll hits must not show the face or ring.");

                // 可 on a small note shows the small ok face.
                Assert.That(play.Session.Hit(false, 4.05), Is.EqualTo(Judgment.Ok));
                face.ShowTime(now);
                Assert.That(face.image.enabled, Is.True);
                Assert.That(face.image.sprite, Is.SameAs(face.ok));
                ring.ShowTime(now);
                Assert.That(ring.image.enabled, Is.True);
                Assert.That(ring.image.sprite.name, Is.EqualTo("HitRing_outer_ok0"));
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
