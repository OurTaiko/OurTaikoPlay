using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Art for the Nijiiro global arcade chrome (Scripts/global/timer.lua, indicator.lua,
    // coin_overlay.lua, entry_overlay.lua). Positions are the skin's texture.json origins.
    [Serializable]
    public sealed class ArcadeOverlayArt
    {
        [Header("Timer")]
        public Sprite timerBackground;
        [Tooltip("counter_black frames 0-9.")]
        public Sprite[] timerDigitsBlack;

        [Header("Control guide")]
        [Tooltip("global/indicator/background cells 210-324: both sticks + 決定 (the decide loop).")]
        public Sprite[] guideDecideFrames;

        [Header("Chips and 2P invite")]
        public Sprite qrChip;
        public Sprite cardChip, stageChip, danChip, inviteBubble;
        public TextAsset creditSideTimeline;
    }

    // Timer:draw — the 240x240 clock at (1669,12) with its digits centred 48 px apart. The arcade
    // counts down (red clock, voices and an automatic pick at 0); here the timer is a placeholder
    // that only shows its starting value (user decision: the simulator does not limit the player's time).
    public sealed class ArcadeTimerView
    {
        const float DigitX = 1781, DigitY = 84, DigitMargin = 48;
        readonly ArcadeOverlayArt art;
        readonly Transform root;
        readonly System.Collections.Generic.List<Image> digits = new System.Collections.Generic.List<Image>();

        public int Seconds { get; private set; } = -1;

        public ArcadeTimerView(Transform parent, ArcadeOverlayArt art)
        {
            this.art = art;
            root = SkinUi.Rect("Timer", parent);
            SkinUi.Image("Background", root, art.timerBackground).rectTransform.TopLeft(1669, 12);
        }

        public void Show(int seconds)
        {
            if (seconds == Seconds) return;
            Seconds = seconds;
            string text = seconds.ToString();
            // song select's list timer shows 100
            while (digits.Count < text.Length) digits.Add(SkinUi.Image("Digit" + digits.Count, root, art.timerDigitsBlack[0]));
            for (int i = 0; i < digits.Count; i++)
            {
                digits[i].enabled = i < text.Length;
                if (i >= text.Length) continue;
                digits[i].sprite = art.timerDigitsBlack[text[i] - '0'];
                digits[i].rectTransform.sizeDelta = digits[i].sprite.rect.size;
                digits[i].rectTransform.TopLeft(DigitX - text.Length * DigitMargin / 2 + i * DigitMargin, DigitY);
            }
        }
    }

    // Indicator:draw — the top-left control guide, a baked loop at 30 fps (every 2nd arcade frame).
    // Entry and single-board screens use the decide loop: both sticks and the 決定 pill.
    public sealed class ControlGuideView
    {
        const double CellsPerSecond = 60 / 2.0;
        readonly ArcadeOverlayArt art;
        readonly Image image;

        public int Frame { get; private set; }
        public Image Image => image;

        public ControlGuideView(Transform parent, ArcadeOverlayArt art)
        {
            this.art = art;
            image = SkinUi.Image("ControlGuide", parent, art.guideDecideFrames[0]);
            image.rectTransform.TopLeft(0, 12);
        }

        public void Show(double elapsedMs, float alpha)
        {
            Frame = (int)Math.Floor(Math.Max(0, elapsedMs) / 1000 * CellsPerSecond) % art.guideDecideFrames.Length;
            image.sprite = art.guideDecideFrames[Frame];
            image.Alpha(alpha);
        }
    }

    // CoinOverlay:draw: 「フリープレイ」 centred at (960,1046), the QR chip (always NG) and, while only
    // 1P is in, the 2P invite cloud on the right seat, blinking on anim/credit_side (2 s shown, 1 s
    // blink, 3 s period) after a 133 ms pop-in. Each screen shows its own subset (coin_overlay.lua):
    // Entry all three, song select the chip and the cloud, result the credit line only.
    public sealed class CoinOverlayView
    {
        const float BubbleWidth = 416, SeatX = 1700, PlayerY = 749, MessageY = 802;
        readonly LumenClip clip;
        readonly Image bubble;
        readonly TextMeshProUGUI player, message;

        public float BubbleAlpha { get; private set; }
        public TMP_Text FreePlay { get; }
        public Image QrChip { get; }
        public bool HasInvite => bubble != null;

        public CoinOverlayView(Transform parent, ArcadeOverlayArt art, TMP_FontAsset font, Material outline,
            bool freePlay = true, bool qrChip = true, bool invite = true)
        {
            clip = art.creditSideTimeline != null ? LumenClip.Parse(art.creditSideTimeline.text) : LumenClip.Empty;
            var root = SkinUi.Rect("CoinOverlay", parent);
            if (freePlay)
            {
                // credit: size 40, white with a black border (OutlinedText 3 x 1.5 = 4.5 px)
                FreePlay = Text(root, "FreePlay", font, outline, "フリープレイ");
                FreePlay.rectTransform.Center(960, 1046);
            }
            if (qrChip)
            {
                QrChip = SkinUi.Image("QrChip", root, art.qrChip);
                QrChip.rectTransform.TopLeft(1570, 38);
            }
            if (!invite) return;
            bubble = SkinUi.Image("InviteBubble", root, art.inviteBubble);
            bubble.rectTransform.TopLeft(1492, 638);
            player = Text(root, "InvitePlayer", font, outline, "2人プレイ");
            player.rectTransform.Center(SeatX, PlayerY);
            player.Squeeze(BubbleWidth);
            message = Text(root, "InviteMessage", font, outline, "太鼓をたたいてスタート!");
            message.rectTransform.Center(SeatX, MessageY);
            message.Squeeze(BubbleWidth);
            ShowInvite(false, 0);
        }

        static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, Material outline, string value)
        {
            var text = SkinUi.Text(name, parent, font, outline, 40, new Color32(0, 0, 0, 255), 0);
            text.OutlineOutside(0.6f);
            text.characterSpacing = 2 * 100f / 40;
            text.text = value;
            return text;
        }

        public void ShowInvite(bool visible, double elapsedMs)
        {
            if (bubble == null) return;
            double f = elapsedMs * 0.06;
            float pop = (float)Math.Min(1, Math.Max(0, f / 8));
            double length = Math.Max(1, clip.Last - clip.First + 1);
            double loop = f % length;
            BubbleAlpha = visible ? (float)clip.Get("#3@0", loop, "a", 0) * pop : 0;
            float textAlpha = visible ? (float)clip.Get("credit_all_instance", loop, "a", 0) * pop : 0;
            bubble.Alpha(BubbleAlpha);
            player.alpha = message.alpha = textAlpha;
            player.enabled = message.enabled = textAlpha > 0.002f;
        }
    }

    // EntryOverlay:draw offline: 段位道場 NG, 「1プレイ 4曲」 and IC Card NG chips.
    public static class StatusChips
    {
        public static RectTransform Build(Transform parent, ArcadeOverlayArt art)
        {
            var root = SkinUi.Rect("StatusChips", parent);
            SkinUi.Image("Stage", root, art.stageChip).rectTransform.TopLeft(1200, 45);
            SkinUi.Image("Card", root, art.cardChip).rectTransform.TopLeft(1460, 38);
            SkinUi.Image("Dan", root, art.danChip).rectTransform.TopLeft(1040, 38);
            return root;
        }
    }
}
