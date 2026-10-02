using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The saved hierarchy of GlobalSettingScene (PyTaikoGreen settings art at 1.5x): the type list
    // on the left, the current type's item list on the right and, under it, the detail panel with
    // the focused item's description and its choice buttons. Row 0 of each list is the authored
    // base; row i sits `pitch` below row i - 1, and missing rows are copied from row 0 at runtime.
    public sealed class GlobalSettingView : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public RectTransform root;
            public Image box;
            public TMP_Text label;
            public TMP_Text value;     // items only: the current choice
            public PointerRelay click;
        }

        public List<Row> typeRows = new List<Row>();
        public List<Row> itemRows = new List<Row>();
        public List<Row> choiceRows = new List<Row>();
        [Tooltip("Vertical distance between type rows / item rows; horizontal between choice buttons.")]
        public float typePitch = 160, itemPitch = 150, choicePitch = 330;
        public SwipeRelay typeSwipe, itemSwipe;
        public Sprite typeBox, typeBoxSelected, itemBox, itemBoxSelected, choiceOff, choiceOn;
        [Tooltip("blue_arrow: points at the focused row or choice from its right.")]
        public RectTransform cursor;
        public float cursorGap = 12;
        public CanvasGroup detail;
        public TMP_Text detailTitle, description;

        Vector2 typeBase, itemBase, choiceBase;
        bool bound;

        void Bind()
        {
            if (bound) return;
            bound = true;
            typeBase = typeRows[0].root.anchoredPosition;
            itemBase = itemRows[0].root.anchoredPosition;
            choiceBase = choiceRows[0].root.anchoredPosition;
        }

        static void Ensure(List<Row> rows, int count)
        {
            while (rows.Count < count)
            {
                var source = rows[0];
                var copy = Instantiate(source.root.gameObject, source.root.parent);
                copy.name = source.root.name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') + rows.Count;
                var t = copy.transform;
                rows.Add(new Row
                {
                    root = (RectTransform)t,
                    box = copy.GetComponent<Image>(),
                    label = source.label != null ? t.Find(source.label.name).GetComponent<TMP_Text>() : null,
                    value = source.value != null ? t.Find(source.value.name).GetComponent<TMP_Text>() : null,
                    click = copy.GetComponent<PointerRelay>(),
                });
            }
        }

        // Hooks the rows' taps; extra rows created later are hooked as they appear.
        public void Bind(Action<int> tapType, Action<int> tapItem, Action<int> tapChoice, Action<int> swipeTypes, Action<int> swipeItems)
        {
            Bind();
            tapTypeHandler = tapType; tapItemHandler = tapItem; tapChoiceHandler = tapChoice;
            typeSwipe.Swiped = swipeTypes;
            itemSwipe.Swiped = swipeItems;
            Hook();
        }

        Action<int> tapTypeHandler, tapItemHandler, tapChoiceHandler;

        void Hook()
        {
            for (int i = 0; i < typeRows.Count; i++) { int index = i; typeRows[i].click.Clicked = () => tapTypeHandler?.Invoke(index); }
            for (int i = 0; i < itemRows.Count; i++) { int index = i; itemRows[i].click.Clicked = () => tapItemHandler?.Invoke(index); }
            for (int i = 0; i < choiceRows.Count; i++) { int index = i; choiceRows[i].click.Clicked = () => tapChoiceHandler?.Invoke(index); }
        }

        public void Show(SettingsMenu menu)
        {
            Bind();
            int types = menu.TypeCount;
            int items = menu.CurrentType != null ? menu.ItemCount : 0;
            var item = menu.CurrentItem;
            int choices = item?.Choices.Count ?? 0;
            int before = typeRows.Count + itemRows.Count + choiceRows.Count;
            Ensure(typeRows, types);
            Ensure(itemRows, Math.Max(1, items));
            Ensure(choiceRows, Math.Max(1, choices));
            if (typeRows.Count + itemRows.Count + choiceRows.Count != before) Hook();

            for (int i = 0; i < typeRows.Count; i++)
            {
                var row = typeRows[i];
                bool shown = i < types;
                row.root.gameObject.SetActive(shown);
                if (!shown) continue;
                row.root.anchoredPosition = typeBase + new Vector2(0, -i * typePitch);
                row.label.text = i < menu.Types.Count ? menu.Types[i].Label : "Return";
                row.box.sprite = i == menu.TypeIndex ? typeBoxSelected : typeBox;
            }

            bool inItems = menu.Focus != SettingsFocus.Types;
            for (int i = 0; i < itemRows.Count; i++)
            {
                var row = itemRows[i];
                bool shown = i < items;
                row.root.gameObject.SetActive(shown);
                if (!shown) continue;
                row.root.anchoredPosition = itemBase + new Vector2(0, -i * itemPitch);
                bool isReturn = i == menu.CurrentType.Items.Count;
                var rowItem = isReturn ? null : menu.CurrentType.Items[i];
                row.label.text = isReturn ? "Return" : rowItem.Label;
                row.value.text = isReturn ? "" : rowItem.Choices[rowItem.Get(menu.Settings)];
                row.box.sprite = inItems && i == menu.ItemIndex ? itemBoxSelected : itemBox;
            }

            // The detail panel follows the focused item; on the types it previews the type's first item.
            var shownItem = inItems ? item : menu.CurrentType?.Items.Count > 0 ? menu.CurrentType.Items[0] : null;
            detail.alpha = shownItem != null ? 1 : 0;
            detail.blocksRaycasts = shownItem != null;
            if (shownItem != null)
            {
                detailTitle.text = shownItem.Label;
                description.text = shownItem.Description;
                int current = shownItem.Get(menu.Settings);
                int lit = menu.Focus == SettingsFocus.Choice ? menu.ChoiceIndex : current;
                int count = shownItem.Choices.Count;
                for (int i = 0; i < choiceRows.Count; i++)
                {
                    var row = choiceRows[i];
                    bool shown = i < count;
                    row.root.gameObject.SetActive(shown);
                    if (!shown) continue;
                    row.root.anchoredPosition = choiceBase + new Vector2((i - (count - 1) / 2f) * choicePitch, 0);
                    row.label.text = shownItem.Choices[i];
                    row.box.sprite = i == lit ? choiceOn : choiceOff;
                }
            }

            RectTransform target = menu.Focus switch
            {
                SettingsFocus.Types => typeRows[menu.TypeIndex].root,
                SettingsFocus.Items => itemRows[menu.ItemIndex].root,
                _ => choiceRows[menu.ChoiceIndex].root,
            };
            PlaceCursor(target);
        }

        void PlaceCursor(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var parent = (RectTransform)cursor.parent;
            // corners: 0 bottom-left, 2 top-right; the arrow sits right of the target, centred on it.
            Vector2 right = parent.InverseTransformPoint((corners[2] + corners[3]) / 2);
            Vector2 middle = parent.InverseTransformPoint((corners[0] + corners[2]) / 2);
            cursor.pivot = new Vector2(0, 0.5f);
            cursor.anchorMin = cursor.anchorMax = new Vector2(0.5f, 0.5f);
            cursor.anchoredPosition = new Vector2(right.x + cursorGap, middle.y) - Center(parent);
        }

        static Vector2 Center(RectTransform rect) => rect.rect.center;
    }
}
