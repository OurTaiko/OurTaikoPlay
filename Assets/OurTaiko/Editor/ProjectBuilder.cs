using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string Root = "Assets/OurTaiko/";

        static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); return r;
        }
        static Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Art/" + path + ".png");
        static UnityEngine.UI.Image Picture(Transform parent, string name, string path, float x, float y, float w = 0, float h = 0)
        {
            var sprite = Sprite(path);
            if (sprite == null) throw new FileNotFoundException(path);
            var r = Rect(name, parent, x, y, w > 0 ? w : sprite.rect.width, h > 0 ? h : sprite.rect.height);
            var image = r.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = sprite; image.raycastTarget = false; return image;
        }
        static UnityEngine.UI.Image Panel(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color; image.raycastTarget = false; return image;
        }
        static TMP_Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = value; label.fontSize = size; label.alignment = align;
            label.color = Color.white; label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.UseUiFont();
            return label;
        }
        static UnityEngine.UI.Button Button(Transform parent, string name, string caption, float x, float y, float w, float h, Color color)
        {
            var background = Panel(parent, name, x, y, w, h, color); background.raycastTarget = true;
            var button = background.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = background;
            var colors = button.colors; colors.highlightedColor = new Color(1, 0.87f, 0.68f); colors.pressedColor = new Color(0.7f, 0.7f, 0.7f); button.colors = colors;
            Label(background.transform, "Label", caption, 0, 0, w, h, 20); return button;
        }
        // One frame of a sheet, cut in the sheet's own importer (see SliceSheet). y counts from the top.
        static Sprite Slice(string name, string path, int x, int y, int width, int height)
            => SliceSheet(path, new[] { (name, x, y, width, height) })[0];

    }
}
