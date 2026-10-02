using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Entry:draw_background (Nijiiro entry.lua): the street, four twinkles and two lantern glows
    // sampled from anim/entry_bg (a 360-frame loop), and the street-light flash over the first
    // 19 frames only. The other parent layers (tower, shops, people, lights) are not drawn.
    public sealed class EntryBackground
    {
        // {texture index (position), texture frame, track}
        static readonly (int Index, int Frame, string Track)[] Twinkles = {
            (0, 0, "#11@1/#5@0/#3@0"), (1, 0, "#11@1/#9@2/#3@0"),
            (2, 1, "#11@1/#8@1/#6@0"), (3, 1, "#11@1/#10@3/#6@0"),
        };
        static readonly Vector2[] TwinklePositions = { new Vector2(1186, -142), new Vector2(576, 16), new Vector2(206, -108), new Vector2(928, 104) };
        static readonly Vector2[] GlowPositions = { new Vector2(0, 186), new Vector2(1331, 234) };
        const double StreetLitFrames = 19;

        readonly LumenClip clip;
        readonly double loopFrames;
        readonly Image[] twinkles = new Image[4], glows = new Image[2];
        readonly Image streetLit;

        public EntryBackground(Transform parent, Sprite background, Sprite street, Sprite[] glow, Sprite[] twinkle, LumenClip clip)
        {
            this.clip = clip;
            loopFrames = Math.Max(1, clip.Last - clip.First + 1);
            var root = SkinUi.Rect("Background", parent);
            SkinUi.Image("Street", root, background, 1920, 1080).rectTransform.TopLeft(0, 0);
            for (int i = 0; i < twinkles.Length; i++)
            {
                twinkles[i] = SkinUi.Image("Twinkle" + i, root, twinkle[Twinkles[i].Frame]);
                twinkles[i].rectTransform.TopLeft(TwinklePositions[i].x, TwinklePositions[i].y);
            }
            for (int i = 0; i < glows.Length; i++)
            {
                glows[i] = SkinUi.Image("Glow" + i, root, glow[i]);
                glows[i].rectTransform.TopLeft(GlowPositions[i].x, GlowPositions[i].y);
            }
            streetLit = SkinUi.Image("StreetLit", root, street, 1920, 1080);
            streetLit.rectTransform.TopLeft(0, 0);
        }

        public void Show(double elapsedMs)
        {
            double frames = elapsedMs * 0.06;
            double f = frames % loopFrames;
            for (int i = 0; i < twinkles.Length; i++)
            {
                double? scale = clip.Get(Twinkles[i].Track, f, "sx");
                float alpha = (float)(clip.Get(Twinkles[i].Track, f, "a") ?? 0);
                twinkles[i].Alpha(scale.HasValue && alpha > 0.002f ? alpha : 0);
                if (scale.HasValue) twinkles[i].rectTransform.localScale = Vector3.one * (float)scale.Value;
            }
            float glow = (float)clip.Get("#16@3/#14@0", f, "a", 1);
            foreach (var image in glows) image.Alpha(glow);
            // monotonic, not wrapped: the lights come on once
            streetLit.Alpha(frames < StreetLitFrames ? (float)clip.Get("#17@7", frames, "a", 0) : 0);
        }
    }

    // Entry:draw_credit — the arcade credit rows 「１人プレイ」/「２人プレイ」 with
    // 「太鼓をたたいてスタート！」: both blink on anim/credit_row (120-frame loop from frame 5),
    // fade in on anim/credit_fade, and on a join the joined row flashes white for 24 frames before
    // the rows fade out.
    public sealed class EntryCredit
    {
        const float LabelX = 960 - 443, MessageX = 960 + 190, FontSize = 56;
        static readonly float[] RowY = { 432 + 72, 432 + 248 };
        const double LoopStart = 5, LoopFrames = 120, DecideFlashFrames = 24;
        static readonly Color32 Yellow = new Color32(255, 236, 67, 255);

        readonly LumenClip row, fade;
        readonly Image[] pills = new Image[2], flashes = new Image[2];
        readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[2], messages = new TextMeshProUGUI[2], highlights = new TextMeshProUGUI[2];

        public float Alpha { get; private set; }
        public float FlashAlpha { get; private set; }
        public bool IsVisible => Alpha > 0.002f;

        public EntryCredit(Transform parent, Sprite pill, Sprite flash, TMP_FontAsset font, Material outline, LumenClip row, LumenClip fade)
        {
            this.row = row; this.fade = fade;
            var root = SkinUi.Rect("Credit", parent);
            string[] names = { "１人プレイ", "２人プレイ" };
            for (int i = 0; i < 2; i++)
            {
                pills[i] = SkinUi.Image("Pill" + (i + 1), root, pill);
                pills[i].rectTransform.TopLeft(380, 404 + i * 176);
                // entry_credit_Np: black fill, white border 7
                labels[i] = Text(root, "Label" + (i + 1), font, outline, Color.black, Color.white, names[i]);
                labels[i].alignment = TextAlignmentOptions.MidlineLeft;
                labels[i].rectTransform.pivot = new Vector2(0, 0.5f);
                labels[i].rectTransform.anchoredPosition = new Vector2(LabelX, -RowY[i]);
                // entry_credit_start: white fill, black border; a yellow-bordered copy blends over it
                messages[i] = Text(root, "Message" + (i + 1), font, outline, Color.white, Color.black, "太鼓をたたいてスタート！");
                messages[i].rectTransform.Center(MessageX, RowY[i]);
                highlights[i] = Text(root, "Highlight" + (i + 1), font, outline, Color.white, Yellow, "太鼓をたたいてスタート！");
                highlights[i].rectTransform.Center(MessageX, RowY[i]);
            }
            for (int i = 0; i < 2; i++)
            {
                flashes[i] = SkinUi.Image("Flash" + (i + 1), root, flash);
                flashes[i].rectTransform.TopLeft(380, 404 + i * 176);
            }
        }

        static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, Material outline, Color fill, Color32 border, string value)
        {
            var text = SkinUi.Text(name, parent, font, outline, FontSize, border, 0);
            text.OutlineOutside(0.6f);
            text.color = fill;
            text.text = value;
            return text;
        }

        // Credit screen: rows fade in on credit_fade frames 5..15 and blink together (arcade model).
        public void ShowWaiting(double msSinceStart, double msSinceBlink)
        {
            double fi = Math.Min(15, 5 + msSinceStart * 0.06);
            double blink = LoopStart + msSinceBlink * 0.06 % LoopFrames;
            Draw((float)fade.Get("player1_text_instance", fi, "a", 1), blink, -1, 0);
        }

        // Entry:draw_credit_decide: flash the joined row (credit_row #8@4 from `choose`), then fade
        // out on credit_fade from frame 16. Returns false once the rows are gone.
        public bool ShowDecided(double msSinceDecide, int joinedRow)
        {
            double t = msSinceDecide * 0.06;
            float flash = (float)row.Get("#8@4", (row.Label("choose") ?? 125) + Math.Min(t, DecideFlashFrames), "a", 0);
            double fadeFrame = 16 + Math.Max(0, t - (DecideFlashFrames + 1));
            float alpha = (float)fade.Get("player1_text_instance", Math.Min(fadeFrame, 35), "a", 0);
            if (alpha <= 0.002f) { Hide(); return false; }
            Draw(alpha, LoopStart, joinedRow, flash);
            return true;
        }

        public void Hide() => Draw(0, LoopStart, -1, 0);

        void Draw(float alpha, double frame, int flashRow, float flash)
        {
            Alpha = alpha;
            FlashAlpha = flashRow >= 0 ? alpha * flash : 0;
            float message = (float)row.Get("text_message_instance", frame, "a", 1);
            float yellow = (float)(row.Get("text_message_instance_2", frame, "cr", 0) / 256.0);
            for (int i = 0; i < 2; i++)
            {
                pills[i].Alpha(alpha);
                labels[i].alpha = alpha;
                messages[i].alpha = alpha * message;
                highlights[i].alpha = alpha * message * yellow;
                labels[i].enabled = alpha > 0.002f;
                messages[i].enabled = alpha * message > 0.002f;
                highlights[i].enabled = alpha * message * yellow > 0.001f;
                flashes[i].Alpha(i == flashRow ? FlashAlpha : 0);
            }
        }
    }

    // One mode board of the Entry list: its open and closed plates, title and comment lines.
    public sealed class EntryMode
    {
        public string Title, Scene;
        public string[] Info;
        public Color32 Rim;          // the title's mode-colour rim (box.lua MODES[*].outline)
        public Sprite On, Off;
    }

    // EntryBox:draw (Nijiiro box.lua): the arcade mode list. The selected board sits open at the
    // centre and the others closed one slot above/below (mode_list `wait`: kanban_2 / kanban_3,
    // boards further than one slot fade out). A ka slides every board to its next slot linearly
    // over 9 frames, and the newly selected board only opens (select_on) after the slide while the
    // old one closes (select_off). The first appearance plays `in`: the selected board fades up and
    // opens, the closed boards fly in from three slots out. The cursor glow pulses on cursor_glow,
    // and the decide plays `choose`'s white flash while the list fades out (entry animation 9).
    public sealed class EntryModeList
    {
        const double FrameMs = 1000.0 / 60, SlideFrames = 9, ListInDelay = 10, ListInFrames = 12;

        readonly LumenClip board, glow, list;
        readonly double selectOn, selectOff, inLabel, chooseLabel, onLimit, offLimit, inLimit, chooseLimit, glowFrames, waitLabel;
        readonly Vector2 listIn;
        readonly EntryModeBoard[] boards;
        int selected;

        public RectTransform Root { get; }
        public IReadOnlyList<EntryModeBoard> Boards => boards;
        public EntryModeBoard Selected => boards[selected];
        // The selected board's state, as the single-board version reported it.
        public float Openness => Selected.Openness;
        public float Fade => Selected.Fade;
        public float ChooseFlash => Selected.ChooseFlash;

        public EntryModeList(Transform parent, IReadOnlyList<EntryMode> modes, Sprite boardFlash, Sprite cursorGlow,
            TMP_FontAsset font, Material outline, LumenClip board, LumenClip glow, LumenClip list)
        {
            this.board = board; this.glow = glow; this.list = list;
            selectOn = board.Label("select_on") ?? 27;
            selectOff = board.Label("select_off") ?? 52;
            inLabel = board.Label("in") ?? 67;
            chooseLabel = board.Label("choose") ?? 117;
            onLimit = selectOff - selectOn - 1;
            offLimit = inLabel - selectOff - 1;
            inLimit = chooseLabel - inLabel - 1;
            chooseLimit = board.Last - chooseLabel;
            glowFrames = Math.Max(1, glow.Last - glow.First + 1);
            waitLabel = list.Label("wait") ?? 13;
            double listInLabel = list.Label("in") ?? 87;
            listIn = new Vector2((float)list.Get("kanban_3", listInLabel, "tx", 170), (float)list.Get("kanban_3", listInLabel, "ty", 878));
            Root = SkinUi.Rect("ModeBoards", parent);
            boards = new EntryModeBoard[modes.Count];
            for (int i = 0; i < modes.Count; i++)
                boards[i] = new EntryModeBoard(Root, modes[i], boardFlash, cursorGlow, font, outline);
        }

        // kanban_1 is the selected slot; the slots above/below are kanban_2/3, 4/5, 6/7.
        Vector2 Slot(int rel)
        {
            if (rel == 0) return Vector2.zero;
            int n = Math.Abs(rel);
            string kanban = "kanban_" + (2 * n + (rel < 0 ? 0 : 1));
            double? tx = list.Get(kanban, waitLabel, "tx"), ty = list.Get(kanban, waitLabel, "ty");
            if (tx.HasValue && ty.HasValue) return new Vector2((float)tx.Value, (float)ty.Value);
            int sign = rel < 0 ? -1 : 1;    // more slots than the arcade list carries
            return new Vector2(sign * (50 + (n - 1) * 40), sign * (305 + (n - 1) * 191));
        }

        double Open(double frame)
        {
            double sy = board.Get("board_bg_center_instance", frame, "sy", EntryModeBoard.ClosedSy);
            return Math.Max(0, Math.Min(1, (sy - EntryModeBoard.ClosedSy) / (1 - EntryModeBoard.ClosedSy)));
        }

        static double Segment(double label, double u, double limit) => label + Math.Max(0, Math.Min(limit, u));
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static double EaseOut(double t) { t = Clamp01(t); return 1 - (1 - t) * (1 - t); }

        // nowMs: real clock; msSinceIn: since the list first appeared; selectedIndex: the flow's
        // selection; fade: entry animation 9 (1 until decided); msSinceChoose: negative before it.
        public void Show(double nowMs, double msSinceIn, int selectedIndex, float fade, double msSinceChoose)
        {
            selected = Math.Max(0, Math.Min(boards.Length - 1, selectedIndex));
            double tIn = msSinceIn / FrameMs;
            bool inRunning = tIn <= inLimit;
            double pulse = glow.Get("#12@0", nowMs / FrameMs % glowFrames, "a", 1);
            for (int i = 0; i < boards.Length; i++)
            {
                var b = boards[i];
                bool isSelected = i == selected;
                // The arcade opens the new board only after the 9-frame slide (mode_select.lua MenuMove).
                if (isSelected != b.WasSelected)
                {
                    b.WasSelected = isSelected;
                    b.OpenStartedAt = nowMs + (isSelected ? SlideFrames * FrameMs : 0);
                    b.Opening = isSelected;
                }
                int rel = i - selected;
                var slot = Slot(rel);
                b.Retarget(nowMs, slot, Math.Abs(rel) <= 1 ? 1 : 0, SlideFrames * FrameMs);
                var position = b.Position;
                if (inRunning && rel != 0)
                {
                    double q = EaseOut((tIn - ListInDelay) / ListInFrames);
                    float sign = rel < 0 ? -1 : 1;
                    position += (float)(1 - q) * (new Vector2(sign * listIn.x, sign * listIn.y) - slot);
                }

                double o, info, cursor;
                float boardFade = fade;
                if (inRunning)
                {
                    double f = Segment(inLabel, tIn, inLimit);
                    o = isSelected ? Open(f) : 0;
                    info = isSelected ? board.Get("text_info_instance", f, "a", 0) : 0;
                    cursor = isSelected ? board.Get("cursor_center", f, "a", 0) : 0;
                    boardFade *= (float)board.Get("board_bg_center_instance", f, "a", 0);
                }
                else
                {
                    double u = double.IsNaN(b.OpenStartedAt) ? 999 : (nowMs - b.OpenStartedAt) / FrameMs;
                    double frame = b.Opening ? Segment(selectOn, u, onLimit) : Segment(selectOff, u, offLimit);
                    o = Open(frame);
                    info = board.Get("text_info_instance", frame, "a", 0);
                    cursor = o;
                }
                float title = inRunning && isSelected ? boardFade * (float)info : boardFade;
                double c = msSinceChoose / FrameMs;
                float flash = isSelected && msSinceChoose >= 0 && c <= chooseLimit ? (float)board.Get("#22@6", chooseLabel + c, "a", 0) : 0;
                b.Draw(position, (float)o, boardFade * b.Visibility, (float)info, (float)(cursor * pulse), title * b.Visibility, flash);
            }
        }
    }

    // One board's plates and texts, drawn at a list offset (y down, from the open-board slot).
    public sealed class EntryModeBoard
    {
        public const double ClosedSy = 0.1857;
        const float CenterX = 960, CenterY = 535, InfoY = 59.5f, InfoLineHeight = 53;
        const float TitleYOn = -97, TitleYOff = 4, TitleSize = 72, InfoSize = 34;

        readonly Image cursor, closed, open, flash;
        readonly TextMeshProUGUI title;
        readonly TextMeshProUGUI[] info;
        Vector2 from, target;
        float fromVisibility, targetVisibility;
        double slideStartedAt = double.NaN;

        public EntryMode Mode { get; }
        public RectTransform Root { get; }
        public float Openness { get; private set; }
        public float Fade { get; private set; }
        public float ChooseFlash { get; private set; }
        public Vector2 Position { get; private set; }
        public float Visibility { get; private set; }
        internal bool WasSelected;
        internal bool Opening;
        internal double OpenStartedAt = double.NaN;

        public EntryModeBoard(Transform parent, EntryMode mode, Sprite boardFlash, Sprite cursorGlow, TMP_FontAsset font, Material outline)
        {
            Mode = mode;
            Root = SkinUi.Rect(mode.Title, parent);
            // the cursor glow sits under the board (mode_select.nulm depth order)
            cursor = Plate("Cursor", cursorGlow);
            closed = Plate("Closed", mode.Off);
            open = Plate("Open", mode.On);
            info = new TextMeshProUGUI[mode.Info.Length];
            for (int i = 0; i < info.Length; i++)
            {
                // text_info: 34, white, black border 5
                info[i] = SkinUi.Text("Info" + i, Root, font, outline, InfoSize, new Color32(0, 0, 0, 255), 0);
                info[i].OutlineOutside(0.6f);
                info[i].characterSpacing = 100f / InfoSize;
                info[i].text = mode.Info[i];
                info[i].rectTransform.Center(CenterX, CenterY + InfoY + (i - (info.Length - 1) / 2f) * InfoLineHeight);
            }
            // two stacked titles in the arcade: a mode-colour rim under a wider black one
            title = SkinUi.Text("Title", Root, font, outline, TitleSize, mode.Rim, 0);
            title.OutlineOutside(0.3f);
            var material = title.fontMaterial;
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, Color.black);
            // Preserve the previous black rim (5.481 design units) when using the wider UI atlas.
            // Underlay dilation shares half of the face expansion; it has its own TMP scale ratio.
            ShaderUtilities.UpdateShaderRatios(material);
            float gradient = material.GetFloat(ShaderUtilities.ID_GradientScale);
            float face = material.GetFloat(ShaderUtilities.ID_FaceDilate) * material.GetFloat(ShaderUtilities.ID_ScaleRatio_A);
            float underlay = (5.481f * title.font.faceInfo.pointSize / (gradient * TitleSize) - face)
                / material.GetFloat(ShaderUtilities.ID_ScaleRatio_C);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, underlay);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0);
            title.UpdateMeshPadding();
            title.characterSpacing = 2 * 100f / TitleSize;
            title.text = mode.Title;
            flash = Plate("Flash", boardFlash);
        }

        Image Plate(string name, Sprite sprite)
        {
            var image = SkinUi.Image(name, Root, sprite);
            image.rectTransform.TopLeft(380, 305);
            return image;
        }

        // list_anim_up / list_anim_down: every board moves to its next slot linearly; the
        // more-than-one-slot fade rides the same ramp. The first call places the board.
        public void Retarget(double nowMs, Vector2 slot, float visibility, double slideMs)
        {
            if (double.IsNaN(slideStartedAt))
            {
                from = target = Position = slot;
                fromVisibility = targetVisibility = Visibility = visibility;
                slideStartedAt = nowMs - slideMs;
            }
            if (slot != target || visibility != targetVisibility)
            {
                from = Position; fromVisibility = Visibility;
                target = slot; targetVisibility = visibility;
                slideStartedAt = nowMs;
            }
            float p = (float)Math.Max(0, Math.Min(1, (nowMs - slideStartedAt) / slideMs));
            Position = Vector2.Lerp(from, target, p);
            Visibility = Mathf.Lerp(fromVisibility, targetVisibility, p);
        }

        public void Draw(Vector2 offset, float openness, float fade, float infoAlpha, float cursorAlpha, float titleAlpha, float chooseFlash)
        {
            Root.anchoredPosition = new Vector2(offset.x, -offset.y);
            Openness = openness;
            Fade = fade;
            ChooseFlash = chooseFlash;
            bool visible = fade > 0.001f;
            Root.gameObject.SetActive(visible);
            if (!visible) return;
            cursor.Alpha(fade * cursorAlpha);
            closed.Alpha(openness < 0.999f ? fade * (1 - openness) : 0);
            open.Alpha(openness > 0.001f ? fade * openness : 0);
            foreach (var line in info)
            {
                line.alpha = fade * infoAlpha;
                line.enabled = line.alpha > 0.001f;
            }
            title.alpha = titleAlpha;
            title.enabled = titleAlpha > 0.001f;
            title.rectTransform.Center(CenterX, CenterY + TitleYOff + (TitleYOn - TitleYOff) * openness);
            flash.Alpha(chooseFlash);
        }
    }
}
