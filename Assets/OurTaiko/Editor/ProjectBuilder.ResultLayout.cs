using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const int SavedGaugeArt = 2;
        static readonly Vector2 ResultCrownCentre = new Vector2(308 + 84, 425 + 84), ResultMessageCentre = new Vector2(410 + 260, 626 + 170);

        // Saves the result screen once as an editable hierarchy under Stage (skin positions, the
        // hard/oni gauge art). An existing layout is never rebuilt.
        [MenuItem("OurTaiko/Apply Result Layout")]
        public static void ApplyResultLayout()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before saving the Result layout.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            var scene = EditorSceneManager.OpenScene(ResultPath);
            var result = UnityEngine.Object.FindFirstObjectByType<ResultScene>();
            if (result == null) throw new InvalidOperationException("Result controller is missing.");
            if (result.view != null)
            {
                Debug.Log("OurTaiko: Result already has a saved layout; the existing hierarchy was preserved.");
                return;
            }
            BuildResultLayout(result);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: Result hierarchy is saved. Use the Result Inspector preview buttons in Edit mode.");
        }

        static void BuildResultLayout(ResultScene result)
        {
            var stage = result.stage;
            if (stage.childCount != 0)
                throw new InvalidOperationException("Result's Stage has children but no ResultView binding. Bind it instead; it will not be overwritten.");
            var view = stage.gameObject.AddComponent<ResultView>();
            result.view = view;
            view.gaugeArt = SavedGaugeArt;
            view.background = BuildResultBackground(stage, "Background");

            view.songTitle = SkinUi.Text("SongTitle", stage, 56);
            view.songTitle.rectTransform.sizeDelta = new Vector2(1730, 90);
            view.songTitle.rectTransform.Center(95 + 1730 / 2f, 82);
            view.songNumber = SkinUi.Text("SongNumber", stage, 24);
            view.songNumber.alignment = TextAlignmentOptions.TopLeft;
            view.songNumber.rectTransform.pivot = new Vector2(0, 1);
            view.songNumber.rectTransform.anchoredPosition = new Vector2(1655, -22);

            view.success = ResultPlace("Success", stage, Sprite("result/background/footer_1p"), 76, 176);
            ResultPlace("Board", stage, Sprite("result/score/overlay/0"), 40, 180);
            ResultPlace("DonBack", stage, Sprite("result/bottom/chara_0/0"), 59, 606);
            view.difficulty = ResultPlace("Difficulty", stage, result.difficulties[3], 7, 156);
            // max_combo_ja is a sheet: the judge counter cuts its rows (ApplyJudgeCounter).
            ResultPlace("JudgeLabels", stage, SliceSheet(JudgeLabelSheet, JudgeLabelCells)[0], 514, 287);
            view.judgeRows = new ResultView.DigitRow[ResultSequence.Rows];
            for (int row = 0; row < view.judgeRows.Length; row++)
            {
                view.judgeRows[row] = new ResultView.DigitRow { digits = new Image[5] };
                for (int i = 0; i < 5; i++)
                    view.judgeRows[row].digits[i] = ResultPlace("Judge" + row + "_" + i, stage, result.judgeDigits[0], 842 - i * 31, 290 + row * 62);
            }
            ResultPlace("ScoreLabel", stage, Sprite("result/score/score_shinuchi_ja"), 74, 274);
            // total_score_back_mc (outlines, frames 0..9) under total_score_mc (fills, 10..19)
            view.scoreOutline = Enumerable.Range(0, 8).Select(i => ResultPlace("ScoreOutline" + i, stage, result.scoreDigits[0], 392 - i * 46, 322)).ToArray();
            view.scoreFill = Enumerable.Range(0, 8).Select(i => ResultPlace("ScoreFill" + i, stage, result.scoreDigits[10], 392 - i * 46, 322)).ToArray();

            view.highScore = SkinUi.Rect("HighScore", stage);
            view.highScoreGroup = view.highScore.gameObject.AddComponent<CanvasGroup>();
            ResultPlace("Bar", view.highScore, Sprite("result/score/high_score"), 64, 266);
            var caption = SkinUi.Text("Caption", view.highScore, 24);
            caption.text = "ベストスコア更新！";
            caption.rectTransform.Center(64 + 154.4f, 266 + 26.6f);
            view.highScoreDigits = Enumerable.Range(0, 7).Select(i => ResultPlace("Digit" + i, view.highScore, result.highScoreDigits[0], 449 - i * 14, 286)).ToArray();

            // result_crown.lua draw order: crown, white ghost, the two bursts, star cluster, loop shine.
            view.crown = ResultCentered("Crown", stage, result.crowns[0], ResultCrownCentre);
            view.crownFade = ResultCentered("CrownFade", stage, Sprite("result/crown/crown_fade"), ResultCrownCentre);
            view.burstA = ResultCentered("BurstA", stage, result.gleam[3], ResultCrownCentre);
            view.burstB = ResultCentered("BurstB", stage, result.gleam[3], ResultCrownCentre);
            view.stars = ResultCentered("Stars", stage, result.gleam[4], ResultCrownCentre);
            view.shine = ResultCentered("Shine", stage, result.gleam[0], ResultCrownCentre);
            view.message = ResultCentered("Message", stage, result.messages[0], ResultMessageCentre);
            view.messageText = SkinUi.Text("MessageText", stage, 52);
            view.messageText.textWrappingMode = TextWrappingModes.Normal;
            view.messageText.rectTransform.sizeDelta = new Vector2(480, 300);
            view.messageText.rectTransform.Center(ResultMessageCentre.x, ResultMessageCentre.y);

            // tamashii gauge at the board's 0.7 scale; positions are the texture.json origins.
            int clearCell = ResultScene.ClearCell[SavedGaugeArt];
            const float unit = ResultScene.GaugeUnit;
            view.unfilled = ResultGauge("Unfilled", stage, result.unfilled[SavedGaugeArt], 74, 209);
            view.bar = ResultGauge("Bar", stage, Sprite("result/gauge/1p_bar"), 87, 240);
            view.clearTransition = ResultGauge("ClearTransition", stage, Sprite("result/gauge/bar_clear_transition"), 87 + (clearCell - 1) * unit, 215);
            view.clearTop = ResultGauge("ClearTop", stage, Sprite("result/gauge/bar_clear_top"), 87 + clearCell * unit, 217);
            view.clearBottom = ResultGauge("ClearBottom", stage, Sprite("result/gauge/bar_clear_bottom"), 87 + clearCell * unit, 241);
            view.rainbow = new[]
            {
                ResultGauge("Rainbow0", stage, result.rainbow[SavedGaugeArt * 8], 82, 212),
                ResultGauge("Rainbow1", stage, result.rainbow[SavedGaugeArt * 8], 82, 212),
            };
            view.overlay = ResultGauge("Overlay", stage, result.overlays[SavedGaugeArt], 74, 209);
            view.overlay.Alpha(0.15f);
            view.clearCaption = ResultGauge("Clear", stage, result.clearCaptionDark, ResultScene.ClearCaptionX[SavedGaugeArt], 210);
            view.soulFire = ResultPlace("SoulFire", stage, result.soulFire[0], 794, 118);
            view.soulFire.rectTransform.sizeDelta *= 0.8f * ResultScene.GaugeScale;
            view.soulFire.rectTransform.TopLeft(794, 118);
            view.soul = ResultGauge("Soul", stage, result.soulDark, 823, 207);
            view.soulSheen = ResultGauge("SoulSheen", stage, Sprite("result/gauge/tamashii_overlay"), 823, 207);

            // ResultPlayer::draw ends with the nameplate at result_player.lua's nameplate_pos (2, 922).
            var plate = AssetDatabase.LoadAssetAtPath<NameplateView>(NameplatePrefabPath);
            view.nameplate = ((GameObject)PrefabUtility.InstantiatePrefab(plate.gameObject, stage)).GetComponent<NameplateView>();
            view.nameplate.name = "Nameplate";
            view.nameplate.Place(2, 922);
            PrefabUtility.RecordPrefabInstancePropertyModifications(view.nameplate.transform);

            view.fadeIn = BuildResultBackground(stage, "FadeIn");
            // coin_overlay's credit line only (result shows no chip or invite), over the wipe
            var coins = SkinUi.Rect("CoinOverlay", stage);
            view.freePlay = SkinUi.Text("FreePlay", coins, 40);
            view.freePlay.characterSpacing = 2 * 100f / 40;
            view.freePlay.text = "フリープレイ";
            view.freePlay.rectTransform.Center(960, 1046);
            view.touchArea = SkinUi.Image("TouchArea", stage, null, 1920, 1080);
            view.touchArea.rectTransform.TopLeft(0, 0);
            view.touchArea.color = Color.clear;
            view.touchArea.raycastTarget = true;
            view.touchRelay = view.touchArea.gameObject.AddComponent<PointerRelay>();

            PersistSongSelectTextMaterials(stage);
            PreviewResultLayout(result, 0, false);
            EditorUtility.SetDirty(result);
            EditorUtility.SetDirty(view);
        }

        // result_bg_layers.lua: sky halves, Fuji (and its clear variant), eleven cloud bars at their
        // frame-0 places with their clear twins, and the header band.
        static ResultView.BackgroundView BuildResultBackground(Transform stage, string name)
        {
            const string bg = "result/background/";
            var root = SkinUi.Rect(name, stage);
            var view = new ResultView.BackgroundView { group = root.gameObject.AddComponent<CanvasGroup>() };
            view.group.interactable = view.group.blocksRaycasts = false;
            view.sky = new Image[2]; view.skyClear = new Image[2];
            // base sky: shape 3 at x=0 and mirrored at x=960; clear sky the other way round
            for (int i = 0; i < 2; i++)
            {
                view.sky[i] = SkinUi.Image("Sky" + i, root, Sprite(bg + "background_1p"));
                view.sky[i].rectTransform.TopLeft(i * 960, 0);
                view.sky[i].rectTransform.localScale = new Vector3(i == 1 ? -1 : 1, 1, 1);
            }
            for (int i = 0; i < 2; i++)
            {
                view.skyClear[i] = SkinUi.Image("SkyClear" + i, root, Sprite(bg + "bg_sky_clear"));
                view.skyClear[i].rectTransform.TopLeft(i == 0 ? 960 : 0, 0);
                view.skyClear[i].rectTransform.localScale = new Vector3(i == 1 ? -1 : 1, 1, 1);
            }
            view.fuji = SkinUi.Image("Fuji", root, Sprite(bg + "bg_fuji"));
            view.fuji.rectTransform.TopLeft(ResultBackground.FujiX, ResultBackground.FujiY);
            view.fujiClear = SkinUi.Image("FujiClear", root, Sprite(bg + "bg_fuji_clear"));
            view.fujiClear.rectTransform.pivot = new Vector2(0.5f, 1);
            var fuji = LumenClip.Parse(RequiredTimeline("result_bg_fuji").text);
            float ty = (float)fuji.Get(ResultBackground.FujiTrack, 0, "ty", ResultBackground.FujiY);
            float sy = (float)fuji.Get(ResultBackground.FujiTrack, 0, "sy", 1);
            float width = view.fujiClear.sprite.rect.width;
            view.fujiClear.rectTransform.sizeDelta = new Vector2(width, ResultBackground.FujiHeight * sy);
            view.fujiClear.rectTransform.anchoredPosition = new Vector2(ResultBackground.FujiClearX + width / 2, -ty);
            var layers = ResultBackground.Layers;
            view.clouds = new Image[layers.Length]; view.cloudsClear = new Image[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                view.clouds[i] = ResultCloud("Cloud" + i, root, Sprite(bg + "bg_cloud_" + layers[i].Cloud), i);
                view.cloudsClear[i] = ResultCloud("CloudClear" + i, root, Sprite(bg + "bg_cloud_clear_" + layers[i].Cloud), i);
            }
            view.header = SkinUi.Image("Header", root, Sprite(bg + "bg_header"));
            view.header.rectTransform.TopLeft(0, 0);
            return view;
        }

        static Image ResultCloud(string name, Transform root, Sprite sprite, int layer)
        {
            var image = SkinUi.Image(name, root, sprite);
            var l = ResultBackground.Layers[layer];
            image.rectTransform.pivot = new Vector2(0, 1);
            image.rectTransform.localScale = new Vector3(l.Mirror ? -l.Scale : l.Scale, l.Scale, 1);
            image.rectTransform.anchoredPosition = ResultBackground.Default(layer);
            return image;
        }

        static Image ResultPlace(string name, Transform parent, Sprite sprite, float x, float y)
        {
            var image = SkinUi.Image(name, parent, sprite);
            image.rectTransform.TopLeft(x, y);
            return image;
        }

        static Image ResultGauge(string name, Transform parent, Sprite sprite, float x, float y)
        {
            var image = SkinUi.Image(name, parent, sprite, sprite.rect.width * ResultScene.GaugeScale, sprite.rect.height * ResultScene.GaugeScale);
            image.rectTransform.pivot = new Vector2(0, 1);
            image.rectTransform.anchoredPosition = new Vector2(x, -y);
            return image;
        }

        static Image ResultCentered(string name, Transform parent, Sprite sprite, Vector2 centre)
        {
            var image = SkinUi.Image(name, parent, sprite);
            image.rectTransform.Center(centre.x, centre.y);
            return image;
        }

        // Edit-mode preview with a sample cleared Oni run: 0 = the settled board, 1 = with the
        // FadeIn wipe over it. Only sprites, texts, visibility and gauge widths change; positions
        // stay as authored. Play mode fills everything from the real run.
        public static void PreviewResultLayout(ResultScene result, int mode, bool recordUndo = true)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Layout preview is available only in Edit mode.");
            var view = result.view;
            if (view == null) throw new InvalidOperationException("Save the Result layout first.");
            if (recordUndo) Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "Preview Result layout");
            PreviewResultBackground(view.background);
            PreviewResultBackground(view.fadeIn);
            view.fadeIn.group.gameObject.SetActive(mode == 1);
            view.success.Alpha(0);
            view.songTitle.text = "TRIPLE HELIX";
            view.songNumber.text = "1曲目";
            view.difficulty.sprite = result.difficulties[3];
            int[] rows = { 612, 98, 47, 41, 233 };
            for (int row = 0; row < view.judgeRows.Length; row++) PreviewDigits(view.judgeRows[row].digits, result.judgeDigits, rows[row % rows.Length], 0);
            PreviewDigits(view.scoreOutline, result.scoreDigits, 754320, 0);
            PreviewDigits(view.scoreFill, result.scoreDigits, 754320, 10);
            view.highScore.gameObject.SetActive(true);
            view.highScoreGroup.alpha = 1;
            PreviewDigits(view.highScoreDigits, result.highScoreDigits, 12340, 0);

            view.crown.sprite = result.crowns[0];
            view.crown.Alpha(1);
            foreach (var image in new[] { view.crownFade, view.burstA, view.burstB, view.stars, view.shine }) image.Alpha(0);
            int message = (int)ResultMessage.Success;
            view.message.sprite = result.messages[message];
            view.message.Alpha(1);
            view.messageText.text = ResultScene.MessageTexts[message];
            view.messageText.Alpha(1);

            // 45 of 50 cells on the hard/oni art: past the clear mark, not full
            int art = Mathf.Clamp(view.gaugeArt, 0, 2), clearCell = ResultScene.ClearCell[art], shown = 45;
            view.unfilled.sprite = result.unfilled[art];
            view.unfilled.enabled = true;
            view.overlay.sprite = result.overlays[art];
            view.overlay.Alpha(0.15f);
            PreviewWidth(view.bar, (clearCell - 1) * ResultScene.GaugeUnit);
            view.clearTransition.enabled = true;
            PreviewWidth(view.clearTop, (shown - clearCell) * ResultScene.GaugeUnit);
            PreviewWidth(view.clearBottom, (shown - clearCell) * ResultScene.GaugeUnit);
            foreach (var image in view.rainbow) image.enabled = false;
            view.clearCaption.sprite = result.clearCaption;
            view.clearCaption.enabled = true;
            view.soul.sprite = result.soul;
            view.soul.enabled = true;
            view.soulFire.enabled = view.soulSheen.enabled = false;
            view.freePlay.Alpha(1);
            EditorSceneManager.MarkSceneDirty(result.gameObject.scene);
            SceneView.RepaintAll();
        }

        static void PreviewResultBackground(ResultView.BackgroundView view)
        {
            view.group.alpha = 1;
            foreach (var image in view.sky) image.Alpha(1);
            foreach (var image in view.skyClear) image.Alpha(0);
            view.fuji.Alpha(1);
            view.fujiClear.Alpha(0);
            for (int i = 0; i < view.clouds.Length; i++)
            {
                float a = ResultBackground.Layers[i].A;
                view.clouds[i].Alpha(a > 0.004f ? a : 0);
                view.cloudsClear[i].Alpha(0);
            }
            view.header.Alpha(1);
        }

        static void PreviewDigits(Image[] images, Sprite[] sprites, int value, int offset)
        {
            string text = value.ToString();
            for (int i = 0; i < images.Length; i++)
            {
                images[i].enabled = i < text.Length;
                images[i].rectTransform.localScale = Vector3.one;
                if (i < text.Length) images[i].sprite = sprites[text[text.Length - 1 - i] - '0' + offset];
            }
        }

        static void PreviewWidth(Image image, float width)
        {
            image.enabled = true;
            image.rectTransform.sizeDelta = new Vector2(width, image.rectTransform.sizeDelta.y);
        }
    }
}
