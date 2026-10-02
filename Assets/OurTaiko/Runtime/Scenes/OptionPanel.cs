using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    [Serializable]
    public sealed class OptionPanelArt
    {
        public Sprite board, player, row, rowHighlight, box, arrow;
        public Sprite auto, doron, abekobe, kimagure, detarame;
        [Tooltip("mod_speed_x1_1 .. mod_speed_x4, in PlayOptions.SpeedBadgeValues order.")]
        public Sprite[] speed;
        public TextAsset cursorTimeline;
        public HitSoundLibrary hitSounds;
        public AudioClip voice;
    }

    // Nijiiro's 演奏オプション board (song_select.lua draw_option_board, which replaces ModifierSelector::draw):
    // modifier/top at (5, 532) when open, rows every 61 px from y 618, name at x 44, value centred at x 300.
    // Off-default values get a yellow box, the cursor row pulses (anim/option_cursor), the pressed
    // arrow nudges 5 px outward and the greyed row gets a flat grey plate and a 50 % black scrim.
    public sealed class OptionPanel
    {
        const float RestTop = 532, BoardX = 5, RowTop = 86, RowPitch = 61, NameX = 44, ValueX = 300;
        const float RowX = 31, BoxX = 208, IconX = 165, ArrowX = 214, ArrowSpan = 140, Nudge = 5;
        const double ChangeMs = 250;
        static readonly string[] Labels = { "オート", "はやさ", "ドロン", "あべこべ", "ランダム", "演奏スキップ", "音色" };
        static readonly Color Grey = new Color32(166, 168, 171, 255), ChangedBox = new Color32(255, 255, 0, 255), Scrim = new Color32(0, 0, 0, 128);

        sealed class RowView
        {
            public Image Row, Highlight, Box, Icon, LeftArrow, RightArrow, Scrim;
            public TextMeshProUGUI Name, Value;
        }

        readonly OptionPanelArt art;
        readonly RectTransform root, board;
        readonly RowView[] rows = new RowView[OptionMenu.Rows.Length];
        readonly LumenClip cursorClip;
        double openedAt, closedAt = -1, changedAt = -1;
        int changeDirection;

        public OptionMenu Menu { get; private set; }
        public bool IsOpen => Menu != null;
        public bool IsClosing => closedAt >= 0;
        // Raised by touch: (row, direction) with direction -1/+1 for the value arrows, 0 for the row itself.
        public event Action<int, int> RowTapped;
        public event Action OutsideTapped;

        public OptionPanel(RectTransform parent, OptionPanelArt art, TMP_FontAsset font, Material outline)
        {
            this.art = art;
            cursorClip = art.cursorTimeline != null ? LumenClip.Parse(art.cursorTimeline.text) : LumenClip.Empty;
            root = SkinUi.Rect("OptionPanel", parent);
            // Everything outside the board closes the panel, and keeps the course cards from taking taps.
            var outside = SkinUi.Image("Outside", root, null, 1920, 1080);
            outside.rectTransform.TopLeft(0, 0);
            outside.color = Color.clear;
            outside.raycastTarget = true;
            outside.gameObject.AddComponent<PointerRelay>().Clicked = () => OutsideTapped?.Invoke();
            board = SkinUi.Rect("Board", root);
            var top = SkinUi.Image("Top", board, art.board);
            top.rectTransform.TopLeft(BoardX, 0);
            top.raycastTarget = true;
            SkinUi.Image("Player", board, art.player).rectTransform.TopLeft(32, 17);
            // song_select.lua draw_modifier_title: font 32, border 4, centred on the header.
            var title = SkinUi.Text("Title", board, font, outline, 32, new Color32(0, 0, 0, 255), 0.3f);
            title.text = "演奏オプション";
            title.rectTransform.Center(215, 49);
            for (int i = 0; i < rows.Length; i++) rows[i] = CreateRow(i, font, outline);
            root.gameObject.SetActive(false);
        }

        RowView CreateRow(int index, TMP_FontAsset font, Material outline)
        {
            float y = RowTop + index * RowPitch;
            var view = new RowView();
            view.Row = SkinUi.Image("Row" + index, board, art.row);
            view.Row.rectTransform.TopLeft(RowX, y);
            view.Highlight = SkinUi.Image("Highlight", board, art.rowHighlight);
            view.Highlight.rectTransform.TopLeft(RowX, y);
            view.Box = SkinUi.Image("Box", board, art.box);
            view.Box.rectTransform.TopLeft(BoxX, y + 8);
            // modifier_text: font 26, white with a 3 px black edge.
            view.Name = SkinUi.Text("Name", board, font, outline, 26, new Color32(0, 0, 0, 255), 0.3f);
            view.Name.alignment = TextAlignmentOptions.Left;
            view.Name.rectTransform.pivot = new Vector2(0, 0.5f);
            view.Name.rectTransform.anchoredPosition = new Vector2(NameX, -(y + 28));
            view.Name.text = Labels[index];
            view.Value = SkinUi.Text("Value", board, font, outline, 26, new Color32(0, 0, 0, 255), 0.3f);
            view.Value.rectTransform.Center(ValueX, y + 28);
            view.Icon = SkinUi.Image("Icon", board, null, 40, 40);
            view.Icon.rectTransform.TopLeft(IconX, y + 8);
            view.LeftArrow = SkinUi.Image("LeftArrow", board, art.arrow);
            view.RightArrow = SkinUi.Image("RightArrow", board, art.arrow);
            view.RightArrow.rectTransform.localScale = new Vector3(-1, 1, 1);
            if (OptionMenu.IsGreyed(OptionMenu.Rows[index]))
            {
                view.Scrim = SkinUi.Image("Scrim", board, art.row);
                view.Scrim.rectTransform.TopLeft(RowX, y);
                view.Scrim.color = Scrim;
            }
            // Touch: the name selects the row (or advances, like don, when already on it); the value
            // box halves turn the value like the two ka rims.
            Zone(index, 0, "Select", RowX, y, BoxX - RowX);
            Zone(index, -1, "Previous", BoxX, y, ValueX - BoxX);
            Zone(index, +1, "Next", ValueX, y, RowX + art.row.rect.width - ValueX);
            return view;
        }

        void Zone(int row, int direction, string name, float x, float y, float width)
        {
            var zone = SkinUi.Image(name + row, board, null, width, RowPitch);
            zone.rectTransform.TopLeft(x, y);
            zone.color = Color.clear;
            zone.raycastTarget = true;
            zone.gameObject.AddComponent<PointerRelay>().Clicked = () => RowTapped?.Invoke(row, direction);
        }

        public void Open(PlayOptions options, double now)
        {
            Menu = new OptionMenu(options, art.hitSounds != null ? art.hitSounds.Count : 0);
            openedAt = now; closedAt = -1; changedAt = -1;
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
        }

        // Starts the slide out once the menu is confirmed.
        public void Close(double now)
        {
            if (Menu == null || closedAt >= 0) return;
            Menu.ConfirmAll();
            closedAt = now;
        }

        public void Changed(int direction, double now)
        {
            changeDirection = direction;
            changedAt = now;
        }

        // Returns true on the frame the slide out ends; the panel is hidden from then on.
        public bool Draw(double now)
        {
            if (Menu == null) return false;
            if (Menu.IsConfirmed && closedAt < 0) closedAt = now;
            double top = closedAt >= 0
                ? RestTop + OptionMenu.SlideOut(now - closedAt)
                : 1080 - OptionMenu.SlideIn(now - openedAt);
            board.anchoredPosition = new Vector2(0, -(float)top);
            if (closedAt >= 0 && now - closedAt >= OptionMenu.SlideMs)
            {
                Menu = null;
                root.gameObject.SetActive(false);
                return true;
            }
            double pulse = cursorClip.IsEmpty ? 1
                : cursorClip.Get("#3@0", now * 0.06 % Math.Max(1, cursorClip.Last - cursorClip.First + 1), "a", 1);
            double change = changedAt < 0 ? 0 : Math.Min(1, (now - changedAt) / ChangeMs);
            float nudge = changedAt < 0 || change >= 1 ? 0 : Nudge * (float)(1 - change * (2 - change));
            for (int i = 0; i < rows.Length; i++) DrawRow(i, pulse, nudge);
            return false;
        }

        void DrawRow(int index, double pulse, float nudge)
        {
            var view = rows[index];
            var row = OptionMenu.Rows[index];
            bool greyed = OptionMenu.IsGreyed(row);
            bool current = !Menu.IsConfirmed && Menu.Index == index;
            float y = RowTop + index * RowPitch;
            view.Highlight.enabled = greyed || current;
            view.Highlight.color = greyed ? Grey : new Color(1, 1, 1, (float)pulse);
            view.Box.color = Menu.IsChanged(row) ? ChangedBox : Color.white;
            view.Value.text = Value(row);
            var icon = Icon(row);
            view.Icon.enabled = icon != null;
            view.Icon.sprite = icon;
            view.LeftArrow.enabled = view.RightArrow.enabled = current;
            view.LeftArrow.rectTransform.TopLeft(ArrowX - (changeDirection < 0 ? nudge : 0), y + 12);
            // Mirrored about its centre: the right arrow's left edge sits at ArrowX + ArrowSpan.
            view.RightArrow.rectTransform.TopLeft(ArrowX + ArrowSpan + (changeDirection > 0 ? nudge : 0), y + 12);
        }

        string Value(OptionRow row)
        {
            var options = Menu.Options;
            switch (row)
            {
                case OptionRow.Speed: return (options.speed / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                case OptionRow.Random: return options.random == RandomMode.Kimagure ? "きまぐれ" : options.random == RandomMode.Detarame ? "でたらめ" : "しない";
                case OptionRow.Neiro:
                    int slot = Menu.NeiroSlot;
                    return art.hitSounds != null && slot < art.hitSounds.Count ? art.hitSounds.names[slot] : "無音";
                case OptionRow.Skip: return "しない";
                default: return Menu.IsChanged(row) ? "する" : "しない";
            }
        }

        Sprite Icon(OptionRow row)
        {
            var options = Menu.Options;
            switch (row)
            {
                case OptionRow.Speed:
                    int badge = PlayOptions.SpeedBadge(options.speed);
                    return badge >= 0 && art.speed != null && badge < art.speed.Length ? art.speed[badge] : null;
                case OptionRow.Random:
                    return options.random == RandomMode.Detarame ? art.detarame : options.random == RandomMode.Kimagure ? art.kimagure : null;
                case OptionRow.Auto: return options.auto ? art.auto : null;
                case OptionRow.Display: return options.display ? art.doron : null;
                case OptionRow.Inverse: return options.inverse ? art.abekobe : null;
                default: return null;
            }
        }
    }
}
