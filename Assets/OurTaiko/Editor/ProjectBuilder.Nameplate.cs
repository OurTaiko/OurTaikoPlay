using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string NameplateArt = "global/nameplate", NameplatePrefabPath = Root + "Generated/Nameplate.prefab";

        // Builds the Nijiiro nameplate prefab and puts it on SinglePlayScene, SongSelect and Result; also
        // swaps SinglePlayScene's placeholder score label for the original score counter, adds the AUTO
        // badge art to the option dock and removes the debug player / play-state labels (autoplay only
        // shows in the dock).
        [MenuItem("OurTaiko/Apply Nijiiro Nameplate")]
        public static void ApplyNameplate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the nameplate.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportSprites(Directory.GetFiles(Root + "Art/" + NameplateArt, "*.png"));
            BuildNameplatePrefab();

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            var lane = (RectTransform)play.noteLayer.parent.parent;
            foreach (string label in new[] { "PlayerName", "PlayState" })
            {
                var debug = lane.parent.Find(label);
                if (debug != null) UnityEngine.Object.DestroyImmediate(debug.gameObject);
            }
            // Opening a scene unloads unreferenced assets, so the prefab is loaded after each one.
            ConfigurePlayNameplate(play, lane, NameplatePrefab());
            ConfigureScoreCounter(play, lane);
            play.modifierBadges.auto = Sprite("song_select/modifier/mod_auto");
            EditorUtility.SetDirty(play.modifierBadges);
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            scene = EditorSceneManager.OpenScene(SongSelectPath);
            var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
            select.nameplatePrefab = NameplatePrefab();
            EditorUtility.SetDirty(select);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // Result saves its nameplate instance with its layout (ApplyResultLayout).
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: nameplate applied to SinglePlayScene and SongSelect.");
        }

        static NameplateView NameplatePrefab() => AssetDatabase.LoadAssetAtPath<NameplateView>(NameplatePrefabPath);

        static void BuildNameplatePrefab()
        {
            var uiFont = UiFont();
            var root = new GameObject("Nameplate", typeof(RectTransform));
            try
            {
                var view = root.AddComponent<NameplateView>();
                view.Place(0, 0);
                string art = NameplateArt + "/";
                // texture.json crops: frame_top 5 x 408x56, frame_top_rainbow 6 x 408x56, dan_emblem 5 x 5 of 72x40.
                view.titleBackgrounds = Enumerable.Range(0, 5).Select(i => Slice("NameplateTitle" + i, art + "frame_top", 0, i * 56, 408, 56)).ToArray();
                view.danEmblems = Enumerable.Range(0, PlayerInfo.DanCount).Select(i => Slice($"NameplateDan{i:00}", art + "dan_emblem", i % 5 * 72, i / 5 * 40, 72, 40)).ToArray();
                view.goldDanEmblems = Enumerable.Range(0, PlayerInfo.DanCount).Select(i => Slice($"NameplateDanGold{i:00}", art + "dan_emblem_gold", i % 5 * 72, i / 5 * 40, 72, 40)).ToArray();
                view.shadow = PlateImage(root.transform, "Shadow", Sprite(art + "shadow"), 0, 0);
                view.bandUnder = PlateImage(root.transform, "BandUnder", NameplateRainbowFrames()[0], 0, 0);
                view.band = PlateImage(root.transform, "Band", view.titleBackgrounds[0], 0, 0);
                view.outline = PlateImage(root.transform, "Outline", Sprite(art + "outline"), 0, 0);
                view.danBackground = PlateImage(root.transform, "DanBackground", Sprite(art + "dan_emblem_bg"), NameplateLayout.DanBackgroundX, NameplateLayout.DanBackgroundY);
                view.dan = PlateImage(root.transform, "Dan", view.danEmblems[0], NameplateLayout.DanX, NameplateLayout.DanY);
                view.badge = PlateImage(root.transform, "Badge", Sprite(art + "1p"), NameplateLayout.BadgeX, NameplateLayout.BadgeY);
                // text_title: black, no border. text_name: white with a black border (outline material).
                view.title = PlateText(root.transform, "Title", uiFont,
                    uiFont.material,
                    Color.black, NameplateLayout.TitleBoxWidth, NameplateLayout.TitleFontSize);
                view.playerName = PlateText(root.transform, "Name", uiFont,
                    UiOutlineMaterial(), Color.white, NameplateLayout.NameBoxWidth, 24);
                AttachClip(root, NameplateRainbowClip());
                PrefabUtility.SaveAsPrefabAsset(root, NameplatePrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("OurTaiko/Apply Nameplate Rainbow Clip")]
        public static void ApplyNameplateRainbowClip()
        {
            var root = PrefabUtility.LoadPrefabContents(NameplatePrefabPath);
            try
            {
                AttachClip(root, NameplateRainbowClip());
                root.GetComponent<NameplateView>().bandUnder.sprite = NameplateRainbowFrames()[0];
                PrefabUtility.SaveAsPrefabAsset(root, NameplatePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Sprite[] NameplateRainbowFrames() => SliceSheet(NameplateArt + "/frame_top_rainbow",
            Enumerable.Range(0, 6).Select(i => ("NameplateRainbow" + i, 0, i * 56, 408, 56)).ToArray());

        // Global animation 12 (texture_change): frame k for 50 ms from 50k ms, looping every 300 ms.
        // Band shows frame k; BandUnder shows frame k - 1 under it from frame 1 on.
        static AnimationClip NameplateRainbowClip()
        {
            var frames = NameplateRainbowFrames();
            return SaveClip("NameplateRainbow", 20, true, clip =>
            {
                var image = typeof(UnityEngine.UI.Image);
                SpriteKeys(clip, "Band", image, frames, frames.Select((_, i) => i * 0.05f).ToArray());
                SpriteKeys(clip, "BandUnder", image, frames.Take(5).ToArray(), Enumerable.Range(1, 5).Select(i => i * 0.05f).ToArray());
                SteppedCurve(clip, "BandUnder", image, "m_Enabled", (0, 0), (0.05f, 1), (0.3f, 1));
            });
        }

        static UnityEngine.UI.Image PlateImage(Transform parent, string name, Sprite sprite, float x, float y)
        {
            if (sprite == null) throw new FileNotFoundException(name);
            var image = Rect(name, parent, x, y, sprite.rect.width, sprite.rect.height).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text PlateText(Transform parent, string name, TMP_FontAsset uiFont, Material material, Color color, float width, float size)
        {
            var rect = Rect(name, parent, 0, 0, width, size * 1.6f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            SetOutlinedUiText(text, uiFont, material, color);
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        // Player::draw: the nameplate comes after the drum, combo and judgments and before the balloon counter.
        static void ConfigurePlayNameplate(PlayScene play, RectTransform lane, NameplateView prefab)
        {
            var old = lane.Find("Nameplate");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var plate = ((GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, lane)).GetComponent<NameplateView>();
            plate.name = "Nameplate";
            // skin_config game_nameplate_1p, relative to the lane's y.
            plate.Place(-44, 161);
            plate.transform.SetSiblingIndex(play.balloonCounter.transform.GetSiblingIndex());
        }

        // ScoreCounter::draw is the last thing Player::draw paints in the lane.
        static void ConfigureScoreCounter(PlayScene play, RectTransform lane)
        {
            var label = lane.Find("Score");
            if (label != null) UnityEngine.Object.DestroyImmediate(label.gameObject);
            var root = lane.Find("ScoreCounter") as RectTransform;
            if (root == null) root = Rect("ScoreCounter", lane, 0, 0, 0, 0);
            root.SetAsLastSibling();
            var view = root.GetComponent<ScoreCounterView>();
            if (view == null) view = root.gameObject.AddComponent<ScoreCounterView>();
            view.cover = PlacePicture(root, "Cover", "game/lane/lane_score_cover", ScoreCounterLayout.CoverX, ScoreCounterLayout.CoverY);
            view.cover.raycastTarget = false;
            // score_number: ten 56x64 digits side by side.
            view.digits = Enumerable.Range(0, 10).Select(i => Slice("ScoreNumber" + i, "game/lane/score_number", i * 56, 0, 56, 64)).ToArray();
            AttachTextStretch(root.gameObject);
            play.scoreCounter = view;
            EditorUtility.SetDirty(view);
        }
    }
}
