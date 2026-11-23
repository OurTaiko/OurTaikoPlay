using System.Linq;
using NUnit.Framework;
using OurTaiko.Chara;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The play backdrop's bands: .otanim clips, the clear clip over them while the player is over the clear
    // line, and how the scenes use them.
    public sealed class StageBandTests
    {
        const string Art = "Assets/OurTaiko/Resources/Stage/";

        static OtanimAsset Asset(string name) => TestStage.Asset(name);

        static (string name, int frames, bool loop, int loopStart)[] Clips(string asset) =>
            Asset(asset).File.Clips.Select(c => (c.Name, c.Frames.Length, c.Loop, c.LoopStart)).ToArray();

        // A player eases a part from one frame's place to the next's, so a tile that goes back to
        // where its scroll started must be another part there, or it is swept across the band.
        [Test]
        public void NoPictureIsEasedAcrossWhereItsLoopComesRound()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:OtanimAsset", new[] { Art.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var file = AssetDatabase.LoadAssetAtPath<OtanimAsset>(path).File;
                foreach (var clip in file.Clips)
                {
                    if (!clip.Loop || clip.Frames.Length < 2) continue;
                    foreach (var last in clip.Frames[clip.Frames.Length - 1].Draws)
                        foreach (var first in clip.Frames[clip.LoopStart].Draws)
                        {
                            if (first.Part != last.Part) continue;
                            float moved = 0;
                            for (int i = 0; i < 2 * file.Parts[last.Part].UVs.Length; i++)
                                moved = Mathf.Max(moved, Mathf.Abs(clip.Positions[first.Offset + i] - clip.Positions[last.Offset + i]));
                            Assert.That(moved, Is.LessThan(100), $"{path} {clip.Name}: part {last.Part} jumps {moved} where the loop comes round.");
                        }
                }
            }
        }

        [Test]
        public void EachSetHasItsClips()
        {
            // A header's layers come round each in its own time; its clear is in the same file.
            Assert.That(Clips("header/toho"), Is.EqualTo(new[] { ("normal", 480, true, 0), ("normal_1", 251, true, 0), ("normal_2", 1920, true, 0), ("clear", 1920, true, 0) }));
            // The scene below stands still; the scene that takes its place past the clear line is a file of its own.
            Assert.That(Clips("scene/toho"), Is.EqualTo(new[] { ("normal", 1, true, 0) }));
            Assert.That(Clips("clear/toho"), Is.EqualTo(new[] { ("clear", 1920, true, 0) }));
            // The dojo's header is five layers, each as long as its own layer takes to come round.
            Assert.That(Clips("header/dojo"), Is.EqualTo(new[] { ("normal_back", 1280, true, 0), ("normal_mid_b", 1599, true, 0), ("normal_mid_c", 1759, true, 0), ("normal_petals", 320, true, 0), ("normal_front", 1280, true, 0) }));
            Assert.That(Asset("header/dojo").File.View, Is.EqualTo(new Rect(0, 0, 1920, 276)));
            foreach (string name in new[] { "header/toho", "scene/toho", "clear/toho" })
            {
                var asset = Asset(name);
                Assert.That(asset.texture, Is.Not.Null, name);
                Assert.That(asset.File.Texture, Is.Empty, "The picture is the texture asset, not PNG bytes kept twice.");
                Assert.That(asset.File.View, Is.EqualTo(name == "header/toho" ? new Rect(0, 0, 1920, 276) : new Rect(0, 0, 1920, 540)));
            }
        }

        [Test]
        public void ASeparateClearSceneReplacesTheNormalSceneUntilTheGaugeFalls()
        {
            var go = new GameObject("Scene", typeof(RectTransform), typeof(StageBandView));
            try
            {
                var band = go.GetComponent<StageBandView>();
                band.Use(Asset("scene/miku"), Asset("clear/miku"));
                band.Show(0);
                band.Cleared = true;
                band.Show(1);
                Assert.That(band.Layers.All(layer => layer.RunCount == 0), Is.True);
                Assert.That(band.Clear.RunCount, Is.GreaterThan(0));
                band.Cleared = false;
                band.Show(1.1);
                Assert.That(band.Layers[0].RunCount, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(go); }
        }

        static OtanimView View(Transform parent, string asset, string clip)
        {
            var go = new GameObject(clip, typeof(RectTransform), typeof(OtanimView));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(1920, 276);
            var view = go.GetComponent<OtanimView>();
            view.asset = AssetDatabase.LoadAssetAtPath<OtanimAsset>(asset);
            view.clip = clip;
            return view;
        }

        [Test]
        public void ScreenPartsAreDrawnByAGraphicOfTheirOwn()
        {
            var root = new GameObject("Band", typeof(RectTransform));
            try
            {
                // Three squares made for this test, back to front: plain, on a screen, plain. The one on a
                // screen is out from the third of the clip's four frames.
                var view = View(root.transform, "Assets/OurTaiko/Tests/EditMode/Stage/screen.otanim", "normal");
                var file = view.File;
                bool Screen(Otanim.Draw draw) => file.Parts[draw.Part].Blend == Otanim.Blend.Screen;
                Assert.That(Otanim.DrawsAt(view.Clip, 0.5 / 60).Any(Screen), Is.False);
                view.Show(0.5 / 60);
                Assert.That(view.RunCount, Is.EqualTo(1));
                var draws = Otanim.DrawsAt(view.Clip, 2.5 / 60);
                Assert.That(draws.Any(Screen), Is.True);
                Assert.That(Screen(draws.First()) || Screen(draws.Last()), Is.False, "It is between the other two.");
                view.Show(2.5 / 60);
                Assert.That(view.RunCount, Is.EqualTo(3), "What is behind them, they, and what is in front.");
                var runs = view.GetComponentsInChildren<UnityEngine.UI.MaskableGraphic>();
                Assert.That(runs.Select(r => r.material.GetFloat("_DstBlend")), Is.EqualTo(new[]
                {
                    (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcColor, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha,
                }));
                Assert.That(runs.Select(r => r.mainTexture), Has.All.SameAs(view.asset.texture));
                view.Show(null, 0);
                Assert.That(view.GetComponentsInChildren<UnityEngine.UI.MaskableGraphic>(), Is.Empty, "Nothing shown, nothing drawn.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheClearClipFadesInFromItsStartAndStartsOverNextTime()
        {
            var root = new GameObject("Band", typeof(RectTransform), typeof(StageBandView));
            try
            {
                var band = root.GetComponent<StageBandView>();
                band.Use(Asset("scene/toho"), Asset("clear/toho"));
                Assert.That(band.Layers.Select(l => (l.asset, l.clip)).Append((band.Clear.asset, band.Clear.clip)), Is.EqualTo(new[] { (Asset("scene/toho"), "normal"), (Asset("clear/toho"), "clear") }));
                band.Show(10);
                Assert.That((band.ClearAlpha, band.Clear.RunCount, band.Layers[0].RunCount), Is.EqualTo((0f, 0, 1)));
                band.Cleared = true;
                band.Show(10.1);
                Assert.That(band.ClearAlpha, Is.EqualTo(0.4f).Within(1e-4));
                Assert.That(band.ClearSeconds, Is.Zero, "The clip starts when the gauge crosses the line.");
                band.Show(10.5);
                Assert.That(band.ClearAlpha, Is.EqualTo(1));
                Assert.That(band.ClearSeconds, Is.EqualTo(0.4).Within(1e-9));
                Assert.That(band.Seconds, Is.EqualTo(0.5).Within(1e-9), "The layers under it keep their own time.");
                Assert.That((band.Clear.RunCount, band.Layers[0].RunCount), Is.EqualTo((1, 0)), "Once it is all in, the scene under it is no longer drawn.");
                band.Show(20);
                // Back under the line: it fades away over the scene, and the next clear starts over.
                band.Cleared = false;
                band.Show(20.1);
                Assert.That(band.ClearAlpha, Is.EqualTo(0.6f).Within(1e-4));
                Assert.That(band.Layers[0].RunCount, Is.EqualTo(1));
                band.Show(21);
                Assert.That((band.ClearAlpha, band.Clear.RunCount), Is.EqualTo((0f, 0)));
                band.Cleared = true;
                band.Show(21.1);
                Assert.That(band.ClearSeconds, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ABandsLayersAreItsNormalClipsAndItsClearMayBeAnotherFiles()
        {
            var root = new GameObject("Band", typeof(RectTransform), typeof(StageBandView));
            try
            {
                var band = root.GetComponent<StageBandView>();
                band.Use(Asset("header/dojo"));
                Assert.That(band.Layers.Select(l => l.clip), Is.EqualTo(new[] { "normal_back", "normal_mid_b", "normal_mid_c", "normal_petals", "normal_front" }));
                Assert.That(band.Layers.Select(l => l.transform.GetSiblingIndex()), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }), "Back to front, in the file's order.");
                Assert.That(band.Clear, Is.Null, "It has no clear clip.");
                band.Show(1);
                Assert.That(band.Layers.Select(l => l.RunCount), Has.All.EqualTo(1));
                // A clear scene drawn for a set that has none of its own; the layers no longer used draw nothing.
                band.Use(Asset("header/dojo"), Asset("clear/toho"));
                Assert.That((band.Clear.asset, band.Clear.clip), Is.EqualTo((Asset("clear/toho"), "clear")));
                band.Use(Asset("scene/toho"), null, null);
                Assert.That((band.Layers.Count, band.Clear), Is.EqualTo((1, (OtanimView)null)), "A pack may want no clear at all.");
                band.Show(2);
                Assert.That(root.GetComponentsInChildren<UnityEngine.UI.MaskableGraphic>().Length, Is.EqualTo(1));
                band.Use(null);
                band.Show(3);
                Assert.That(root.GetComponentsInChildren<UnityEngine.UI.MaskableGraphic>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ABandDarkensWhileItsPlayerKeepsMissing()
        {
            var root = new GameObject("Band", typeof(RectTransform), typeof(StageBandView));
            try
            {
                var band = root.GetComponent<StageBandView>();
                band.Use(Asset("scene/toho"));
                band.Show(0);
                Assert.That(band.Shade, Is.EqualTo(1));
                band.Dark = true;
                band.Show(0.125);
                Assert.That(band.Shade, Is.EqualTo((1 + band.darkShade) / 2).Within(1e-4), "Half the fade in.");
                band.Show(1);
                Assert.That(band.Shade, Is.EqualTo(band.darkShade));
                band.Dark = false;
                band.Show(2);
                Assert.That(band.Shade, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        static T[] In<T>(UnityEngine.SceneManagement.Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

        [Test]
        public void ThePlayScenesHaveTheStagesParts()
        {
            foreach (string name in new[] { "Single", "Practice", "Double" })
            {
                var scene = EditorSceneManager.OpenPreviewScene($"Assets/Scenes/Play/Play.{name}.unity");
                try
                {
                    var play = In<PlayScene>(scene).Single();
                    var stage = play.stage;
                    Assert.That(stage.catalog, Is.Null, "The scene has no catalog of its own: the game's, with the player's added, is StageLibrary's.");
                    Assert.That(stage.mode.ToString(), Is.EqualTo(name));
                    var parts = new System.Collections.Generic.List<Component>(stage.headers);
                    parts.AddRange(stage.renda);
                    if (name == "Double")
                    {
                        // No room for the scene below: 2P has a header in its place, on 2P's gauge.
                        Assert.That(stage.headers.Select(b => (b.name, b.player)), Is.EqualTo(new[] { ("StageUpper", 0), ("StageUpper2P", 1) }));
                        Assert.That(stage.renda.Select(r => (r.name, r.player)), Is.EqualTo(new[] { ("StageRenda", 0), ("StageRenda2P", 1) }));
                        Assert.That(((RectTransform)stage.headers[1].transform).anchoredPosition, Is.EqualTo(new Vector2(0, -804)));
                        Assert.That((stage.scene, stage.footer, stage.crowd, stage.dancers), Is.EqualTo(((StageBandView)null, (StageBandView)null, (StageBandView)null, (DancerTroupeView)null)));
                    }
                    else
                    {
                        Assert.That(stage.headers.Select(b => (b.name, b.player)), Is.EqualTo(new[] { ("StageUpper", 0) }));
                        Assert.That(stage.renda.Select(r => (r.name, r.player)), Is.EqualTo(new[] { ("StageRenda", 0) }));
                        Assert.That((stage.scene.name, stage.footer.name, stage.crowd.name, stage.dancers.name), Is.EqualTo(("StageLower", "StageFooter", "StageCrowd", "Dancers")));
                        // Back to front: the scene, the dancers, the stand, the crowd. A canvas of the dancers' own
                        // with its own sorting order would put them over the stand and the crowd.
                        Assert.That(new Component[] { stage.scene, stage.dancers, stage.footer, stage.crowd }.Select(c => c.transform.GetSiblingIndex()), Is.Ordered);
                        Assert.That(new Component[] { stage.scene, stage.dancers, stage.footer, stage.crowd }.Select(c => c.transform.parent).Distinct().Count(), Is.EqualTo(1));
                        Assert.That(stage.dancers.GetComponentsInParent<Canvas>(true).Where(c => !c.isRootCanvas).Select(c => c.overrideSorting), Has.None.True);
                        foreach (var lower in new[] { stage.scene, stage.footer, stage.crowd })
                        {
                            Assert.That(((RectTransform)lower.transform).anchoredPosition, Is.EqualTo(new Vector2(0, -540)), lower.name);
                            Assert.That(((RectTransform)lower.transform).sizeDelta, Is.EqualTo(new Vector2(1920, 540)), lower.name);
                        }
                        // Back to front: the dancers stand in the scene, behind the stand and the crowd.
                        Assert.That(new Component[] { stage.scene, stage.dancers, stage.footer, stage.crowd }.Select(c => c.transform.GetSiblingIndex()), Is.Ordered);
                        parts.AddRange(new[] { stage.scene, stage.footer, stage.crowd });
                    }
                    foreach (var header in stage.headers)
                    {
                        Assert.That(((RectTransform)header.transform).sizeDelta, Is.EqualTo(new Vector2(1920, 276)));
                        // Each drumroll effect is over its player's header.
                        var effect = stage.renda.Single(r => r.player == header.player);
                        Assert.That(((RectTransform)effect.transform).anchoredPosition, Is.EqualTo(((RectTransform)header.transform).anchoredPosition));
                        Assert.That(((RectTransform)effect.transform).sizeDelta, Is.EqualTo(new Vector2(1920, 276)));
                        Assert.That(effect.transform.GetSiblingIndex(), Is.GreaterThan(header.transform.GetSiblingIndex()));
                    }
                    // Over the plain backdrop, under the background movie and everything of the play.
                    var viewport = stage.headers[0].transform.parent;
                    Assert.That(viewport.GetChild(0).name, Is.EqualTo("Backdrop"));
                    Assert.That(parts.Select(c => c.transform.parent), Has.All.SameAs(viewport));
                    Assert.That(parts.Select(c => c.transform.GetSiblingIndex()), Has.All.InRange(1, viewport.Find("BackgroundMovie").GetSiblingIndex() - 1));
                    foreach (var part in parts)
                    {
                        // What leaves the band is cut off, as the project's frame cuts it.
                        Assert.That(part.GetComponent<UnityEngine.UI.RectMask2D>(), Is.Not.Null, part.name);
                        Assert.That(In<BackgroundMovieView>(scene).Single().stageArt, Has.Member(part.GetComponent<CanvasGroup>()), "A background movie hides it.");
                        Assert.That(part.transform.childCount, Is.Zero, "Its pictures are made as it plays.");
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }

        [Test]
        public void TheDojoHasItsOwnHeaderOverTheSameScene()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Play/Play.Dan.unity");
            try
            {
                var stage = In<DanPlayScene>(scene).Single().stage;
                Assert.That((stage.mode, stage.headers.Single().name, stage.scene.name), Is.EqualTo((StageMode.Dan, "DanBackground", "StageLower")));
                Assert.That(((RectTransform)stage.headers[0].transform).sizeDelta, Is.EqualTo(new Vector2(1920, 276)));
                Assert.That((stage.renda.Length, stage.footer, stage.crowd), Is.EqualTo((0, (StageBandView)null, (StageBandView)null)));
                // Whatever the chart says, the dojo plays the catalog's DAN set.
                stage.Setup("anything");
                Assert.That(stage.Choice.Preset, Is.EqualTo("DAN"));
                Assert.That(stage.headers[0].Layers.Select(l => (l.asset, l.clip)), Is.EqualTo(new[] { "normal_back", "normal_mid_b", "normal_mid_c", "normal_petals", "normal_front" }.Select(c => (Asset("header/dojo"), c))));
                Assert.That(stage.scene.Layers.Select(l => (l.asset, l.clip)), Is.EqualTo(new[] { (Asset("scene/dojo"), "normal") }));
                Assert.That((stage.headers[0].Clear, stage.scene.Clear, stage.dancers.Troupe), Is.EqualTo(((OtanimView)null, (OtanimView)null, (DancerTroupe)null)), "No clear state and no dancers there.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
