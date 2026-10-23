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

    // The 段位 plates' lettering. The plaque art (yellow_box/frame, loading_dan/plaque, 280×560) is
    // opaque in x 24..257, y 34..524 (measured on the texture): its wood is centred 140.5 px in and
    // 279 px down, so a name centred there sits on the wood, not on the image's frame.
    public static class DanPlateText
    {
        public const float WoodCenterX = 140.5f, WoodCenterY = 279;
        // The select plate at (117, 171), the loading plaque at (1557, 40).
        public static readonly Vector2 SelectPlateCenter = new Vector2(117 + WoodCenterX, 171 + WoodCenterY);
        public static readonly Vector2 LoadingPlateCenter = new Vector2(1557 + WoodCenterX, 40 + WoodCenterY);
        public const float PlateWidth = 200, PlateHeight = 440, PlateFontMax = 96, PlateFontMin = 36, PlateLineSpacing = -65;
        // The arcade's chip shows two ~40 px characters stacked with next to no gap (lines 42 px
        // apart) around y 115 of the 184 px folder. This font's line is 1.67 em, so the spacing
        // (1/100 em units) pulls the second line up by 0.88 em.
        public const float ChipFontSize = 54, ChipLineSpacing = -88, ChipTextTop = 72, ChipTextHeight = 112;

        // A name written top to bottom in the plate, as large as fits its height.
        public static void StylePlate(TextMeshProUGUI text, Vector2 center)
        {
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(center.x, -center.y);
            rect.sizeDelta = new Vector2(PlateWidth, PlateHeight);
            text.enableAutoSizing = true;
            text.fontSizeMin = PlateFontMin;
            text.fontSizeMax = PlateFontMax;
            text.lineSpacing = PlateLineSpacing;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        // A chip's two characters, big and close together, in the lower part of the folder.
        public static void StyleChip(TextMeshProUGUI text)
        {
            var rect = text.rectTransform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -(ChipTextTop + ChipTextHeight / 2));
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, ChipTextHeight);
            text.fontSize = ChipFontSize;
            text.lineSpacing = ChipLineSpacing;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
        }
    }

    // The 自己ベスト lines under the exam conditions (dan_select "自己ベスト" rows): a small label
    // at the left and the best value at the right, each a child of its condition's caption and
    // as wide as it, so they line up with the caption however the board is laid out.
    public sealed class ExamBestLines
    {
        public const string Label = "自己ベスト";
        const float LineHeight = 30, LabelSize = 20, ValueSize = 26, Below = 42, LabelInset = 12;
        // The gauge box art (exam_gauge_box) carries a white pill at its foot for this line (its
        // rows 735..762 on screen, the caption's top at 666): the line sits on the pill.
        const float GaugeBelow = 67;
        public readonly TextMeshProUGUI gaugeLabel, gaugeValue;
        public readonly TextMeshProUGUI[][] labels, values;

        public ExamBestLines(DanSelectView view)
        {
            gaugeLabel = Make("BestLabel", view.gaugeCaption, Label, LabelSize, TextAlignmentOptions.Left, GaugeBelow);
            gaugeValue = Make("BestValue", view.gaugeCaption, "", ValueSize, TextAlignmentOptions.Right, GaugeBelow);
            labels = new TextMeshProUGUI[view.examRows.Length][];
            values = new TextMeshProUGUI[view.examRows.Length][];
            for (int i = 0; i < view.examRows.Length; i++)
            {
                var row = view.examRows[i];
                labels[i] = new TextMeshProUGUI[row.captions.Length];
                values[i] = new TextMeshProUGUI[row.captions.Length];
                for (int k = 0; k < row.captions.Length; k++)
                {
                    labels[i][k] = Make("BestLabel", row.captions[k], Label, LabelSize, TextAlignmentOptions.Left);
                    values[i][k] = Make("BestValue", row.captions[k], "", ValueSize, TextAlignmentOptions.Right);
                }
            }
        }

        // A line Below the caption's top, spanning the caption's width (its own text is right-aligned,
        // so the value's right edge is the caption's).
        static TextMeshProUGUI Make(string name, TextMeshProUGUI caption, string text, float size, TextAlignmentOptions alignment, float below = Below)
        {
            var label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            var rect = label.rectTransform;
            rect.SetParent(caption.rectTransform, false);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -below);
            rect.sizeDelta = new Vector2(0, LineHeight);
            label.font = caption.font;
            label.fontSharedMaterial = caption.fontSharedMaterial;
            label.fontSize = size;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.color = caption.color;
            label.outlineWidth = caption.outlineWidth;
            label.outlineColor = caption.outlineColor;
            label.margin = alignment == TextAlignmentOptions.Left ? new Vector4(LabelInset, 0, 0, 0) : Vector4.zero;
            label.text = text;
            return label;
        }

        public void ShowGauge(string value)
        {
            gaugeValue.text = value ?? "";
            gaugeLabel.gameObject.SetActive(value != null);
            gaugeValue.gameObject.SetActive(value != null);
        }

        // value null hides the segment's line.
        public void ShowRow(int row, int segment, string value)
        {
            labels[row][segment].gameObject.SetActive(value != null);
            values[row][segment].gameObject.SetActive(value != null);
            values[row][segment].text = value ?? "";
        }
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
