using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OurTaiko.Multi;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The drawing and the focus of the screens made of plates: マルチ演奏's room (MultiRoomScene) and
    // ゴーストバトル's list (GhostSelectScene). A screen is drawn in code on the 1920x1080 stage, in
    // skin pixels from the top left, from the MultiArt sprites and the one UI font; it is drawn again
    // whenever what it shows changes (Begin, the screen's own drawing, Settle).
    //
    // What the player can press is a control. One control has the focus: ka moves it, don presses
    // it, and a tap or click focuses and presses a control directly. Across a redraw the focus stays
    // on the control it was on when the player has moved it in this context (the screen's view and
    // state); until then it is on the control the screen prefers there.
    public sealed class PlateUi
    {
        public static readonly Color Ink = new Color(0.1f, 0.08f, 0.16f);
        public static readonly Color Orange = new Color(1f, 0.62f, 0.2f), Teal = new Color(0.3f, 0.82f, 0.74f), Violet = new Color(0.74f, 0.6f, 0.96f),
            Slate = new Color(0.72f, 0.78f, 0.88f), Rose = new Color(1f, 0.52f, 0.5f), Paper = new Color(1f, 0.98f, 0.93f), Dim = new Color(0.55f, 0.55f, 0.6f),
            Gold = new Color(1f, 0.9f, 0.5f), Highlight = new Color(1f, 0.9f, 0.45f, 0.9f);

        // One thing the player can press.
        public sealed class Control
        {
            public string Key = "";
            public RectTransform Root;
            public Image Ring;
            public Action Press;
        }

        public readonly MultiArt Art;
        // What the screen now shows; replaced by Begin.
        public RectTransform Content { get; private set; }
        public IReadOnlyList<Control> Controls => controls;
        public Control Focused => focus >= 0 && focus < controls.Count ? controls[focus] : null;
        // The texts on screen, for tests.
        public string AllText => Content == null ? "" : string.Join("\n", Content.GetComponentsInChildren<TextMeshProUGUI>().Select(t => t.text));

        readonly RectTransform stage;
        readonly Action<string> tapped;
        readonly List<Control> controls = new List<Control>();
        int focus;
        string focusKey = "", focusContext = "";
        bool focusMoved;

        // tapped: a control was tapped or clicked (its key); the screen focuses and presses it.
        public PlateUi(RectTransform stage, MultiArt art, Action<string> tapped)
        {
            this.stage = stage; Art = art; this.tapped = tapped;
        }

        // A new drawing: the last one goes (at the end of the frame) with its controls.
        public void Begin()
        {
            if (Content != null) UnityEngine.Object.Destroy(Content.gameObject);
            Content = SkinUi.Rect("Content", stage, 1920, 1080);
            Content.TopLeft(0, 0);
            controls.Clear();
        }

        // The drawing is done: the focus settles (see the class comment).
        public void Settle(string context, string preferred)
        {
            if (context != focusContext) { focusContext = context; focusMoved = false; }
            int kept = controls.FindIndex(c => c.Key == focusKey), wanted = controls.FindIndex(c => c.Key == preferred);
            focus = Math.Max(0, focusMoved && kept >= 0 ? kept : wanted >= 0 ? wanted : kept);
            focusKey = Focused?.Key ?? "";
            ShowFocus();
        }

        // The focus goes to the next or the previous control, round the ends. False without controls.
        public bool Move(int delta)
        {
            if (controls.Count == 0) return false;
            focus = ((focus + delta) % controls.Count + controls.Count) % controls.Count;
            focusKey = controls[focus].Key;
            focusMoved = true;
            ShowFocus();
            return true;
        }

        // The focus goes to a control by its key. False when there is none.
        public bool Focus(string key)
        {
            int index = controls.FindIndex(c => c.Key == key);
            if (index < 0) return false;
            focus = index; focusKey = key;
            focusMoved = true;
            ShowFocus();
            return true;
        }

        void ShowFocus()
        {
            for (int i = 0; i < controls.Count; i++)
                if (controls[i].Ring != null) controls[i].Ring.enabled = i == focus;
        }

        // ---------------------------------------------------------------- pieces

        // align: -1 left of x, 0 centred on x, 1 right of x.
        public TextMeshProUGUI Label(Transform parent, string text, float size, float x, float y, float maxWidth = 0, bool dark = false, int align = 0)
        {
            var label = SkinUi.Text("Label", parent, size);
            if (dark) { label.color = Ink; label.UseUiFont(); }
            label.alignment = align < 0 ? TextAlignmentOptions.Left : align > 0 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
            label.rectTransform.pivot = new Vector2(align < 0 ? 0 : align > 0 ? 1 : 0.5f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(x, -y);
            label.text = text ?? "";
            if (maxWidth > 0) label.Squeeze(maxWidth);
            return label;
        }

        public Image Picture(Transform parent, Sprite sprite, float width, float height, float x, float y, Color? color = null)
        {
            var image = SkinUi.Image(sprite != null ? sprite.name : "Image", parent, sprite, width, height);
            image.rectTransform.Center(x, y);
            if (color.HasValue) image.color = color.Value;
            return image;
        }

        // A plate the player can press. Its children are placed from its top left corner.
        public RectTransform Button(string key, Transform parent, float width, float height, float x, float y, Color color, Action press, bool enabled = true)
        {
            var root = SkinUi.Rect(key, parent, width, height);
            root.Center(x, y);
            var plate = Picture(root, Art.plate, width, height, width / 2, height / 2, enabled ? color : Dim);
            var ring = Picture(root, Art.focus, width + 34, height + 34, width / 2, height / 2 - 2);
            ring.enabled = false;
            if (!enabled) return root;
            plate.raycastTarget = true;
            plate.gameObject.AddComponent<PointerRelay>().Clicked = () => tapped?.Invoke(key);
            controls.Add(new Control { Key = key, Root = root, Ring = ring, Press = press });
            return root;
        }

        public RectTransform TextButton(string key, Transform parent, float width, float height, float x, float y, Color color, string text, float size, Action press, bool enabled = true)
        {
            var root = Button(key, parent, width, height, x, y, color, press, enabled);
            Label(root, text, size, width / 2, height / 2 - 3, width - 36, true);
            return root;
        }

        // A player's disc: with an address, their account picture once it has come (MultiAvatars);
        // until then, and without one, their colour and the first letter of their name.
        public void Avatar(Transform parent, int slot, string nickname, float size, float x, float y, string pictureUrl = null)
        {
            var picture = MultiAvatars.Get(pictureUrl);
            Picture(parent, Art.disc, size, size, x, y, picture != null ? Color.white : MultiArt.SlotColor(slot));
            if (picture != null) { Picture(parent, picture, size * 0.84f, size * 0.84f, x, y); return; }
            string name = MultiText.Name(nickname);
            Label(parent, StringInfo.GetNextTextElement(name).ToUpperInvariant(), size * 0.5f, x, y - size * 0.03f);
        }

        // A course as the skin's own icon with its stars beside it; a course the skin has no icon
        // for keeps its name.
        public void CourseIcon(Transform parent, string course, int level, float height, float x, float y)
        {
            var icon = Art.CourseIcon(course);
            if (icon == null) { CoursePill(parent, course, level, 210, Math.Min(54, height), x, y); return; }
            float width = height * icon.rect.width / icon.rect.height;
            Picture(parent, icon, width, height, x - 34, y);
            if (level > 0) Label(parent, "★" + level, height * 0.44f, x + width / 2 - 26, y - 2, 0, false, -1);
        }

        // The result screen's own labels, each with its count, one after another from x.
        public void Counts(Transform parent, float x, float y, float height, float gap, params (Sprite Label, int Count)[] counts)
        {
            foreach (var (label, count) in counts)
            {
                if (label != null)
                {
                    float width = height * label.rect.width / label.rect.height;
                    Picture(parent, label, width, height, x + width / 2, y);
                    x += width + 6;
                }
                var number = Label(parent, count.ToString(CultureInfo.InvariantCulture), height * 0.84f, x, y - 2, 0, false, -1);
                x += number.preferredWidth + gap;
            }
        }

        // A course's name and stars on its colour.
        public void CoursePill(Transform parent, string course, int level, float width, float height, float x, float y)
        {
            Picture(parent, Art.plate, width, height, x, y, MultiArt.CourseColor(course));
            string text = MultiText.Course(course) + (level > 0 ? " ★" + level : "");
            Label(parent, text, height * 0.46f, x, y - 3, width - 30);
        }

        public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
