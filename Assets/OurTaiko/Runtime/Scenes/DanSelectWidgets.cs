using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The 段位道場 select's page tabs (ローカル / Fanmade / Database) and the 外伝 code entry. Both are
    // built at run time over the saved scene, in its 1920×1080 top-left pixels, from the scene's
    // own text style (the "empty" caption) so the font and material match.
    public sealed class PageTabs
    {
        public const float Top = 8, Width = 180, Height = 40, Gap = 10;
        public readonly RectTransform root;
        public readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
        public readonly List<Image> backs = new List<Image>();

        public PageTabs(RectTransform canvas, TextMeshProUGUI style, IReadOnlyList<string> titles, Action<int> tapped)
        {
            root = DanSelectUi.Rect("PageTabs", canvas, 1920 - titles.Count * (Width + Gap), Top, titles.Count * (Width + Gap), Height);
            for (int i = 0; i < titles.Count; i++)
            {
                int index = i;
                var back = DanSelectUi.Box("Tab" + i, root, i * (Width + Gap), 0, Width, Height, new Color(0, 0, 0, 0.55f));
                var label = DanSelectUi.Text("Label", back.rectTransform, style, titles[i], 26, 0, 0, Width, Height);
                var hit = back.gameObject.AddComponent<PointerRelay>();
                hit.Clicked = () => tapped(index);
                backs.Add(back);
                labels.Add(label);
            }
        }

        public void Show(int current)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                bool on = i == current;
                labels[i].color = on ? Color.white : new Color(1, 1, 1, 0.55f);
                labels[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
                backs[i].color = on ? new Color(0.1f, 0.45f, 0.3f, 0.9f) : new Color(0, 0, 0, 0.55f);
            }
        }
    }

    public sealed class CodeEntry
    {
        public readonly RectTransform root;
        public readonly TMP_InputField field;
        public readonly TextMeshProUGUI title, message;
        readonly Action<string> submit;

        public CodeEntry(RectTransform canvas, TextMeshProUGUI style, Action<string> submit, Action cancel, Action keyConsumed)
        {
            this.submit = submit;
            root = DanSelectUi.Rect("CodeEntry", canvas, 0, 0, 1920, 1080);
            var scrim = DanSelectUi.Box("Scrim", root, 0, 0, 1920, 1080, new Color(0, 0, 0, 0.6f));
            scrim.gameObject.AddComponent<PointerRelay>().Clicked = cancel;
            var box = DanSelectUi.Box("Box", root, 560, 300, 800, 320, new Color(0.08f, 0.08f, 0.1f, 0.96f));
            title = DanSelectUi.Text("Title", box.rectTransform, style, "段位コードを入力", 36, 0, 24, 800, 50);
            // TMP's stock input field, restyled: six capitals, wide spacing, the scene's font.
            var resources = new TMP_DefaultControls.Resources();
            var fieldObject = TMP_DefaultControls.CreateInputField(resources);
            fieldObject.name = "Code";
            var rect = fieldObject.GetComponent<RectTransform>();
            rect.SetParent(box.rectTransform, false);
            DanSelectUi.Place(rect, 160, 100, 480, 80);
            field = fieldObject.GetComponent<TMP_InputField>();
            field.characterLimit = 6;
            field.contentType = TMP_InputField.ContentType.Alphanumeric;
            field.onValueChanged.AddListener(v => { string up = v.ToUpperInvariant(); if (up != v) field.text = up; });
            field.onSubmit.AddListener(v => submit(v));
            field.onEndEdit.AddListener(_ => keyConsumed());
            var image = fieldObject.GetComponent<Image>();
            image.color = new Color(1, 1, 1, 0.95f);
            foreach (var text in fieldObject.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.font = style.font;
                text.fontSharedMaterial = style.fontSharedMaterial;
                text.fontSize = 40;
                text.characterSpacing = 20;
                text.alignment = TextAlignmentOptions.Center;
                text.color = text == field.textComponent ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.5f, 0.5f, 0.5f);
            }
            if (field.placeholder is TextMeshProUGUI placeholder) placeholder.text = "ABC123";
            message = DanSelectUi.Text("Message", box.rectTransform, style, "", 24, 0, 200, 800, 40);
            var hint = DanSelectUi.Text("Hint", box.rectTransform, style, "Enter で決定　Esc で戻る", 22, 0, 250, 800, 40);
            hint.color = new Color(1, 1, 1, 0.6f);
            root.gameObject.SetActive(false);
        }

        public void Open(string pageTitle)
        {
            title.text = pageTitle + " の段位コードを入力";
            field.text = "";
            field.interactable = true;
            message.text = "";
            root.gameObject.SetActive(true);
            field.ActivateInputField();
        }

        public void Close()
        {
            field.DeactivateInputField();
            root.gameObject.SetActive(false);
        }

        public void Say(string text) => message.text = text;
    }

    static class DanSelectUi
    {
        // A top-left anchored rectangle at (x, y) of the design's 1920×1080.
        public static RectTransform Rect(string name, RectTransform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, x, y, width, height);
            return rect;
        }

        public static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        public static Image Box(string name, RectTransform parent, float x, float y, float width, float height, Color color)
        {
            var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static TextMeshProUGUI Text(string name, RectTransform parent, TextMeshProUGUI style, string text, float size, float x, float y, float width, float height)
        {
            var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = style.font;
            label.fontSharedMaterial = style.fontSharedMaterial;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }
    }
}
