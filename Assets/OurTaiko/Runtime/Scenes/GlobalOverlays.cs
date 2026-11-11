using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Art for the Nijiiro global arcade chrome (Scripts/global/indicator.lua, coin_overlay.lua,
    // entry_overlay.lua). Positions are the skin's texture.json origins. The arcade's timer is
    // not shown anywhere (user decision: the simulator does not limit the player's time).
    [Serializable]
    public sealed class ArcadeOverlayArt
    {
        [Header("Control guide")]
        [Tooltip("ControlGuide.anim: global/indicator/background cells 210-324 (both sticks + 決定, the decide loop) at 30 fps.")]
        public AnimationClip guideClip;

        [Header("Chips and 2P invite")]
        public Sprite qrChip;
        public Sprite cardChip, stageChip, danChip, inviteBubble;
        public TextAsset creditSideTimeline;
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

        // Binds a saved guide image (Entry places it at (0,12), 352x276); its ClipSampler plays
        // the overlay art's clip.
        public ControlGuideView(Image image, ArcadeOverlayArt art)
        {
            clip = art.guideClip;
            FrameCount = Mathf.RoundToInt(clip.length * clip.frameRate);
            this.image = image;
            sampler = image.GetComponent<ClipSampler>();
            if (sampler == null) sampler = ClipSampler.Attach(image.gameObject, clip);
            sampler.clip = clip;
            Sample(0);
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

        public CoinOverlayView(Transform parent, ArcadeOverlayArt art,
            bool freePlay = true, bool qrChip = true, bool invite = true)
        {
            clip = art.creditSideTimeline != null ? LumenClip.Parse(art.creditSideTimeline.text) : LumenClip.Empty;
            var root = SkinUi.Rect("CoinOverlay", parent);
            if (freePlay)
            {
                // credit: size 40, white
                FreePlay = Text(root, "FreePlay", L10n.Get("Menu", "overlay.freePlay"));
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
            player = Text(root, "InvitePlayer", L10n.Get("Menu", "overlay.invitePlayer"));
            player.rectTransform.Center(SeatX, PlayerY);
            player.Squeeze(BubbleWidth);
            message = Text(root, "InviteMessage", L10n.Get("Menu", "overlay.inviteMessage"));
            message.rectTransform.Center(SeatX, MessageY);
            message.Squeeze(BubbleWidth);
            ShowInvite(false, 0);
        }

        public CoinOverlayView(SongSelectOverlayView view, ArcadeOverlayArt art)
            : this(art, null, view.qrChip, view.inviteBubble, view.invitePlayer, view.inviteMessage) { }

        // Binds saved objects; a null part is a part this screen does not show.
        public CoinOverlayView(ArcadeOverlayArt art, TMP_Text freePlay, Image qrChip,
            Image inviteBubble, TextMeshProUGUI invitePlayer, TextMeshProUGUI inviteMessage)
        {
            clip = art.creditSideTimeline != null ? LumenClip.Parse(art.creditSideTimeline.text) : LumenClip.Empty;
            FreePlay = freePlay;
            QrChip = qrChip;
            bubble = inviteBubble;
            player = invitePlayer;
            message = inviteMessage;
        }

        // The credit line's text (size 40, white, 2 px apart), which other screens' announcements share.
        public static TextMeshProUGUI Text(Transform parent, string name, string value)
        {
            var text = SkinUi.Text(name, parent, 40);
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
}
