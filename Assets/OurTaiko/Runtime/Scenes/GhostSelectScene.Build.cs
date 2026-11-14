using System;
using System.Globalization;
using System.Linq;
using OurTaiko.Multi;
using OurTaiko.Online;
using UnityEngine;
using static OurTaiko.PlateUi;

namespace OurTaiko
{
    // ゴーストバトル's drawing: every view is laid out here on the 1920x1080 stage with PlateUi's
    // pieces, the same plates, panels and focus ring as マルチ演奏's room, over the mode's own night.
    public sealed partial class GhostSelectScene
    {
        void BuildBackdrop()
        {
            ui.Picture(stage, ghostArt.background, 1920, 1080, 960, 540);
            ui.Picture(stage, ghostArt.mark, 112, 112, 92, 76);
            ui.Label(stage, L10n.Get("Play", "ghostSelect.heading"), 58, 164, 74, 0, false, -1);
            ui.Label(stage, L10n.Get("Play", "ghostSelect.hint"), 28, 960, 1048);
        }

        void Rebuild()
        {
            dirty = false;
            Status = StatusNow();
            ui.Begin();
            string preferred;
            switch (Shown)
            {
                case View.Categories: preferred = BuildCategories(); break;
                case View.Plays: preferred = BuildPlays(); break;
                case View.Result: preferred = BuildResult(); break;
                default: preferred = BuildServers(); break;
            }
            ui.Settle(Shown + "/" + CurrentServer?.Id + "/" + CurrentCategory?.Id + "/" + CurrentCategory?.Page, preferred);
        }

        // The heading of a view, and the way back.
        void Heading(string text, string right = "")
        {
            var content = ui.Content;
            ui.Picture(content, art.panel, 1800, 100, 960, 196);
            ui.Picture(content, ghostArt.wisp, 50, 61, 112, 192);
            ui.Label(content, text, 42, 160, 192, 1100, false, -1);
            if (right.Length > 0) ui.Label(content, right, 34, 1810, 194, 500, false, 1);
        }

        void BackButton() => ui.TextButton("back", ui.Content, 300, 92, 210, 950, Slate, L10n.Get("Common", "back"), 38, Back, !IsStarting);

        void StatusLine(float y)
        {
            if (Status.Length == 0) return;
            ui.Picture(ui.Content, art.panel, 1300, 84, 960, y);
            ui.Label(ui.Content, Status, 32, 960, y - 2, 1240);
        }

        // ---------------------------------------------------------------- servers

        string BuildServers()
        {
            var content = ui.Content;
            Heading(L10n.Get("Play", "ghostSelect.pickServer"));
            int count = servers.Count;
            Color[] colors = { Orange, Teal, Violet };
            for (int i = 0; i < count; i++)
            {
                var server = servers[i];
                float x = 960 + (i - (count - 1) / 2f) * 580;
                var board = ui.Button("server:" + server.Id, content, 520, 340, x, 520, colors[i % colors.Length], () => OpenServer(server));
                ui.Picture(board, art.inset, 464, 110, 260, 262, new Color(1, 1, 1, 0.5f));
                ui.Picture(board, ghostArt.mark, 112, 112, 260, 76);
                ui.Label(board, server.Name, 42, 260, 164, 470, true);
                var all = server.Categories[0];
                string plays = all.IsLoading ? L10n.Get("Common", "loading") : all.Error != null ? L10n.Get("Play", "ghostSelect.offline") : Count(all);
                ui.Label(board, plays, 34, 260, 260, 430, true);
            }
            BackButton();
            StatusLine(800);
            return count > 0 ? "server:" + servers[0].Id : "back";
        }

        // ---------------------------------------------------------------- categories

        string BuildCategories()
        {
            var content = ui.Content;
            var server = CurrentServer;
            Heading(server.Name + "　" + L10n.Get("Play", "ghostSelect.pickCategory"));
            int count = server.Categories.Count, columns = count > 12 ? 5 : 4;
            float width = columns == 5 ? 330 : 400, pitch = width + 24;
            for (int i = 0; i < count; i++)
            {
                var category = server.Categories[i];
                float x = 960 + (i % columns - (columns - 1) / 2f) * pitch, y = 340 + i / columns * 146;
                // すべて stands apart from the catalog's categories.
                var plate = ui.Button("category:" + (category.Id ?? ""), content, width, 124, x, y, category.Id == null ? Orange : Paper, () => OpenCategory(category));
                string plays = Count(category);
                ui.Label(plate, category.Title, 36, width / 2, plays.Length > 0 ? 44 : 58, width - 40, true);
                if (plays.Length > 0) ui.Label(plate, plays, 24, width / 2, 90, width - 40, true).alpha = 0.75f;
            }
            BackButton();
            return "category:";
        }

        // ---------------------------------------------------------------- plays

