using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using State = OurTaiko.SongSelectManager.State;
using ItemKind = OurTaiko.SongSelectManager.ItemKind;
using WheelItem = OurTaiko.SongSelectManager.WheelItem;

namespace OurTaiko
{
    // Song select: binding wheel items to saved or pooled board views and the course panel.
    public sealed partial class SongSelectScene
    {
        // ---------------------------------------------------------------- saved view binding

        // Every wheel item gets a board; the authored boards draw the first local songs (Manager.Songs)
        // wherever they are on the wheel, and other songs borrow pooled prefabs on screen.
        void BindBoards()
        {
            var saved = view.songBoards ?? Array.Empty<SongBoardView>();
            savedSlots = saved.Select(item => NewSlot(item, true)).ToArray();
            foreach (var item in Manager.Items) visuals[item] = new Board { Item = item };
            for (int i = 0; i < saved.Length; i++) saved[i].gameObject.SetActive(false);
        }

        Slot[] savedSlots = Array.Empty<Slot>();

        // Later songs draw over earlier ones, as when every song had its own board: the bound views
        // trade their sibling slots so they follow song order (other wheel children keep theirs).
        void RestackBoards()
        {
            restackBoards = false;
            var bound = Manager.Items.Select(BoardOf).Where(b => b.Slot != null || b.FolderSlot != null).ToList();
            var indices = bound.Select(b => b.Root.GetSiblingIndex()).OrderBy(i => i).ToList();
            for (int i = 0; i < bound.Count; i++) bound[i].Root.SetSiblingIndex(indices[i]);
        }

        static Slot NewSlot(SongBoardView item, bool saved) => new Slot
        {
            View = item, Saved = saved,
            PanelSize = item.panel.rectTransform.sizeDelta, GlowSize = item.glow.rectTransform.sizeDelta,
            TitlePosition = item.title.rectTransform.anchoredPosition,
            CrownPosition = item.crown.rectTransform.anchoredPosition, CrownSize = item.crown.rectTransform.sizeDelta,
            RankPosition = item.scoreRank != null ? ((RectTransform)item.scoreRank.transform).anchoredPosition : Vector2.zero,
            RootOffset = saved ? item.Root.anchoredPosition - item.authoredWheelPosition : Vector2.zero,
            PlateBase = item.plates.Select(plate => ((RectTransform)plate.group.transform).anchoredPosition).ToArray(),
        };

        void AcquireView(Board board)
        {
            if (board.Kind != ItemKind.Song)
            {
                if (board.FolderSlot != null) return;
                FolderSlot folderSlot;
                if (folderPool.Count > 0) folderSlot = folderPool.Pop();
                else
                {
                    var item = Instantiate(view.folderPrefab, wheel);
                    folderSlot = new FolderSlot
                    {
                        View = item, PanelSize = item.panelClosed.rectTransform.sizeDelta, GlowSize = item.glow.rectTransform.sizeDelta,
                        TitlePosition = item.title.rectTransform.anchoredPosition, CountPosition = item.count.rectTransform.anchoredPosition,
                    };
                }
                BindFolder(board, folderSlot);
                return;
            }
            if (board.Slot != null) return;
            int authored = Array.IndexOf(Manager.Songs, board.Song);
            var slot = authored >= 0 && authored < savedSlots.Length ? savedSlots[authored]
                : boardPool.Count > 0 ? boardPool.Pop() : NewSlot(Instantiate(view.boardPrefab, wheel), false);
            Bind(board, slot);
        }

        // Saved boards stay bound to their song; pooled ones go back for the next board on screen.
        void ReleaseView(Board board)
        {
            if (board.FolderSlot != null)
            {
                board.Root.gameObject.SetActive(false);
                folderPool.Push(board.FolderSlot);
                board.FolderSlot = null; board.Root = null; board.Group = null;
                return;
            }
            if (board.Slot == null) return;
            board.Root.gameObject.SetActive(false);
            if (board.Slot.Saved) return;
            boardPool.Push(board.Slot);
            board.Slot = null; board.View = null;
        }

        // A folder board shows its category and server; a もどる board only its label.
        void BindFolder(Board board, FolderSlot slot)
        {
            var item = slot.View;
            restackBoards = true;
            board.FolderSlot = slot; board.Root = item.Root; board.Group = item.group; board.RootOffset = Vector2.zero;
            item.click.Clicked = () => Manager.SelectItem(board.Item);
            bool back = board.Kind == ItemKind.Back, search = board.Kind == ItemKind.Search;
            var folder = board.Folder >= 0 ? Manager.Folders[board.Folder] : null;
            item.panelClosed.sprite = back ? backBoard : boards[board.Genre];
            item.panelClosed.Alpha(1);
            item.panelOpen.enabled = false;
            item.charaLeft.enabled = item.charaRight.enabled = false;
            item.title.text = back ? SongSelectManager.BackLabel : search ? Manager.SearchFolderTitle : folder.Title;
            item.title.Squeeze(860);
            item.count.text = back ? "" : search ? Manager.SearchDescription(board.Item) : $"{folder.SongCount} songs　{folder.ServerName}";
            if (!back)
            {
                item.panelOpen.sprite = folderBoards[board.Genre];
                int chara = Mathf.Clamp(board.Genre, 0, charaLeft.Length - 1);
                item.charaLeft.sprite = charaLeft[chara];
                item.charaRight.sprite = charaRight[chara];
            }
            item.count.Squeeze(860);
            item.count.enabled = false;
            item.coverMask.gameObject.SetActive(false);
        }

