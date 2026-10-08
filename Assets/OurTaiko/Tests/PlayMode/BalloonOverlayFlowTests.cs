using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class BalloonOverlayFlowTests
    {
        [UnityTest]
        public IEnumerator InflatedBalloonDrawsAboveGaugeInSinglePlay() => CheckOverlay(false);

        [UnityTest]
        public IEnumerator InflatedBalloonDrawsAboveGaugeInPractice() => CheckOverlay(true);

        static IEnumerator CheckOverlay(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Balloon Overlay\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:100\n#START\n7008,\n0000,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.PracticeMode = practice;
                SceneSwitcher.Instance.Play(song, "Oni", false);
                float deadline = Time.realtimeSinceStartup + 25;
                string target = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
                while (SceneSwitcher.Instance.IsInputBlocked || SceneManager.GetActiveScene().name != target)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                yield return null;
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                if (practice) play.practiceView.panel.SetActive(false);
                var view = play.balloonCounter;
                var lane = (RectTransform)play.noteLayer.parent.parent;
                view.RecordHit(0, 100, 99, 10, 0);
                view.ShowTime(.2);
                Assert.That(view.body.sprite, Is.SameAs(view.bodyFrames[6]));
                Assert.That(view.transform.parent, Is.SameAs(lane.parent));
                Assert.That(view.transform.GetSiblingIndex(), Is.LessThan(play.pauseButton.transform.GetSiblingIndex()));
                Assert.That(lane.parent.GetSiblingIndex(), Is.LessThan(play.pausePanel.transform.GetSiblingIndex()),
                    "The pause menu must remain above the gameplay viewport.");

                void Verify(Camera camera)
                {
                    int gaugeDepth = play.soulGauge.GetComponentsInChildren<UnityEngine.UI.Graphic>()
                        .Max(graphic => graphic.depth);
                    foreach (var graphic in view.GetComponentsInChildren<UnityEngine.UI.Graphic>())
                        Assert.That(graphic.depth, Is.GreaterThan(gaugeDepth),
                            graphic.name + " must render above all gauge graphics.");
                    Vector3 bodyOrigin = lane.InverseTransformPoint(view.body.transform.TransformPoint(Vector3.zero));
                    Assert.That(bodyOrigin.x, Is.EqualTo(645).Within(.01));
                    Assert.That(bodyOrigin.y, Is.EqualTo(81).Within(.01), "Lifting draw order must preserve the original position.");
                }
                TestCapture.Capture("BalloonAboveGauge-" + target + ".png", 1920, 1080, Verify);
                TestCapture.Capture("BalloonAboveGauge-" + target + "-720.png", 1280, 720, Verify);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
