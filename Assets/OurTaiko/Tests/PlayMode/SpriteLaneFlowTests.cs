using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SpriteLaneFlowTests
    {
        [UnityTest, Category("SpriteRendering")] public IEnumerator SinglePlaySpriteLayers() => Verify(false);
        [UnityTest, Category("SpriteRendering")] public IEnumerator PracticeSpriteLayers() => Verify(true);

        IEnumerator Verify(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Sprite Layers\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:5\n#START\n1122,\n5008,\n7008,\n#SCROLL -1\n6008,\n#SCROLL 1+1i\n1,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.PracticeMode = practice;
                SceneSwitcher.Instance.Play(song);
                float deadline = Time.realtimeSinceStartup + 25;
                string scene = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                var play = Object.FindAnyObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                if (practice) play.practiceView.panel.SetActive(false);
                var canvas = play.noteLayer.GetComponentInParent<Canvas>().rootCanvas;
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
                Assert.That(play.noteLayer.GetComponentsInChildren<UnityEngine.UI.Graphic>(true), Is.Empty);
                Assert.That(play.noteLayer.GetComponentsInChildren<CanvasRenderer>(true), Is.Empty);
                Assert.That(play.mojiLayer.GetComponentsInChildren<CanvasRenderer>(true), Is.Empty);
                var pass = play.noteLayer.GetComponent<LaneSpriteLayer>();
                Assert.That(play.hitFace.GetComponentInParent<Canvas>().sortingOrder,
                    Is.LessThan(pass.GetComponent<SortingGroup>().sortingOrder));
                Assert.That(play.hitRing.GetComponentInParent<Canvas>().sortingOrder,
                    Is.GreaterThan(play.mojiLayer.GetComponent<SortingGroup>().sortingOrder));
                Assert.That(play.balloonCounter.GetComponentInParent<Canvas>().sortingOrder,
                    Is.GreaterThan(play.scoreCounter.GetComponentInParent<Canvas>().sortingOrder));

                var render = typeof(PlayScene).GetMethod("RenderNotes", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var time in new[] { 0.0, 2.0, 4.0, 7.0, 7.95, 8.5, 0.0 })
                {
                    render.Invoke(play, new object[] { time });
                    Canvas.ForceUpdateCanvases();
                    foreach (var sprite in play.noteLayer.GetComponentsInChildren<LaneSprite>())
                    {
                        Assert.That(sprite.Renderer.sharedMaterial, Is.SameAs(pass.Material));
                        Assert.That(sprite.sprite.vertices.Length, Is.EqualTo(4), "Full rectangles preserve the original UI sprite sampling.");
                        Assert.That(sprite.GetComponent<CanvasRenderer>(), Is.Null);
                    }
                }
                foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(2560, 1080) })
                {
                    TestCapture.Capture("SpriteLayers-" + scene + "-" + size.x + ".png", size.x, size.y, camera =>
                    {
                        var corners = new Vector3[4];
                        pass.clipRect.GetWorldCorners(corners);
                        var clip = pass.Material.GetVector("_ClipRect");
                        // Only the right edge: notes past the left edge slide under the cover, and notes
                        // taken off the lane vertically stay drawn (OpenTaiko).
                        Assert.That(clip.x, Is.LessThan(corners[0].x - 10000));
                        Assert.That(clip.z, Is.EqualTo(corners[2].x).Within(1e-5));
                        Assert.That(clip.y, Is.LessThan(corners[0].y - 10000));
                        Assert.That(clip.w, Is.GreaterThan(corners[2].y + 10000));
                        var root = play.NoteRoot(0);
                        var head = root.Find("Head").GetComponent<LaneSprite>();
                        var screen = camera.WorldToScreenPoint(head.Renderer.bounds.center);
                        var expected = camera.WorldToScreenPoint(root.TransformPoint(Vector3.zero));
                        Assert.That(Vector2.Distance(screen, expected), Is.LessThan(.05f), "Sprite alignment must survive resizing.");
                    });
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
