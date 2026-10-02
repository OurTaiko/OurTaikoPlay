using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Runtime helpers for the 1920x1080 Nijiiro stage: positions are the skin's top-left pixel
    // coordinates, every element is anchored to the stage's top-left corner and the Canvas scales it.
    public static class SkinUi
    {
        public static RectTransform Rect(string name, Transform parent, float width = 0, float height = 0)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static Image Image(string name, Transform parent, Sprite sprite, float width = -1, float height = -1)
        {
            var rect = Rect(name, parent,
                width >= 0 ? width : sprite != null ? sprite.rect.width : 0,
                height >= 0 ? height : sprite != null ? sprite.rect.height : 0);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
            return image;
        }

        // Centre of a child of a stage-anchored parent, in skin pixels (y grows downwards).
        public static void Center(this RectTransform rect, float x, float y) => rect.anchoredPosition = new Vector2(x, -y);
        public static void TopLeft(this RectTransform rect, float x, float y)
            => rect.anchoredPosition = new Vector2(x + rect.sizeDelta.x * rect.pivot.x, -y - rect.sizeDelta.y * (1 - rect.pivot.y));

        public static void Alpha(this Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
            graphic.enabled = alpha > 0.001f;
        }

        // Mobile SDF draws outlines only with OUTLINE_ON; scenes pass a saved material that enables it
        // so the shader variant also survives player builds.
        public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, Material outlineMaterial, float size, Color32 outline, float outlineWidth)
        {
            var rect = Rect(name, parent, 1200, size * 1.6f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            // Bind the material before the outline: TMP instances it for per-text outline colours.
            var material = new Material(outlineMaterial != null ? outlineMaterial : font.material);
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            text.fontSharedMaterial = material;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;
            text.outlineWidth = outlineWidth;
            text.outlineColor = outline;
            return text;
        }

        // TMP centres its outline on the glyph edge; dilating the face by the same width puts the
        // whole border outside the ink, as OutlinedText draws it. Gives the text its own material.
        public static void OutlineOutside(this TMP_Text text, float width)
        {
            text.outlineWidth = width;
            text.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, width);
            text.UpdateMeshPadding();
        }

        // Arcade EditText boxes squeeze long text horizontally down to the box width.
        public static void Squeeze(this TMP_Text text, float maxWidth, float minScale = 0)
        {
            text.rectTransform.localScale = Vector3.one;
            float width = text.preferredWidth;
            float scale = width > maxWidth && width > 0 ? Mathf.Max(minScale, maxWidth / width) : 1;
            text.rectTransform.localScale = new Vector3(scale, 1, 1);
        }

        public static double CubicOut(double t) => 1 - System.Math.Pow(1 - System.Math.Min(1, System.Math.Max(0, t)), 3);
    }
}
