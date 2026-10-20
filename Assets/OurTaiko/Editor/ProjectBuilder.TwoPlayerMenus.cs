using System;
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
        // skin_config option_p2: 2P's 演奏オプション board sits this far right of 1P's.
        const float OptionPanel2PX = 1490;

        // Entry and SongSelect pieces for 2P joining (issue #5): 2P's nameplate (nameplate_entry_right,
        // song_select_nameplate_2p), 2P's course cursor and option board, and the 2P voices. Only adds
        // what is missing; saved layouts are kept.
        [MenuItem("OurTaiko/Apply Two Player Menus")]
        public static void ApplyTwoPlayerMenus()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            ImportSprites(new[] { "global/nameplate/2p", "song_select/diff_select/2p_balloon", "song_select/diff_select/difficulty_select_bar2", "song_select/modifier/2p" }
                .Select(art => Root + "Art/" + art + ".png").ToArray());

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Entry.unity");
            var entry = Object.FindFirstObjectByType<EntryScene>();
            bool changed = false;
            if (entry.view.nameplate2P == null)
            {
                entry.view.nameplate2P = SecondNameplate(entry.view.nameplate, 1494, 910);
                entry.view.nameplateGroup2P = entry.view.nameplate2P.GetComponent<CanvasGroup>();
                entry.view.nameplateGroup2P.alpha = 0;
                EditorUtility.SetDirty(entry.view);
                changed = true;
            }
            if (entry.entryStart2P == null) { entry.entryStart2P = RequiredClip("entry/entry_start_2p"); changed = true; }
            if (changed) { EditorUtility.SetDirty(entry); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }

            scene = EditorSceneManager.OpenScene("Assets/Scenes/SongSelect.unity");
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            var view = select.view;
            changed = false;
            if (view.nameplate2P == null && view.nameplate != null)
            {
                view.nameplate2P = SecondNameplate(view.nameplate, 1494, 908);
                view.nameplateGroup2P = view.nameplate2P.GetComponent<CanvasGroup>();
                view.nameplateGroup2P.alpha = 0;
                changed = true;
            }
            if (view.frame2P == null)
            {
                view.frame2P = SecondImage(view.frame, "Frame2P", "song_select/diff_select/difficulty_select_bar2");
                view.balloon2P = SecondImage(view.balloon, "PlayerBalloon2P", "song_select/diff_select/2p_balloon");
                changed = true;
            }
            if (view.options2P == null)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(view.options.gameObject);
                var copy = source != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(source, view.options.transform.parent)
                    : Object.Instantiate(view.options.gameObject, view.options.transform.parent);
                copy.name = "OptionPanel2P";
                copy.transform.SetSiblingIndex(view.options.transform.GetSiblingIndex() + 1);
                view.options2P = copy.GetComponent<OptionPanelView>();
                view.options2P.board.anchoredPosition += new Vector2(OptionPanel2PX, 0);
                if (view.options2P.player != null) view.options2P.player.sprite = Sprite("song_select/modifier/2p");
                // The outside tap closes 1P's board; 2P's board leaves the screen to 1P's touch.
                if (view.options2P.outside != null) view.options2P.outside.enabled = false;
                copy.SetActive(false);
                if (source != null) PrefabUtility.RecordPrefabInstancePropertyModifications(copy);
                changed = true;
            }
            if (select.voiceStartSong2P == null)
            {
                select.voiceStartSong2P = RequiredClip("song_select/voice_start_song_2p");
                select.voiceOptions2P = RequiredClip("song_select/voice_options_2p");
                select.secondJoined = RequiredClip("song_select/entry_2p_add");
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(view); EditorUtility.SetDirty(select);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            Debug.Log("OurTaiko: two-player Entry and SongSelect pieces saved.");
        }

        static AudioClip RequiredClip(string path) => Clip(path) ?? throw new FileNotFoundException(path);

        // A 2P plate beside 1P's: the same prefab, seat 2, faded out until 2P joins.
        static NameplateView SecondNameplate(NameplateView first, float x, float y)
        {
            var plate = (GameObject)PrefabUtility.InstantiatePrefab(NameplatePrefab().gameObject, first.transform.parent);
            plate.name = "Nameplate2P";
            plate.transform.SetSiblingIndex(first.transform.GetSiblingIndex() + 1);
            var view = plate.GetComponent<NameplateView>();
            view.Place(x, y);
            view.seat = 1;
            view.secondBadge = Sprite("global/nameplate/2p");
            if (plate.GetComponent<CanvasGroup>() == null) plate.AddComponent<CanvasGroup>();
            plate.GetComponent<CanvasGroup>().blocksRaycasts = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            PrefabUtility.RecordPrefabInstancePropertyModifications(plate.transform);
            return view;
        }

        static Image SecondImage(Image first, string name, string art)
        {
            var copy = Object.Instantiate(first.gameObject, first.transform.parent).GetComponent<Image>();
            copy.name = name;
            copy.transform.SetSiblingIndex(first.transform.GetSiblingIndex() + 1);
            copy.sprite = Sprite(art) ?? throw new FileNotFoundException(art);
            copy.raycastTarget = false;
            copy.enabled = false;
            return copy;
        }
    }
}
