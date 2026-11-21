using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Chara
{
    // The RawImage a DonView shows its render target with, drawn over only a part of its rect: the
    // Don's composite material reads the target twenty times a pixel, and the Don stands in a fifth
    // of the area it may move in. The texture still maps onto the whole rect, so what is drawn is
    // what a whole RawImage would draw there.
    public sealed class DonImage : RawImage
    {
        // The part to draw, as a share of the rect from its bottom left (empty: nothing).
        public Rect Shown
        {
            get => shown;
            set
            {
                if (shown == value) return;
                shown = value;
                SetVerticesDirty();
            }
        }
        Rect shown = new Rect(0, 0, 1, 1);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (shown.width <= 0 || shown.height <= 0) return;
            var rect = GetPixelAdjustedRect();
            var uv = uvRect;
            Color32 tint = color;
            for (int corner = 0; corner < 4; corner++)
            {
                // bottom left, top left, top right, bottom right
                float x = corner < 2 ? shown.xMin : shown.xMax, y = corner == 1 || corner == 2 ? shown.yMax : shown.yMin;
                vh.AddVert(new Vector3(rect.x + rect.width * x, rect.y + rect.height * y), tint,
                    new Vector2(uv.x + uv.width * x, uv.y + uv.height * y));
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
