using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string ComboArt = "game/combo/";
        // Nijiiro texture.json / skin_config, in lane coordinates (y down): combo_ja at (320, 136),
        // counter at x 395 y 58 (the digit row is centred on 395 + 64/2 - 52/2 = 401), gleam y -276 plus
        // PyTaikoGreen's combo_glimmer_1-3 scaled by 1920/1280. The root's top-left is (320, 39).
        static readonly Vector2 ComboOrigin = new Vector2(320, 39);
        static readonly Vector2[] GleamRows = { new Vector2(337.5f, 315 - 276), new Vector2(300, 345 - 276), new Vector2(375, 345 - 276) };

        // Replaces SinglePlayScene's text combo with the Nijiiro combo (counter / counter_100 /
        // counter_gold digits, combo_ja / combo_100_ja and the gold glimmer). An existing ComboView keeps
        // its saved layout; only its sprites and clips are refreshed.
        [MenuItem("OurTaiko/Apply Nijiiro Combo")]
        public static void ApplyCombo() => EditPlayScene(play =>
        {
            var lane = play.hitFace.transform.parent;
            var existing = lane.Find("Combo");
            var view = existing != null ? existing.GetComponent<ComboView>() : null;
            if (view == null)
            {
                int sibling = existing != null ? existing.GetSiblingIndex() : play.judgment.transform.GetSiblingIndex();
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                view = BuildCombo(lane);
                view.transform.SetSiblingIndex(sibling);
            }
            else AssignComboArt(view);
            play.combo = view;
        });

        static ComboView BuildCombo(Transform lane)
        {
            var root = Rect("Combo", lane, 0, 0, 160, 137);
            root.anchoredPosition = new Vector2(ComboOrigin.x, -ComboOrigin.y);
            var view = root.gameObject.AddComponent<ComboView>();
            AssignComboArt(view);

            view.captionImage = SkinUi.Image("Caption", root, view.goldCaption);
            view.captionImage.rectTransform.TopLeft(320 - ComboOrigin.x, 136 - ComboOrigin.y);

            view.digitRow = Rect("Digits", root, 0, 0, 156, 80);
            view.digitRow.pivot = new Vector2(0.5f, 1);
            view.digitRow.anchoredPosition = new Vector2(401 - ComboOrigin.x, -(58 - ComboOrigin.y));
            // A gold 123 previews the whole design in the editor; play hides it until the combo reaches 10.
            for (int i = 0; i < 3; i++)
            {
                var digit = SkinUi.Image("Digit" + i, view.digitRow, view.goldDigits[i + 1]);
                ComboView.PlaceDigit(digit, i, 3, view.pitch, 0);
            }

            var glimmer = Rect("Glimmer", root, 0, 0, 0, 0);
            for (int row = 0; row < GleamRows.Length; row++)
            {
                var rowRect = Rect("Row" + row, glimmer, 0, 0, 0, 0);
                rowRect.anchoredPosition = new Vector2(GleamRows[row].x - ComboOrigin.x, -(GleamRows[row].y - ComboOrigin.y));
                var rise = Rect("Rise", rowRect, 0, 0, 0, 0);
                rise.gameObject.AddComponent<CanvasGroup>();
                // combo.cpp draws three gleams per row, one combo_margin apart, whatever the digit count.
                for (int i = 0; i < 3; i++)
                {
                    var gleam = SkinUi.Image("Gleam" + i, rise, Sprite(ComboArt + "gleam"));
                    gleam.rectTransform.TopLeft(i * view.pitch, 0);
                }
            }
            view.glimmer = AttachClip(glimmer.gameObject, ComboGlimmerClip());
            return view;
        }

        static void AssignComboArt(ComboView view)
        {
            ImportComboArt();
            // counter*: ten 64x80 digits side by side.
            Sprite[] Digits(string sheet, string prefix) => SliceSheet(ComboArt + sheet,
                Enumerable.Range(0, 10).Select(i => (prefix + i, i * 64, 0, 64, 80)).ToArray());
            view.whiteDigits = Digits("counter", "Combo");
            view.silverDigits = Digits("counter_100", "ComboSilver");
            view.goldDigits = Digits("counter_gold", "ComboGold");
            view.caption = Sprite(ComboArt + "combo_ja");
            view.goldCaption = Sprite(ComboArt + "combo_100_ja");
            AttachTextStretch(view.gameObject);
            if (view.glimmer != null) AttachClip(view.glimmer.gameObject, ComboGlimmerClip());
            EditorUtility.SetDirty(view);
        }

        // Adds ComboAnnounce over the soul gauge: Player::draw paints the gauge first and the announce
        // in draw_overlays, so it sits on the viewport after GaugeHitEffect, aligned with NoteLane. An
        // existing view keeps its saved layout; only its sprites, voices and clip are refreshed.
        [MenuItem("OurTaiko/Apply Nijiiro Combo Announce")]
        public static void ApplyComboAnnounce() => EditPlayScene(play =>
        {
            var lane = (RectTransform)play.combo.transform.parent;
            var viewport = lane.parent;
            var existing = viewport.Find("ComboAnnounce");
            var view = existing != null ? existing.GetComponent<ComboAnnounceView>() : null;
            if (view == null)
            {
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                view = BuildComboAnnounce(lane);
            }
            view.transform.SetSiblingIndex(play.noteArcs.transform.parent.Find("GaugeHitEffect").GetSiblingIndex() + 1);
            AssignComboAnnounceArt(view);
            play.comboAnnounce = view;
        });

        static ComboAnnounceView BuildComboAnnounce(RectTransform lane)
        {
            var root = Rect("ComboAnnounce", lane.parent, 0, 0, 0, 0);
            root.anchorMin = lane.anchorMin; root.anchorMax = lane.anchorMax; root.pivot = lane.pivot;
            root.anchoredPosition = lane.anchoredPosition; root.sizeDelta = lane.sizeDelta;
            root.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            var view = root.gameObject.AddComponent<ComboAnnounceView>();
            AssignComboAnnounceArt(view);
            // texture.json, relative to the lane's top: announce_bg_1p (362, -264), announce_digit_1p
            // (362, -196), announce_text at the same x 362.
            SkinUi.Image("Background", root, Sprite(ComboArt + "announce_bg_1p")).rectTransform.TopLeft(362, -264);
            view.number = Rect("Number", root, 362, -196, 0, 0);
            view.text = SkinUi.Image("Text", view.number, Sprite(ComboArt + "announce_text"));
            // A 300 previews the scroll in the editor; play hides it until the 100th combo.
            view.Layout("300");
            return view;
        }

        static void AssignComboAnnounceArt(ComboAnnounceView view)
        {
            ImportComboArt();
            // announce_digit_1p: ten 104x104 digits stacked vertically.
            view.digits = SliceSheet(ComboArt + "announce_digit_1p",
                Enumerable.Range(0, 10).Select(i => ("ComboAnnounce" + i, 0, i * 104, 104, 104)).ToArray());
            view.voices = Enumerable.Range(1, 50)
                .Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}Audio/combo/{i * 100}_1p.ogg") ?? throw new System.IO.FileNotFoundException($"combo voice {i * 100}"))
                .ToArray();
            AttachClip(view.gameObject, ComboAnnounceClip());
            EditorUtility.SetDirty(view);
        }

        // ComboAnnounce: fade (animation 65, 100 ms) in, hold until 1666.67 ms, fade out over 100 ms.
        static AnimationClip ComboAnnounceClip() => SaveClip("ComboAnnounce", 60, false, clip =>
            LinearCurve(clip, "", typeof(CanvasGroup), "m_Alpha", (0, 0), (0.1f, 1), (1.66667f, 1), (1.76667f, 0)));

        // Unity's default import auto-slices new PNGs into trimmed sprites; the combo art starts as whole
        // Single sprites (the counters are then cut by SliceSheet, which keeps its own cuts on reruns).
        static void ImportComboArt()
        {
            foreach (string name in new[] { "counter", "counter_100", "counter_gold", "combo_ja", "combo_100_ja", "gleam",
                "announce_bg_1p", "announce_digit_1p", "announce_text" })
            {
                string path = Root + "Art/" + ComboArt + name + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool cut = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Any(sprite => sprite.name.StartsWith("Combo"));
                if (importer.textureType == TextureImporterType.Sprite && (importer.spriteImportMode == SpriteImportMode.Single || cut)) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
            }
        }

        // Combo::update: three phases of a 500 ms cycle, row j started (2/3) x 500 x j ms early. For
        // its first 250 ms a row rises 1 px every 16.67 ms (int steps) and holds full alpha until
        // 86 ms, then fades linearly to 0 at 250 ms; it is hidden for the rest of the cycle.
        static AnimationClip ComboGlimmerClip() => SaveClip("ComboGlimmer", 60, true, clip =>
        {
            const float cycle = 0.5f, active = 0.25f, fadeFrom = active - 0.164f, step = 0.01667f;
            for (int row = 0; row < GleamRows.Length; row++)
            {
                float shift = 2f / 3f * cycle * row % cycle;
                float Elapsed(float t) => ((t + shift) % cycle + cycle) % cycle;
                // Rounded so breakpoints that land on the loop's start are not split from it by float error.
                float At(float e) => (float)Math.Round((e - shift + cycle) % cycle, 5) % cycle;
                string path = $"Row{row}/Rise";

                // Alpha: linear between the breakpoints; the jump to 1 as a phase starts is a step.
                var alpha = Keys(cycle, Elapsed, e => e > active ? 0 : e < fadeFrom ? 1 : 1 - (e - fadeFrom) / (active - fadeFrom),
                    (At(0), 1), (At(fadeFrom), 1), (At(active), 0));
                for (int i = 0; i < alpha.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(alpha, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(alpha, i, AnimationUtility.TangentMode.Linear);
                }
                for (int i = 1; i < alpha.length; i++)
                    if (alpha[i].time == At(0)) AnimationUtility.SetKeyRightTangentMode(alpha, i - 1, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(CanvasGroup), "m_Alpha"), alpha);

                // Rise: whole pixels upwards, back to 0 when the phase ends.
                var rise = Keys(cycle, Elapsed, e => e > active ? 0 : (int)(e / step),
                    Enumerable.Range(0, (int)(active / step) + 1).Select(k => (At(k * step), (float)k)).Append((At(active), 0f)).ToArray());
                SteppedCurve(clip, path, typeof(RectTransform), "m_AnchoredPosition.y",
                    Enumerable.Range(0, rise.length).Select(i => (rise[i].time, rise[i].value)).ToArray());
            }
        });

        // Keys at the given (time, value) breakpoints, plus the loop's ends evaluated from the phase
        // (the last key from just before the wrap, so it does not take the next cycle's value).
        static AnimationCurve Keys(float cycle, Func<float, float> elapsed, Func<float, float> value, params (float time, float value)[] points)
        {
            var keys = new SortedDictionary<float, float>();
            foreach (var (time, v) in points) keys[time] = v;
            if (!keys.ContainsKey(0)) keys[0] = value(elapsed(0));
            keys[cycle] = value(elapsed(cycle - 1e-5f));
            return new AnimationCurve(keys.Select(k => new Keyframe(k.Key, k.Value)).ToArray());
        }
    }
}
