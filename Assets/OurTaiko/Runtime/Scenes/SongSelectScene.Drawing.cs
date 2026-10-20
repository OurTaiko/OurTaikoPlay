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
    // Song select: drawing the wheel, its boards, the course panel and the overlays each frame.
    public sealed partial class SongSelectScene
    {
        // ---------------------------------------------------------------- drawing

        void DrawBackground(double now)
        {
            // navigator background: 2880-px genre strip, 1920 px per 15 s; the new genre fades in over 200 ms.
            float move = (float)(now % BackgroundLoopMs / BackgroundLoopMs * 1920);
            float change = (float)Clamp01((now - genreChangedAt) / GenreFadeMs);
            for (int i = 0; i < 2; i++)
            {
                backgroundTiles[i].sprite = genreBackgrounds[previousGenre];
                backgroundTiles[i + 2].sprite = genreBackgrounds[currentGenre];
                backgroundTiles[i + 2].Alpha(change);
                backgroundTiles[i].rectTransform.anchoredPosition = backgroundPositions[i] + Vector2.left * move;
                backgroundTiles[i + 2].rectTransform.anchoredPosition = backgroundPositions[i + 2] + Vector2.left * move;
            }
        }

        void DrawBoard(Board board, bool focused, double now)
        {
            if (board.MoveStart >= 0)
            {
                double p = SkinUi.CubicOut((now - board.MoveStart) / board.MoveDuration);
                board.Position = board.MoveFrom + (board.MoveTo - board.MoveFrom) * p;
                board.Cross = board.CrossFrom + (board.CrossTo - board.CrossFrom) * p;
                if (now - board.MoveStart >= board.MoveDuration) board.MoveStart = -1;
            }
            if (board.Kind != ItemKind.Song) { DrawFolderBoard(board, focused, now); return; }
            bool hidden = focused && Manager.Phase != State.Browsing;
            if (hidden || board.Position <= -400 || board.Position >= 1480) { ReleaseView(board); return; }
            AcquireView(board);
            board.Root.gameObject.SetActive(true);
            board.Group.alpha = (float)BoardFade(board, now);
            board.Root.anchoredPosition = new Vector2((float)board.Cross, -(float)board.Position) + board.RootOffset;

            // select_on / select_off of the song board clip (anim/song_board): board centre sy and
            // the song_kanban_info alpha, after the 508 ms pre-growth hold.
            double frame = -1;
            if (focused && board.OpenStart >= 0)
            {
                double t = now - board.OpenStart - board.Hold;
                if (t >= 0) frame = songBoard.Label("select_on").GetValueOrDefault(5) + Math.Min(t, OpenGrowMs) * 0.06;
            }
            else if (board.CloseStart >= 0)
            {
                double e = now - board.CloseStart;
                if (e < CloseMs) frame = songBoard.Label("select_off").GetValueOrDefault(30) + e * 0.06;
                else board.CloseStart = -1;
            }
            double pb = 0, ia = 0;
            if (frame >= 0)
            {
                double sy = songBoard.Get("instance_board_center", frame, "sy", 0.2168);
                pb = Clamp01((sy - 0.2168) / (1 - 0.2168));
                ia = Clamp01(songBoard.Get("song_kanban_info", frame, "a", pb >= 1 ? 1 : 0));
            }
            var expansion = Vector2.up * (float)(board.View.expansionHeight * pb);
            board.Panel.rectTransform.sizeDelta = board.PanelSize + expansion;
            board.Glow.rectTransform.sizeDelta = board.GlowSize + expansion;
            double pulse = glowClip.Get("#12@0", now * 0.06 % Math.Max(1, glowClip.Last - glowClip.First + 1), "a", 1);
            board.Glow.Alpha((float)(pb * pulse));
            board.Title.rectTransform.anchoredPosition = board.TitlePosition + board.View.titleOpenOffset * (float)pb;
            board.Contents.alpha = (float)ia;
            board.Subtitle.Alpha((float)ia);
            DrawPlates(board, now);
            DrawCrown(board, (float)pb, (float)(pb > 0 ? ia : 1));
            DrawScoreRank(board, (float)pb, (float)(pb > 0 ? ia : 1));
        }

        // draw_folder_board / draw_back_board: the genre folder grows into folder_graphic with its two
        // characters and song count (anim/folder_board, labels select_on 5 / select_off 30); もどる
        // only grows and glows.
        void DrawFolderBoard(Board board, bool focused, double now)
        {
            if (board.Position <= -400 || board.Position >= 1480) { ReleaseView(board); return; }
            AcquireView(board);
            var slot = board.FolderSlot;
            var item = slot.View;
            board.Root.gameObject.SetActive(true);
            board.Group.alpha = (float)BoardFade(board, now);
            board.Root.anchoredPosition = new Vector2((float)board.Cross, -(float)board.Position) + board.RootOffset;
            double frame = -1;
            if (focused && board.OpenStart >= 0)
            {
                double t = now - board.OpenStart - board.Hold;
                if (t >= 0) frame = folderClip.Label("select_on").GetValueOrDefault(5) + Math.Min(t, OpenGrowMs) * 0.06;
            }
            else if (board.CloseStart >= 0)
            {
                double e = now - board.CloseStart;
                if (e < FolderCloseMs) frame = folderClip.Label("select_off").GetValueOrDefault(30) + e * 0.06;
                else board.CloseStart = -1;
            }
            double pb = 0, chAlpha = 0, chOffset = 340, introAlpha = 0;
            if (frame >= 0)
            {
                pb = Clamp01((folderClip.Get("instance_board_center", frame, "sy", 0.2168) - 0.2168) / (1 - 0.2168));
                chAlpha = Clamp01(folderClip.Get("instance_character_l", frame, "a", 0));
                chOffset = -folderClip.Get("instance_character_l", frame, "tx", -340);
                introAlpha = Clamp01(folderClip.Get("text_kanban_intro_genre", frame, "a", 0));
            }
            var expansion = Vector2.up * (float)(item.expansionHeight * pb);
            item.panelOpen.rectTransform.sizeDelta = item.panelClosed.rectTransform.sizeDelta = slot.PanelSize + expansion;
            item.glow.rectTransform.sizeDelta = slot.GlowSize + expansion;
            double pulse = glowClip.Get("#12@0", now * 0.06 % Math.Max(1, glowClip.Last - glowClip.First + 1), "a", 1);
            item.glow.Alpha((float)(pb * pulse));
            if (board.Kind == ItemKind.Back)
            {
                item.title.rectTransform.anchoredPosition = slot.TitlePosition;
                return;
            }
            // Closed: bar_genre; growing: folder_graphic with the closed look fading out over it.
            item.panelOpen.Alpha(pb > 0 ? 1 : 0);
            item.panelClosed.Alpha((float)(1 - pb));
            float slide = (float)(450 - chOffset);
            item.charaLeft.Alpha((float)chAlpha);
            item.charaRight.Alpha((float)chAlpha);
            item.charaLeft.rectTransform.anchoredPosition = new Vector2(-480 + slide, item.charaLeft.rectTransform.anchoredPosition.y);
            item.charaRight.rectTransform.anchoredPosition = new Vector2(480 - slide, item.charaRight.rectTransform.anchoredPosition.y);
            item.title.rectTransform.anchoredPosition = slot.TitlePosition + item.titleOpenOffset * (float)pb;
            item.count.rectTransform.anchoredPosition = slot.CountPosition + item.countOpenOffset * (float)pb;
            item.count.Alpha((float)introAlpha);
            // The picture may finish loading while the board is on screen.
            DrawFolderCover(item, pb > 0 && board.Folder >= 0 ? Manager.Folders[board.Folder].Cover : null, slot.PanelSize + expansion);
        }

        // A playlist's picture fills the open board under the fading closed look, cropped to cover
        // it whatever its proportions; null hides it.
        static void DrawFolderCover(FolderBoardView item, Sprite picture, Vector2 boardSize)
        {
            item.coverMask.gameObject.SetActive(picture != null);
            if (picture == null) return;
            var area = boardSize - item.coverInset;
            float aspect = picture.rect.width / picture.rect.height;
            item.coverMask.rectTransform.sizeDelta = area;
            item.cover.sprite = picture;
            item.cover.rectTransform.sizeDelta = area.x / area.y > aspect ? new Vector2(area.x, area.x / aspect) : new Vector2(area.y * aspect, area.y);
        }

        void DrawPlates(Board board, double now)
        {
            if (board.Contents.alpha <= 0) return;
            // song_kanban_info 'ura': a 180-frame loop cross-fading the oni and ura chips.
            double f = uraLoop.First + now * 0.06 % Math.Max(1, uraLoop.Last - uraLoop.First + 1);
            float ura = (float)uraLoop.Get("ura/text_course_ura", f, "a", 0);
            float oni = (float)uraLoop.Get("oni/text_course_oni", f, "a", 1);
            bool both = board.Info.Has(Difficulty.Oni) && board.Info.Has(Difficulty.Ura);
            foreach (var plate in board.Plates)
                plate.Group.alpha = !both ? 1 : plate.Difficulty == Difficulty.Ura ? ura : plate.Difficulty == Difficulty.Oni ? oni : 1;
        }

        // SetCrown / SearchAnyCrown: the highest crowned course; 72 px at (-425,-114) open,
        // 0.75 scale at (-425,-27) closed.
        void DrawCrown(Board board, float p, float fade)
        {
            Crown best = Crown.None; Difficulty course = Difficulty.Easy;
            foreach (var info in board.Info.Courses)
            {
                var record = SongScores.Get(board.Song, info.Difficulty);
                if (record != null && record.crown != Crown.None) { best = record.crown; course = info.Difficulty; }
            }
            if (best == Crown.None) { board.Crown.enabled = false; return; }
            var set = best == Crown.DonderfulCombo ? crownDonderful : best == Crown.FullCombo ? crownFullCombo : crownClear;
            board.Crown.sprite = set[(int)course];
            board.Crown.rectTransform.sizeDelta = board.CrownSize * (0.75f + 0.25f * p);
            board.Crown.rectTransform.anchoredPosition = board.CrownPosition + board.View.crownOpenOffset * p;
            board.Crown.Alpha(fade);
        }

        void DrawCoursePanel(double now)
        {
            float fade = (float)CourseFade;
            var group = coursePanel.GetComponent<CanvasGroup>();
            group.alpha = fade;
            var board = FocusedBoard;
            var cursor = Manager.Cursor;
            var selected = cursor.Selected;
            int column = selected >= Difficulty.Easy ? Math.Min((int)Difficulty.Oni, (int)selected) : -1;
            mark.enabled = column >= 0;
            if (column >= 0) mark.sprite = courseMarks[(int)selected];

            double ura = uraChangedAt < 0 ? -1 : now - uraChangedAt;
            if (ura >= UraChangeMs) { uraChangedAt = -1; ura = -1; }
            bool oniShowsUra = cursor.IsUra;
            if (ura >= 0 && ura < UraSwapMs) oniShowsUra = !oniShowsUra;
            for (int i = 0; i < 4; i++)
                FillCard(cards[i], board, i == 3 && oniShowsUra ? Difficulty.Ura : (Difficulty)i, i == 3 && ura >= 0);
            uraChange.enabled = ura >= 0;
            if (ura >= 0)
            {
                var cells = uraChangeToUraSide ? UraFrames(ref uraToUraCells, uraChangeToUra) : UraFrames(ref uraToOniCells, uraChangeToOni);
                uraChange.sprite = cells[Math.Min(UraCells - 1, (int)(ura / UraChangeMs * UraCells))];
            }
            auto.enabled = Manager.AutoPlay;

            // draw_selector: the course frame / button glow under the boards, the 1P bubble above.
            float x = column >= 0 ? cards[column].Board.rectTransform.anchoredPosition.x
                : (selected == Difficulty.Modifier ? option : back).rectTransform.anchoredPosition.x;
            bool hideForUra = column == 3 && ura >= 0;
            frame.enabled = column >= 0 && !hideForUra;
            glow.enabled = column < 0;
            balloon.enabled = !hideForUra;
            float cardX = cards[0].Board.rectTransform.anchoredPosition.x;
            frame.rectTransform.anchoredPosition = framePosition + Vector2.right * (x - cardX);
            glow.rectTransform.anchoredPosition = glowPosition + Vector2.right * (x - back.rectTransform.anchoredPosition.x);
            var second = Manager.TwoPlayers ? Manager.CursorOf(1) : null;
            // selector.lua: while both bubbles sit on the same slot they part by 40 px each way.
            float part = second != null && CourseSlot(second.Selected) == CourseSlot(selected) ? 40 : 0;
            balloon.rectTransform.anchoredPosition = balloonPosition + Vector2.right * (x - cardX - part);
            DrawSecondCursor(second, cardX, ura, part);
        }

        // Oni and Ura share a column; Back and Modifier are their buttons.
        static int CourseSlot(Difficulty difficulty) => difficulty >= Difficulty.Easy ? Math.Min((int)Difficulty.Oni, (int)difficulty) : (int)difficulty;

        // 2P's cursor by 1P's rules: difficulty_select_bar2 on a card, the button glow on もどる /
        // オプション, and 2p_balloon above.
        void DrawSecondCursor(DifficultyCursor cursor, float cardX, double ura, float part)
        {
            var frame2 = view.frame2P; var balloon2 = view.balloon2P; var glow2 = view.glow2P;
            if (frame2 == null || balloon2 == null) return;
            if (cursor == null) { frame2.enabled = balloon2.enabled = false; if (glow2 != null) glow2.enabled = false; return; }
            var selected = cursor.Selected;
            int column = selected >= Difficulty.Easy ? Math.Min((int)Difficulty.Oni, (int)selected) : -1;
            float x = column >= 0 ? cards[column].Board.rectTransform.anchoredPosition.x
                : (selected == Difficulty.Modifier ? option : back).rectTransform.anchoredPosition.x;
            bool hideForUra = column == 3 && ura >= 0;
            // A seat that has decided keeps its cursor on the course, half faded like the original's fade on ready.
            // Alpha() also switches a graphic on, so the fade comes first and what is shown after.
            float fade = Manager.IsReady(1) || Manager.IsOptionPanelOpenFor(1) ? 0.5f : 1;
            frame2.Alpha(fade); balloon2.Alpha(fade);
            if (glow2 != null) glow2.Alpha(fade);
            frame2.enabled = column >= 0 && !hideForUra;
            balloon2.enabled = !hideForUra;
            if (glow2 != null)
            {
                glow2.enabled = column < 0;
                glow2.rectTransform.anchoredPosition = glow2PPosition + Vector2.right * (x - back.rectTransform.anchoredPosition.x);
            }
            frame2.rectTransform.anchoredPosition = frame2PPosition + Vector2.right * (x - cardX);
            balloon2.rectTransform.anchoredPosition = balloon2PPosition + Vector2.right * (x - cardX + part);
        }

        Sprite[] UraFrames(ref Sprite[] cells, Texture2D sheet)
        {
            if (cells != null) return cells;
            cells = new Sprite[UraCells];
            for (int i = 0; i < UraCells; i++)
            {
                var rect = new Rect(i % 10 * 340, sheet.height - (i / 10 + 1) * 400, 340, 400);
                cells[i] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), 100);
            }
            return cells;
        }

        void FillCoursePanel(Board board)
        {
            backboard.sprite = backboards[board.Genre];
            header.text = board.Info.Title;
            header.Squeeze(1000);
            headerSub.text = board.Info.Subtitle;
            headerSub.Squeeze(1000);
        }

        void DrawScoreRank(Board board, float p, float fade)
        {
            var view = board.View.scoreRank;
            if (view == null) return;
            ScoreRank rank = ScoreRank.None;
            Difficulty course = Difficulty.Easy;
            // SearchAnyCrown walks upward in difficulty; rank and crown are independent.
            foreach (var info in board.Info.Courses)
            {
                ScoreRank candidate = SongScores.Rank(board.Song, info.Difficulty);
                if (candidate > 0 && (rank == 0 || info.Difficulty > course)) { rank = candidate; course = info.Difficulty; }
            }
            // While selecting a course, prefer its record if it has an earned rank.
            if (board.Song == Manager.FocusedSong && Manager.Phase != State.Browsing && Manager.Cursor != null)
            {
                var selected = Manager.Cursor.Selected;
                if ((int)selected >= 0 && (int)selected <= 4 && board.Info.Has(selected))
                {
                    ScoreRank candidate = SongScores.Rank(board.Song, selected);
                    if (candidate > 0) { rank = candidate; course = selected; }
                }
            }
            view.Show(rank, course);
            view.group.alpha = fade;
            view.transform.localScale = Vector3.one * (0.75f + 0.25f * p);
            ((RectTransform)view.transform).anchoredPosition = board.RankPosition + board.View.rankOpenOffset * p;
        }

        void FillCard(CourseCard card, Board board, Difficulty difficulty, bool changing)
        {
            var info = board.Info.Course(difficulty);
            bool has = info != null;
            card.Board.sprite = courseBoards[(int)difficulty];
            card.Board.color = new Color(1, 1, 1, has ? 1 : 0.4f);
            bool details = has && !changing;
            card.Name.enabled = has;
            card.Name.text = ChipNames[(int)difficulty];
            foreach (var image in new[] { card.Crown, card.Star, card.Level, card.Bar }) image.enabled = details;
            card.Branch.enabled = details && info.IsBranching;
            for (int k = 0; k < card.Dots.Length; k++) card.Dots[k].enabled = details && k < Math.Min(10, info.Level);
            if (!details) { card.Rank?.Show(0); return; }
            var record = SongScores.Get(board.Song, difficulty);
            card.Rank?.Show(SongScores.Rank(board.Song, difficulty), difficulty);
            card.Crown.sprite = smallCrowns[(int)(record?.crown ?? Crown.None)];
            card.Level.sprite = smallStars[Mathf.Clamp(info.Level, 1, 11)];
        }
    }
}