        string BuildPlays()
        {
            var content = ui.Content;
            var category = CurrentCategory;
            string count = Count(category);
            Heading(CurrentServer.Name + "　›　" + category.Title, count);
            category.Page = Math.Max(0, Math.Min(category.Pages - 1, category.Page));
            int first = category.Page * PageRows;
            string preferred = "back";
            for (int i = 0; i < PageRows && first + i < category.Rows.Count; i++)
            {
                var row = category.Rows[first + i];
                PlayRow(row, 304 + i * 118);
                if (i == 0) preferred = "play:" + row.ScoreId;
            }
            BackButton();
            bool more = category.Page + 1 < category.Pages;
            ui.TextButton("prev", content, 300, 92, 760, 950, Slate, L10n.Get("Play", "ghostSelect.prev"), 34, () => TurnPage(-1), category.Page > 0 && !IsStarting);
            ui.Label(content, (category.Page + 1) + " / " + category.Pages, 36, 960, 948);
            ui.TextButton("next", content, 300, 92, 1160, 950, Slate, L10n.Get("Play", "ghostSelect.next"), 34, () => TurnPage(1), more && !IsStarting);
            StatusLine(category.Rows.Count == 0 ? 560 : 866);
            return preferred;
        }

        // One recorded play: who, what song, which course, the score and the judgments.
        void PlayRow(FanmadeReplay row, float y)
        {
            var plate = ui.Button("play:" + row.ScoreId, ui.Content, 1700, 110, 960, y, Paper, () => Pick(row), !IsStarting);
            // The plate's face ends a little above its rectangle (its shadow lies below).
            ui.Picture(plate, ghostArt.face, 80, 80, 64, 48);
            ui.Label(plate, MultiText.Name(row.Nickname), 32, 122, 30, 330, true, -1);
            if (row.SubmittedAt != default) ui.Label(plate, row.SubmittedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 22, 122, 68, 330, true, -1).alpha = 0.6f;
            ui.Label(plate, row.Title, 34, 480, row.Subtitle.Length > 0 ? 30 : 46, 560, true, -1);
            if (row.Subtitle.Length > 0) ui.Label(plate, row.Subtitle, 22, 480, 68, 560, true, -1).alpha = 0.6f;
            ui.CourseIcon(plate, row.Course, row.Level, 62, 1150, 47);
            ui.Label(plate, Number(row.Score), 38, 1660, 28, 300, true, 1);
            ui.Counts(plate, 1392, 68, 26, 12, (art.judgeGood, row.Good), (art.judgeOk, row.Ok), (art.judgeBad, row.Bad));
        }

        // ---------------------------------------------------------------- result

        string BuildResult()
        {
            var content = ui.Content;
            var outcome = LastOutcome;
            ui.Picture(content, null, 1920, 1080, 960, 540, new Color(0, 0, 0, 0.5f));
            ui.Picture(content, art.panel, 1700, 700, 960, 540);
            ui.Picture(content, ghostArt.versus, 192, 120, 330, 268);
            ui.Label(content, L10n.Get("Play", "ghostResult.heading"), 54, 960, 250, 700);
            ui.Label(content, outcome.Title, 34, 960, 314, 1100).color = Gold;
            bool playerFirst = outcome.PlayerWon || outcome.IsDraw;
            ResultRow(outcome, true, playerFirst ? 430 : 556, playerFirst || outcome.IsDraw ? 1 : 2);
            ResultRow(outcome, false, playerFirst ? 556 : 430, !playerFirst || outcome.IsDraw ? 1 : 2);
            ui.TextButton("ok", content, 320, 92, 960, 800, Orange, L10n.Get("Common", "ok"), 40, CloseResult);
            return "ok";
        }

        void ResultRow(Outcome outcome, bool player, float y, int rank)
        {
            var content = ui.Content;
            var result = player ? outcome.Player : outcome.Ghost;
            ui.Picture(content, art.inset, 1620, 106, 960, y, player ? new Color(1f, 0.9f, 0.45f, 0.32f) : new Color(1, 1, 1, 0.1f));
            ui.Picture(content, art.disc, 74, 74, 200, y, MultiHudView.RankColor(rank));
            ui.Label(content, rank.ToString(CultureInfo.InvariantCulture), 38, 200, y - 2, 0, true);
            if (player) ui.Avatar(content, 0, outcome.PlayerName, 74, 290, y);
            else ui.Picture(content, ghostArt.face, 74, 74, 290, y);
            ui.Label(content, MultiText.Name(player ? outcome.PlayerName : outcome.GhostName), 34, 344, y - 2, 300, false, -1);
            ui.CourseIcon(content, result.Course, result.Level, 72, 770, y);
            ui.Label(content, Number(result.Score), 46, 1120, y - 2, 230, false, 1);
            ui.Counts(content, 1146, y - 20, 36, 16, (art.judgeGood, result.Good), (art.judgeOk, result.Ok), (art.judgeBad, result.Bad));
            ui.Counts(content, 1146, y + 22, 30, 18, (art.labelMaxCombo, result.MaxCombo), (art.labelDrumroll, result.Rolls));
            var crown = result.ResultCrown == Crown.DonderfulCombo ? art.crownDonderful : result.ResultCrown == Crown.FullCombo ? art.crownFullCombo
                : result.ResultCrown == Crown.Clear ? art.crownClear : null;
            if (crown != null) ui.Picture(content, crown, 68, 60, 1530, y);
            bool won = !outcome.IsDraw && player == outcome.PlayerWon;
            if (won || outcome.IsDraw)
            {
                ui.Picture(content, art.ribbon, 172, 58, 1688, y, won ? new Color(1f, 0.83f, 0.2f) : Slate);
                ui.Label(content, L10n.Get("Play", won ? "ghostResult.win" : "ghostResult.draw"), 22, 1688, y - 6, 100, true);
            }
        }
    }
}
