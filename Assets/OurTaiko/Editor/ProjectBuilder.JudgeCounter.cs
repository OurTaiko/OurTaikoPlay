using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string JudgeCounterArt = "game/judge_counter/", JudgeLabelSheet = "result/score/max_combo_ja";
        // Panel top-left in the viewport and its size; rows 45 apart from y 18, bars 174x34 at x 26,
        // labels 0.68 of their art centred on their bar, counts 38 high with their right edge at x 300 (measured from the
        // user's reference shot, scaled to the 1920 stage).
        static readonly Vector2 JudgePanelPosition = new Vector2(29, 50), JudgePanelSize = new Vector2(352, 212);
        const float JudgeRowTop = 18, JudgeRowPitch = 45, JudgeBarX = 26, JudgeBarWidth = 174, JudgeBarHeight = 34;
        const float JudgeLabelScale = 0.68f, JudgeCountRight = 300, JudgeCountWidth = 90, JudgeCountHeight = 38;
        // max_combo_ja rows (良, 可, 不可, 連打数) cut 2 px around their glyphs; the whole sheet stays for Result.
        static readonly (string name, int x, int y, int width, int height)[] JudgeLabelCells =
        {
            ("ResultJudgeLabels", 0, 0, 220, 300),
            ("JudgeLabelGood", 13, 1, 42, 49), ("JudgeLabelOk", 13, 66, 43, 47),
            ("JudgeLabelBad", 12, 129, 76, 47), ("JudgeLabelRoll", 8, 190, 123, 47),
        };

        // Adds the judgement counter to SinglePlayScene, after ComboAnnounce like the original's
        // judge_counter after combo_announce in draw_overlays. Cutting the label rows turns
        // max_combo_ja into a sheet, so Result's judgeLabels moves to the whole-sheet cut. An existing
        // view keeps its saved layout; only sprites are refreshed.
        [MenuItem("OurTaiko/Apply Judge Counter")]
        public static void ApplyJudgeCounter()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing scenes.");
            GenerateJudgeCounterArt();
            var labels = SliceSheet(JudgeLabelSheet, JudgeLabelCells);

            var scene = EditorSceneManager.OpenScene(ResultPath);
            var result = UnityEngine.Object.FindFirstObjectByType<ResultScene>();
            result.judgeLabels = labels[0];
            EditorUtility.SetDirty(result);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditPlayScene(play =>
            {
                var viewport = play.comboAnnounce.transform.parent;
                var existing = viewport.Find("JudgeCounter");
                var view = existing != null ? existing.GetComponent<JudgeCounterView>() : null;
                if (view == null)
                {
                    if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                    view = BuildJudgeCounter(viewport, play.scoreCounter.digits, labels.Skip(1).ToArray());
                    // The FPS readout sat where the 連打数 row now is: move it into the gap above the panel.
                    if (viewport.Find("FpsPanel") is RectTransform fps && fps.anchoredPosition.y < -JudgePanelPosition.y + fps.sizeDelta.y)
                        fps.anchoredPosition = new Vector2(fps.anchoredPosition.x, -2);
                }
                view.digits = play.scoreCounter.digits;
                view.transform.SetSiblingIndex(play.comboAnnounce.transform.GetSiblingIndex() + 1);
                EditorUtility.SetDirty(view);
                play.judgeCounter = view;
            });
        }

        static JudgeCounterView BuildJudgeCounter(Transform viewport, Sprite[] digits, Sprite[] labels)
        {
            var root = Rect("JudgeCounter", viewport, JudgePanelPosition.x, JudgePanelPosition.y, JudgePanelSize.x, JudgePanelSize.y);
            var panel = root.gameObject.AddComponent<Image>();
            panel.sprite = Sprite(JudgeCounterArt + "panel"); panel.type = Image.Type.Sliced; panel.raycastTarget = false;
            var view = root.gameObject.AddComponent<JudgeCounterView>();
            view.digits = digits;
            string[] names = { "Good", "Ok", "Bad", "Roll" };
            for (int i = 0; i < 4; i++)
            {
                var row = Rect(names[i], root, 0, JudgeRowTop + i * JudgeRowPitch, JudgePanelSize.x, JudgeBarHeight);
                var bar = SkinUi.Image("Bar", row, Sprite(JudgeCounterArt + "bar"), JudgeBarWidth, JudgeBarHeight);
                bar.type = Image.Type.Sliced;
                bar.rectTransform.TopLeft(JudgeBarX, 0);
                var label = SkinUi.Image("Label", row, labels[i], labels[i].rect.width * JudgeLabelScale, labels[i].rect.height * JudgeLabelScale);
                // Each cut keeps 2 px on both sides of its glyph, so centring the sprite centres the glyph.
                label.rectTransform.anchoredPosition = bar.rectTransform.anchoredPosition;
                var count = Rect("Count", row, JudgeCountRight - JudgeCountWidth, (JudgeBarHeight - JudgeCountHeight) / 2, JudgeCountWidth, JudgeCountHeight);
                view.counts[i] = count;
                // A 0 previews each count in the editor; play rewrites it from the session.
                JudgeCounterView.PlaceDigit(SkinUi.Image("Digit0", count, null), digits[0], 0, 1, JudgeCountHeight, view.pitch);
            }
            return view;
        }

        // The panel and the row bars are drawn here (no skin has them): a rounded panel with a 5-px
        // light-orange border around a translucent orange-red fill, and a translucent white pill.
        // Colours sampled from the reference shot; alpha is baked into the PNGs.
        static void GenerateJudgeCounterArt()
        {
            string folder = Root + "Art/" + JudgeCounterArt;
            Directory.CreateDirectory(folder);
            var border = new Color32(255, 162, 50, 255);
            var fill = new Color32(240, 88, 40, 225);
            WriteRoundedRect(folder + "panel.png", 64, 64, 20, 5, border, fill, new Vector4(24, 24, 24, 24));
            var white = new Color32(255, 255, 255, 128);
            WriteRoundedRect(folder + "bar.png", 68, 34, 17, 0, white, white, new Vector4(17, 17, 17, 17));
        }

        // Anti-aliased rounded rectangle: coverage from the signed distance to the edge, the border
        // colour within `borderWidth` of it and the fill inside. Writes and imports a sliced sprite.
        static void WriteRoundedRect(string path, int width, int height, float radius, float borderWidth, Color32 border, Color32 fill, Vector4 slice)
        {
            // Rewritten every run (same bytes when unchanged); the existing .meta keeps the GUID.
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var p = new Vector2(x + 0.5f - width / 2f, y + 0.5f - height / 2f);
                    var q = new Vector2(Mathf.Abs(p.x) - (width / 2f - radius), Mathf.Abs(p.y) - (height / 2f - radius));
                    float distance = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
                    float coverage = Mathf.Clamp01(0.5f - distance);
                    Color color = Color.Lerp(border, fill, Mathf.Clamp01(-distance - borderWidth + 0.5f));
                    color.a *= coverage;
                    texture.SetPixel(x, y, color);
                }
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single && importer.spriteBorder == slice) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = slice;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