        void Bind(Board board, Slot slot)
        {
            var item = slot.View;
            restackBoards = true;
            board.Slot = slot; board.View = item; board.Root = item.Root;
            board.Group = item.group; board.Glow = item.glow; board.Panel = item.panel; board.Crown = item.crown;
            board.Title = item.title; board.Subtitle = item.subtitle; board.Contents = item.contents;
            board.PanelSize = slot.PanelSize; board.GlowSize = slot.GlowSize; board.TitlePosition = slot.TitlePosition;
            board.RankPosition = slot.RankPosition; board.CrownPosition = slot.CrownPosition; board.CrownSize = slot.CrownSize; board.RootOffset = slot.RootOffset;
            item.click.Clicked = () => Manager.SelectItem(board.Item);
            board.Panel.sprite = boards[board.Genre];
            board.Title.text = board.Info.Title;
            board.Title.Squeeze(860);
            board.Subtitle.text = board.Info.Subtitle;
            board.Subtitle.Squeeze(860, 0.75f);
            board.Plates.Clear();
            for (int i = 0; i < item.plates.Length; i++)
            {
                var plate = item.plates[i];
                var info = board.Info.Course(plate.difficulty);
                plate.group.gameObject.SetActive(info != null);
                if (info == null) continue;
                ((RectTransform)plate.group.transform).anchoredPosition = slot.PlateBase[i] + Vector2.right *
                    (PlatePosition(board.Info, plate.difficulty, item.platePitch) - plate.authoredContentX);
                plate.level.sprite = levels[(int)plate.difficulty * 11 + Mathf.Clamp(info.Level, 1, 11) - 1];
                plate.branch.enabled = info.IsBranching;
                board.Plates.Add(new Plate { Difficulty = plate.difficulty, Group = plate.group });
            }
        }

        static float PlatePosition(SongInfo info, Difficulty difficulty, float pitch)
        {
            var columns = new List<Difficulty>();
            for (var d = Difficulty.Easy; d <= Difficulty.Hard; d++)
                if (info.Has(d)) columns.Add(d);
            if (info.Has(Difficulty.Oni) || info.Has(Difficulty.Ura)) columns.Add(Difficulty.Oni);
            int index = columns.IndexOf(difficulty == Difficulty.Ura ? Difficulty.Oni : difficulty);
            return (index - (columns.Count - 1) / 2f) * pitch;
        }

        void BindCoursePanel()
        {
            mark = view.mark; backboard = view.backboard; back = view.back; option = view.option;
            auto = view.auto; frame = view.frame; glow = view.glow; balloon = view.balloon; uraChange = view.uraChange;
            header = view.header; headerSub = view.headerSub;
            framePosition = frame.rectTransform.anchoredPosition;
            glowPosition = glow.rectTransform.anchoredPosition;
            balloonPosition = balloon.rectTransform.anchoredPosition;
            if (view.frame2P != null) frame2PPosition = view.frame2P.rectTransform.anchoredPosition;
            if (view.balloon2P != null) balloon2PPosition = view.balloon2P.rectTransform.anchoredPosition;
            if (view.glow2P != null) glow2PPosition = view.glow2P.rectTransform.anchoredPosition;
            AddClick(back, Difficulty.Back);
            AddClick(option, Difficulty.Modifier);
            for (int i = 0; i < cards.Length; i++)
            {
                var saved = view.cards[i];
                cards[i] = new CourseCard { Rank = saved.scoreRank, Board = saved.board, Crown = saved.crown, Star = saved.star,
                    Level = saved.level, Bar = saved.bar, Branch = saved.branch, Dots = saved.dots, Name = saved.name };
                AddClick(saved.board, (Difficulty)i);
            }
            optionPanel = new OptionPanel(view.options, optionArt);
            optionPanel.RowTapped += Manager.TapOptionRow;
            optionPanel.OutsideTapped += Manager.CloseOptions;
            // 2P's panel follows 2P's drum only; touch belongs to 1P.
            if (view.options2P != null) optionPanel2P = new OptionPanel(view.options2P, optionArt);
        }

        void DrawOverlays(double now)
        {
            if (TimerView == null) return;
            TimerView.Show(Manager.Phase == State.Browsing ? ListTimerSeconds : CourseTimerSeconds);
            // coin_overlay: the invite shows while a 2P join would still be allowed (songs played < 2).
            Coins.ShowInvite(switcher.SongsPlayed < 2 && !Manager.TwoPlayers, now);
        }

        void AddClick(Image image, Difficulty difficulty)
        {
            image.raycastTarget = true;
            var pointer = image.GetComponent<PointerRelay>();
            if (difficulty == Difficulty.Oni)
            {
                pointer.CanLongPress = Manager.CanLongPressOni;
                pointer.LongPressed = Manager.LongPressOni;
            }
            pointer.Clicked = () => Manager.SelectCourse(difficulty);
        }
    }
}
