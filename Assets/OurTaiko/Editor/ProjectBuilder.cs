using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string Root = "Assets/OurTaiko/";

        static Transform NewScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 3.6f;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            // Menu controls are authored on the original grid, then expanded once before saving.
            var viewport = Rect("Viewport1280x720", canvas.transform, 0, 0, 1280, 720);
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0.5f, 0.5f); viewport.anchoredPosition = Vector2.zero;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            return viewport;
        }
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
        static void Background(Transform root)
        {
            Panel(root, "Backdrop", 0, 0, 1280, 720, new Color32(165, 44, 36, 255));
            for (int i = 0; i < 4; i++) Picture(root, "HeaderPattern" + i, "background/donbg/0_1/background/0", i * 328, 0);
            Picture(root, "FestivalBackground", "background/bg_normal/bg_0/background", 0, 360);
            Picture(root, "FestivalLights", "background/bg_normal/bg_0/overlay", 0, 360);
            Picture(root, "Footer", "background/footer/0", 0, 656);
        }
    }
}
