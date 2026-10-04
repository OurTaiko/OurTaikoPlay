using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        public const string SongBoardPrefabPath = Root + "Generated/SongBoard.prefab";
        public const string PlayOptionsPrefabPath = Root + "Generated/PlayOptions.prefab";
        static readonly string[] SavedCourseNames = { "かんたん", "ふつう", "むずかしい", "おに", "おに(裏)" };
        static readonly float[] SavedCourseX = { 745, 960, 1175, 1390 };

        [MenuItem("OurTaiko/Apply Song Select Layout")]
        public static void ApplySongSelectLayout()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before saving the SongSelect layout.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            var scene = EditorSceneManager.OpenScene(SongSelectPath);
            var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
            if (select == null) throw new InvalidOperationException("SongSelect controller is missing.");
            if (select.view != null)
            {
                Debug.Log("OurTaiko: SongSelect already has a saved layout; existing hierarchy and Prefab edits were preserved.");
                return;
            }
            if (select.wheel.childCount != 0 || select.coursePanel.childCount != 0)
                throw new InvalidOperationException("SongSelect contains an authored hierarchy without a view binding. Bind it before running migration; it will not be overwritten.");

            var stage = (RectTransform)select.wheel.parent;
            var view = stage.gameObject.AddComponent<SongSelectView>();
            select.view = view;
            view.boardPrefab = CreateSavedSongBoardPrefab(select);
            view.songBoards = new SongBoardView[select.songs.Length];
            for (int i = 0; i < select.songs.Length; i++)
            {
                var board = ((GameObject)PrefabUtility.InstantiatePrefab(view.boardPrefab.gameObject, select.wheel)).GetComponent<SongBoardView>();
                board.name = select.songs[i].name;
                board.song = select.songs[i];
                PopulateSavedSongBoard(select, board);
                float offset = i;
                if (offset > select.songs.Length / 2f) offset -= select.songs.Length;
                board.Root.Center(view.wheelCentre.x + offset * view.rowCurve,
                    view.wheelCentre.y + offset * view.rowPitch + Math.Sign(offset) * view.expandGap);
                board.authoredWheelPosition = board.Root.anchoredPosition;
                view.songBoards[i] = board;
            }
            CreateSavedCoursePanel(select, view);

            if (select.nameplatePrefab != null)
            {
                view.nameplate = ((GameObject)PrefabUtility.InstantiatePrefab(select.nameplatePrefab.gameObject, stage)).GetComponent<NameplateView>();
                view.nameplate.name = "Nameplate";
                view.nameplate.Place(14, 908);
                view.nameplate.transform.SetSiblingIndex(select.coursePanel.GetSiblingIndex());
            }
            view.overlays = CreateSongSelectOverlayView(stage, select.overlay);
            view.overlays.transform.SetSiblingIndex(select.coursePanel.GetSiblingIndex() + 1);
            PersistSongSelectTextMaterials(stage);
            PreviewSongSelectLayout(select, 0, false);
            foreach (var component in stage.GetComponentsInChildren<Component>(true))
                if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    if (component is Transform) PrefabUtility.RecordPrefabInstancePropertyModifications(component.gameObject);
                }
            EditorUtility.SetDirty(select);
            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = select.gameObject;
            Debug.Log("OurTaiko: SongSelect hierarchy, SongBoard prefab and PlayOptions prefab are saved. Use the SongSelect Inspector preview buttons in Edit mode.");
        }

        static SongBoardView CreateSavedSongBoardPrefab(SongSelectScene select)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SongBoardView>(SongBoardPrefabPath);
            if (existing != null) return existing;
            var root = SkinUi.Rect("SongBoard", null);
            var board = root.gameObject.AddComponent<SongBoardView>();
            board.group = root.GetComponent<CanvasGroup>();
            board.group.blocksRaycasts = true;
            board.glow = SkinUi.Image("CursorGlow", root, select.cursorGlow, 1024, 212);
            board.glow.enabled = false;
            board.panel = SkinUi.Image("Board", root, select.boards[0], 960, 164);
            board.panel.raycastTarget = true;
            board.click = board.panel.gameObject.AddComponent<PointerRelay>();
            var contents = SkinUi.Rect("Contents", root);
            board.contents = contents.gameObject.AddComponent<CanvasGroup>();
            board.contents.blocksRaycasts = false;
            board.contents.alpha = 0;
            board.plates = new SongBoardView.PlateView[5];
            for (int d = 0; d < board.plates.Length; d++)
                board.plates[d] = CreateSavedPlate(select, contents, (Difficulty)d);
            board.crown = SkinUi.Image("Crown", root, select.crownClear[0], 72, 72);
            board.crown.rectTransform.Center(-425, -27);
            board.crown.enabled = false;
            board.title = SkinUi.Text("Title", root, 42);
            board.title.text = "曲名";
            board.subtitle = SkinUi.Text("Subtitle", root, 24);
            board.subtitle.text = "サブタイトル";
            board.subtitle.rectTransform.Center(0, -28);
            board.subtitle.enabled = false;
            PersistSongSelectTextMaterials(root);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, SongBoardPrefabPath);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return prefab.GetComponent<SongBoardView>();
        }

        static SongBoardView.PlateView CreateSavedPlate(SongSelectScene select, Transform parent, Difficulty difficulty)
        {
            int d = (int)difficulty;
            var root = SkinUi.Rect(difficulty.ToString(), parent);
            root.Center((Math.Min(d, 3) - 1.5f) * 182, 75);
            var plate = new SongBoardView.PlateView { difficulty = difficulty, group = root.gameObject.AddComponent<CanvasGroup>() };
            plate.authoredContentX = root.anchoredPosition.x;
            plate.plate = SkinUi.Image("Plate", root, select.plates[d], 184, 96);
            plate.star = SkinUi.Image("Star", root, select.stars[d], 40, 40);
            plate.star.rectTransform.Center(27, 0);
            plate.level = SkinUi.Image("Level", root, select.levels[d * 11], 48, 48);
            plate.level.rectTransform.Center(61, 0);
            plate.label = SkinUi.Text("Course", root, 18);
            plate.label.characterSpacing = 100f / 18;
            plate.label.text = SavedCourseNames[d];
            plate.label.rectTransform.Center(-42, 30);
            plate.branch = SkinUi.Image("Branch", root, select.branch, 40, 40);
            plate.branch.rectTransform.Center(-69, -23);
            plate.branch.enabled = false;
            return plate;
        }

        static void PopulateSavedSongBoard(SongSelectScene select, SongBoardView board)
        {
            var info = board.song.ReadInfo();
            board.panel.sprite = select.boards[board.song.genre];
            board.title.text = info.Title;
            board.title.Squeeze(860);
            board.subtitle.text = info.Subtitle;
            board.subtitle.Squeeze(860, 0.75f);
            var columns = new List<Difficulty>();
            for (var d = Difficulty.Easy; d <= Difficulty.Hard; d++) if (info.Has(d)) columns.Add(d);
            if (info.Has(Difficulty.Oni) || info.Has(Difficulty.Ura)) columns.Add(Difficulty.Oni);
            foreach (var plate in board.plates)
            {
                var course = info.Course(plate.difficulty);
                plate.group.gameObject.SetActive(course != null);
                if (course == null) continue;
                int column = columns.IndexOf(plate.difficulty == Difficulty.Ura ? Difficulty.Oni : plate.difficulty);
                ((RectTransform)plate.group.transform).Center((column - (columns.Count - 1) / 2f) * board.platePitch, 75);
                plate.authoredContentX = ((RectTransform)plate.group.transform).anchoredPosition.x;
                plate.level.sprite = select.levels[(int)plate.difficulty * 11 + Mathf.Clamp(course.Level, 1, 11) - 1];
                plate.branch.enabled = course.IsBranching;
                plate.group.alpha = plate.difficulty == Difficulty.Ura && info.Has(Difficulty.Oni) ? 0 : 1;
            }
        }

        static void CreateSavedCoursePanel(SongSelectScene select, SongSelectView view)
        {
            var panel = select.coursePanel;
            view.courseGroup = panel.gameObject.AddComponent<CanvasGroup>();
            view.mark = SkinUi.Image("CourseMark", panel, select.courseMarks[0], 680, 680);
            view.mark.rectTransform.Center(200, 408);
            view.backboard = SkinUi.Image("Backboard", panel, select.backboards[0], 1272, 784);
            view.backboard.rectTransform.Center(960, 440);
            view.glow = SkinUi.Image("ButtonCursor", panel, select.buttonGlow, 168, 168);
            view.glow.rectTransform.Center(440, 462);
            view.glow.enabled = false;
            view.frame = SkinUi.Image("CourseCursor", panel, select.courseFrame, 248, 408);
            view.frame.rectTransform.Center(SavedCourseX[0], 583);
            view.back = SkinUi.Image("Back", panel, select.backButton, 128, 128);
            view.back.rectTransform.Center(440, 462);
            AddSavedClick(view.back);
            view.option = SkinUi.Image("Option", panel, select.optionButton, 128, 128);
            view.option.rectTransform.Center(572, 462);
            AddSavedClick(view.option);
            view.auto = SkinUi.Image("AutoPlay", panel, select.autoIcon, 40, 40);
            view.auto.rectTransform.Center(616, 506);
            view.auto.enabled = false;
            view.cards = new SongSelectView.CourseCardView[4];
            for (int i = 0; i < view.cards.Length; i++)
            {
                var card = view.cards[i] = new SongSelectView.CourseCardView();
                card.board = SkinUi.Image("Course" + i, panel, select.courseBoards[i], 200, 360);
                card.board.rectTransform.Center(SavedCourseX[i], 583);
                card.click = AddSavedClick(card.board);
                card.crown = SkinUi.Image("Crown", card.board.transform, select.smallCrowns[0], 40, 40);
                card.crown.rectTransform.Center(42, 42);
                card.star = SkinUi.Image("Star", card.board.transform, select.smallStars[0], 56, 40);
                card.star.rectTransform.TopLeft(60, 246);
                card.level = SkinUi.Image("Level", card.board.transform, select.smallStars[1], 56, 40);
                card.level.rectTransform.TopLeft(96, 248);
                card.bar = SkinUi.Image("LevelBar", card.board.transform, select.levelBar, 172, 24);
                card.bar.rectTransform.Center(100, 296);
                card.dots = new Image[10];
                for (int k = 0; k < card.dots.Length; k++)
                {
                    card.dots[k] = SkinUi.Image("Dot" + k, card.board.transform, select.levelDot, 24, 24);
                    card.dots[k].rectTransform.Center(33 + k * 15, 296);
                }
                card.branch = SkinUi.Image("Branch", card.board.transform, select.courseBranch, 40, 40);
                card.branch.rectTransform.Center(100, 324);
                card.name = SkinUi.Text("CourseName", card.board.transform, 34);
                card.name.characterSpacing = 100f / 34;
                card.name.rectTransform.Center(100, 229);
            }
            view.uraChange = SkinUi.Image("UraChange", panel, null, 340, 400);
            view.uraChange.rectTransform.TopLeft(SavedCourseX[3] - 170, 373);
            view.uraChange.enabled = false;
            view.header = SkinUi.Text("Title", panel, 48);
            view.header.rectTransform.Center(960, 178);
            view.headerSub = SkinUi.Text("Subtitle", panel, 30);
            view.headerSub.rectTransform.Center(960, 242);
            view.balloon = SkinUi.Image("PlayerBalloon", panel, select.playerBalloon, 124, 124);
            view.balloon.rectTransform.Center(SavedCourseX[0], 370);

            var optionsPrefab = AssetDatabase.LoadAssetAtPath<OptionPanelView>(PlayOptionsPrefabPath);
            if (optionsPrefab == null)
            {
                var options = CreateOptionPanelView(null, select.optionArt);
                PersistSongSelectTextMaterials(options.transform);
                var prefab = PrefabUtility.SaveAsPrefabAsset(options.gameObject, PlayOptionsPrefabPath);
                optionsPrefab = prefab.GetComponent<OptionPanelView>();
                UnityEngine.Object.DestroyImmediate(options.gameObject);
            }
            view.options = ((GameObject)PrefabUtility.InstantiatePrefab(optionsPrefab.gameObject, panel)).GetComponent<OptionPanelView>();
            view.options.name = "OptionPanel";
            FillSavedCoursePreview(select);
        }

        static PointerRelay AddSavedClick(Image image)
        {
            image.raycastTarget = true;
            return image.gameObject.AddComponent<PointerRelay>();
        }

        static void FillSavedCoursePreview(SongSelectScene select)
        {
            if (select.songs.Length == 0) return;
            var song = select.songs[0];
            var info = song.ReadInfo();
            var view = select.view;
            view.backboard.sprite = select.backboards[song.genre];
            view.header.text = info.Title;
            view.header.Squeeze(1000);
            view.headerSub.text = info.Subtitle;
            view.headerSub.Squeeze(1000);
            for (int i = 0; i < view.cards.Length; i++)
            {
                var card = view.cards[i];
                var details = info.Course((Difficulty)i);
                bool has = details != null;
                card.board.color = new Color(1, 1, 1, has ? 1 : 0.4f);
                card.name.text = SavedCourseNames[i];
                card.name.enabled = has;
                foreach (var image in new[] { card.crown, card.star, card.level, card.bar }) image.enabled = has;
                card.branch.enabled = has && details.IsBranching;
                for (int k = 0; k < card.dots.Length; k++) card.dots[k].enabled = has && k < Math.Min(10, details.Level);
                if (has) card.level.sprite = select.smallStars[Mathf.Clamp(details.Level, 1, 11)];
            }
        }

        // Explicit Edit-mode controls reveal the saved hierarchy without executing scene gameplay,
        // reading player data or replacing the authored RectTransform animation baselines.
        public static void PreviewSongSelectLayout(SongSelectScene select, int mode, bool recordUndo = true)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Layout preview is available only in Edit mode.");
            if (select.view == null) throw new InvalidOperationException("Save the SongSelect layout first.");
            var view = select.view;
            if (recordUndo) Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "Preview SongSelect layout");
            select.wheel.gameObject.SetActive(mode == 0);
            select.coursePanel.gameObject.SetActive(mode != 0);
            view.courseGroup.alpha = 1;
            if (view.bestScore != null)
            {
                PreviewBestScore(view.bestScore);
                view.bestScore.group.alpha = mode == 2 ? 0 : 1;
            }
            view.options.gameObject.SetActive(mode == 2);
            if (PrefabUtility.IsPartOfPrefabInstance(view.options))
                PrefabUtility.RecordPrefabInstancePropertyModifications(view.options.gameObject);
            new ArcadeTimerView(view.overlays, select.overlay).Show(mode == 0 ? 100 : 60);
            view.overlays.inviteBubble.Alpha(1);
            view.overlays.invitePlayer.alpha = view.overlays.inviteMessage.alpha = 1;
            view.overlays.invitePlayer.enabled = view.overlays.inviteMessage.enabled = true;
            EditorSceneManager.MarkSceneDirty(select.gameObject.scene);
            SceneView.RepaintAll();
        }

        // Texts share SkinUi's saved materials; drop any TMP instance caches before serializing.
        static void PersistSongSelectTextMaterials(Transform root)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var serialized = new SerializedObject(text);
                serialized.FindProperty("m_fontMaterial").objectReferenceValue = null;
                serialized.FindProperty("m_fontSharedMaterials").ClearArray();
                serialized.FindProperty("m_fontMaterials").ClearArray();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                text.UpdateMeshPadding();
                EditorUtility.SetDirty(text);
            }
        }
    }
}
