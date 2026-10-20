using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string DoublePlayScenePath = "Assets/Scenes/DoublePlayScene.unity";
        // GameScreen::draw_players: 1P's lane at 184, 2P's at game_p2_y (PyTaikoGreen 360), both x1.5 in Nijiiro.
        const float Lane2PY = 540;
        // PyTaikoGreen's 2P offsets from the 2P lane's top, x1.5: gauge_2p_offset, combo_announce_p2_y_offset
        // (also the drumroll counter), score_counter_2p_y_offset; Nijiiro's own balloon_counter_2p_offset and
        // game_nameplate_2p (1P's plate sits at game_nameplate_1p y 161).
        const float Gauge2PY = 342, Announce2PY = 564, Score2PY = 195, Balloon2PY = 180, Nameplate2PY = 7;
        // background game_2p_offset (536 x1.5): 2P's donbg band.
        const float DonBg2PY = 804;

        static readonly string[] TwoPlayerArt =
        {
            "global/nameplate/2p", "game/lane/2p_lane_cover", "game/lane/2p_icon",
            "game/gauge/2p_bar", "game/gauge/2p_bar_fade", "game/gauge/2p_unfilled_easy", "game/gauge/2p_unfilled_normal", "game/gauge/2p_unfilled_hard",
            "game/combo/announce_bg_2p", "background/donbg/0_2/background/0", "background/donbg/0_2/background/1",
        };

        // DoublePlayScene: a copy of SinglePlayScene whose lane, soul gauge, flying notes, combo
        // announce and balloon counter are cloned for 2P under the 1P lane, with Nijiiro's 2P art and
        // offsets (Player::draw with is_2p); the background drops the dancers and the stage like
        // Background with TWO_PLAYER, and adds 2P's donbg band. Only builds what is missing.
        [MenuItem("OurTaiko/Apply Double Play Scene")]
        public static void ApplyDoublePlay()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            ImportSprites(TwoPlayerArt.Select(art => Root + "Art/" + art + ".png").ToArray());
            ImportComboArt2P();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DoublePlayScenePath) == null
                && !AssetDatabase.CopyAsset("Assets/Scenes/SinglePlayScene.unity", DoublePlayScenePath))
                throw new InvalidOperationException("Could not copy SinglePlayScene.");
            var scene = EditorSceneManager.OpenScene(DoublePlayScenePath);
            var play = Object.FindFirstObjectByType<PlayScene>();
            if (play.secondLane == null)
            {
                BuildSecondLane(play);
                TwoPlayerBackground(play);
            }
            MirrorSecondReadouts(play);
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!EditorBuildSettings.scenes.Any(s => s.path == DoublePlayScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(DoublePlayScenePath, true)).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: DoublePlayScene saved.");
        }

        static void BuildSecondLane(PlayScene play)
        {
            var lane = (RectTransform)play.noteLayer.parent.parent;
            // Everything the 1P lane's views live under: the lane itself and the HUD pieces drawn over it.
            var sources = new Transform[] { lane, play.soulGauge.transform, play.noteArcs.transform,
                play.noteArcs.gaugeHitEffect.transform, play.comboAnnounce.transform, play.balloonCounter.transform };
            var map = new Dictionary<Object, Object>();
            var clones = new List<Transform>();
            foreach (var source in sources)
            {
                var copy = Object.Instantiate(source.gameObject, source.parent).transform;
                copy.name = source.name + "2P";
                copy.SetSiblingIndex(source.GetSiblingIndex() + 1);
                MapHierarchy(source, copy, map);
                clones.Add(copy);
            }
            // References between the cloned pieces (and to the lane) point at the 2P copies.
            foreach (var component in clones.SelectMany(c => c.GetComponentsInChildren<Component>(true)))
            {
                if (component == null || component is Transform) continue;
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                bool changed = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null) continue;
                    if (!map.TryGetValue(property.objectReferenceValue, out var target)) continue;
                    property.objectReferenceValue = target;
                    changed = true;
                }
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var lane2 = (RectTransform)clones[0];
            var views = lane2.gameObject.AddComponent<LaneViews>();
            foreach (var field in typeof(LaneViews).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                // 2P's judge counter is its own copy (MirrorSecondReadouts).
                if (field.Name == nameof(LaneViews.judgeCounter)) continue;
                field.SetValue(views, Remap(field.GetValue(play), map));
            }
            Lay2P(lane, lane2, views);
            play.secondLane = views;
        }

        // 2P's timing feedback and judge counter (user decision, 2026-10-20; YataiDON gives 2P no
        // judge counter) sit below the 2P lane, mirrored from 1P's across the line between the lanes.
        static void MirrorSecondReadouts(PlayScene play)
        {
            var views = play.secondLane;
            var viewport = (RectTransform)play.noteLayer.GetComponentInParent<Canvas>().rootCanvas.transform.Find("Viewport1920x1080");
            var lane1 = (RectTransform)play.noteLayer.parent.parent;
            var lane2 = (RectTransform)views.transform;
            // A scene just opened has not been laid out: its canvas scale would make every box a point.
            Canvas.ForceUpdateCanvases();
            if (Bounds(viewport, lane1).height < 1) throw new InvalidOperationException("The play canvas is not laid out.");
            float split = (Bounds(viewport, lane1).yMin + Bounds(viewport, lane2).yMax) / 2;
            if (views.judgeCounter == null && play.judgeCounter != null)
            {
                var copy = Object.Instantiate(play.judgeCounter.gameObject, play.judgeCounter.transform.parent);
                copy.name = play.judgeCounter.name + "2P";
                copy.transform.SetSiblingIndex(play.judgeCounter.transform.GetSiblingIndex() + 1);
                views.judgeCounter = copy.GetComponent<JudgeCounterView>();
            }
            if (views.judgeCounter != null) MirrorAcross(viewport, play.judgeCounter.transform, views.judgeCounter.transform, split);
            if (views.timingFeedback != null) MirrorAcross(viewport, play.timingFeedback.transform, views.timingFeedback.transform, split);
            // The play ScoreRank (user request): under the 2P lane so it leaves 2P's nameplate clear. Its clip
            // moves the inner badge, so the box is the badge's and the move is the holder's.
            var rank = play.scoreCounter.scoreRank; var rank2P = views.scoreCounter.scoreRank;
            if (rank != null && rank2P != null)
                MirrorAcross(viewport, rank.transform.Find("ScoreRank") ?? rank.transform, rank2P.transform.Find("ScoreRank") ?? rank2P.transform, split, rank2P.transform);
            // Gauges built before SoulGaugeView took these from the scene still have 1P's.
            if (Mathf.Approximately(views.soulGauge.goldTopY, 60)) KeepGauge2PHeights(views.soulGauge);
            EditorUtility.SetDirty(views);
        }

        static UnityEngine.Rect Bounds(RectTransform space, Transform target)
        {
            var corners = new Vector3[4];
            ((RectTransform)target).GetWorldCorners(corners);
            Vector2 a = space.InverseTransformPoint(corners[0]), b = space.InverseTransformPoint(corners[2]);
            return UnityEngine.Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        // Moves target (a copy of source) so its box is source's box reflected across y = split; the
        // contents keep their upright layout.
        static void MirrorAcross(RectTransform space, Transform source, Transform target, float split, Transform moved = null)
        {
            var from = Bounds(space, source);
            var at = Bounds(space, target);
            var wanted = new Vector2(from.center.x, 2 * split - from.center.y);
            (moved ?? target).position += space.TransformVector(wanted - at.center);
        }

        static void MapHierarchy(Transform source, Transform copy, Dictionary<Object, Object> map)
        {
            var from = source.GetComponentsInChildren<Transform>(true);
            var to = copy.GetComponentsInChildren<Transform>(true);
            if (from.Length != to.Length) throw new InvalidOperationException("The clone of " + source.name + " differs from its source.");
            for (int i = 0; i < from.Length; i++)
            {
                map[from[i].gameObject] = to[i].gameObject;
                var a = from[i].GetComponents<Component>();
                var b = to[i].GetComponents<Component>();
                for (int k = 0; k < a.Length && k < b.Length; k++) if (a[k] != null) map[a[k]] = b[k];
            }
        }

        // A view field pointed at the clone: objects through the map, arrays and [Serializable] rows copied.
        static object Remap(object value, Dictionary<Object, Object> map)
        {
            if (value is Object obj) return obj != null && map.TryGetValue(obj, out var target) ? target : null;
            if (value is Array array)
            {
                var copy = Array.CreateInstance(array.GetType().GetElementType(), array.Length);
                for (int i = 0; i < array.Length; i++) copy.SetValue(Remap(array.GetValue(i), map), i);
                return copy;
            }
            if (value != null && value.GetType().IsClass && value.GetType().IsSerializable && !(value is string))
            {
                var copy = Activator.CreateInstance(value.GetType());
                foreach (var field in value.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    field.SetValue(copy, Remap(field.GetValue(value), map));
                return copy;
            }
            return value;
        }

        static void Lay2P(RectTransform lane, RectTransform lane2, LaneViews views)
        {
            lane2.anchoredPosition = new Vector2(lane.anchoredPosition.x, -Lane2PY);
            var foreground = lane2.Find("LaneForeground");
            SetSprite(foreground.Find("PlayerCover"), "game/lane/2p_lane_cover");
            SetSprite(foreground.Find("PlayerIcon"), "game/lane/2p_icon");
            // lane_difficulty index 1.
            if (views.laneDifficulty != null) views.laneDifficulty.rectTransform.anchoredPosition = new Vector2(29, -98);
            var nameplate = foreground.GetComponentInChildren<NameplateView>(true);
            if (nameplate != null)
            {
                nameplate.seat = 1;
                nameplate.secondBadge = Sprite("global/nameplate/2p");
                var rect = nameplate.RectTransform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -Nameplate2PY);
                EditorUtility.SetDirty(nameplate);
            }
            Shift(views.drumrollCounter.transform, -Announce2PY);
            // ScoreCounter::draw with is_2p: the cover mirrored and everything score_counter_2p_y_offset lower.
            // The modifier badges follow the cover down (Player::draw_modifiers mirrors their rows too).
            Shift(views.scoreCounter.transform, -Score2PY);
            if (views.scoreCounter.cover != null) Mirror(views.scoreCounter.cover.rectTransform);
            if (views.modifierBadges != null) Shift(views.modifierBadges.transform, -Score2PY);

            SetY(views.soulGauge.transform, -(Lane2PY + Gauge2PY));
            Gauge2P(views.soulGauge);
            SetY(views.comboAnnounce.transform, -(Lane2PY + Announce2PY));
            AssignComboAnnounce2P(views.comboAnnounce);
            SetY(views.balloonCounter.transform, -(Lane2PY + Balloon2PY));
            views.noteArcs.seat = 1;
            views.noteArcs.gaugeHitEffect.seat = 1;
            EditorUtility.SetDirty(views.noteArcs); EditorUtility.SetDirty(views.noteArcs.gaugeHitEffect);
            EditorUtility.SetDirty(views);
        }

        // game/gauge texture.json index 1: 2P's gauge sits under its lane, its frame art mirrored
        // vertically (Gauge:draw mirrors below mid-screen), its bar the 2p art. Each entry is how much
        // higher index 1 is drawn than index 0 (Unity y up).
        static readonly (string Child, float Up, bool Mirrored)[] Gauge2PLayout =
        {
            ("Border", 18, true), ("Empty", 18, true), ("Grid", 18, true), ("Rainbow", 22, true),
            ("ClearCap", 24, true), ("GoldTop", -8, true), ("GoldBottom", 58, false), ("Fill", 57, false),
            ("CellFade", 57, false), ("SoulFire", 18, false), ("Soul", 18, false), ("SoulOverlay", 18, false),
        };

        static void Gauge2P(SoulGaugeView gauge)
        {
            foreach (var (child, up, mirrored) in Gauge2PLayout)
            {
                var rect = gauge.transform.Find(child) as RectTransform;
                if (rect == null) continue;
                rect.anchoredPosition += new Vector2(0, up);
                if (!mirrored) continue;
                if (child == "Rainbow") foreach (RectTransform cell in rect) Mirror(cell);
                else Mirror(rect);
            }
            gauge.clearLabelY = 68;
            KeepGauge2PHeights(gauge);
            foreach (var style in gauge.styles) style.empty = SecondArt(style.empty);
            gauge.red.sprite = SecondArt(gauge.red.sprite);
            gauge.empty.sprite = SecondArt(gauge.empty.sprite);
            gauge.redFade = SecondArt(gauge.redFade);
            EditorUtility.SetDirty(gauge);
        }

        // SoulGaugeView places the clear cap, gold zone and fading cell every frame, so 2P's gauge keeps
        // their 2P (index 1, mirrored) y from its saved layout.
        static void KeepGauge2PHeights(SoulGaugeView gauge)
        {
            gauge.clearCapY = ((RectTransform)gauge.transform.Find("ClearCap")).anchoredPosition.y;
            gauge.goldTopY = ((RectTransform)gauge.transform.Find("GoldTop")).anchoredPosition.y;
            gauge.goldBottomY = ((RectTransform)gauge.transform.Find("GoldBottom")).anchoredPosition.y;
            // bar_fade, bar_clear_fade and bar_clear_transition_fade all sit at y -84 in index 1.
            gauge.redFadeY = gauge.goldFadeY = ((RectTransform)gauge.transform.Find("CellFade")).anchoredPosition.y;
            EditorUtility.SetDirty(gauge);
        }

        // The 2p_ art beside a 1p_ sprite.
        static Sprite SecondArt(Sprite sprite)
        {
            if (sprite == null) return null;
            string path = AssetDatabase.GetAssetPath(sprite);
            string second = Path.GetDirectoryName(path).Replace('\\', '/') + "/" + Path.GetFileName(path).Replace("1p_", "2p_");
            return second == path ? sprite : AssetDatabase.LoadAssetAtPath<Sprite>(second) ?? throw new FileNotFoundException(second);
        }

        static void ImportComboArt2P()
        {
            foreach (string name in new[] { "announce_bg_2p", "announce_digit_2p" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "Art/" + ComboArt + name + ".png");
                if (importer.textureType == TextureImporterType.Sprite) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        // announce_bg_2p / announce_digit_2p at 1P's places, and the 2p voices.
        static void AssignComboAnnounce2P(ComboAnnounceView view)
        {
            SetSprite(view.transform.Find("Background"), ComboArt + "announce_bg_2p");
            view.digits = SliceSheet(ComboArt + "announce_digit_2p",
                Enumerable.Range(0, 10).Select(i => ("ComboAnnounce2P" + i, 0, i * 104, 104, 104)).ToArray());
            view.voices = new[] { 50 }.Concat(Enumerable.Range(1, 50).Select(i => i * 100))
                .Select(count => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}Audio/combo/{count}_2p.ogg") ?? throw new FileNotFoundException($"combo voice {count} 2p"))
                .ToArray();
            view.Layout("300");
            EditorUtility.SetDirty(view);
        }

        // Background with TWO_PLAYER draws neither bg_normal, the dancers nor the footer: the 1P
        // donbg band at the top and 2P's at game_2p_offset.
        static void TwoPlayerBackground(PlayScene play)
        {
            var viewport = play.noteLayer.GetComponentInParent<Canvas>().rootCanvas.transform.Find("Viewport1920x1080");
            if (play.dancers != null) Object.DestroyImmediate(play.dancers.gameObject);
            play.dancers = null;
            foreach (string name in new[] { "Footer", "FestivalBackground", "FestivalLights" })
            {
                var target = viewport.Find(name);
                if (target != null) Object.DestroyImmediate(target.gameObject);
            }
            for (int i = 0; ; i++)
            {
                var band = viewport.Find("HeaderPattern" + i) as RectTransform;
                if (band == null) break;
                var copy = Object.Instantiate(band.gameObject, viewport).GetComponent<RectTransform>();
                copy.name = "HeaderPattern2P" + i;
                copy.SetSiblingIndex(band.GetSiblingIndex() + 1);
                copy.anchoredPosition = band.anchoredPosition - new Vector2(0, DonBg2PY);
                var image = copy.GetComponent<Image>();
                string art = "background/donbg/0_2/background/" + Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(image.sprite));
                image.sprite = Sprite(art) ?? throw new FileNotFoundException(art);
                foreach (var behaviour in copy.GetComponents<MonoBehaviour>()) if (!(behaviour is Graphic)) Object.DestroyImmediate(behaviour);
                var animator = copy.GetComponent<Animator>();
                if (animator != null) Object.DestroyImmediate(animator);
            }
        }

        static void SetSprite(Transform target, string art)
        {
            var image = target != null ? target.GetComponent<Image>() : null;
            if (image == null) throw new InvalidOperationException("Missing image for " + art);
            image.sprite = Sprite(art) ?? throw new FileNotFoundException(art);
            EditorUtility.SetDirty(image);
        }

        static void Shift(Transform target, float dy) => ((RectTransform)target).anchoredPosition += new Vector2(0, dy);
        static void SetY(Transform target, float y)
        {
            var rect = (RectTransform)target;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        }

        // Flips an image upside down in place, keeping the rectangle it covers.
        static void Mirror(RectTransform rect)
        {
            var scale = rect.localScale;
            rect.localScale = new Vector3(scale.x, -scale.y, scale.z);
            rect.anchoredPosition += new Vector2(0, (1 - 2 * rect.pivot.y) * rect.rect.height);
        }
    }
}
