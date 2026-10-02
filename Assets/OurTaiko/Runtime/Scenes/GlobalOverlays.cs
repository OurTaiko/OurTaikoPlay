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
        [Tooltip("ControlGuide.anim: global/indicator/background cells 210-324 (both sticks + 決定, the decide loop) at 30 fps.")]
        public AnimationClip guideClip;

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
        readonly SongSelectOverlayView savedView;

        public int Seconds { get; private set; } = -1;

        public ArcadeTimerView(Transform parent, ArcadeOverlayArt art)
        {
            this.art = art;
            root = SkinUi.Rect("Timer", parent);
            SkinUi.Image("Background", root, art.timerBackground).rectTransform.TopLeft(1669, 12);
        }

        // SongSelect keeps both supported placeholder layouts in the scene. Binding does not
        // recreate graphics or reset their Inspector-authored positions, dimensions or styling.
        public ArcadeTimerView(SongSelectOverlayView view, ArcadeOverlayArt art)
        {
            savedView = view;
            this.art = art;
        }

        public void Show(int seconds)
        {
            if (seconds == Seconds) return;
            Seconds = seconds;
            string text = seconds.ToString();
            if (savedView != null)
            {
                ShowSavedDigits(savedView.timerTwoDigits, text);
                ShowSavedDigits(savedView.timerThreeDigits, text);
                return;
            }
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

        void ShowSavedDigits(Image[] images, string text)
        {
            bool visible = images.Length == text.Length;
            for (int i = 0; i < images.Length; i++)
            {
                images[i].enabled = visible;
                if (visible) images[i].sprite = art.timerDigitsBlack[text[i] - '0'];
            }
        }
    }

    // Indicator:draw — the top-left control guide, a baked loop at 30 fps (every 2nd arcade frame).
    // Entry and single-board screens use the decide loop: both sticks and the 決定 pill. The frames
    // live in one sprite-swap clip; callers own the clock (Entry restarts it when 1P joins).
    public sealed class ControlGuideView
    {
        readonly AnimationClip clip;
        readonly Image image;
        readonly ClipSampler sampler;

        public int Frame { get; private set; }
        public int FrameCount { get; }
        public Image Image => image;

        public ControlGuideView(Transform parent, ArcadeOverlayArt art)
        {
            clip = art.guideClip;
            FrameCount = Mathf.RoundToInt(clip.length * clip.frameRate);
            image = SkinUi.Image("ControlGuide", parent, null);
            sampler = ClipSampler.Attach(image.gameObject, clip);
            Sample(0);
            image.rectTransform.sizeDelta = image.sprite.rect.size;
            image.rectTransform.TopLeft(0, 12);
        }

        public void Show(double elapsedMs, float alpha)
        {
            Sample((int)Math.Floor(Math.Max(0, elapsedMs) / 1000 * clip.frameRate) % FrameCount);
            image.Alpha(alpha);
        }

        // Sample mid-frame so float rounding never lands on the previous key.
        void Sample(int frame)
        {
            Frame = frame;
            sampler.Sample((frame + 0.5) / clip.frameRate);
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
                FreePlay = Text(root, "FreePlay", font, outline, "フリープレイ", 4.5f);
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

        public CoinOverlayView(SongSelectOverlayView view, ArcadeOverlayArt art)
        {
            clip = art.creditSideTimeline != null ? LumenClip.Parse(art.creditSideTimeline.text) : LumenClip.Empty;
            QrChip = view.qrChip;
            bubble = view.inviteBubble;
            player = view.invitePlayer;
            message = view.inviteMessage;
        }

        static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, Material outline, string value, float border = 6)
        {
            var text = SkinUi.Text(name, parent, font, outline, 40, new Color32(0, 0, 0, 255), 0);
            text.OutlineOutsidePixels(border);
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
