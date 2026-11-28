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

        // The part the Don's background layer is drawn over, under the Don (empty: none). It is
        // a quad of the same mesh whose vertex colour has no red, which the composite material
        // reads as "show the background layer here" (DonComposite.shader).
        public Rect Back
        {
            get => back;
            set
            {
                if (back == value) return;
                back = value;
                SetVerticesDirty();
            }
        }
        Rect back;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 tint = color;
            AddQuad(vh, back, new Color32(0, tint.g, tint.b, tint.a));
            AddQuad(vh, shown, new Color32(255, tint.g, tint.b, tint.a));
        }

        void AddQuad(VertexHelper vh, Rect part, Color32 tint)
        {
            if (part.width <= 0 || part.height <= 0) return;
            var rect = GetPixelAdjustedRect();
            var uv = uvRect;
            int first = vh.currentVertCount;
            for (int corner = 0; corner < 4; corner++)
            {
                // bottom left, top left, top right, bottom right
                float x = corner < 2 ? part.xMin : part.xMax, y = corner == 1 || corner == 2 ? part.yMax : part.yMin;
                vh.AddVert(new Vector3(rect.x + rect.width * x, rect.y + rect.height * y), tint,
                    new Vector2(uv.x + uv.width * x, uv.y + uv.height * y));
            }
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
