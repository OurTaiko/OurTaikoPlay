using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OurTaiko
{
    // The settings overlay's hierarchy, built in code over whatever scene is open (Steam overlay
    // layout): a dimmed backdrop, a panel with the header, the category rail on the left, the
    // page's rows on the right and the footer with the focused row's hint and the drum keys. List
    // and key rows open a side sheet; text rows and key capture open a card. One focus ring follows
    // the focused rail entry, row or sheet option, snaps there quickly and pulses (Switch feel);
    // it bumps at either end of a list. Sizes are design pixels of the 1920x1080 canvas, chosen so a
    // row stays at least 44 pt tall on a phone.
    public sealed class SettingsOverlayView : MonoBehaviour
    {
        public static readonly Color Ink = Hex(0xF4F7FB), Muted = Hex(0x9FB0CC), Section = Hex(0x8496B6), Dark = Hex(0x0E1626),
            Cyan = Hex(0x4FD8EB), Amber = Hex(0xFFC46B), Don = Hex(0xF2482E), Seat2 = Hex(0x2F86E0), Button = Hex(0x26324D),
            RowColor = new Color(1, 1, 1, 0.035f), RowFocus = new Color(0.31f, 0.85f, 0.92f, 0.10f), Selected = Hex(0x22304D),
            TrackOff = Hex(0x3A4763), TrackOn = Hex(0x2FA9BE), Groove = Hex(0x1D2840), SliderTrack = Hex(0x2A3754);

        const float Inset = 30, HeaderHeight = 108, FooterHeight = 90, RailWidth = 435, RailItem = 102, RowHeight = 120,
            RowGap = 12, GroupGap = 39, WrappedSpacing = -32, SectionHeight = 48, PagePad = 48, SheetWidth = 810, SheetOption = 126, DescWidth = 760;

        // Taps and drags, wired by SettingsOverlay.
        public Action<int> TapPage, TapRow, OpenRow, TapSheet;
        public Action<int, int> SetRow;
        public Action TapShade;
        public Action<string> SubmitText;
        public Action CancelText;

        CanvasGroup group;
        RectTransform panel, rail, viewport, list, sheet, modal, ring, glow;
        Image shade, ringImage, glowImage, sheetShade, modalShade;
        TextMeshProUGUI breadcrumb, footerText, donHint, modalTitle, modalText, sheetTitle, sheetText;
        readonly TextMeshProUGUI[] seatNames = new TextMeshProUGUI[2];
        readonly Image[] seatDots = new Image[2];
        RectTransform hints, sheetViewport, sheetList, card;
        float sheetBaseY, sheetScroll, sheetScrollTarget;
        TMP_InputField field;
        readonly List<(RectTransform root, Image box, TextMeshProUGUI label)> railItems = new();
        readonly List<RowView> rows = new();
        readonly List<(RectTransform root, Image box, Image radio, Image dot)> sheetItems = new();
        TextMeshProUGUI pageTitle, pageSubtitle, banner;
        Image bannerBox;
        int shownPage = -1, shownSheetRow = -1;
        string rowsSignature;
        float scroll, scrollTarget, listHeight, focusTop, focusBottom, sheetOffset = 1, openAt, bumpAt = -10, bumpDirection;
        bool following = true, ringPlaced, closing;
        Rect ringRect;
        SettingsZone lastZone;
        int lastFocus = -1;

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }

        public bool IsShown => group != null && group.alpha > 0.001f;
        // The card's text field for typed rows.
        public TMP_InputField Field => field;

        // Builds the canvas and its hierarchy under this object.
        public void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over every scene, under SceneSwitcher's transition cover (32767).
            canvas.sortingOrder = 32000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            group.blocksRaycasts = false;

            var root = (RectTransform)transform;
            gameObject.layer = LayerMask.NameToLayer("UI");
            shade = Full("Shade", root, new Color(0.027f, 0.047f, 0.094f, 0.62f));
            shade.raycastTarget = true;

            panel = Node("Panel", root);
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.pivot = new Vector2(0.5f, 0.5f);
            var panelBox = SettingsShapes.Box("Box", panel, new Color(0.051f, 0.078f, 0.141f, 0.94f), 36);
            Stretch(panelBox.rectTransform, 0, 0, 0, 0);
            panelBox.raycastTarget = true;

            BuildHeader();
            BuildRail();
            BuildPage();
            BuildFooter();
            BuildSheet();

            glow = SettingsShapes.Box("FocusGlow", panel, Alpha(Cyan, 0.25f), 30, true).rectTransform;
            glowImage = glow.GetComponent<Image>();
            ring = SettingsShapes.Box("Focus", panel, Cyan, 30, true).rectTransform;
            ringImage = ring.GetComponent<Image>();
            foreach (var r in new[] { ring, glow }) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); }

            BuildModal();
            foreach (var child in GetComponentsInChildren<Transform>(true)) child.gameObject.layer = gameObject.layer;
            gameObject.SetActive(false);
        }

        void BuildHeader()
        {
            var header = Node("Header", panel);
            Stretch(header, 0, 0, 0, -1); header.anchorMin = new Vector2(0, 1); header.sizeDelta = new Vector2(0, HeaderHeight);
            header.anchoredPosition = new Vector2(0, -HeaderHeight / 2);
            var title = Label("Title", header, "Settings", 36, Ink, TextAlignmentOptions.MidlineLeft);
            Pin(title.rectTransform, 36, HeaderHeight / 2, left: true, width: 220);
            breadcrumb = Label("Breadcrumb", header, "", 27, Section, TextAlignmentOptions.MidlineLeft);
            Pin(breadcrumb.rectTransform, 36 + title.preferredWidth + 21, HeaderHeight / 2, left: true, width: 700);
            for (int seat = 1; seat >= 0; seat--)
            {
                var chip = SettingsShapes.Box("Seat" + (seat + 1), header, new Color(1, 1, 1, 0.06f), 36);
                chip.rectTransform.sizeDelta = new Vector2(330, 72);
                Pin(chip.rectTransform, 36 + (1 - seat) * 348, HeaderHeight / 2, left: false);
                seatDots[seat] = SettingsShapes.Dot("Avatar", chip.transform, seat == 0 ? Don : Seat2, 51);
                Pin(seatDots[seat].rectTransform, 10 + 25.5f, 36, left: true);
                var badge = Label("Seat", seatDots[seat].transform, (seat + 1) + "P", 21, Ink, TextAlignmentOptions.Center);
                Center(badge.rectTransform, 51, 51);
                seatNames[seat] = Label("Name", chip.transform, "", 25, Ink, TextAlignmentOptions.MidlineLeft);
                seatNames[seat].richText = false;
                seatNames[seat].overflowMode = TextOverflowModes.Ellipsis;
                Pin(seatNames[seat].rectTransform, 76, 36, left: true, width: 238);
            }
            Divider(header, false);
        }

        void BuildRail()
        {
            rail = Node("Rail", panel);
            rail.anchorMin = new Vector2(0, 0); rail.anchorMax = new Vector2(0, 1); rail.pivot = new Vector2(0, 1);
            rail.offsetMin = new Vector2(0, FooterHeight); rail.offsetMax = new Vector2(RailWidth, -HeaderHeight);
            var back = SettingsShapes.Box("Back", rail, new Color(0.03f, 0.05f, 0.1f, 0.45f), 0);
            back.sprite = null;
            Stretch(back.rectTransform, 0, 0, 0, 0);
            var edge = SkinUi.Image("Edge", rail, null, 1, 0);
            edge.color = new Color(1, 1, 1, 0.07f);
            edge.rectTransform.anchorMin = new Vector2(1, 0); edge.rectTransform.anchorMax = Vector2.one;
            edge.rectTransform.sizeDelta = new Vector2(1, 0); edge.rectTransform.anchoredPosition = Vector2.zero;
        }

        void BuildPage()
        {
            viewport = Node("Page", panel);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.pivot = new Vector2(0.5f, 1);
            viewport.offsetMin = new Vector2(RailWidth, FooterHeight); viewport.offsetMax = new Vector2(0, -HeaderHeight);
            viewport.gameObject.AddComponent<RectMask2D>().padding = new Vector4(0, 0, 0, 0);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var drag = viewport.gameObject.AddComponent<SettingsScroll>();
            drag.Scrolled = delta => { following = false; scroll = scrollTarget = ClampScroll(scroll + delta); };
            list = Node("List", viewport);
            list.anchorMin = new Vector2(0, 1); list.anchorMax = Vector2.one; list.pivot = new Vector2(0.5f, 1);
            list.offsetMin = new Vector2(PagePad, 0); list.offsetMax = new Vector2(-PagePad, 0);
        }

        void BuildFooter()
        {
            var footer = Node("Footer", panel);
            footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.pivot = new Vector2(0.5f, 0);
            footer.sizeDelta = new Vector2(0, FooterHeight); footer.anchoredPosition = Vector2.zero;
            var back = SkinUi.Image("Back", footer, null, 0, 0);
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.55f);
            Stretch(back.rectTransform, 0, 0, 0, 0);
            Divider(footer, true);
            footerText = Label("Hint", footer, "", 27, Muted, TextAlignmentOptions.MidlineLeft);
            footerText.overflowMode = TextOverflowModes.Ellipsis;
            footerText.rectTransform.anchorMin = new Vector2(0, 0.5f); footerText.rectTransform.anchorMax = new Vector2(1, 0.5f);
            footerText.rectTransform.offsetMin = new Vector2(36, -27); footerText.rectTransform.offsetMax = new Vector2(-760, 27);

            hints = Node("Keys", footer);
            hints.anchorMin = hints.anchorMax = new Vector2(1, 0.5f); hints.pivot = new Vector2(1, 0.5f);
            hints.sizeDelta = new Vector2(720, FooterHeight); hints.anchoredPosition = new Vector2(-36, 0);
            float x = 720;
            x = Hint(x, "Back", out _, () => Keycap(hints, "Esc"));
            x = Hint(x, "Select", out donHint, () => SettingsShapes.Dot("Don", hints, Don, 33).rectTransform);
            Hint(x, "Move", out _, () => SettingsShapes.Dot("Ka", hints, Cyan, 33, ring: true).rectTransform);
            // Phones and tablets are touched; their drums are on screen.
            hints.gameObject.SetActive(!Application.isMobilePlatform);
        }

        // Lays one footer key hint out leftwards from x; returns where the next one ends.
        float Hint(float x, string text, out TextMeshProUGUI label, Func<RectTransform> icon)
        {
            label = Label("Hint " + text, hints, text, 27, Ink, TextAlignmentOptions.MidlineLeft);
            label.rectTransform.sizeDelta = new Vector2(130, 45);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(x - 130, 0);
            var glyph = icon();
            glyph.anchorMin = glyph.anchorMax = new Vector2(0, 0.5f); glyph.pivot = new Vector2(1, 0.5f);
            glyph.anchoredPosition = new Vector2(x - 142, 0);
            return x - 154 - glyph.sizeDelta.x - 30;
        }

        RectTransform Keycap(Transform parent, string text)
        {
            var cap = SettingsShapes.Box("Key " + text, parent, Ink, 10);
            var label = Label("Text", cap.transform, text, 21, Dark, TextAlignmentOptions.Center);
            float width = Mathf.Max(48, label.GetPreferredValues(text).x + 24);
            cap.rectTransform.sizeDelta = new Vector2(width, 42);
            Center(label.rectTransform, width, 42);
            return cap.rectTransform;
        }

        void BuildSheet()
        {
            sheetShade = SkinUi.Image("SheetShade", panel, null, 0, 0);
            sheetShade.color = new Color(0.02f, 0.035f, 0.07f, 0.6f);
            sheetShade.rectTransform.anchorMin = Vector2.zero; sheetShade.rectTransform.anchorMax = Vector2.one;
            sheetShade.rectTransform.offsetMin = new Vector2(RailWidth, FooterHeight); sheetShade.rectTransform.offsetMax = new Vector2(0, -HeaderHeight);
            sheetShade.raycastTarget = true;
            sheetShade.gameObject.AddComponent<PointerRelay>().Clicked = () => TapShade?.Invoke();

            sheet = Node("Sheet", panel);
            sheet.anchorMin = new Vector2(1, 0); sheet.anchorMax = Vector2.one; sheet.pivot = new Vector2(1, 0.5f);
            sheet.offsetMin = new Vector2(-SheetWidth, FooterHeight); sheet.offsetMax = new Vector2(0, -HeaderHeight);
            var back = SkinUi.Image("Back", sheet, null, 0, 0);
            back.color = Hex(0x121B2F);
            back.raycastTarget = true;
            Stretch(back.rectTransform, 0, 0, 0, 0);
            sheetTitle = Label("Title", sheet, "", 45, Ink, TextAlignmentOptions.TopLeft);
            Top(sheetTitle.rectTransform, 36, 36, -36, 60);
            sheetText = Label("Description", sheet, "", 27, Muted, TextAlignmentOptions.TopLeft);
            sheetText.textWrappingMode = TextWrappingModes.Normal;
            sheetText.lineSpacing = WrappedSpacing;
            Top(sheetText.rectTransform, 36, 102, -36, 80);
            sheetViewport = Node("Options", sheet);
            sheetViewport.anchorMin = Vector2.zero; sheetViewport.anchorMax = Vector2.one; sheetViewport.pivot = new Vector2(0.5f, 1);
            sheetViewport.offsetMin = new Vector2(24, 0); sheetViewport.offsetMax = new Vector2(-24, -196);
            sheetViewport.gameObject.AddComponent<RectMask2D>();
            sheetList = Node("List", sheetViewport);
            Top(sheetList, 12, 12, -12, 0);
            sheetBaseY = sheet.anchoredPosition.y;
            sheetShade.gameObject.SetActive(false);
            sheet.gameObject.SetActive(false);
        }

        void BuildModal()
        {
            modal = Node("Modal", panel);
            Stretch(modal, 0, 0, 0, 0);
            modalShade = Full("Shade", modal, new Color(0.02f, 0.035f, 0.07f, 0.7f));
            modalShade.raycastTarget = true;
            modalShade.gameObject.AddComponent<PointerRelay>().Clicked = () => TapShade?.Invoke();
            var box = SettingsShapes.Box("Card", modal, Hex(0x16213A), 36);
            card = box.rectTransform;
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(1020, 420);
            box.raycastTarget = true;
            modalTitle = Label("Title", card, "", 45, Ink, TextAlignmentOptions.TopLeft);
            Top(modalTitle.rectTransform, 54, 48, -54, 60);
            modalText = Label("Text", card, "", 27, Muted, TextAlignmentOptions.TopLeft);
            modalText.textWrappingMode = TextWrappingModes.Normal;
            modalText.lineSpacing = WrappedSpacing;
            Top(modalText.rectTransform, 54, 300, -54, 90);
            BuildField();
            modal.gameObject.SetActive(false);
        }

        void BuildField()
        {
            var box = SettingsShapes.Box("Field", card, Hex(0x0D1424), 18);
            box.raycastTarget = true;
            Top(box.rectTransform, 54, 138, -54, 120);
            box.gameObject.SetActive(false);
            var area = Node("Text Area", box.transform);
            Stretch(area, 30, 12, 30, 12);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = Label("Placeholder", area, "", 39, Section, TextAlignmentOptions.MidlineLeft);
            Stretch(placeholder.rectTransform, 0, 0, 0, 0);
            var text = Label("Text", area, "", 39, Ink, TextAlignmentOptions.MidlineLeft);
            text.richText = false;
            Stretch(text.rectTransform, 0, 0, 0, 0);
            field = box.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.fontAsset = SkinUi.Font;
            field.pointSize = 39;
            field.richText = false;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.caretColor = Cyan;
            field.customCaretColor = true;
            field.caretWidth = 3;
            field.selectionColor = Alpha(Cyan, 0.35f);
            field.onSubmit.AddListener(value => SubmitText?.Invoke(value));
            field.onEndEdit.AddListener(_ => { if (modal.gameObject.activeSelf && field.gameObject.activeSelf) CancelText?.Invoke(); });
        }

        // -- Showing --------------------------------------------------------------------------

        public void Open()
        {
            gameObject.SetActive(true);
            closing = false;
            openAt = Time.unscaledTime;
            group.blocksRaycasts = true;
            ringPlaced = false;
            following = true;
            shownPage = -1;
        }

        public void Close()
        {
            closing = true;
            openAt = Time.unscaledTime;
            group.blocksRaycasts = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void Bump(int direction)
        {
            bumpAt = Time.unscaledTime;
            bumpDirection = direction;
        }

        // The seats' sign-in chips: a name, or null when not signed in.
        public void ShowSeats(string p1, string p2)
        {
            for (int seat = 0; seat < 2; seat++)
            {
                string name = seat == 0 ? p1 : p2;
                seatNames[seat].text = string.IsNullOrEmpty(name) ? "Not signed in" : name;
                Recolor(seatNames[seat], string.IsNullOrEmpty(name) ? Muted : Ink);
            }
        }

        // Draws the navigator's state. status replaces the footer hint; sound is the Sound page's
        // banner (null hides it), soundPending marks it as waiting for the overlay to close.
        public void Show(SettingsNavigator nav, string status, string sound, bool soundPending)
        {
            ShowRail(nav);
            if (nav.PageIndex != shownPage || Signature(nav) != rowsSignature) BuildRows(nav);
            breadcrumb.text = nav.Page != null ? "›  " + nav.Page.Title : "";
            if (banner != null)
            {
                bannerBox.gameObject.SetActive(sound != null && nav.Page?.Id == "sound");
                banner.text = sound ?? "";
                bannerBox.color = soundPending ? Alpha(Amber, 0.12f) : new Color(1, 1, 1, 0.05f);
                Recolor(banner, soundPending ? Amber : Ink);
            }
            for (int i = 0; i < rows.Count; i++) rows[i].Show(nav, i, this);
            Layout(nav);
            ShowSheet(nav);
            ShowModal(nav);

            footerText.text = !string.IsNullOrEmpty(status) ? status : FooterHint(nav);
            donHint.text = nav.Zone switch
            {
                SettingsZone.Rail => nav.OnClose ? "Close" : "Open",
                SettingsZone.Adjust => "Keep",
                SettingsZone.Sheet => "Choose",
                _ => nav.Row?.Kind switch
                {
                    SettingKind.Toggle => "Flip",
                    SettingKind.Choice or SettingKind.Number or SettingKind.Slider => "Adjust",
                    SettingKind.Action => "Run",
                    SettingKind.Text or SettingKind.Password => "Type",
                    _ => "Choose",
                },
            };
            int focus = nav.Zone == SettingsZone.Rail ? -1 - nav.PageIndex : nav.Zone == SettingsZone.Sheet ? 1000 + nav.SheetIndex : nav.RowIndex;
            if (focus != lastFocus || nav.Zone != lastZone) following = true;
            lastFocus = focus; lastZone = nav.Zone;
        }

        static string FooterHint(SettingsNavigator nav)
        {
            if (nav.Zone == SettingsZone.Rail) return nav.Page?.Subtitle ?? "Close Settings";
            if (nav.Zone == SettingsZone.Adjust) return nav.Row.Label + "  ·  Ka changes, Don keeps, Esc cancels";
            var row = nav.Row;
            if (row == null) return "";
            return row.Hint.Length > 0 ? row.Label + "  ·  " + row.Hint : row.Label;
        }

        void ShowRail(SettingsNavigator nav)
        {
            int count = nav.Pages.Count + 1;
            while (railItems.Count < count)
            {
                int index = railItems.Count;
                var box = SettingsShapes.Box("Category" + index, rail, Color.clear, 24);
                box.raycastTarget = true;
                box.gameObject.AddComponent<PointerRelay>().Clicked = () => TapPage?.Invoke(index);
                var label = Label("Label", box.transform, "", 33, Muted, TextAlignmentOptions.MidlineLeft);
                Stretch(label.rectTransform, 30, 0, 18, 0);
                railItems.Add((box.rectTransform, box, label));
            }
            for (int i = 0; i < railItems.Count; i++)
            {
                var (root, box, label) = railItems[i];
                bool close = i == nav.Pages.Count;
                root.anchorMin = new Vector2(0, close ? 0 : 1); root.anchorMax = new Vector2(1, close ? 0 : 1);
                root.pivot = new Vector2(0.5f, close ? 0 : 1);
                root.offsetMin = new Vector2(21, 0); root.offsetMax = new Vector2(-21, 0);
                root.sizeDelta = new Vector2(-42, RailItem);
                root.anchoredPosition = new Vector2(0, close ? 24 : -24 - i * (RailItem + 9));
                label.text = close ? "Close" : nav.Pages[i].Title;
                bool selected = i == nav.PageIndex;
                box.color = selected ? Selected : Color.clear;
                Recolor(label, selected ? Ink : Muted);
            }
        }

        // Rows are rebuilt when the page, its set of rows or a row's text changes, not for a value
        // change. A key row draws its keys as keycaps, so its keys are part of the signature (not
        // just how many: Reset to Default can swap one key for another).
        static string Signature(SettingsNavigator nav)
        {
            if (nav.Page == null) return "";
            var text = new System.Text.StringBuilder();
            foreach (var row in nav.Page.Rows)
            {
                text.Append(row.Id).Append('\n').Append(row.Description).Append('\n');
                if (row.Kind == SettingKind.Keys) text.Append(string.Join(" ", nav.Settings.general.keyboard.Get(row.Key))).Append('\n');
            }
            return text.ToString();
        }

        void BuildRows(SettingsNavigator nav)
        {
            bool samePage = nav.PageIndex == shownPage;
            shownPage = nav.PageIndex;
            rowsSignature = Signature(nav);
            foreach (Transform child in list) Destroy(child.gameObject);
            rows.Clear();
            banner = null;
            if (!samePage) scroll = scrollTarget = 0;
            if (nav.Page == null) { listHeight = 0; return; }

            pageTitle = Label("Title", list, nav.Page.Title, 54, Ink, TextAlignmentOptions.TopLeft);
            Top(pageTitle.rectTransform, 0, 36, 0, 66);
            pageSubtitle = Label("Subtitle", list, nav.Page.Subtitle, 27, Muted, TextAlignmentOptions.TopLeft);
            Top(pageSubtitle.rectTransform, 0, 102, 0, 40);
            if (nav.Page.Id == "sound")
            {
                bannerBox = SettingsShapes.Box("Output", list, new Color(1, 1, 1, 0.05f), 24);
                Top(bannerBox.rectTransform, 0, 168, 0, 84);
                banner = Label("Text", bannerBox.transform, "", 28, Ink, TextAlignmentOptions.MidlineLeft);
                banner.overflowMode = TextOverflowModes.Ellipsis;
                Stretch(banner.rectTransform, 30, 0, 30, 0);
            }
            int index = 0;
            foreach (var g in nav.Page.Groups)
            {
                var title = Label("Group " + g.Title, list, g.Title.ToUpperInvariant(), 24, Section, TextAlignmentOptions.BottomLeft);
                title.characterSpacing = 8;
                rows.Add(RowView.Section(title));
                foreach (var row in g.Rows) rows.Add(new RowView(this, list, row, index++, nav.Settings));
            }
        }

        // Stacks the page and keeps the focused row in view.
        void Layout(SettingsNavigator nav)
        {
            float y = banner != null && bannerBox.gameObject.activeSelf ? 282 : 168;
            bool first = true;
            focusTop = focusBottom = -1;
            foreach (var row in rows)
            {
                if (row.IsSection)
                {
                    if (!first) y += GroupGap - RowGap;
                    first = false;
                    Top(row.Root, 6, y, 0, SectionHeight);
                    y += SectionHeight + 14;
                    continue;
                }
                float height = row.Height;
                Top(row.Root, 0, y, 0, height);
                if (row.Index == nav.RowIndex && nav.Zone is SettingsZone.Rows or SettingsZone.Adjust) { focusTop = row.Index == 0 ? 0 : y; focusBottom = y + height; }
                y += height + RowGap;
            }
            listHeight = y + 36;
        }

        // Keeps the focused row inside the page with a margin, unless the page was scrolled by hand.
        void Follow()
        {
            if (following && focusBottom >= 0)
            {
                float view = viewport.rect.height, margin = 48;
                if (focusTop - scrollTarget < margin) scrollTarget = focusTop - margin;
                if (focusBottom - scrollTarget > view - margin) scrollTarget = focusBottom - view + margin;
            }
            scrollTarget = ClampScroll(scrollTarget);
        }

        float ClampScroll(float value) => Mathf.Clamp(value, 0, Mathf.Max(0, listHeight - viewport.rect.height));

        void ShowSheet(SettingsNavigator nav)
        {
            bool open = nav.Zone == SettingsZone.Sheet || nav.Zone == SettingsZone.Capture && nav.Row?.Kind == SettingKind.Keys;
            if (open)
            {
                sheet.gameObject.SetActive(true);
                sheetShade.gameObject.SetActive(true);
                var options = nav.SheetOptions;
                int signature = nav.RowIndex * 1000 + options.Count + nav.PageIndex * 100000;
                if (signature != shownSheetRow || sheetItems.Count != options.Count) { BuildSheetItems(nav, options); sheetScroll = sheetScrollTarget = 0; }
                shownSheetRow = signature;
                sheetTitle.text = nav.Row.Label;
                sheetText.text = nav.Row.Description + (nav.Row.AppliesOnClose ? "  Applies when you close Settings." : "");
                float textHeight = sheetText.GetPreferredValues(sheetText.text, SheetWidth - 72, 0).y;
                Top(sheetText.rectTransform, 36, 102, -36, textHeight + 4);
                sheetViewport.offsetMax = new Vector2(-24, -(102 + textHeight + 18));
                // Keep the focused option inside the sheet.
                float pitch = OptionHeight(nav) + 12, top = nav.SheetIndex * pitch, view = sheetViewport.rect.height - 24;
                if (top - sheetScrollTarget < 0) sheetScrollTarget = top;
                if (top + pitch - 12 - sheetScrollTarget > view) sheetScrollTarget = top + pitch - 12 - view;
                sheetScrollTarget = Mathf.Clamp(sheetScrollTarget, 0, Mathf.Max(0, options.Count * pitch - 12 - view));
                int current = nav.Row.Kind == SettingKind.List ? nav.Row.Get(nav.Settings) : -1;
                for (int i = 0; i < sheetItems.Count; i++)
                {
                    var (root, box, radio, dot) = sheetItems[i];
                    box.color = i == nav.SheetIndex ? RowFocus : RowColor;
                    if (radio != null) { radio.color = i == current ? Cyan : Section; dot.enabled = i == current; }
                }
            }
            else shownSheetRow = -1;
        }

        static float OptionHeight(SettingsNavigator nav) => nav.SheetNotes.Any(n => !string.IsNullOrEmpty(n)) ? SheetOption : 108;

        void BuildSheetItems(SettingsNavigator nav, IReadOnlyList<string> options)
        {
            float height = OptionHeight(nav);
            foreach (Transform child in sheetList) Destroy(child.gameObject);
            sheetItems.Clear();
            var notes = nav.SheetNotes;
            bool radios = nav.Row.Kind == SettingKind.List;
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var box = SettingsShapes.Box("Option" + i, sheetList, RowColor, 24);
                box.raycastTarget = true;
                box.gameObject.AddComponent<PointerRelay>().Clicked = () => TapSheet?.Invoke(index);
                Top(box.rectTransform, 0, i * (height + 12), 0, height);
                Image radio = null, dot = null;
                float x = 30;
                if (radios)
                {
                    radio = SettingsShapes.Dot("Radio", box.transform, Section, 42, ring: true);
                    Pin(radio.rectTransform, 30 + 21, height / 2, left: true);
                    dot = SettingsShapes.Dot("Dot", radio.transform, Cyan, 20);
                    dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    x = 102;
                }
                bool noted = i < notes.Count && !string.IsNullOrEmpty(notes[i]);
                var label = Label("Label", box.transform, options[i], 36, Ink, TextAlignmentOptions.MidlineLeft);
                Top(label.rectTransform, x, noted ? 18 : 0, -30, noted ? 54 : height);
                if (noted)
                {
                    var note = Label("Note", box.transform, notes[i], 25, Muted, TextAlignmentOptions.TopLeft);
                    Top(note.rectTransform, x, 72, -30, 40);
                }
                sheetItems.Add((box.rectTransform, box, radio, dot));
            }
        }

        void ShowModal(SettingsNavigator nav)
        {
            bool typing = nav.Zone == SettingsZone.Typing, capture = nav.Zone == SettingsZone.Capture;
            bool was = modal.gameObject.activeSelf;
            modal.gameObject.SetActive(typing || capture);
            field.gameObject.SetActive(typing);
            if (!typing && !capture) return;
            string group = nav.Page.Groups[nav.Page.GroupOf(nav.RowIndex)].Title;
            modalTitle.text = capture ? "Press a key for " + nav.Row.Label : (group.Length <= 2 ? group + "  ·  " : "") + nav.Row.Label;
            modalText.text = capture
                ? "Any key is added, including Enter and Escape. Tap outside this card to cancel."
                : nav.Row.Kind == SettingKind.Password ? "Enter signs in. The password is not saved.  Esc: cancel" : "Enter: save   Esc: cancel";
            Top(modalText.rectTransform, 54, capture ? 138 : 300, -54, 90);
            if (typing && !was)
            {
                field.contentType = nav.Row.Kind == SettingKind.Password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
                ((TextMeshProUGUI)field.placeholder).text = nav.Row.Kind == SettingKind.Password ? "Password" : "Username";
                field.SetTextWithoutNotify(nav.Row.GetText(nav.Settings));
                field.ForceLabelUpdate();
                field.Select();
                field.ActivateInputField();
                field.MoveTextEnd(false);
            }
        }

        // -- Animation ------------------------------------------------------------------------

        void Update()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            FitSafeArea();
            float t = Mathf.Clamp01((now - openAt) / (closing ? 0.12f : 0.18f));
            float eased = 1 - Mathf.Pow(1 - t, 3);
            group.alpha = closing ? 1 - eased : eased;
            panel.localScale = Vector3.one * (closing ? 1 - 0.015f * eased : 0.975f + 0.025f * eased);
            if (closing && t >= 1) { gameObject.SetActive(false); return; }

            Follow();
            float k = 1 - Mathf.Exp(-dt * 22);
            scroll = Mathf.Lerp(scroll, scrollTarget, k);
            list.anchoredPosition = new Vector2(0, scroll);

            bool sheetOpen = sheet.gameObject.activeSelf && shownSheetRow >= 0;
            sheetOffset = Mathf.Lerp(sheetOffset, sheetOpen ? 0 : 1, 1 - Mathf.Exp(-dt * 24));
            sheet.anchoredPosition = new Vector2(sheetOffset * (SheetWidth + 60), sheetBaseY);
            sheetScroll = Mathf.Lerp(sheetScroll, sheetScrollTarget, k);
            sheetList.anchoredPosition = new Vector2(sheetList.anchoredPosition.x, -12 + sheetScroll);
            sheetShade.color = new Color(0.02f, 0.035f, 0.07f, 0.6f * (1 - sheetOffset));
            if (!sheetOpen && sheetOffset > 0.99f) { sheet.gameObject.SetActive(false); sheetShade.gameObject.SetActive(false); }

            foreach (var row in rows) row.Animate(dt);
            PlaceRing(now, dt);
        }

        // The panel keeps clear of notches and rounded corners; phones get a thinner margin.
        void FitSafeArea()
        {
            var canvas = (RectTransform)transform;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            float sx = canvas.rect.width / Screen.width, sy = canvas.rect.height / Screen.height;
            float inset = Application.isMobilePlatform ? 12 : Inset;
            panel.offsetMin = new Vector2(safe.xMin * sx + inset, safe.yMin * sy + inset);
            panel.offsetMax = new Vector2(-(Screen.width - safe.xMax) * sx - inset, -(Screen.height - safe.yMax) * sy - inset);
        }

        void PlaceRing(float now, float dt)
        {
            var target = FocusTarget(out float radius);
            bool visible = target != null && !modal.gameObject.activeSelf;
            ring.gameObject.SetActive(visible);
            glow.gameObject.SetActive(visible);
            if (!visible) return;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = panel.InverseTransformPoint(corners[0]), max = panel.InverseTransformPoint(corners[2]);
            var goal = Rect.MinMaxRect(min.x - 6, min.y - 6, max.x + 6, max.y + 6);
            float k = ringPlaced ? 1 - Mathf.Exp(-dt * 30) : 1;
            ringPlaced = true;
            ringRect = new Rect(Vector2.Lerp(ringRect.position, goal.position, k), Vector2.Lerp(ringRect.size, goal.size, k));
            float since = now - bumpAt;
            float bump = since < 0.2f ? bumpDirection * 14 * Mathf.Sin(since / 0.2f * Mathf.PI) * (1 - since / 0.2f) : 0;
            var center = ringRect.center + new Vector2(0, -bump);
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2 / 1.3f);
            bool adjusting = lastZone == SettingsZone.Adjust;
            var color = adjusting ? Amber : Cyan;
            ringImage.color = Color.Lerp(color, Color.white, 0.35f * pulse);
            ring.anchoredPosition = center; ring.sizeDelta = ringRect.size;
            float spread = 6 + 9 * (1 - pulse);
            glow.anchoredPosition = center; glow.sizeDelta = ringRect.size + Vector2.one * spread * 2;
            glowImage.color = Alpha(color, 0.28f * (1 - pulse));
            float r = radius + 6;
            ringImage.pixelsPerUnitMultiplier = 64f / r;
            glowImage.pixelsPerUnitMultiplier = 64f / (r + spread);
        }

        RectTransform FocusTarget(out float radius)
        {
            radius = 24;
            switch (lastZone)
            {
                case SettingsZone.Rail:
                    int page = -1 - lastFocus;
                    return page >= 0 && page < railItems.Count ? railItems[page].root : null;
                case SettingsZone.Sheet:
                    int option = lastFocus - 1000;
                    return option >= 0 && option < sheetItems.Count ? sheetItems[option].root : null;
                case SettingsZone.Rows:
                case SettingsZone.Adjust:
                    var row = rows.FirstOrDefault(r => !r.IsSection && r.Index == lastFocus);
                    return row?.Root;
                default: return null;
            }
        }

        // -- Building blocks ------------------------------------------------------------------

        internal static RectTransform Node(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        internal static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
        }

        // A full-width strip `height` tall at `top` below the parent's top edge; right < 0 insets the right edge.
        internal static void Top(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(right, -top);
        }

        // Centres a rect at x from the parent's left (or right) edge and y below its top.
        internal static void Pin(RectTransform rect, float x, float y, bool left, float width = -1)
        {
            if (width >= 0) rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            rect.anchorMin = rect.anchorMax = new Vector2(left ? 0 : 1, 1);
            rect.pivot = new Vector2(left ? (width >= 0 ? 0 : 0.5f) : 1, 0.5f);
            rect.anchoredPosition = new Vector2(left ? x : -x, -y);
        }

        static void Center(RectTransform rect, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;
        }

        static Image Full(string name, Transform parent, Color color)
        {
            var image = SkinUi.Image(name, parent, null, 0, 0);
            image.color = color;
            Stretch(image.rectTransform, 0, 0, 0, 0);
            return image;
        }

        static void Divider(RectTransform parent, bool top)
        {
            var line = SkinUi.Image("Divider", parent, null, 0, 1);
            line.color = new Color(1, 1, 1, 0.07f);
            line.rectTransform.anchorMin = new Vector2(0, top ? 1 : 0); line.rectTransform.anchorMax = new Vector2(1, top ? 1 : 0);
            line.rectTransform.pivot = new Vector2(0.5f, top ? 1 : 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1); line.rectTransform.anchoredPosition = Vector2.zero;
        }

        internal static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
        {
            var label = SkinUi.Text(name, parent, size);
            label.color = color;
            label.UseUiFont();
            label.alignment = alignment;
            label.text = text;
            return label;
        }

        // Light text has the shared black outline and dark text none (SkinUi.UseUiFont).
        internal static void Recolor(TMP_Text text, Color color)
        {
            bool dark = SkinUi.IsDark(color);
            text.color = color;
            if (dark != (text.fontSharedMaterial == SkinUi.Font.material)) text.UseUiFont();
        }

        internal static Image ButtonBox(Transform parent, string name, Color color, float width, float height, Action clicked)
        {
            var box = SettingsShapes.Box(name, parent, color, Mathf.Min(21, height / 2));
            box.rectTransform.sizeDelta = new Vector2(width, height);
            box.raycastTarget = true;
            if (clicked != null) box.gameObject.AddComponent<PointerRelay>().Clicked = clicked;
            return box;
        }

        // One row (or a group's section title) of the page.
        sealed class RowView
        {
            public RectTransform Root { get; private set; }
            public bool IsSection { get; private set; }
            public int Index { get; private set; }
            public float Height { get; private set; } = RowHeight;
            // The page's current row object; the pages rebuild their rows on every change.
            SettingRow row;
            readonly Image box;
            readonly TextMeshProUGUI label, description;
            TextMeshProUGUI value;
            readonly RectTransform badge, control;
            Image track, knob, fill;
            readonly List<(Image box, TextMeshProUGUI text)> choices = new();
            float knobAt = -1, knobGoal;

            RowView() { }

            public static RowView Section(TextMeshProUGUI title) => new RowView { Root = title.rectTransform, IsSection = true, Index = -1 };

            public RowView(SettingsOverlayView view, RectTransform list, SettingRow row, int index, GameSettings settings)
            {
                this.row = row;
                Index = index;
                box = SettingsShapes.Box(row.Id, list, RowColor, 24);
                box.raycastTarget = true;
                box.gameObject.AddComponent<PointerRelay>().Clicked = () => view.TapRow?.Invoke(index);
                Root = box.rectTransform;
                label = Label("Label", Root, row.Label, 36, Ink, TextAlignmentOptions.MidlineLeft);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0, 1);
                label.rectTransform.sizeDelta = new Vector2(label.preferredWidth + 4, 54);
                if (row.AppliesOnClose)
                {
                    var tag = SettingsShapes.Box("OnClose", Root, Alpha(Amber, 0.16f), 12);
                    var text = Label("Text", tag.transform, "On close", 22, Amber, TextAlignmentOptions.Center);
                    float width = text.GetPreferredValues("On close").x + 28;
                    tag.rectTransform.sizeDelta = new Vector2(width, 40);
                    Center(text.rectTransform, width, 40);
                    tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = tag.rectTransform.pivot = new Vector2(0, 1);
                    badge = tag.rectTransform;
                }
                description = Label("Description", Root, row.Description, 27, Hex(0xA9B8D1), TextAlignmentOptions.TopLeft);
                description.textWrappingMode = TextWrappingModes.Normal;
                description.lineSpacing = WrappedSpacing;
                description.rectTransform.anchorMin = description.rectTransform.anchorMax = description.rectTransform.pivot = new Vector2(0, 1);
                description.rectTransform.sizeDelta = new Vector2(DescWidth, 40);
                description.gameObject.SetActive(false);

                control = Node("Control", Root);
                control.anchorMin = control.anchorMax = control.pivot = new Vector2(1, 0.5f);
                control.anchoredPosition = new Vector2(-27, 0);
                BuildControl(view, settings);
            }

            void BuildControl(SettingsOverlayView view, GameSettings settings)
            {
                int index = Index;
                float x = 0;
                RectTransform Add(RectTransform rect)
                {
                    rect.SetParent(control, false);
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0.5f);
                    rect.anchoredPosition = new Vector2(-x, 0);
                    x += rect.sizeDelta.x + 18;
                    return rect;
                }
                TextMeshProUGUI Text(string name, float width, float size, TextAlignmentOptions align)
                {
                    var text = Label(name, control, "", size, Ink, align);
                    text.rectTransform.sizeDelta = new Vector2(width, 60);
                    text.overflowMode = TextOverflowModes.Ellipsis;
                    Add(text.rectTransform);
                    return text;
                }
                switch (row.Kind)
                {
                    case SettingKind.Toggle:
                        track = ButtonBox(control, "Switch", TrackOff, 114, 66, () => view.SetRow?.Invoke(index, 1 - row.Get(SettingsOverlay.CurrentSettings)));
                        Add(track.rectTransform);
                        knob = SettingsShapes.Dot("Knob", track.transform, Color.white, 51);
                        knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                        break;
                    case SettingKind.Choice:
                        var groove = SettingsShapes.Box("Choices", control, Groove, 24);
                        float width = 6;
                        var widths = row.Options.Select(o => Label("Measure", groove.transform, o, 30, Ink, TextAlignmentOptions.Center)).ToList();
                        for (int i = 0; i < row.Options.Count; i++)
                        {
                            float w = widths[i].preferredWidth + 60;
                            Destroy(widths[i].gameObject);
                            int option = i;
                            var b = ButtonBox(groove.transform, "Option" + i, Color.clear, w, 78, () => view.SetRow?.Invoke(index, option));
                            b.rectTransform.anchorMin = b.rectTransform.anchorMax = b.rectTransform.pivot = new Vector2(0, 0.5f);
                            b.rectTransform.anchoredPosition = new Vector2(width, 0);
                            var t = Label("Text", b.transform, row.Options[i], 30, Ink, TextAlignmentOptions.Center);
                            Center(t.rectTransform, w, 60);
                            choices.Add((b, t));
                            width += w + 6;
                        }
                        groove.rectTransform.sizeDelta = new Vector2(width, 90);
                        Add(groove.rectTransform);
                        break;
                    case SettingKind.Number:
                        Add(Repeat(view, ">", index, 1));
                        value = Text("Value", 210, 36, TextAlignmentOptions.Center);
                        Add(Repeat(view, "<", index, -1));
                        break;
                    case SettingKind.Slider:
                        value = Text("Value", 114, 36, TextAlignmentOptions.MidlineRight);
                        track = SettingsShapes.Box("Track", control, SliderTrack, 9);
                        track.rectTransform.sizeDelta = new Vector2(420, 18);
                        var hit = Node("Hit", track.transform);
                        Stretch(hit, -6, -24, -6, -24);
                        var hitImage = hit.gameObject.AddComponent<Image>();
                        hitImage.color = Color.clear;
                        hit.gameObject.AddComponent<PointerRelay>().Clicked = () => { };
                        hit.gameObject.AddComponent<SettingsSliderDrag>().Dragged = t =>
                        {
                            int steps = Mathf.RoundToInt(t * (row.Maximum - row.Minimum) / row.Step);
                            view.SetRow?.Invoke(index, row.Minimum + steps * row.Step);
                        };
                        Add(track.rectTransform);
                        fill = SettingsShapes.Box("Fill", track.transform, Cyan, 9);
                        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1); fill.rectTransform.pivot = new Vector2(0, 0.5f);
                        fill.rectTransform.offsetMin = Vector2.zero;
                        knob = SettingsShapes.Dot("Knob", track.transform, Color.white, 51);
                        knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0, 0.5f);
                        break;
                    case SettingKind.List:
                        var list = ButtonBox(control, "Open", Button, 300, 84, () => view.OpenRow?.Invoke(index));
                        value = Label("Value", list.transform, "", 30, Ink, TextAlignmentOptions.MidlineRight);
                        value.overflowMode = TextOverflowModes.Ellipsis;
                        Stretch(value.rectTransform, 24, 0, 30, 0);
                        Add(list.rectTransform);
                        break;
                    case SettingKind.Keys:
                        var add = ButtonBox(control, "Add", Button, 84, 84, () => view.OpenRow?.Invoke(index));
                        Add(add.rectTransform);
                        Center(Label("Plus", add.transform, "+", 42, Ink, TextAlignmentOptions.Center).rectTransform, 84, 84);
                        var keys = settings.general.keyboard.Get(row.Key);
                        if (keys.Count == 0) Text("Unbound", 200, 30, TextAlignmentOptions.MidlineRight).color = Muted;
                        foreach (var key in keys.Reverse())
                        {
                            var cap = SettingsShapes.Box("Key " + key, control, Ink, 15);
                            var t = Label("Text", cap.transform, key.ToString(), 30, Dark, TextAlignmentOptions.Center);
                            float w = Mathf.Max(78, t.preferredWidth + 36);
                            cap.rectTransform.sizeDelta = new Vector2(w, 78);
                            Center(t.rectTransform, w, 78);
                            Add(cap.rectTransform);
                        }
                        break;
                    case SettingKind.Action:
                        var button = ButtonBox(control, "Run", Ink, 0, 84, () => view.OpenRow?.Invoke(index));
                        var caption = Label("Text", button.transform, row.ActionLabel, 30, Dark, TextAlignmentOptions.Center);
                        float bw = caption.preferredWidth + 72;
                        button.rectTransform.sizeDelta = new Vector2(bw, 84);
                        Center(caption.rectTransform, bw, 84);
                        Add(button.rectTransform);
                        value = Text("Status", 520, 28, TextAlignmentOptions.MidlineRight);
                        value.richText = false;
                        break;
                    default:
                        value = Text("Value", 520, 30, TextAlignmentOptions.MidlineRight);
                        value.richText = false;
                        break;
                }
            }

            static RectTransform Repeat(SettingsOverlayView view, string glyph, int index, int delta)
            {
                // The press repeats; the click is swallowed so it does not reach the row.
                var box = ButtonBox(null, glyph == ">" ? "Increase" : "Decrease", Button, 84, 84, () => { });
                box.gameObject.AddComponent<SettingsRepeatPress>().Pressed = () =>
                {
                    var row = SettingsOverlay.CurrentRow(index);
                    if (row != null) view.SetRow?.Invoke(index, row.Move(row.Get(SettingsOverlay.CurrentSettings), delta));
                };
                var text = Label("Glyph", box.transform, glyph == ">" ? "›" : "‹", 48, Ink, TextAlignmentOptions.Center);
                Center(text.rectTransform, 84, 84);
                return box.rectTransform;
            }

            public void Show(SettingsNavigator nav, int listIndex, SettingsOverlayView view)
            {
                if (IsSection) return;
                if (nav.Page != null && Index < nav.Page.Rows.Count) row = nav.Page.Rows[Index];
                bool focused = (nav.Zone == SettingsZone.Rows || nav.Zone == SettingsZone.Adjust) && nav.RowIndex == Index;
                bool adjusting = focused && nav.Zone == SettingsZone.Adjust;
                int current = row.HasValue ? (adjusting ? nav.AdjustValue : row.Get(nav.Settings)) : 0;
                box.color = focused ? RowFocus : RowColor;
                description.gameObject.SetActive(focused);
                float descHeight = focused ? description.GetPreferredValues(row.Description, DescWidth, 0).y : 0;
                Height = focused ? Mathf.Max(RowHeight, 78 + descHeight + 24) : RowHeight;
                label.rectTransform.anchoredPosition = new Vector2(36, focused ? -24 : -(RowHeight - 54) / 2);
                if (badge != null) badge.anchoredPosition = new Vector2(36 + label.rectTransform.sizeDelta.x + 16, label.rectTransform.anchoredPosition.y - 7);
                if (focused)
                {
                    description.rectTransform.sizeDelta = new Vector2(DescWidth, descHeight + 4);
                    description.rectTransform.anchoredPosition = new Vector2(36, -78);
                }
                switch (row.Kind)
                {
                    case SettingKind.Toggle:
                        track.color = current != 0 ? TrackOn : TrackOff;
                        knobGoal = current != 0 ? 24 : -24;
                        if (knobAt == -1) knobAt = knobGoal;
                        break;
                    case SettingKind.Choice:
                        for (int i = 0; i < choices.Count; i++)
                        {
                            choices[i].box.color = i == current ? Ink : Color.clear;
                            Recolor(choices[i].text, i == current ? Dark : Hex(0xB7C4DA));
                        }
                        break;
                    case SettingKind.Slider:
                        float t = row.Maximum > row.Minimum ? (current - row.Minimum) / (float)(row.Maximum - row.Minimum) : 0;
                        knobGoal = t * 420;
                        if (knobAt == -1) knobAt = knobGoal;
                        value.text = row.Format(current);
                        break;
                    case SettingKind.Number:
                    case SettingKind.List:
                        value.text = row.Format(current) + (row.Kind == SettingKind.List ? "  ›" : "");
                        break;
                    case SettingKind.Action:
                        value.text = row.Status;
                        Recolor(value, Muted);
                        break;
                    case SettingKind.Text:
                        value.text = row.Display(nav.Settings);
                        Recolor(value, row.GetText(nav.Settings).Length == 0 ? Muted : Ink);
                        break;
                    case SettingKind.Password:
                        value.text = "Type to sign in";
                        Recolor(value, Muted);
                        break;
                }
            }

            public void Animate(float dt)
            {
                if (IsSection || knob == null) return;
                knobAt = Mathf.Lerp(knobAt, knobGoal, 1 - Mathf.Exp(-dt * (row.Kind == SettingKind.Toggle ? 26 : 40)));
                knob.rectTransform.anchoredPosition = new Vector2(knobAt, 0);
                if (fill != null) fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(18, knobAt), 0);
            }
        }
    }

    // Wheel and drag scrolling of the page; a drag that starts on a row still scrolls.
    public sealed class SettingsScroll : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public Action<float> Scrolled;

        float Scale => GetComponentInParent<Canvas>() is Canvas canvas ? Mathf.Max(0.0001f, canvas.rootCanvas.scaleFactor) : 1;
        public void OnBeginDrag(PointerEventData eventData) { }
        public void OnDrag(PointerEventData eventData) => Scrolled?.Invoke(eventData.delta.y / Scale);
        public void OnEndDrag(PointerEventData eventData) { }
        public void OnScroll(PointerEventData eventData) => Scrolled?.Invoke(-eventData.scrollDelta.y * 60);
    }

    // A slider's track: pressing or dragging reports the position along it, 0 to 1.
    public sealed class SettingsSliderDrag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        public Action<float> Dragged;

        void Report(PointerEventData eventData)
        {
            var track = (RectTransform)transform.parent;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, eventData.position, eventData.pressEventCamera, out var local)) return;
            Dragged?.Invoke(Mathf.Clamp01((local.x - track.rect.xMin) / Mathf.Max(1, track.rect.width)));
        }

        public void OnPointerDown(PointerEventData eventData) => Report(eventData);
        public void OnBeginDrag(PointerEventData eventData) => Report(eventData);
        public void OnDrag(PointerEventData eventData) => Report(eventData);
    }

    // A stepper button: one step on press, then repeating faster while held.
    public sealed class SettingsRepeatPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action Pressed;
        bool held;
        float heldAt, nextAt;

        public void OnPointerDown(PointerEventData eventData)
        {
            held = true;
            heldAt = Time.unscaledTime;
            nextAt = heldAt + SettingsOverlay.RepeatDelay;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData) => held = false;
        public void OnPointerExit(PointerEventData eventData) => held = false;
        void OnDisable() => held = false;

        void Update()
        {
            if (!held || Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + SettingsOverlay.RepeatInterval(Time.unscaledTime - heldAt);
            Pressed?.Invoke();
        }
    }
}
