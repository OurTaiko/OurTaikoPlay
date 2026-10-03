using System;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        // Builds the former runtime hierarchy once. Save this view in a scene/prefab; runtime only
        // binds its controls and adds animation offsets to the editable resting RectTransforms.
        public static OptionPanelView CreateOptionPanelView(RectTransform parent, OptionPanelArt art)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Exit Play mode before creating the option panel.");
            const float restTop = 532, boardX = 5;
            var root = SkinUi.Rect("OptionPanel", parent, 1920, 1080);
            root.pivot = new Vector2(0, 1);
            root.anchoredPosition = Vector2.zero;
            var view = root.gameObject.AddComponent<OptionPanelView>();
            view.outside = SkinUi.Image("Outside", root, null, 1920, 1080);
            view.outside.rectTransform.TopLeft(0, 0);
            view.outside.color = Color.clear;
            view.outside.raycastTarget = true;
            view.outsideClick = view.outside.gameObject.AddComponent<PointerRelay>();

            view.board = SkinUi.Rect("Board", root);
            view.board.anchoredPosition = new Vector2(0, -restTop);
            view.top = SkinUi.Image("Top", view.board, art.board);
            view.top.rectTransform.TopLeft(boardX, 0);
            view.top.raycastTarget = true;
            view.player = SkinUi.Image("Player", view.board, art.player);
            view.player.rectTransform.TopLeft(32, 17);
            view.title = SkinUi.Text("Title", view.board, 32);
            view.title.text = "演奏オプション";
            view.title.rectTransform.Center(215, 49);

            view.rows = new OptionPanelView.RowView[OptionMenu.Rows.Length];
            for (int i = 0; i < view.rows.Length; i++)
                view.rows[i] = CreateOptionPanelRow(view.board, i, art);
            // Leave the new hierarchy visible with ordinary default settings for Prefab/Scene view.
            // No PlayOptions.Shared access here: editor construction must not load or save user data.
            return view;
        }

        static OptionPanelView.RowView CreateOptionPanelRow(RectTransform board, int index,
            OptionPanelArt art)
        {
            const float rowTop = 86, rowPitch = 61, rowX = 31, boxX = 208, iconX = 165;
            const float nameX = 44, valueX = 300, arrowX = 214, arrowSpan = 140;
            string[] labels = { "オート", "はやさ", "ドロン", "あべこべ", "ランダム", "演奏スキップ", "音色" };
            float y = rowTop + index * rowPitch;
            bool greyed = OptionMenu.IsGreyed(OptionMenu.Rows[index]);
            var view = new OptionPanelView.RowView();
            view.row = SkinUi.Image("Row" + index, board, art.row);
            view.row.rectTransform.TopLeft(rowX, y);
            view.highlight = SkinUi.Image("Highlight", board, art.rowHighlight);
            view.highlight.rectTransform.TopLeft(rowX, y);
            view.highlight.enabled = index == 0 || greyed;
            view.highlight.color = greyed ? new Color32(166, 168, 171, 255) : Color.white;
            view.box = SkinUi.Image("Box", board, art.box);
            view.box.rectTransform.TopLeft(boxX, y + 8);
            view.name = SkinUi.Text("Name", board, 26);
            view.name.alignment = TextAlignmentOptions.Left;
            view.name.rectTransform.pivot = new Vector2(0, 0.5f);
            view.name.rectTransform.anchoredPosition = new Vector2(nameX, -(y + 28));
            view.name.text = labels[index];
            view.value = SkinUi.Text("Value", board, 26);
            view.value.rectTransform.Center(valueX, y + 28);
            view.value.text = OptionMenu.Rows[index] == OptionRow.Speed ? "1.0"
                : OptionMenu.Rows[index] == OptionRow.Neiro
                    ? art.hitSounds != null && art.hitSounds.Count > 0 ? art.hitSounds.names[0] : "無音"
                    : "しない";
            view.icon = SkinUi.Image("Icon", board, null, 40, 40);
            view.icon.rectTransform.TopLeft(iconX, y + 8);
            view.icon.enabled = false;
            view.leftArrow = SkinUi.Image("LeftArrow", board, art.arrow);
            view.leftArrow.rectTransform.TopLeft(arrowX, y + 12);
            view.rightArrow = SkinUi.Image("RightArrow", board, art.arrow);
            view.rightArrow.rectTransform.localScale = new Vector3(-1, 1, 1);
            view.rightArrow.rectTransform.TopLeft(arrowX + arrowSpan, y + 12);
            view.leftArrow.enabled = view.rightArrow.enabled = index == 0;
            if (greyed)
            {
                view.scrim = SkinUi.Image("Scrim", board, art.row);
                view.scrim.rectTransform.TopLeft(rowX, y);
                view.scrim.color = new Color32(0, 0, 0, 128);
            }
            view.select = CreateOptionPanelZone(board, "Select" + index, rowX, y, boxX - rowX, rowPitch);
            view.previous = CreateOptionPanelZone(board, "Previous" + index, boxX, y, valueX - boxX, rowPitch);
            view.next = CreateOptionPanelZone(board, "Next" + index, valueX, y,
                rowX + art.row.rect.width - valueX, rowPitch);
            return view;
        }

        static PointerRelay CreateOptionPanelZone(RectTransform board, string zoneName,
            float x, float y, float width, float height)
        {
            var zone = SkinUi.Image(zoneName, board, null, width, height);
            zone.rectTransform.TopLeft(x, y);
            zone.color = Color.clear;
            zone.raycastTarget = true;
            return zone.gameObject.AddComponent<PointerRelay>();
        }
    }
}
