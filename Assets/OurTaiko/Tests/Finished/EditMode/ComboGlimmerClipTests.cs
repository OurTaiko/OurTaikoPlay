using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // ComboGlimmer.anim, sampled through ClipSampler the way ComboView plays it.
    public sealed class ComboGlimmerClipTests
    {
        [Test]
        public void ComboGlimmerRowsRiseAndFadeOnTheirOwnPhase()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/OurTaiko/Generated/Clips/ComboGlimmer.anim");
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.isLooping, Is.True);
            Assert.That(clip.length, Is.EqualTo(0.5f).Within(1e-4));
            var root = new GameObject("Glimmer", typeof(RectTransform));
            try
            {
                var rises = Enumerable.Range(0, 3).Select(row =>
                {
                    var rowRect = new GameObject("Row" + row, typeof(RectTransform));
                    rowRect.transform.SetParent(root.transform, false);
                    var rise = new GameObject("Rise", typeof(RectTransform), typeof(CanvasGroup));
                    rise.transform.SetParent(rowRect.transform, false);
                    return rise;
                }).ToArray();
                var sampler = ClipSampler.Attach(root, clip);
                // Combo::update: row j is (2/3) x 500 x j ms ahead; for 250 ms it rises int(e / 16.67) px,
                // is opaque until 86 ms and fades to 0 at 250 ms; otherwise it is hidden.
                for (int ms = 1; ms < 500; ms += 7)
                {
                    sampler.Sample(ms / 1000.0);
                    for (int row = 0; row < 3; row++)
                    {
                        double e = (ms + 1000.0 / 3 * row) % 500;
                        if (Math.Abs(e % 16.67) < 0.05 || Math.Abs(e - 250) < 0.05) continue;
                        float alpha = e > 250 ? 0 : e < 86 ? 1 : 1 - (float)(e - 86) / 164;
                        float y = e > 250 ? 0 : (int)(e / 16.67);
                        Assert.That(rises[row].GetComponent<CanvasGroup>().alpha, Is.EqualTo(alpha).Within(2e-3), $"row {row} at {ms} ms");
                        Assert.That(((RectTransform)rises[row].transform).anchoredPosition.y, Is.EqualTo(y), $"row {row} at {ms} ms");
                    }
                }
            }
            finally { Object.DestroyImmediate(root); }
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SinglePlayScene.unity");
            try
            {
                var play = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single();
                Assert.That(play.combo.glimmer.clip, Is.SameAs(clip));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
