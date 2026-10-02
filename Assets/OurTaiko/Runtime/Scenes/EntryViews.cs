using System;
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

    // EntryBox:draw (Nijiiro box.lua) for the single 演奏ゲーム board: the `in` timeline opens it
    // (board fade, crossfade from the closed to the open plate, contents, cursor glow), the cursor
    // glow pulses on anim/cursor_glow, and the decide plays `choose`'s white flash while the
    // board fades out (entry animation 9).
    public sealed class EntryModeBoard
    {
        const float CenterX = 960, CenterY = 535, InfoY = 59.5f, InfoLineHeight = 53;
        const float TitleYOn = -97, TitleYOff = 4, TitleSize = 72, InfoSize = 34;
        const double ClosedSy = 0.1857;
        static readonly Color32 ModeColor = new Color32(251, 1, 29, 255);   // 演奏ゲーム title rim

        readonly LumenClip board, glow;
        readonly double inLabel, chooseLabel, inLimit, chooseLimit, glowFrames;
        readonly Image cursor, closed, open, flash;
        readonly TextMeshProUGUI title;
        readonly TextMeshProUGUI[] info = new TextMeshProUGUI[2];

        public float Openness { get; private set; }
        public float Fade { get; private set; }
        public float ChooseFlash { get; private set; }
        public RectTransform Root { get; }

        public EntryModeBoard(Transform parent, Sprite boardOn, Sprite boardOff, Sprite boardFlash, Sprite cursorGlow,
            TMP_FontAsset font, Material outline, LumenClip board, LumenClip glow)
        {
            this.board = board; this.glow = glow;
            inLabel = board.Label("in") ?? 67;
            chooseLabel = board.Label("choose") ?? 117;
            inLimit = chooseLabel - inLabel - 1;
            chooseLimit = board.Last - chooseLabel;
            glowFrames = Math.Max(1, glow.Last - glow.First + 1);
            Root = SkinUi.Rect("ModeBoard", parent);
            // the cursor glow sits under the board (mode_select.nulm depth order)
            cursor = Plate("Cursor", cursorGlow);
            closed = Plate("Closed", boardOff);
            open = Plate("Open", boardOn);
            string[] lines = { "すきな曲や、むずかしさを", "えらんであそべるよ！" };
            for (int i = 0; i < info.Length; i++)
            {
                // text_info: 34, white, black border 5
                info[i] = SkinUi.Text("Info" + i, Root, font, outline, InfoSize, new Color32(0, 0, 0, 255), 0);
                info[i].OutlineOutside(0.6f);
                info[i].characterSpacing = 100f / InfoSize;
                info[i].text = lines[i];
                info[i].rectTransform.Center(CenterX, CenterY + InfoY + (i - 0.5f) * InfoLineHeight);
            }
            // two stacked titles in the arcade: a mode-colour rim under a wider black one
            title = SkinUi.Text("Title", Root, font, outline, TitleSize, ModeColor, 0);
            title.OutlineOutside(0.3f);
            var material = title.fontMaterial;
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, Color.black);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.9f);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0);
            title.UpdateMeshPadding();
            title.characterSpacing = 2 * 100f / TitleSize;
            title.text = "演奏ゲーム";
            flash = Plate("Flash", boardFlash);
        }

        Image Plate(string name, Sprite sprite)
        {
            var image = SkinUi.Image(name, Root, sprite);
            image.rectTransform.TopLeft(380, 305);
            return image;
        }

        double Open(double frame)
        {
            double sy = board.Get("board_bg_center_instance", frame, "sy", ClosedSy);
            return Math.Max(0, Math.Min(1, (sy - ClosedSy) / (1 - ClosedSy)));
        }

        double InFrame(double t) => inLabel + Math.Max(0, Math.Min(inLimit, t));

        // msSinceIn: since the board first appeared; fade: entry animation 9 (1 until decided);
        // msSinceChoose: since the decide, or negative before it.
        public void Show(double nowMs, double msSinceIn, float fade, double msSinceChoose)
        {
            double t = msSinceIn * 0.06;
            double o, contents, cursorAlpha;
            float boardFade = fade;
            if (t <= inLimit)
            {
                double f = InFrame(t);
                o = Open(f);
                contents = board.Get("text_info_instance", f, "a", 0);
                cursorAlpha = board.Get("cursor_center", f, "a", 0);
                boardFade *= (float)board.Get("board_bg_center_instance", f, "a", 0);
            }
            else
            {
                // select_on has long finished by now: the open board at rest
                o = 1; contents = 1; cursorAlpha = 1;
            }
            Openness = (float)o;
            Fade = boardFade;
            double pulse = glow.Get("#12@0", nowMs * 0.06 % glowFrames, "a", 1);
            cursor.Alpha((float)(boardFade * cursorAlpha * pulse));
            closed.Alpha(o < 0.999 ? boardFade * (float)(1 - o) : 0);
            open.Alpha(o > 0.001 ? boardFade * (float)o : 0);
            foreach (var line in info)
            {
                line.alpha = (float)(boardFade * contents);
                line.enabled = line.alpha > 0.001f;
            }
            float titleAlpha = t <= inLimit ? boardFade * (float)contents : boardFade;
            title.alpha = titleAlpha;
            title.enabled = titleAlpha > 0.001f;
            title.rectTransform.Center(CenterX, CenterY + TitleYOff + (TitleYOn - TitleYOff) * (float)o);
            double c = msSinceChoose * 0.06;
            ChooseFlash = msSinceChoose >= 0 && c <= chooseLimit ? (float)board.Get("#22@6", chooseLabel + c, "a", 0) : 0;
            flash.Alpha(ChooseFlash);
        }
    }
}
