using UnityEngine;

namespace OurTaiko.Chara
{
    // Puts a seat's Don on a menu screen (song select, Entry, results, 段位) at YataiDON's
    // Nijiiro skin positions. Every scene lays its screen out on Canvas/Viewport1920x1080, whose
    // top-left-anchored children use the skin's coordinates (x, -y), so that is the Don's parent.
    public static class DonStage
    {
        public const string LayoutName = "Viewport1920x1080";

        // YataiDON skin chara_3d positions in the Nijiiro layout (1920×1080, y down).
        public static readonly Vector2 SongSelect1P = new Vector2(225, 925), SongSelect2P = new Vector2(1695, 925);
        // Nijiiro Scripts/result/result_player.lua chara_pos: 238 / 1682, 919, drawn at 0.865.
        public static readonly Vector2 Result = new Vector2(238, 919), Result2P = new Vector2(1682, 919);
        public const float ResultScale = 0.865f;
        public static readonly Vector2 DanResult = new Vector2(187, 934);

        public static bool Enabled => SettingManager.EnsureInstance().Settings.display.chara3D;

        public static RectTransform LayoutOf(Component inside)
        {
            for (var t = inside != null ? inside.transform : null; t != null; t = t.parent)
                if (t.name == LayoutName && t is RectTransform rect) return rect;
            return null;
        }

        // A seat's Don standing at `spot` (layout coordinates), drawn just above `above`: right after
        // it among its siblings (beside: true, so it hides and moves with that part of the screen),
        // or after the layout child that holds it. In the seat's look; null with the 3D Don turned
        // off or no layout.
        public static DonView Place(Component above, string name, Vector2 spot, int seat, bool mirror, DonAnim anim,
            float drawScale = 1, bool useSkinPose = false, bool beside = false)
        {
            if (!Enabled || above == null) return null;
            var layout = LayoutOf(above);
            if (layout == null) return null;
            var view = DonView.Create(layout, name, spot.x, spot.y);
            view.drawScale = drawScale;
            if (beside && above.transform.parent != null)
            {
                view.transform.SetParent(above.transform.parent, true);
                view.transform.SetSiblingIndex(above.transform.GetSiblingIndex() + 1);
            }
            else
            {
                var child = above.transform;
                while (child != null && child.parent != layout) child = child.parent;
                if (child != null) view.transform.SetSiblingIndex(child.GetSiblingIndex() + 1);
            }
            view.Show(DonLooks.For(seat), mirror, useSkinPose).SetAnim(anim);
            view.Seat = seat;
            return view;
        }
    }
}
