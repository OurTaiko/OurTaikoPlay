using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The settings overlay's shapes, drawn once at runtime: a filled and an outlined rounded
    // rectangle (nine-sliced, so one sprite serves every size and corner radius) and a circle.
    public static class SettingsShapes
    {
        // Texture pixels of a corner; Image.pixelsPerUnitMultiplier scales it to the wanted radius.
        const int Radius = 64, Stroke = 14;
        static Sprite fill, outline, circle, ring;

        public static Sprite Fill => fill != null ? fill : fill = Rounded(false);
        public static Sprite Outline => outline != null ? outline : outline = Rounded(true);
        public static Sprite Circle => circle != null ? circle : circle = Disc(false);
        public static Sprite Ring => ring != null ? ring : ring = Disc(true);

        // A sliced rounded rectangle with corners of `radius` design pixels.
        public static Image Box(string name, Transform parent, Color color, float radius, bool outlined = false)
        {
            var image = SkinUi.Image(name, parent, outlined ? Outline : Fill, 0, 0);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.pixelsPerUnitMultiplier = Radius / Mathf.Max(1f, radius);
            return image;
        }

        public static Image Dot(string name, Transform parent, Color color, float size, bool ring = false)
        {
            var image = SkinUi.Image(name, parent, ring ? Ring : Circle, size, size);
            image.color = color;
            return image;
        }

        // The outline's stroke in design pixels for a box of the given radius.
        public static float StrokeFor(float radius) => Stroke * radius / Radius;

        static Sprite Rounded(bool hollow)
        {
            int size = Radius * 2 + 4;
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float half = size / 2f, inner = half - Radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance outside a rounded rectangle whose straight edges run to the texture's edge.
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0), dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - Radius;
                    pixels[y * size + x] = new Color32(255, 255, 255, Coverage(d, hollow));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = Radius + 2;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        static Sprite Disc(bool hollow)
        {
            const int size = 128;
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude - (size / 2f - 1);
                    pixels[y * size + x] = new Color32(255, 255, 255, Coverage(d, hollow, 10));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        // d < 0 is inside the edge; a hollow shape keeps only the band `stroke` wide inside it.
        static byte Coverage(float d, bool hollow, int stroke = Stroke)
        {
            float a = Mathf.Clamp01(0.5f - d);
            if (hollow) a = Mathf.Min(a, Mathf.Clamp01(d + stroke + 0.5f));
            return (byte)Mathf.RoundToInt(a * 255);
        }

        static Texture2D NewTexture(int size) => new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SettingsShape" };
    }
}
