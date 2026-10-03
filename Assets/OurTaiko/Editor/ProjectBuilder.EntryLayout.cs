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
        // box_manager.cpp's board order with the boards this port has; texts are Nijiiro's
        // skin_config entry_* (ja). mode_select/box frames: 0 / 1 = 演奏ゲーム open / closed,
        // 9 / 10 = ゲーム設定 (box.lua MODES.settings, the baked `aprilfool` board).
        static readonly (string Title, string[] Info, string On, string Off, string Scene)[] EntryModes =
        {
            ("演奏ゲーム", new[] { "すきな曲や、むずかしさを", "えらんであそべるよ！" }, "entry/mode_select/box/0", "entry/mode_select/box/1", SceneSwitcher.SongSelectScene),
            ("ゲーム設定", new[] { "ゲームのせっていを", "かえられるよ！" }, "entry/mode_select/box/9", "entry/mode_select/box/10", SceneSwitcher.SettingScene),
        };

        // Saves the Entry screen once as an editable hierarchy under Stage, at the skin's
        // positions. An existing layout is never rebuilt.
        static void BuildEntryLayout(EntryScene entry)
        {
            var stage = entry.stage;
            if (stage.childCount != 0)
                throw new InvalidOperationException("Entry's Stage has children but no EntryView binding. Bind it instead; it will not be overwritten.");
            var view = stage.gameObject.AddComponent<EntryView>();
            entry.view = view;
            BuildEntryBackground(view, stage);
            BuildEntryBoards(view, stage);
            BuildEntryCredit(view, stage);

            var guideFirst = AssetDatabase.LoadAllAssetsAtPath(Root + "Art/" + GuideSheet + ".png").OfType<Sprite>()
                .First(sprite => sprite.name == $"ControlGuide{GuideFirstDecide:000}");
            view.controlGuide = SkinUi.Image("ControlGuide", stage, guideFirst);
            view.controlGuide.rectTransform.TopLeft(0, 12);
            ClipSampler.Attach(view.controlGuide.gameObject, entry.overlay.guideClip);

            // nameplate_entry_left
            var plate = AssetDatabase.LoadAssetAtPath<NameplateView>(NameplatePrefabPath);
            view.nameplate = ((GameObject)PrefabUtility.InstantiatePrefab(plate.gameObject, stage)).GetComponent<NameplateView>();
            view.nameplate.name = "Nameplate";
            view.nameplate.Place(14, 910);
            view.nameplateGroup = view.nameplate.gameObject.AddComponent<CanvasGroup>();
            PrefabUtility.RecordPrefabInstancePropertyModifications(view.nameplate.transform);

            // Timer:draw, the 60 placeholder
            var timer = SkinUi.Rect("Timer", stage);
            SkinUi.Image("Background", timer, entry.overlay.timerBackground).rectTransform.TopLeft(1669, 12);
            view.timerDigits = CreatePlaceholderDigits(timer, entry.overlay, EntryScene.TimerSeconds.ToString(), true);

            // EntryOverlay:draw offline: 段位道場 NG, 「1プレイ 4曲」 and IC Card NG chips
            var chips = SkinUi.Rect("StatusChips", stage);
            SkinUi.Image("Stage", chips, entry.overlay.stageChip).rectTransform.TopLeft(1200, 45);
            SkinUi.Image("Card", chips, entry.overlay.cardChip).rectTransform.TopLeft(1460, 38);
            SkinUi.Image("Dan", chips, entry.overlay.danChip).rectTransform.TopLeft(1040, 38);

            // CoinOverlay:draw — Entry shows all three parts
            var coins = SkinUi.Rect("CoinOverlay", stage);
            view.freePlay = SkinUi.Text("FreePlay", coins, 40);
            view.freePlay.characterSpacing = 2 * 100f / 40;
            view.freePlay.text = "フリープレイ";
            view.freePlay.rectTransform.Center(960, 1046);
            view.qrChip = SkinUi.Image("QrChip", coins, entry.overlay.qrChip);
            view.qrChip.rectTransform.TopLeft(1570, 38);
            view.inviteBubble = SkinUi.Image("InviteBubble", coins, entry.overlay.inviteBubble);
            view.inviteBubble.rectTransform.TopLeft(1492, 638);
            view.invitePlayer = CreateInviteText(coins, "InvitePlayer", "2人プレイ", 749);
            view.inviteMessage = CreateInviteText(coins, "InviteMessage", "太鼓をたたいてスタート!", 802);

            PersistSongSelectTextMaterials(stage);
            PreviewEntryLayout(entry, 0, false);
            EditorUtility.SetDirty(entry);
            EditorUtility.SetDirty(view);
        }

        // Entry:draw_background: the street, four twinkles, two lantern glows, the street-light flash.
        static void BuildEntryBackground(EntryView view, Transform stage)
        {
            var root = SkinUi.Rect("Background", stage);
            view.street = SkinUi.Image("Street", root, Required("entry/background/bg"), 1920, 1080);
            view.street.rectTransform.TopLeft(0, 0);
            // twinkle texture frame and position per entry_bg twinkle
            var twinkles = new[] { (0, 1186f, -142f), (0, 576f, 16f), (1, 206f, -108f), (1, 928f, 104f) };
            view.twinkles = new Image[twinkles.Length];
            for (int i = 0; i < twinkles.Length; i++)
            {
                view.twinkles[i] = SkinUi.Image("Twinkle" + i, root, Required("entry/background/twinkle/" + twinkles[i].Item1));
                view.twinkles[i].rectTransform.TopLeft(twinkles[i].Item2, twinkles[i].Item3);
            }
            var glows = new[] { new Vector2(0, 186), new Vector2(1331, 234) };
            view.glows = new Image[glows.Length];
            for (int i = 0; i < glows.Length; i++)
            {
                view.glows[i] = SkinUi.Image("Glow" + i, root, Required("entry/background/glow/" + i));
                view.glows[i].rectTransform.TopLeft(glows[i].x, glows[i].y);
            }
            view.streetLit = SkinUi.Image("StreetLit", root, Required("entry/background/street_lit"), 1920, 1080);
            view.streetLit.rectTransform.TopLeft(0, 0);
        }

        // The full-stage touch area under the boards, then the boards at their first layout
        // (first board open at the centre, the others closed in their mode_list slots).
        static void BuildEntryBoards(EntryView view, Transform stage)
        {
            var touch = SkinUi.Image("TouchArea", stage, null, 1920, 1080);
            touch.rectTransform.TopLeft(0, 0);
            touch.color = Color.clear;
            touch.raycastTarget = true;
            view.touchArea = touch;
            view.touchRelay = touch.gameObject.AddComponent<PointerRelay>();
            view.touchSwipe = touch.gameObject.AddComponent<SwipeRelay>();
            view.touchSwipe.step = 200;

            view.modeBoards = SkinUi.Rect("ModeBoards", stage);
            view.boardSwipe = view.modeBoards.gameObject.AddComponent<SwipeRelay>();
            view.boardSwipe.step = 200;
            var list = LumenClip.Parse(RequiredTimeline("mode_list").text);
            var flash = Required("entry/mode_select/box/8");
            var cursor = Required("entry/mode_select/box_highlight_center");
            view.boards = new EntryView.BoardView[EntryModes.Length];
            for (int i = 0; i < EntryModes.Length; i++)
            {
                var mode = EntryModes[i];
                var board = view.boards[i] = new EntryView.BoardView { scene = mode.Scene };
                board.root = SkinUi.Rect(mode.Title, view.modeBoards);
                var slot = EntryModeList.Slot(list, i);
                board.root.anchoredPosition = new Vector2(slot.x, -slot.y);
                // the cursor glow sits under the board (mode_select.nulm depth order)
                board.cursor = EntryPlate(board.root, "Cursor", cursor);
                board.closed = EntryPlate(board.root, "Closed", Required(mode.Off));
                board.open = EntryPlate(board.root, "Open", Required(mode.On));
                board.info = new TextMeshProUGUI[mode.Info.Length];
                for (int line = 0; line < board.info.Length; line++)
                {
                    // text_info: 34, white
                    board.info[line] = SkinUi.Text("Info" + line, board.root, 34);
                    board.info[line].characterSpacing = 100f / 34;
                    board.info[line].text = mode.Info[line];
                    board.info[line].rectTransform.Center(960, 535 + 59.5f + (line - (board.info.Length - 1) / 2f) * 53);
                }
                board.title = SkinUi.Text("Title", board.root, 72);
                board.title.characterSpacing = 2 * 100f / 72;
                board.title.text = mode.Title;
                board.titleOpen = SkinUi.Rect("TitleOpen", board.root);
                board.titleOpen.Center(960, 535 - 97);
                board.titleClosed = SkinUi.Rect("TitleClosed", board.root);
                board.titleClosed.Center(960, 535 + 4);
                board.flash = EntryPlate(board.root, "Flash", flash);
                // the visible plate inside the 1160x460 frames (box.lua: "off" 964x157, "on" 1050x436)
                board.hit = SkinUi.Image("Hit", board.root, null, board.closedHitSize.x, board.closedHitSize.y);
                board.hit.color = Color.clear;
                board.hit.raycastTarget = true;
                board.hit.rectTransform.Center(960, 535);
                board.hitRelay = board.hit.gameObject.AddComponent<PointerRelay>();
            }
        }

        static Image EntryPlate(Transform board, string name, Sprite sprite)
        {
            var image = SkinUi.Image(name, board, sprite);
            image.rectTransform.TopLeft(380, 305);
            return image;
        }

        // Entry:draw_credit: the 「１人プレイ」/「２人プレイ」 rows with 「太鼓をたたいてスタート！」.
        static void BuildEntryCredit(EntryView view, Transform stage)
        {
            view.credit = SkinUi.Rect("Credit", stage);
            var pill = Required("entry/side_select/credit_pill");
            var flash = Required("entry/side_select/credit_flash");
            string[] names = { "１人プレイ", "２人プレイ" };
            view.creditPills = new Image[2]; view.creditFlashes = new Image[2];
            view.creditLabels = new TextMeshProUGUI[2]; view.creditMessages = new TextMeshProUGUI[2]; view.creditHighlights = new TextMeshProUGUI[2];
            for (int i = 0; i < 2; i++)
            {
                float y = 432 + 72 + i * 176;
                view.creditPills[i] = SkinUi.Image("Pill" + (i + 1), view.credit, pill);
                view.creditPills[i].rectTransform.TopLeft(380, 404 + i * 176);
                // entry_credit_Np: black fill
                var label = view.creditLabels[i] = CreditText(view.credit, "Label" + (i + 1), Color.black, names[i]);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.rectTransform.pivot = new Vector2(0, 0.5f);
                label.rectTransform.anchoredPosition = new Vector2(960 - 443, -y);
                // entry_credit_start: white fill; a highlighted copy blends over it
                view.creditMessages[i] = CreditText(view.credit, "Message" + (i + 1), Color.white, "太鼓をたたいてスタート！");
                view.creditMessages[i].rectTransform.Center(960 + 190, y);
                view.creditHighlights[i] = CreditText(view.credit, "Highlight" + (i + 1), Color.white, "太鼓をたたいてスタート！");
                view.creditHighlights[i].rectTransform.Center(960 + 190, y);
            }
            for (int i = 0; i < 2; i++)
            {
                view.creditFlashes[i] = SkinUi.Image("Flash" + (i + 1), view.credit, flash);
                view.creditFlashes[i].rectTransform.TopLeft(380, 404 + i * 176);
            }
        }

        static TextMeshProUGUI CreditText(Transform parent, string name, Color fill, string value)
        {
            var text = SkinUi.Text(name, parent, 56);
            text.color = fill;
            text.UseUiFont();
            text.text = value;
            return text;
        }

        // Edit-mode preview of the saved Entry screen: 0 = the credit screen, 1 = the mode list
        // (first board open, nameplate and 2P invite shown). Only visibility and alpha change;
        // positions and sizes stay as authored. Play mode drives everything from code.
        public static void PreviewEntryLayout(EntryScene entry, int mode, bool recordUndo = true)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Layout preview is available only in Edit mode.");
            var view = entry.view;
            if (view == null) throw new InvalidOperationException("Save the Entry layout first.");
            if (recordUndo) Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "Preview Entry layout");
            bool credit = mode == 0;
            view.streetLit.Alpha(0);
            foreach (var image in view.twinkles.Concat(view.glows)) image.Alpha(1);
            view.credit.gameObject.SetActive(credit);
            for (int i = 0; i < view.creditPills.Length; i++)
            {
                view.creditPills[i].Alpha(1);
                view.creditLabels[i].Alpha(1);
                view.creditMessages[i].Alpha(1);
                view.creditHighlights[i].Alpha(0);
                view.creditFlashes[i].Alpha(0);
            }
            view.modeBoards.gameObject.SetActive(!credit);
            for (int i = 0; i < view.boards.Length; i++)
            {
                var board = view.boards[i];
                bool open = i == 0;
                board.root.gameObject.SetActive(i <= 1);
                board.cursor.Alpha(open ? 1 : 0);
                board.closed.Alpha(open ? 0 : 1);
                board.open.Alpha(open ? 1 : 0);
                board.flash.Alpha(0);
                foreach (var line in board.info) line.Alpha(open ? 1 : 0);
                board.title.Alpha(1);
                board.title.rectTransform.anchoredPosition = (open ? board.titleOpen : board.titleClosed).anchoredPosition;
                board.hit.rectTransform.sizeDelta = open ? board.openHitSize : board.closedHitSize;
            }
            view.controlGuide.Alpha(1);
            if (view.nameplateGroup != null) view.nameplateGroup.alpha = credit ? 0 : 1;
            view.freePlay.Alpha(1);
            view.qrChip.Alpha(1);
            view.inviteBubble.Alpha(credit ? 0 : 1);
            view.invitePlayer.Alpha(credit ? 0 : 1);
            view.inviteMessage.Alpha(credit ? 0 : 1);
            EditorSceneManager.MarkSceneDirty(entry.gameObject.scene);
            SceneView.RepaintAll();
        }
    }
}
