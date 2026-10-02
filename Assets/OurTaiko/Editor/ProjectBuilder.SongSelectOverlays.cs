using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        public static SongSelectOverlayView CreateSongSelectOverlayView(RectTransform parent,
            ArcadeOverlayArt art, TMP_FontAsset font, Material outline)
        {
            var root = SkinUi.Rect("GlobalOverlays", parent);
            var view = root.gameObject.AddComponent<SongSelectOverlayView>();
            var timer = SkinUi.Rect("Timer", root);
            view.timerBackground = SkinUi.Image("Background", timer, art.timerBackground);
            view.timerBackground.rectTransform.TopLeft(1669, 12);
            view.timerTwoDigits = CreatePlaceholderDigits(timer, art, "60", false);
            view.timerThreeDigits = CreatePlaceholderDigits(timer, art, "100", true);

            var coins = SkinUi.Rect("CoinOverlay", root);
            view.qrChip = SkinUi.Image("QrChip", coins, art.qrChip);
            view.qrChip.rectTransform.TopLeft(1570, 38);
            view.inviteBubble = SkinUi.Image("InviteBubble", coins, art.inviteBubble);
            view.inviteBubble.rectTransform.TopLeft(1492, 638);
            view.invitePlayer = CreateInviteText(coins, "InvitePlayer", font, outline, "2人プレイ", 749);
            view.inviteMessage = CreateInviteText(coins, "InviteMessage", font, outline, "太鼓をたたいてスタート!", 802);
            return view;
        }

        static Image[] CreatePlaceholderDigits(Transform parent, ArcadeOverlayArt art, string value, bool visible)
        {
            var row = SkinUi.Rect(value.Length + "Digits", parent);
            var images = new Image[value.Length];
            for (int i = 0; i < images.Length; i++)
            {
                images[i] = SkinUi.Image("Digit" + i, row, art.timerDigitsBlack[value[i] - '0']);
                images[i].rectTransform.TopLeft(1781 - value.Length * 48 / 2 + i * 48, 84);
                images[i].enabled = visible;
            }
            return images;
        }

        static TextMeshProUGUI CreateInviteText(Transform parent, string name, TMP_FontAsset font,
            Material outline, string value, float y)
        {
            var text = SkinUi.Text(name, parent, font, outline, 40, new Color32(0, 0, 0, 255), 0);
            text.OutlineOutsidePixels(6);
            text.characterSpacing = 2 * 100f / 40;
            text.text = value;
            text.rectTransform.Center(1700, y);
            text.Squeeze(416);
            return text;
        }
    }
}
