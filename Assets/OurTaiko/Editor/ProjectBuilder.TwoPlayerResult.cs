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
        // result texture.json index 1: 2P's board is 1P's moved right by 956 (the score, judge rows,
        // crown and difficulty tab); the gauge moves 947, the message bubble 579 and the nameplate 1507.
        const float Board2PX = 956, Gauge2PX = 947, Message2PX = 579, ResultNameplate2PX = 1507;

        // Result for two players (result_2p.cpp, Nijiiro index 1): a copy of 1P's score page on the right
        // half with the right-hand board, blue donback, 2p gauge and right-hand message bubbles, plus
        // each board's half of footer_2p as its clear backdrop. Only builds what is missing.
        [MenuItem("OurTaiko/Apply Two Player Result")]
        public static void ApplyTwoPlayerResult()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            ImportSprites(new[] { "result/score/overlay/1", "result/bottom/chara_0/1", "result/background/footer_2p/0", "result/background/footer_2p/1",
                    "result/gauge/2p_bar", "result/gauge/2p_unfilled_easy", "result/gauge/2p_unfilled_normal", "result/gauge/2p_unfilled_hard",
                    "result/crown/message/4", "result/crown/message/5", "result/crown/message/6", "result/crown/message/7" }
                .Select(art => Root + "Art/" + art + ".png").ToArray());
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Result.unity");
            var result = Object.FindFirstObjectByType<ResultScene>();
            var view = result.view;
            if (view.secondBoard == null)
            {
                var page = view.crown.transform.parent;
                // 1P's half of footer_2p, beside its own clear backdrop.
                view.successTwoPlayers = SuccessHalf(view.success, "result/background/footer_2p/0", 0);
                var copy = Object.Instantiate(page.gameObject, page.parent).transform;
                copy.name = page.name + "2P";
                copy.SetSiblingIndex(page.GetSiblingIndex() + 1);
                var map = new Dictionary<Object, Object>();
                MapHierarchy(page, copy, map);
                // The analysis pages slide and fade 1P's page; 2P's stays put on the right.
                var rect = (RectTransform)copy;
                rect.anchoredPosition += new Vector2(Board2PX, 0);
                var group = copy.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1;
                var board = copy.gameObject.AddComponent<ResultBoardView>();
                foreach (var field in typeof(ResultBoardView).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    field.SetValue(board, Remap(field.GetValue(view), map));
                Lay2PBoard(copy, board);
                view.secondBoard = board;
                copy.gameObject.SetActive(false);
                EditorUtility.SetDirty(view);
            }
            result.unfilled2P = new[] { "easy", "normal", "hard" }.Select(a => Sprite("result/gauge/2p_unfilled_" + a)).ToArray();
            result.messages2P = Enumerable.Range(4, 4).Select(i => Sprite("result/crown/message/" + i)).ToArray();
            result.highScoreVoice2P = RequiredClip("result/high_score_voice_2p");
            result.fullComboVoice2P = RequiredClip("result/full_combo_voice_2p");
            result.messageVoices2P = new[] { "max_fail_voice_2p", "fail_voice_2p", "clear_voice_2p", "max_clear_voice_2p" }
                .Select(name => RequiredClip("result/" + name)).ToArray();
            EditorUtility.SetDirty(result);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("OurTaiko: two-player result board saved.");
        }

        static void Lay2PBoard(Transform page, ResultBoardView board)
        {
            SetSprite(page.Find("Board"), "result/score/overlay/1");
            SetSprite(page.Find("DonBack"), "result/bottom/chara_0/1");
            foreach (var image in new Graphic[] { board.unfilled, board.bar, board.clearTransition, board.clearTop, board.clearBottom,
                         board.overlay, board.clearCaption, board.soulFire, board.soul, board.soulSheen }.Concat(board.rainbow))
                if (image != null) image.rectTransform.anchoredPosition += new Vector2(Gauge2PX - Board2PX, 0);
            board.bar.sprite = Sprite("result/gauge/2p_bar");
            foreach (var graphic in new Graphic[] { board.message, board.messageText })
                graphic.rectTransform.anchoredPosition += new Vector2(Message2PX - Board2PX, 0);
            // footer_2p index 1 sits at x 960: 4 to the right of the moved page's origin.
            Object.DestroyImmediate(board.successTwoPlayers.gameObject);
            board.successTwoPlayers = SuccessHalf(board.success, "result/background/footer_2p/1", 960 - Board2PX);
            if (board.nameplate != null)
            {
                board.nameplate.seat = 1;
                board.nameplate.secondBadge = Sprite("global/nameplate/2p");
                board.nameplate.RectTransform.anchoredPosition += new Vector2(ResultNameplate2PX - Board2PX, 0);
                EditorUtility.SetDirty(board.nameplate);
            }
            EditorUtility.SetDirty(board);
        }

        // footer_2p (x, 165), drawn where the single-player clear backdrop is, hidden until used.
        static Image SuccessHalf(Image success, string art, float x)
        {
            var half = ResultPlace(success.name + "TwoPlayers", success.transform.parent, Sprite(art) ?? throw new FileNotFoundException(art), x, 165);
            half.transform.SetSiblingIndex(success.transform.GetSiblingIndex() + 1);
            half.Alpha(0);
            return half;
        }
    }
}
